"""Read-only migration preflight: synthetic databases only, no real students."""

import hashlib
import importlib.util
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import sys

import pytest


SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "school_ownership_preflight.py"
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
PRIVATE_MARKER = "SYNTHETIC_PRIVATE_MARKER"


def _run(path: Path) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [sys.executable, str(SCRIPT), str(path)],
        text=True,
        capture_output=True,
        check=False,
    )


def _digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _load_tool():
    spec = importlib.util.spec_from_file_location("school_ownership_preflight", SCRIPT)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _fixture(path: Path, *, owned: bool = False) -> None:
    """A small hand-built schema; CI also checks actual EF migration schemas."""
    with sqlite3.connect(path) as db:
        db.executescript("""
            PRAGMA foreign_keys = ON;
            CREATE TABLE __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT NOT NULL);
            CREATE TABLE Organizations (OrganizationId INTEGER PRIMARY KEY, Name TEXT, IsActive INTEGER);
            CREATE TABLE Students (StudentId INTEGER PRIMARY KEY, UserId INTEGER, FirstName TEXT);
            CREATE TABLE Teachers (TeacherId INTEGER PRIMARY KEY, UserId INTEGER, FirstName TEXT);
            CREATE TABLE Courses (CourseId INTEGER PRIMARY KEY, TeacherId INTEGER REFERENCES Teachers(TeacherId));
            CREATE TABLE OrganizationMemberships (OrganizationMembershipId INTEGER PRIMARY KEY, OrganizationId INTEGER REFERENCES Organizations(OrganizationId), UserId INTEGER, IsActive INTEGER);
            CREATE TABLE AcademicYears (AcademicYearId INTEGER PRIMARY KEY, OrganizationId INTEGER REFERENCES Organizations(OrganizationId));
            CREATE TABLE Terms (TermId INTEGER PRIMARY KEY, AcademicYearId INTEGER REFERENCES AcademicYears(AcademicYearId));
            CREATE TABLE SchoolClasses (SchoolClassId INTEGER PRIMARY KEY, AcademicYearId INTEGER REFERENCES AcademicYears(AcademicYearId));
            CREATE TABLE Sections (SectionId INTEGER PRIMARY KEY, SchoolClassId INTEGER REFERENCES SchoolClasses(SchoolClassId), TeacherId INTEGER REFERENCES Teachers(TeacherId));
            CREATE TABLE Enrollments (EnrollmentId INTEGER PRIMARY KEY, StudentId INTEGER REFERENCES Students(StudentId), AcademicYearId INTEGER REFERENCES AcademicYears(AcademicYearId), TermId INTEGER REFERENCES Terms(TermId), SectionId INTEGER REFERENCES Sections(SectionId), Status TEXT);
            CREATE TABLE Parents (ParentId INTEGER PRIMARY KEY, UserId INTEGER);
            CREATE TABLE StudentParents (StudentParentId INTEGER PRIMARY KEY, StudentId INTEGER REFERENCES Students(StudentId), ParentId INTEGER REFERENCES Parents(ParentId), IsAuthorized INTEGER);
            CREATE TABLE AttendanceRecords (AttendanceRecordId INTEGER PRIMARY KEY, StudentId INTEGER REFERENCES Students(StudentId), Date TEXT);
            CREATE TABLE BehaviorRecords (BehaviorRecordId INTEGER PRIMARY KEY, StudentId INTEGER REFERENCES Students(StudentId));
            CREATE TABLE QuranRecords (QuranRecordId INTEGER PRIMARY KEY, StudentId INTEGER REFERENCES Students(StudentId));
            INSERT INTO Organizations VALUES (1, 'synthetic A', 1), (2, 'synthetic B', 1);
            INSERT INTO Students VALUES (1, 101, 'SYNTHETIC_PRIVATE_MARKER student A'), (2, 102, 'synthetic B'), (3, 103, 'synthetic C');
            INSERT INTO Teachers VALUES (1, 201, 'synthetic teacher A'), (2, 202, 'synthetic teacher B'), (3, 203, 'synthetic teacher C');
            INSERT INTO Courses VALUES (1, 1), (2, 3);
            INSERT INTO OrganizationMemberships VALUES
                (1, 1, 101, 1), (2, 2, 101, 1), (3, 2, 102, 1),
                (4, 1, 201, 1), (5, 1, 202, 1), (6, 2, 202, 1), (7, 1, 301, 1);
            INSERT INTO AcademicYears VALUES (1, 1), (2, 2);
            INSERT INTO Terms VALUES (1, 1), (2, 2);
            INSERT INTO SchoolClasses VALUES (1, 1), (2, 2);
            INSERT INTO Sections VALUES (1, 1, 1), (2, 2, 2), (3, 1, 2);
            INSERT INTO Enrollments VALUES
                (1, 1, 1, 1, 1, 'ACTIVE'), (2, 1, 2, 2, 2, 'ACTIVE'),
                (3, 2, 2, 2, 2, 'ACTIVE'), (4, 2, 2, 1, 1, 'WITHDRAWN');
            INSERT INTO Parents VALUES (1, 301), (2, 302);
            INSERT INTO StudentParents VALUES (1, 1, 1, 1), (2, 2, 1, 1), (3, 3, 2, 1), (4, 2, 2, 0);
            INSERT INTO AttendanceRecords VALUES (1, 1, '2026-09-01'), (2, 2, '2026-09-02'), (3, 3, '2026-09-03');
            INSERT INTO BehaviorRecords VALUES (1, 1), (2, 2);
            INSERT INTO QuranRecords VALUES (1, 1), (2, 3);
            """)
        db.executemany(
            "INSERT INTO __EFMigrationsHistory VALUES (?, '10.0.11')",
            [(migration,) for migration in LEGACY_MIGRATIONS],
        )
        if owned:
            for table in ("Students", "Teachers", "AttendanceRecords", "BehaviorRecords", "QuranRecords"):
                db.execute(f'ALTER TABLE "{table}" ADD COLUMN OrganizationId INTEGER')
            db.executescript("""
                UPDATE Students SET OrganizationId = 1 WHERE StudentId = 1;
                UPDATE Students SET OrganizationId = 2 WHERE StudentId = 2;
                UPDATE Teachers SET OrganizationId = 1 WHERE TeacherId IN (1, 2);
                UPDATE AttendanceRecords SET OrganizationId = CASE AttendanceRecordId WHEN 1 THEN 1 WHEN 2 THEN 1 ELSE 2 END;
                INSERT INTO AttendanceRecords (AttendanceRecordId, StudentId, Date, OrganizationId) VALUES (4, 1, '2026-09-01', 2);
                UPDATE BehaviorRecords SET OrganizationId = 1 WHERE BehaviorRecordId = 2;
                UPDATE QuranRecords SET OrganizationId = 1 WHERE QuranRecordId = 1;
                """)
            db.execute("INSERT INTO __EFMigrationsHistory VALUES (?, '10.0.11')", (OWNERSHIP_MIGRATION,))


def _assert_no_identifiers(result: subprocess.CompletedProcess[str], path: Path) -> None:
    assert PRIVATE_MARKER not in result.stdout + result.stderr
    assert str(path) not in result.stdout + result.stderr


def test_legacy_snapshot_reports_counts_and_review_signals_without_modifying_it(tmp_path):
    path = tmp_path / "synthetic.db"
    _fixture(path)
    before = _digest(path)
    before_files = {p.name for p in tmp_path.iterdir()}

    result = _run(path)

    assert result.returncode == 0, result.stderr
    assert not result.stderr
    _assert_no_identifiers(result, path)
    report = json.loads(result.stdout)
    assert report["schema"] == "legacy"
    assert report["integrity"] == {"quick_check_ok": True, "foreign_key_violation_count": 0}
    assert report["totals"] == {
        "students": 3, "teachers": 3, "courses": 2, "enrollments": 4,
        "guardian_links": 4, "attendance": 3, "behavior": 2, "quran": 2,
    }
    assert report["ownership"] == {
        "current_unassigned": None,
        "projected_unassigned_after_upgrade": {
            "students": 3, "teachers": 3, "attendance": 3, "behavior": 2, "quran": 2,
        },
    }
    assert report["review"] == {
        "students_with_multiple_enrollment_schools": 1,
        "students_with_multiple_active_membership_schools": 1,
        "students_without_enrollments": 1,
        "teachers_with_multiple_active_membership_schools": 1,
        "teachers_with_courses_and_no_active_memberships": 1,
        "enrollments_with_inconsistent_or_missing_year_links": 1,
        "authorized_guardian_links_missing_membership_for_an_enrollment_school": 2,
        "authorized_guardian_links_without_enrollments": 1,
        "duplicate_attendance_student_date_groups": 0,
    }
    assert report["post_migration_review"] is None
    assert _digest(path) == before
    assert {p.name for p in tmp_path.iterdir()} == before_files


def test_owned_snapshot_reports_unassigned_mismatches_and_downgrade_conflicts(tmp_path):
    path = tmp_path / "synthetic-owned.db"
    _fixture(path, owned=True)
    before = _digest(path)

    result = _run(path)

    assert result.returncode == 0, result.stderr
    _assert_no_identifiers(result, path)
    report = json.loads(result.stdout)
    assert report["schema"] == "owned"
    assert report["ownership"] == {
        "current_unassigned": {"students": 1, "teachers": 1, "attendance": 0, "behavior": 1, "quran": 1},
        "projected_unassigned_after_upgrade": None,
    }
    assert report["review"]["duplicate_attendance_student_date_groups"] == 1
    assert report["post_migration_review"] == {
        "enrollments_with_school_mismatch": 1,
        "sections_with_teacher_school_mismatch": 1,
        "authorized_guardian_links_without_student_owner_membership": 1,
        "record_owner_student_mismatches": {"attendance": 2, "behavior": 1, "quran": 0},
        "records_with_school_but_unassigned_student": {"attendance": 1, "behavior": 0, "quran": 0},
    }
    assert report["integrity"]["foreign_key_violation_count"] == 0
    assert _digest(path) == before


@pytest.mark.parametrize("sidecar", ["-wal", "-shm", "-journal"])
def test_sidecars_even_if_empty_are_rejected_without_writes(tmp_path, sidecar):
    path = tmp_path / "private-synthetic.db"
    _fixture(path)
    companion = Path(str(path) + sidecar)
    companion.write_bytes(b"")
    before = _digest(path)

    result = _run(path)

    assert result.returncode != 0
    assert result.stdout == ""
    assert "sidecar" in result.stderr.lower()
    _assert_no_identifiers(result, path)
    assert _digest(path) == before
    assert companion.read_bytes() == b""


def test_live_wal_is_rejected_even_when_it_contains_committed_changes(tmp_path):
    path = tmp_path / "synthetic-live.db"
    _fixture(path)
    with sqlite3.connect(path) as writer:
        assert writer.execute("PRAGMA journal_mode=WAL").fetchone()[0] == "wal"
        writer.execute("PRAGMA wal_autocheckpoint=0")
        writer.execute("INSERT INTO Students VALUES (4, 104, 'synthetic WAL row')")
        writer.commit()
        assert Path(str(path) + "-wal").stat().st_size > 0
        result = _run(path)
        assert result.returncode != 0
        assert result.stdout == ""
        assert "sidecar" in result.stderr.lower()
        _assert_no_identifiers(result, path)
        assert writer.execute("SELECT COUNT(*) FROM Students").fetchone()[0] == 4


@pytest.mark.parametrize("variant", ["partial-columns", "columns-only", "history-only", "unknown-migration", "missing-history", "table-is-view"])
def test_unknown_or_partial_schema_fails_closed_without_a_report(tmp_path, variant):
    path = tmp_path / "private-synthetic.db"
    _fixture(path)
    with sqlite3.connect(path) as db:
        if variant == "partial-columns":
            db.execute("ALTER TABLE Students ADD COLUMN OrganizationId INTEGER")
        elif variant == "columns-only":
            for table in ("Students", "Teachers", "AttendanceRecords", "BehaviorRecords", "QuranRecords"):
                db.execute(f'ALTER TABLE "{table}" ADD COLUMN OrganizationId INTEGER')
        elif variant == "history-only":
            db.execute("INSERT INTO __EFMigrationsHistory VALUES (?, '10.0.11')", (OWNERSHIP_MIGRATION,))
        elif variant == "unknown-migration":
            db.execute("INSERT INTO __EFMigrationsHistory VALUES ('20990101000000_Unknown', '10.0.11')")
        elif variant == "missing-history":
            db.execute("DROP TABLE __EFMigrationsHistory")
        else:
            db.executescript("DROP TABLE Courses; CREATE VIEW Courses AS SELECT 1 AS CourseId, 1 AS TeacherId;")
    before = _digest(path)

    result = _run(path)

    assert result.returncode != 0
    assert result.stdout == ""
    assert "unsupported or incomplete schema" in result.stderr.lower()
    _assert_no_identifiers(result, path)
    assert _digest(path) == before


def test_foreign_key_violations_are_counted_without_emitting_row_identifiers(tmp_path):
    path = tmp_path / "private-synthetic.db"
    _fixture(path)
    with sqlite3.connect(path) as db:
        db.execute("PRAGMA foreign_keys=OFF")
        db.execute("INSERT INTO AttendanceRecords VALUES (987654321, 123456789, '2026-10-01')")
    result = _run(path)
    assert result.returncode == 0, result.stderr
    report = json.loads(result.stdout)
    assert report["integrity"]["foreign_key_violation_count"] == 1
    assert "987654321" not in result.stdout + result.stderr
    assert "123456789" not in result.stdout + result.stderr
    _assert_no_identifiers(result, path)


@pytest.mark.parametrize("kind", ["missing", "corrupt", "directory"])
def test_invalid_input_never_creates_a_database_or_prints_its_path(tmp_path, kind):
    path = tmp_path / "SYNTHETIC_PRIVATE_MARKER.db"
    if kind == "corrupt":
        path.write_text("not a sqlite database", encoding="utf-8")
    elif kind == "directory":
        path.mkdir()
    result = _run(path)
    assert result.returncode != 0
    assert result.stdout == ""
    if kind == "corrupt":
        assert "cannot read or validate" in result.stderr.lower()
    else:
        assert "existing regular file" in result.stderr.lower()
    _assert_no_identifiers(result, path)
    if kind == "missing":
        assert not path.exists()


def test_filename_with_uri_metacharacters_is_opened_as_a_file(tmp_path):
    path = tmp_path / "synthetic ? # % database.db"
    _fixture(path)
    before = _digest(path)
    result = _run(path)
    assert result.returncode == 0, result.stderr
    assert json.loads(result.stdout)["schema"] == "legacy"
    _assert_no_identifiers(result, path)
    assert _digest(path) == before


def test_changed_snapshot_is_rejected_before_emitting_the_report(tmp_path, monkeypatch, capsys):
    path = tmp_path / "synthetic.db"
    _fixture(path)
    tool = _load_tool()
    actual = tool._identity(path)
    identities = iter((actual, (*actual[:-1], actual[-1] + 1)))
    monkeypatch.setattr(tool, "_identity", lambda _: next(identities))

    assert tool.main([str(path)]) != 0
    output = capsys.readouterr()
    assert output.out == ""
    assert "changed during inspection" in output.err.lower()
    assert str(path) not in output.err


def test_read_only_connection_rejects_even_an_accidental_write(tmp_path, monkeypatch, capsys):
    path = tmp_path / "synthetic.db"
    _fixture(path)
    before = _digest(path)
    tool = _load_tool()
    monkeypatch.setitem(
        tool.REVIEW_QUERIES,
        "students_without_enrollments",
        "UPDATE Students SET FirstName = 'incorrect' RETURNING StudentId",
    )

    assert tool.main([str(path)]) != 0
    output = capsys.readouterr()
    assert output.out == ""
    assert "cannot read or validate" in output.err.lower()
    assert str(path) not in output.err
    assert _digest(path) == before


def test_symlink_is_rejected_to_avoid_sidecar_alias_ambiguity(tmp_path):
    path = tmp_path / "synthetic.db"
    _fixture(path)
    alias = tmp_path / "alias.db"
    try:
        os.symlink(path, alias)
    except (OSError, NotImplementedError):
        pytest.skip("symlinks are unavailable on this platform")
    result = _run(alias)
    assert result.returncode != 0
    assert result.stdout == ""
    assert "existing regular file" in result.stderr.lower()
    _assert_no_identifiers(result, alias)
