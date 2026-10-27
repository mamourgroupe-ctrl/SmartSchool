-- Synthetic-only fixture for a temporary database created by EF Core in CI.
-- No real student, school, credential, or token data is used.
INSERT INTO Users (UserId, Username, PasswordHash, Role) VALUES
    (1, 'example-student-a', 'fixture-hash', 'STUDENT'),
    (2, 'example-student-b', 'fixture-hash', 'STUDENT'),
    (3, 'example-student-c', 'fixture-hash', 'STUDENT'),
    (4, 'example-teacher-a', 'fixture-hash', 'TEACHER'),
    (5, 'example-teacher-b', 'fixture-hash', 'TEACHER'),
    (6, 'example-guardian', 'fixture-hash', 'PARENT');
INSERT INTO Organizations (OrganizationId, Name, IsActive, CreatedAtUtc) VALUES
    (1, 'Example A', 1, '2026-01-01 00:00:00'), (2, 'Example B', 1, '2026-01-01 00:00:00');
INSERT INTO Students (StudentId, FirstName, LastName, UserId) VALUES
    (1, 'Example', 'A', 1), (2, 'Example', 'B', 2), (3, 'Example', 'C', 3);
INSERT INTO Teachers (TeacherId, FirstName, LastName, SubjectSpecialty, UserId) VALUES
    (1, 'Example', 'Teacher A', 'Example', 4), (2, 'Example', 'Teacher B', 'Example', 5);
INSERT INTO Courses (CourseId, CourseName, TeacherId) VALUES (1, 'Example A', 1), (2, 'Example B', 2);
INSERT INTO OrganizationMemberships (OrganizationId, UserId, IsActive, CreatedAtUtc) VALUES
    (1, 4, 1, '2026-01-01 00:00:00'), (2, 5, 1, '2026-01-01 00:00:00'),
    (1, 6, 1, '2026-01-01 00:00:00');
INSERT INTO AcademicYears (AcademicYearId, OrganizationId, Name, StartDate, EndDate, IsActive) VALUES
    (1, 1, 'Example year A', '2026-09-01', '2027-06-30', 1),
    (2, 2, 'Example year B', '2026-09-01', '2027-06-30', 1);
INSERT INTO Terms (TermId, AcademicYearId, Name, StartDate, EndDate) VALUES
    (1, 1, 'Term A', '2026-09-01', '2026-12-31'), (2, 2, 'Term B', '2026-09-01', '2026-12-31');
INSERT INTO SchoolClasses (SchoolClassId, AcademicYearId, Name, GradeLevel) VALUES
    (1, 1, 'Class A', 'Example'), (2, 2, 'Class B', 'Example');
INSERT INTO Sections (SectionId, SchoolClassId, TeacherId, Name) VALUES
    (1, 1, 1, 'Section A'), (2, 2, 2, 'Section B');
INSERT INTO Enrollments (EnrollmentId, StudentId, AcademicYearId, TermId, SectionId, Status, StartDate) VALUES
    (1, 1, 1, 1, 1, 'ACTIVE', '2026-09-01'), (2, 2, 2, 2, 2, 'ACTIVE', '2026-09-01'),
    (3, 2, 1, 1, 1, 'ACTIVE', '2026-09-01');
INSERT INTO Parents (ParentId, UserId, FirstName, LastName) VALUES (1, 6, 'Example', 'Guardian');
INSERT INTO StudentParents (StudentParentId, StudentId, ParentId, Relationship, IsAuthorized) VALUES
    (1, 1, 1, 'Example', 1), (2, 2, 1, 'Example', 1);
INSERT INTO AttendanceRecords (AttendanceRecordId, StudentId, Date, Status, Notes, RecordedByUserId, CreatedAtUtc) VALUES
    (1, 1, '2026-09-01', 'PRESENT', 'Example A', 4, '2026-09-01 00:00:00'),
    (2, 2, '2026-09-01', 'ABSENT', 'Example B', 5, '2026-09-01 00:00:00');
INSERT INTO BehaviorRecords (BehaviorRecordId, StudentId, Type, Category, Description, Visibility, Points, RecordedByUserId, Date, CreatedAtUtc) VALUES
    (1, 1, 'POSITIVE', 'Example', 'Example A', 'PARENT_VISIBLE', 1, 4, '2026-09-01', '2026-09-01 00:00:00'),
    (2, 2, 'INCIDENT', 'Example', 'Example B', 'INTERNAL', -1, 5, '2026-09-01', '2026-09-01 00:00:00');
INSERT INTO QuranRecords (QuranRecordId, StudentId, SurahName, FromAyah, ToAyah, MemorizationType, Rating, TajweedErrorsCount, Date, RecordedByUserId, CreatedAtUtc) VALUES
    (1, 1, 'Example A', 1, 2, 'NEW', 8, 0, '2026-09-01', 4, '2026-09-01 00:00:00'),
    (2, 2, 'Example B', 1, 2, 'REVIEW', 7, 1, '2026-09-01', 5, '2026-09-01 00:00:00');
