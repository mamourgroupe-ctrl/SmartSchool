"""Read-only duplicate-username report for a SmartSchool SQLite database (P0-B pre-flight).

Why this exists
---------------
Before a unique index (or a normalized unique index) can be applied to a real database, the
existing data must be proven clean. This tool answers that question without modifying anything:

* the database is opened with SQLite's ``mode=ro`` URI and the connection additionally runs
  ``PRAGMA query_only = 1``, so INSERT/UPDATE/DELETE/DDL cannot be executed even by accident;
* the SHA-256 of the file is printed before and after the scan, and a mismatch fails the run;
* no transaction is opened, no row is written, and no password hash is ever read or printed.

Coverage
--------
The report scans the ``Users`` table and then *checks* that it is the only place a username can
live: every table's columns are inspected and any column whose name looks like a username is
listed. If a second username column exists, the report says so loudly instead of assuming
completeness. Completeness signals (integrity check, foreign-key check, migration history) are
reported so a copy can be trusted before it is used as evidence.

Usage
-----
    python find_duplicate_usernames.py school_system.db
    python find_duplicate_usernames.py "/path/to/copy of school_system.db" --verify
    python find_duplicate_usernames.py copy.db --json --mask

Exit codes
----------
    0  no conflicts found (and, with --verify, no integrity problems)
    2  conflicts found (details printed)
    1  usage, database or verification error

Always run this against a copy of the production file, never against the live file.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sqlite3
import sys
from collections import defaultdict
from pathlib import Path

CONFLICT_KINDS = ("exact", "case", "whitespace")
USERNAME_COLUMN_PATTERN = re.compile(r"^(user_?name|login|loginname|account)$", re.IGNORECASE)


def normalize_case(username: str) -> str:
    """Case-only normalization, matching LoginAttemptThrottle.Normalize (without trimming)."""
    return username.strip().lower()


def normalize_full(username: str) -> str:
    """Case + surrounding-whitespace normalization (the key of the proposed unique index).

    This mirrors UsernameRules.ComparisonKey: Trim() then ToLowerInvariant().
    """
    return username.strip().lower()


def normalize_whitespace(username: str) -> str:
    """Whitespace-only normalization (case preserved), used to isolate whitespace-only collisions."""
    return username.strip()


def mask(username: str) -> str:
    visible = username[-2:] if len(username) > 3 else ""
    return "*" * max(len(username) - len(visible), 1) + visible


def file_state(path: Path) -> dict:
    """Size and modification time, used together with the hash to prove the file was quiescent."""
    stat = path.stat()
    return {"size": stat.st_size, "mtime": int(stat.st_mtime)}


def copy_environment(path: Path) -> dict:
    """Signals about HOW the copy was produced (read-only: it only looks at sibling files).

    A copy taken while the source was being written can be torn, and SQLite keeps its in-flight
    state in ``-journal`` (rollback mode) or ``-wal``/``-shm`` (WAL mode) siblings. Their presence
    next to a copy means the copy may need them to be consistent. Their absence, plus a matching
    hash and an ``integrity_check`` of ``ok``, is strong evidence but *not* a proof: only the copy
    mechanism itself (sqlite3 .backup, VACUUM INTO, or a copy taken with the service stopped)
    guarantees a consistent snapshot, which is why this is reported rather than assumed.
    """
    siblings = {
        suffix: (path.parent / f"{path.name}{suffix}").exists()
        for suffix in ("-wal", "-shm", "-journal")
    }
    return {
        "journalSiblings": [suffix for suffix, present in siblings.items() if present],
        "siblingCount": sum(siblings.values()),
        "note": "no journal sibling found: the copy can be read on its own"
        if not any(siblings.values())
        else "journal siblings present: confirm the copy mechanism produced a consistent snapshot",
    }


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def connect_read_only(path: Path) -> sqlite3.Connection:
    if not path.exists():
        raise SystemExit(f"error: database not found: {path}")
    # mode=ro guarantees the file cannot be written; query_only makes it a connection-level
    # guarantee as well, so even a future edit that adds a statement cannot modify anything.
    connection = sqlite3.connect(f"file:{path}?mode=ro", uri=True)
    connection.row_factory = sqlite3.Row
    connection.execute("PRAGMA query_only = 1")
    return connection


def table_exists(connection: sqlite3.Connection, name: str) -> bool:
    row = connection.execute(
        "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = ?", (name,)
    ).fetchone()
    return row is not None


def collect(connection: sqlite3.Connection) -> list[dict]:
    rows = connection.execute(
        "SELECT UserId, Username, Role FROM Users ORDER BY UserId"
    ).fetchall()
    return [{"userId": row["UserId"], "username": row["Username"], "role": row["Role"]} for row in rows]


def group_by(users: list[dict], key) -> list[dict]:
    groups: dict[str, list[dict]] = defaultdict(list)
    for user in users:
        groups[key(user["username"])].append(user)
    return [
        {"key": group_key, "count": len(members), "members": members}
        for group_key, members in sorted(groups.items())
        if len(members) > 1
    ]


def distinct_raw_groups(users: list[dict], key) -> list[dict]:
    """Groups where a normalization key merges two or more genuinely different usernames."""
    return [
        group
        for group in group_by(users, key)
        if len({member["username"] for member in group["members"]}) > 1
    ]


def schema_report(connection: sqlite3.Connection) -> dict:
    """Completeness and coverage signals, all read-only."""
    tables = [
        row["name"]
        for row in connection.execute(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name"
        ).fetchall()
    ]

    table_counts = {}
    for table in tables:
        table_counts[table] = connection.execute(f'SELECT COUNT(*) FROM "{table}"').fetchone()[0]

    username_columns: dict[str, list[str]] = {}
    user_id_columns: list[str] = []
    columns_by_table: dict[str, list[str]] = {}
    for table in tables:
        columns = [row["name"] for row in connection.execute(f'PRAGMA table_info("{table}")').fetchall()]
        columns_by_table[table] = columns
        matching = [column for column in columns if USERNAME_COLUMN_PATTERN.match(column)]
        if matching:
            username_columns[table] = matching
        if "UserId" in columns:
            user_id_columns.append(table)

    users_table_sql = None
    if "Users" in tables:
        row = connection.execute(
            "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'Users'"
        ).fetchone()
        users_table_sql = row["sql"] if row else None

    unique_indexes: list[dict] = []
    if "Users" in tables:
        for index in connection.execute('PRAGMA index_list("Users")').fetchall():
            index_columns = [
                info["name"] for info in connection.execute(f'PRAGMA index_info("{index["name"]}")').fetchall()
            ]
            if index["unique"]:
                unique_indexes.append(
                    {"name": index["name"], "columns": index_columns, "origin": index["origin"]}
                )

    migrations: dict | None = None
    if "__EFMigrationsHistory" in tables:
        applied = [
            row["MigrationId"]
            for row in connection.execute("SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId").fetchall()
        ]
        migrations = {"appliedCount": len(applied), "lastApplied": applied[-1] if applied else None}

    collations = [row[0] for row in connection.execute("PRAGMA collation_list").fetchall()]
    integrity = connection.execute("PRAGMA integrity_check").fetchone()[0]
    foreign_key_violations = len(connection.execute("PRAGMA foreign_key_check").fetchall())

    return {
        "integrityCheck": integrity,
        "foreignKeyViolations": foreign_key_violations,
        "journalMode": connection.execute("PRAGMA journal_mode").fetchone()[0],
        "userVersion": connection.execute("PRAGMA user_version").fetchone()[0],
        "tables": tables,
        "tableCounts": table_counts,
        "tablesWithUserId": user_id_columns,
        "usernameColumns": username_columns,
        "usernameCoverageComplete": list(username_columns.keys()) == ["Users"],
        "usersTableSql": users_table_sql,
        "uniqueIndexesOnUsers": unique_indexes,
        "migrations": migrations,
        "collations": collations[:8],
        "encodings": connection.execute("PRAGMA encoding").fetchone()[0],
    }


def build_report(users: list[dict], database: str, hide_names: bool, schema: dict, fingerprint: dict, copy_state: dict) -> dict:
    """Classify conflicts by the index they would block. Buckets are mutually informative:
    a single conflict can appear under several buckets because several index shapes are affected."""
    exact = group_by(users, lambda name: name)
    case_groups = distinct_raw_groups(users, normalize_case)
    whitespace_groups = distinct_raw_groups(users, normalize_whitespace)
    normalized_groups = distinct_raw_groups(users, normalize_full)

    def render(groups: list[dict]) -> list[dict]:
        rendered = []
        for group in groups:
            rendered.append(
                {
                    "normalized": group["key"] if not hide_names else mask(group["key"]),
                    "count": group["count"],
                    "accounts": [
                        {
                            "userId": member["userId"],
                            "username": member["username"] if not hide_names else mask(member["username"]),
                            "role": member["role"],
                        }
                        for member in group["members"]
                    ],
                }
            )
        return rendered

    padded = [
        {"userId": user["userId"], "username": user["username"] if not hide_names else mask(user["username"]), "role": user["role"]}
        for user in users
        if user["username"] != user["username"].strip()
    ]

    report = {
        "database": database,
        "fingerprint": fingerprint,
        "copyEnvironment": copy_state,
        "totalUsers": len(users),
        "exactDuplicates": render(exact),
        "caseInsensitiveCollisions": render(case_groups),
        "whitespaceCollisions": render(whitespace_groups),
        "normalizationConflicts": render(normalized_groups),
        "paddedUsernames": padded,
        "schema": schema,
    }
    # The normalized index can only be created when no two accounts collapse onto the same key.
    affected = {account["userId"] for account in report["normalizationConflicts"] for account in account["accounts"]}
    affected.update(account["userId"] for account in padded)
    report["conflictCount"] = len(report["normalizationConflicts"])
    report["affectedAccounts"] = len(affected)
    report["safeToAddExactUniqueIndex"] = len(report["exactDuplicates"]) == 0
    report["safeToAddNormalizedUniqueIndex"] = len(report["normalizationConflicts"]) == 0 and len(padded) == 0
    return report


def print_human(report: dict) -> None:
    fingerprint = report["fingerprint"]
    schema = report["schema"]
    print(f"database: {report['database']}")
    print(f"sha256 before: {fingerprint['before']}")
    print(f"sha256 after : {fingerprint['after']}")
    print(f"file unchanged by the scan: {fingerprint['unchanged']} | quiescent (size+mtime+hash): {fingerprint['quiescent']}")
    print(f"size before/after: {fingerprint['sizeBefore']}/{fingerprint['sizeAfter']} | mtime before/after: {fingerprint['mtimeBefore']}/{fingerprint['mtimeAfter']}")
    print(f"journal siblings next to the copy: {report['copyEnvironment']['journalSiblings'] or 'none'}")
    print(f"integrity_check: {schema['integrityCheck']} | foreign_key_check violations: {schema['foreignKeyViolations']}")
    print(f"journal_mode: {schema['journalMode']} | encoding: {schema['encodings']}")
    print(f"tables: {len(schema['tables'])} | migrations: {schema['migrations']}")
    print(f"username columns found in: {schema['usernameColumns']}")
    print(f"username coverage complete (only Users): {schema['usernameCoverageComplete']}")
    print(f"unique indexes on Users: {schema['uniqueIndexesOnUsers']}")
    print(f"users: {report['totalUsers']}")
    for kind, label in (
        ("exactDuplicates", "exact duplicates (break login: SingleOrDefaultAsync)"),
        ("caseInsensitiveCollisions", "case-insensitive collisions (differing case only)"),
        ("whitespaceCollisions", "whitespace collisions (differing surrounding spaces only)"),
        ("normalizationConflicts", "conflicts under the unified rule (trim + case fold)"),
    ):
        groups = report[kind]
        print(f"{label}: {len(groups)}")
        for group in groups:
            accounts = ", ".join(
                f"#{account['userId']}:{account['username']}({account['role']})" for account in group["accounts"]
            )
            print(f"  - {group['normalized']} x{group['count']} -> {accounts}")
    print(f"padded usernames (stored with surrounding spaces): {len(report['paddedUsernames'])}")
    for account in report["paddedUsernames"]:
        print(f"  - #{account['userId']}:{account['username']!r}({account['role']})")
    print(f"accounts affected by a conflict: {report['affectedAccounts']}")
    print(f"safe to add UNIQUE(Username): {report['safeToAddExactUniqueIndex']}")
    print(f"safe to add normalized unique index: {report['safeToAddNormalizedUniqueIndex']}")
    if report["conflictCount"]:
        print("action: resolve the listed conflicts manually; this tool never renames or deletes accounts.")


def verify_only(report: dict) -> int:
    schema = report["schema"]
    problems = []
    if schema["integrityCheck"] != "ok":
        problems.append(f"integrity_check: {schema['integrityCheck']}")
    if schema["foreignKeyViolations"]:
        problems.append(f"foreign_key_check: {schema['foreignKeyViolations']} violations")
    if not schema["usernameCoverageComplete"]:
        problems.append(f"username columns outside Users: {schema['usernameColumns']}")
    if schema["migrations"] is None:
        problems.append("__EFMigrationsHistory is missing (is this really an application database?)")
    if not report["fingerprint"]["unchanged"]:
        problems.append("the file changed during the scan")
    if report["copyEnvironment"]["siblingCount"]:
        problems.append(
            "journal siblings next to the copy "
            f"({report['copyEnvironment']['journalSiblings']}): confirm the copy mechanism produced a consistent snapshot"
        )

    print("verification: " + ("OK" if not problems else "PROBLEMS FOUND"))
    for problem in problems:
        print(f"  - {problem}")
    return 0 if not problems else 1


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Read-only duplicate username report (P0-B pre-flight).")
    parser.add_argument("database", help="path to a SQLite database file (use a copy, never the live file)")
    parser.add_argument("--json", action="store_true", help="emit machine-readable JSON")
    parser.add_argument("--mask", action="store_true", help="mask usernames in the output")
    parser.add_argument("--verify", action="store_true", help="only check integrity, coverage and migration history")
    args = parser.parse_args(argv)

    path = Path(args.database).resolve()
    if not path.exists():
        print(f"error: database not found: {path}", file=sys.stderr)
        return 1
    if not path.is_file():
        print(f"error: not a regular file: {path}", file=sys.stderr)
        return 1
    try:
        before = sha256_of(path)
    except OSError as error:
        print(f"error: cannot read {path}: {error}", file=sys.stderr)
        return 1

    connection = connect_read_only(path)
    try:
        if not table_exists(connection, "Users"):
            print("error: the database has no Users table (has it been migrated?)", file=sys.stderr)
            return 1
        schema = schema_report(connection)
        users = collect(connection)
    finally:
        connection.close()

    after = sha256_of(path)
    state_before, state_after = file_state(path), file_state(path)
    fingerprint = {
        "algorithm": "sha256",
        "before": before,
        "after": after,
        "unchanged": before == after,
        "sizeBefore": state_before["size"],
        "sizeAfter": state_after["size"],
        "mtimeBefore": state_before["mtime"],
        "mtimeAfter": state_after["mtime"],
        "quiescent": before == after and state_before == state_after,
    }
    if not fingerprint["unchanged"]:
        print("error: the database file changed during a supposedly read-only scan", file=sys.stderr)
        return 1

    report = build_report(users, str(path), args.mask, schema, fingerprint, copy_environment(path))
    if args.verify:
        return verify_only(report)
    if args.json:
        print(json.dumps(report, ensure_ascii=False, indent=2))
    else:
        print_human(report)
    return 2 if report["conflictCount"] or report["paddedUsernames"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
