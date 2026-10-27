#!/usr/bin/env python3
"""Count school-ownership review signals in an offline SQLite snapshot.

Only aggregate counts are emitted. This tool never assigns ownership, changes a
schema, copies a database, or determines whether a migration is safe. It must
only be run on an independently verified, offline snapshot with no writers.
"""

from __future__ import annotations

import json
import os
from pathlib import Path
import sqlite3
import stat
import sys
from contextlib import closing


LEGACY_MIGRATIONS = (
    "20260815161030_InitialCreate",
    "20260815163056_AddTeachersAndCourses",
    "20260815163718_AddSchoolEntities",
    "20260816094247_AddAuditLog",
    "20260817220926_Stage1_CoreSchoolDomain",
    "20261009090000_AddQuranRecords",
    "20261009120000_AddRefreshTokens",
)
OWNERSHIP_MIGRATION = "20261027162508_AddExplicitSchoolOwnership"
OWNERSHIP_TABLES = {
    "students": "Students",
    "teachers": "Teachers",
    "attendance": "AttendanceRecords",
    "behavior": "BehaviorRecords",
    "quran": "QuranRecords",
}
TOTAL_TABLES = {
    **OWNERSHIP_TABLES,
    "courses": "Courses",
    "enrollments": "Enrollments",
    "guardian_links": "StudentParents",
}
REQUIRED_COLUMNS = {
    "__EFMigrationsHistory": {"MigrationId", "ProductVersion"},
    "Organizations": {"OrganizationId", "IsActive"},
    "Students": {"StudentId", "UserId"},
    "Teachers": {"TeacherId", "UserId"},
    "Courses": {"CourseId", "TeacherId"},
    "OrganizationMemberships": {"OrganizationId", "UserId", "IsActive"},
    "AcademicYears": {"AcademicYearId", "OrganizationId"},
    "Terms": {"TermId", "AcademicYearId"},
    "SchoolClasses": {"SchoolClassId", "AcademicYearId"},
    "Sections": {"SectionId", "SchoolClassId", "TeacherId"},
    "Enrollments": {"EnrollmentId", "StudentId", "AcademicYearId", "TermId", "SectionId", "Status"},
    "Parents": {"ParentId", "UserId"},
    "StudentParents": {"StudentParentId", "StudentId", "ParentId", "IsAuthorized"},
    "AttendanceRecords": {"AttendanceRecordId", "StudentId", "Date"},
    "BehaviorRecords": {"BehaviorRecordId", "StudentId"},
    "QuranRecords": {"QuranRecordId", "StudentId"},
}

# Each result is a count of entities or links requiring review, not proof of
# school ownership or unauthorized access. Enrollments include every status.
REVIEW_QUERIES = {
    "students_with_multiple_enrollment_schools": """
        SELECT COUNT(*) FROM Students s WHERE
          (SELECT COUNT(DISTINCT y.OrganizationId) FROM Enrollments e
           JOIN AcademicYears y ON y.AcademicYearId = e.AcademicYearId
           WHERE e.StudentId = s.StudentId) > 1
    """,
    "students_with_multiple_active_membership_schools": """
        SELECT COUNT(*) FROM Students s WHERE
          (SELECT COUNT(DISTINCT m.OrganizationId) FROM OrganizationMemberships m
           WHERE m.UserId = s.UserId AND m.IsActive = 1) > 1
    """,
    "students_without_enrollments": """
        SELECT COUNT(*) FROM Students s
        WHERE NOT EXISTS (SELECT 1 FROM Enrollments e WHERE e.StudentId = s.StudentId)
    """,
    "teachers_with_multiple_active_membership_schools": """
        SELECT COUNT(*) FROM Teachers t WHERE
          (SELECT COUNT(DISTINCT m.OrganizationId) FROM OrganizationMemberships m
           WHERE m.UserId = t.UserId AND m.IsActive = 1) > 1
    """,
    "teachers_with_courses_and_no_active_memberships": """
        SELECT COUNT(*) FROM Teachers t
        WHERE EXISTS (SELECT 1 FROM Courses c WHERE c.TeacherId = t.TeacherId)
          AND NOT EXISTS (SELECT 1 FROM OrganizationMemberships m
                          WHERE m.UserId = t.UserId AND m.IsActive = 1)
    """,
    "enrollments_with_inconsistent_or_missing_year_links": """
        SELECT COUNT(*) FROM Enrollments e
        LEFT JOIN AcademicYears y ON y.AcademicYearId = e.AcademicYearId
        LEFT JOIN Terms t ON t.TermId = e.TermId
        LEFT JOIN Sections s ON s.SectionId = e.SectionId
        LEFT JOIN SchoolClasses c ON c.SchoolClassId = s.SchoolClassId
        WHERE y.AcademicYearId IS NULL OR t.TermId IS NULL OR s.SectionId IS NULL
           OR c.SchoolClassId IS NULL OR t.AcademicYearId <> e.AcademicYearId
           OR c.AcademicYearId <> e.AcademicYearId
    """,
    "authorized_guardian_links_missing_membership_for_an_enrollment_school": """
        SELECT COUNT(*) FROM StudentParents sp JOIN Parents p ON p.ParentId = sp.ParentId
        WHERE sp.IsAuthorized = 1 AND EXISTS (
          SELECT 1 FROM Enrollments e JOIN AcademicYears y ON y.AcademicYearId = e.AcademicYearId
          WHERE e.StudentId = sp.StudentId AND NOT EXISTS (
            SELECT 1 FROM OrganizationMemberships m WHERE m.UserId = p.UserId
              AND m.OrganizationId = y.OrganizationId AND m.IsActive = 1))
    """,
    "authorized_guardian_links_without_enrollments": """
        SELECT COUNT(*) FROM StudentParents sp WHERE sp.IsAuthorized = 1
          AND NOT EXISTS (SELECT 1 FROM Enrollments e WHERE e.StudentId = sp.StudentId)
    """,
    "duplicate_attendance_student_date_groups": """
        SELECT COUNT(*) FROM (
          SELECT StudentId, Date FROM AttendanceRecords GROUP BY StudentId, Date HAVING COUNT(*) > 1)
    """,
}
POST_MIGRATION_QUERIES = {
    "enrollments_with_school_mismatch": """
        SELECT COUNT(*) FROM Enrollments e JOIN Students s ON s.StudentId = e.StudentId
        JOIN AcademicYears y ON y.AcademicYearId = e.AcademicYearId
        WHERE s.OrganizationId IS NOT NULL AND y.OrganizationId <> s.OrganizationId
    """,
    "sections_with_teacher_school_mismatch": """
        SELECT COUNT(*) FROM Sections s JOIN Teachers t ON t.TeacherId = s.TeacherId
        JOIN SchoolClasses c ON c.SchoolClassId = s.SchoolClassId
        JOIN AcademicYears y ON y.AcademicYearId = c.AcademicYearId
        WHERE t.OrganizationId IS NOT NULL AND y.OrganizationId <> t.OrganizationId
    """,
    "authorized_guardian_links_without_student_owner_membership": """
        SELECT COUNT(*) FROM StudentParents sp JOIN Students s ON s.StudentId = sp.StudentId
        JOIN Parents p ON p.ParentId = sp.ParentId
        WHERE sp.IsAuthorized = 1 AND s.OrganizationId IS NOT NULL AND NOT EXISTS (
          SELECT 1 FROM OrganizationMemberships m WHERE m.UserId = p.UserId
            AND m.OrganizationId = s.OrganizationId AND m.IsActive = 1)
    """,
}


class PreflightError(Exception):
    """A static, non-sensitive error intended for the command line."""


def _has_sidecars(path: Path) -> bool:
    # lexists also rejects a dangling symlink with a sidecar name.
    return any(os.path.lexists(str(path) + suffix) for suffix in ("-wal", "-shm", "-journal"))


def _identity(path: Path) -> tuple[int, int, int, int, int]:
    info = path.stat()
    return info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns, info.st_ctime_ns


def _count(db: sqlite3.Connection, sql: str) -> int:
    return int(db.execute(sql).fetchone()[0])


def _schema_state(db: sqlite3.Connection) -> str:
    actual_tables = {
        row[0] for row in db.execute("SELECT name FROM sqlite_master WHERE type = 'table'")
    }
    if not set(REQUIRED_COLUMNS).issubset(actual_tables):
        raise PreflightError("Unsupported or incomplete schema or migration history.")
    columns = {}
    for table, required in REQUIRED_COLUMNS.items():
        # Table identifiers are constants above; no user input becomes SQL.
        columns[table] = {row[1] for row in db.execute(f'PRAGMA table_info("{table}")')}
        if not required.issubset(columns[table]):
            raise PreflightError("Unsupported or incomplete schema or migration history.")
    history = tuple(row[0] for row in db.execute(
        "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId"
    ))
    has_owner = ["OrganizationId" in columns[table] for table in OWNERSHIP_TABLES.values()]
    if history == LEGACY_MIGRATIONS and not any(has_owner):
        return "legacy"
    if history == (*LEGACY_MIGRATIONS, OWNERSHIP_MIGRATION) and all(has_owner):
        return "owned"
    raise PreflightError("Unsupported or incomplete schema or migration history.")


def _make_report(db: sqlite3.Connection) -> dict:
    schema = _schema_state(db)
    integrity_rows = [row[0] for row in db.execute("PRAGMA quick_check")]
    if integrity_rows != ["ok"]:
        raise PreflightError("SQLite integrity check failed; no report was produced.")
    # foreign_key_check can include row ids: count internally, never emit rows.
    foreign_key_count = sum(1 for _ in db.execute("PRAGMA foreign_key_check"))
    totals = {key: _count(db, f'SELECT COUNT(*) FROM "{table}"') for key, table in TOTAL_TABLES.items()}
    if schema == "legacy":
        ownership = {
            "current_unassigned": None,
            "projected_unassigned_after_upgrade": {key: totals[key] for key in OWNERSHIP_TABLES},
        }
        post = None
    else:
        ownership = {
            "current_unassigned": {
                key: _count(db, f'SELECT COUNT(*) FROM "{table}" WHERE OrganizationId IS NULL')
                for key, table in OWNERSHIP_TABLES.items()
            },
            "projected_unassigned_after_upgrade": None,
        }
        post = {key: _count(db, sql) for key, sql in POST_MIGRATION_QUERIES.items()}
        post["record_owner_student_mismatches"] = {}
        post["records_with_school_but_unassigned_student"] = {}
        for key in ("attendance", "behavior", "quran"):
            table = OWNERSHIP_TABLES[key]
            post["record_owner_student_mismatches"][key] = _count(db, f"""
                SELECT COUNT(*) FROM "{table}" r JOIN Students s ON s.StudentId = r.StudentId
                WHERE r.OrganizationId IS NOT NULL AND s.OrganizationId IS NOT NULL
                  AND r.OrganizationId <> s.OrganizationId
            """)
            post["records_with_school_but_unassigned_student"][key] = _count(db, f"""
                SELECT COUNT(*) FROM "{table}" r JOIN Students s ON s.StudentId = r.StudentId
                WHERE r.OrganizationId IS NOT NULL AND s.OrganizationId IS NULL
            """)
    return {
        "schema": schema,
        "integrity": {"quick_check_ok": True, "foreign_key_violation_count": foreign_key_count},
        "totals": totals,
        "ownership": ownership,
        "review": {key: _count(db, sql) for key, sql in REVIEW_QUERIES.items()},
        "post_migration_review": post,
    }


def inspect_snapshot(input_path: str) -> dict:
    original = Path(os.path.abspath(os.path.expanduser(input_path)))
    if original.is_symlink():
        raise PreflightError("Snapshot must be an existing regular file, not a symlink.")
    try:
        path = original.resolve(strict=True)
        if not stat.S_ISREG(path.stat().st_mode):
            raise PreflightError("Snapshot must be an existing regular file, not a symlink.")
    except (OSError, RuntimeError):
        raise PreflightError("Snapshot must be an existing regular file, not a symlink.") from None
    if _has_sidecars(original) or _has_sidecars(path):
        raise PreflightError("SQLite sidecar present; use a verified offline snapshot.")
    before = _identity(path)

    # immutable avoids SQLite creating -shm or acquiring locks, but ignores WAL.
    # Sidecar rejection and an independently verified offline snapshot are
    # mandatory; this does not establish that no concurrent writer exists.
    uri = path.as_uri() + "?mode=ro&immutable=1"
    with closing(sqlite3.connect(uri, uri=True, isolation_level=None)) as db:
        db.execute("PRAGMA trusted_schema = OFF")
        db.execute("PRAGMA query_only = ON")
        db.execute("BEGIN")
        report = _make_report(db)
        db.execute("ROLLBACK")

    if _has_sidecars(original) or _has_sidecars(path) or _identity(path) != before:
        raise PreflightError("Snapshot changed during inspection; no report was produced.")
    return report


def main(argv: list[str] | None = None) -> int:
    args = sys.argv[1:] if argv is None else argv
    if len(args) != 1:
        print("Usage: python3 scripts/school_ownership_preflight.py OFFLINE_SNAPSHOT.db", file=sys.stderr)
        return 2
    try:
        report = inspect_snapshot(args[0])
    except PreflightError as exc:
        print(f"Preflight refused: {exc}", file=sys.stderr)
        return 2
    except (OSError, sqlite3.Error, ValueError, RuntimeError):
        # SQLite and OS exceptions can include input paths or row values.
        print("Preflight refused: cannot read or validate the SQLite snapshot.", file=sys.stderr)
        return 2
    print(json.dumps(report, sort_keys=True, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
