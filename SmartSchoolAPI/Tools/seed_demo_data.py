"""Idempotently add demo teachers and courses to SmartSchool's local SQLite database."""

from __future__ import annotations

import json
import shutil
import sqlite3
import sys
from datetime import datetime
from pathlib import Path


TEACHERS = [
    ("sara.hassan.demo", "DemoTeacher123!", "Teacher", "سارة", "حسن", "الرياضيات"),
    ("omar.nasser.demo", "DemoTeacher123!", "Teacher", "عمر", "ناصر", "الفيزياء"),
    ("layan.ahmad.demo", "DemoTeacher123!", "Teacher", "ليان", "أحمد", "اللغة العربية"),
    ("yousef.saleh.demo", "DemoTeacher123!", "Teacher", "يوسف", "صالح", "الحاسوب"),
    ("noor.ali.demo", "DemoTeacher123!", "Teacher", "نور", "علي", "اللغة الإنجليزية"),
    ("khaled.mahdi.demo", "DemoTeacher123!", "Teacher", "خالد", "مهدي", "الكيمياء"),
]

COURSES = [
    ("الجبر المتقدم", "sara.hassan.demo"),
    ("الهندسة التحليلية", "sara.hassan.demo"),
    ("الفيزياء العامة", "omar.nasser.demo"),
    ("الطاقة والحركة", "omar.nasser.demo"),
    ("النحو والصرف", "layan.ahmad.demo"),
    ("الأدب العربي", "layan.ahmad.demo"),
    ("أساسيات البرمجة", "yousef.saleh.demo"),
    ("قواعد البيانات", "yousef.saleh.demo"),
    ("اللغة الإنجليزية الأكاديمية", "noor.ali.demo"),
    ("مهارات المحادثة", "noor.ali.demo"),
    ("الكيمياء العضوية", "khaled.mahdi.demo"),
    ("التجارب المخبرية", "khaled.mahdi.demo"),
]


def table_names(connection: sqlite3.Connection) -> set[str]:
    return {row[0] for row in connection.execute("SELECT name FROM sqlite_master WHERE type='table'")}


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("Usage: seed_demo_data.py <path-to-school_system.db>")

    database = Path(sys.argv[1]).resolve()
    if not database.is_file():
        raise SystemExit(f"Database not found: {database}")

    timestamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    backup = database.with_name(f"{database.stem}.before-demo-seed-{timestamp}{database.suffix}.bak")
    shutil.copy2(database, backup)

    connection = sqlite3.connect(database)
    connection.execute("PRAGMA foreign_keys = ON")
    required_tables = {"Users", "Teachers", "Courses"}
    missing = required_tables - table_names(connection)
    if missing:
        raise RuntimeError(f"Missing required tables: {', '.join(sorted(missing))}")

    inserted_users = 0
    inserted_teachers = 0
    inserted_courses = 0
    try:
        with connection:
            for username, password, role, first_name, last_name, specialty in TEACHERS:
                user = connection.execute(
                    "SELECT UserId FROM Users WHERE Username = ?", (username,)
                ).fetchone()
                if user is None:
                    cursor = connection.execute(
                        "INSERT INTO Users (Username, PasswordHash, Role) VALUES (?, ?, ?)",
                        (username, password, role),
                    )
                    user_id = cursor.lastrowid
                    inserted_users += 1
                else:
                    user_id = user[0]

                teacher = connection.execute(
                    "SELECT TeacherId FROM Teachers WHERE UserId = ?", (user_id,)
                ).fetchone()
                if teacher is None:
                    connection.execute(
                        "INSERT INTO Teachers (FirstName, LastName, SubjectSpecialty, UserId) VALUES (?, ?, ?, ?)",
                        (first_name, last_name, specialty, user_id),
                    )
                    inserted_teachers += 1

            for course_name, teacher_username in COURSES:
                teacher = connection.execute(
                    """
                    SELECT t.TeacherId
                    FROM Teachers t
                    INNER JOIN Users u ON u.UserId = t.UserId
                    WHERE u.Username = ?
                    """,
                    (teacher_username,),
                ).fetchone()
                if teacher is None:
                    raise RuntimeError(f"Teacher record was not created for {teacher_username}")

                exists = connection.execute(
                    "SELECT CourseId FROM Courses WHERE CourseName = ? AND TeacherId = ?",
                    (course_name, teacher[0]),
                ).fetchone()
                if exists is None:
                    connection.execute(
                        "INSERT INTO Courses (CourseName, TeacherId) VALUES (?, ?)",
                        (course_name, teacher[0]),
                    )
                    inserted_courses += 1

        counts = {
            table: connection.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0]
            for table in ("Users", "Teachers", "Courses")
        }
        joined_courses = connection.execute(
            """
            SELECT c.CourseName, t.FirstName || ' ' || t.LastName AS TeacherName, t.SubjectSpecialty
            FROM Courses c
            INNER JOIN Teachers t ON t.TeacherId = c.TeacherId
            ORDER BY t.LastName, c.CourseName
            """
        ).fetchall()
        print(
            json.dumps(
                {
                    "backup": str(backup),
                    "inserted": {
                        "users": inserted_users,
                        "teachers": inserted_teachers,
                        "courses": inserted_courses,
                    },
                    "counts": counts,
                    "joinedCourseRows": len(joined_courses),
                    "integrity": connection.execute("PRAGMA integrity_check").fetchone()[0],
                },
                ensure_ascii=False,
                indent=2,
            )
        )
    finally:
        connection.close()


if __name__ == "__main__":
    main()
