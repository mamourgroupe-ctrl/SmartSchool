"""Read-only duplicate-username report for a SmartSchool SQLite database (P0-B pre-flight).

Why this exists
---------------
Before a unique index (or a normalized unique index) can be applied to a real database, the
existing data must be proven clean. This tool answers that question without modifying anything:
the database is opened with SQLite's ``mode=ro`` URI, no transaction is used, and no row is
written. It never reads or prints password hashes.

Usage
-----
    python find_duplicate_usernames.py school_system.db
    python find_duplicate_usernames.py school_system.db --json
    python find_duplicate_usernames.py "/path/to/copy of school_system.db" --mask

Exit codes
----------
    0  no conflicts found
    2  conflicts found (details printed)
    1  usage or database error

Always run this against a copy of the production file.
"""

from __future__ import annotations

import argparse
import json
import sqlite3
import sys
from collections import defaultdict
from pathlib import Path

CONFLICT_KINDS = ("exact", "case", "whitespace")


def normalize_case(username: str) -> str:
    """Case-only normalization, matching LoginAttemptThrottle.Normalize (without trimming)."""
    return username.strip().lower()


def normalize_full(username: str) -> str:
    """Case + surrounding-whitespace normalization (the proposed key for a normalized unique index)."""
    return username.strip().lower()


def normalize_whitespace(username: str) -> str:
    """Whitespace-only normalization (case preserved), used to isolate whitespace-only collisions."""
    return username.strip()


def mask(username: str) -> str:
    visible = username[-2:] if len(username) > 3 else ""
    return "*" * max(len(username) - len(visible), 1) + visible


def connect_read_only(path: Path) -> sqlite3.Connection:
    if not path.exists():
        raise SystemExit(f"error: database not found: {path}")
    connection = sqlite3.connect(f"file:{path}?mode=ro", uri=True)
    connection.row_factory = sqlite3.Row
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


def build_report(users: list[dict], database: str, hide_names: bool) -> dict:
    """Classify conflicts by the index they would block. Buckets are mutually informative:
    a single conflict can appear under several buckets because several index shapes are affected."""
    exact = group_by(users, lambda name: name)
    case_groups = distinct_raw_groups(users, normalize_case)
    whitespace_groups = distinct_raw_groups(users, normalize_whitespace)

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
        "totalUsers": len(users),
        "exactDuplicates": render(exact),
        "caseInsensitiveCollisions": render(case_groups),
        "whitespaceCollisions": render(whitespace_groups),
        "paddedUsernames": padded,
    }
    # The normalized index can only be created when no two accounts collapse onto the same key.
    report["conflictCount"] = len(report["exactDuplicates"]) + len(report["whitespaceCollisions"])
    report["safeToAddExactUniqueIndex"] = len(report["exactDuplicates"]) == 0
    report["safeToAddNormalizedUniqueIndex"] = (
        len(report["caseInsensitiveCollisions"]) == 0 and len(report["whitespaceCollisions"]) == 0
    )
    return report


def print_human(report: dict) -> None:
    print(f"database: {report['database']}")
    print(f"users: {report['totalUsers']}")
    for kind, label in (
        ("exactDuplicates", "exact duplicates (break login: SingleOrDefaultAsync)"),
        ("caseInsensitiveCollisions", "case-insensitive collisions (differing case only)"),
        ("whitespaceCollisions", "whitespace collisions (differing surrounding spaces only)"),
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
    print(f"safe to add UNIQUE(Username): {report['safeToAddExactUniqueIndex']}")
    print(f"safe to add normalized unique index: {report['safeToAddNormalizedUniqueIndex']}")
    if report["conflictCount"]:
        print("action: resolve the listed conflicts manually; this tool never renames or deletes accounts.")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Read-only duplicate username report (P0-B pre-flight).")
    parser.add_argument("database", help="path to a SQLite database file (use a copy, never the live file)")
    parser.add_argument("--json", action="store_true", help="emit machine-readable JSON")
    parser.add_argument("--mask", action="store_true", help="mask usernames in the output")
    args = parser.parse_args(argv)

    path = Path(args.database)
    connection = connect_read_only(path)
    try:
        if not table_exists(connection, "Users"):
            print("error: the database has no Users table (has it been migrated?)", file=sys.stderr)
            return 1
        users = collect(connection)
    finally:
        connection.close()

    report = build_report(users, str(path), args.mask)
    if args.json:
        print(json.dumps(report, ensure_ascii=False, indent=2))
    else:
        print_human(report)
    return 2 if report["conflictCount"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
