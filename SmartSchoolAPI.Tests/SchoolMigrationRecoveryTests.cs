using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartSchoolAPI.Authorization;
using SmartSchoolAPI.Data;
using SmartSchoolAPI.Services;

namespace SmartSchoolAPI.Tests;

/// <summary>
/// Rehearsals use only synthetic data and uniquely named temporary SQLite files.
/// They do not connect to or modify an application or production database.
/// </summary>
public sealed class SchoolMigrationRecoveryTests
{
    private const string LegacyMigration = "20261009120000_AddRefreshTokens";
    private const string OwnershipMigration = "20261027162508_AddExplicitSchoolOwnership";

    [Fact]
    public async Task LegacyFile_UpgradeAndBackupRestore_PreserveDataAndQuarantineSchoolAccess()
    {
        using var scratch = new ScratchDirectory();
        await using var source = await OpenAsync(scratch.File("legacy.db"));
        await EnableWalAsync(source);
        await using var db = CreateContext(source);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(LegacyMigration);
        await SeedLegacyAsync(db);

        await AssertHealthyAsync(source);
        Assert.Equal(3, await ScalarAsync(source, "SELECT COUNT(*) FROM Students"));
        Assert.Equal(3, await ScalarAsync(source, "SELECT COUNT(*) FROM Enrollments"));
        Assert.Equal(2, await ScalarAsync(source, "SELECT COUNT(*) FROM StudentParents"));
        Assert.Equal(2, await ScalarAsync(source, "SELECT COUNT(*) FROM Courses"));
        var original = await CaptureLegacyRowsAsync(source);
        Assert.True(new FileInfo(scratch.File("legacy.db-wal")).Length > 0);
        var backupPath = scratch.File("before-upgrade.db");
        await BackupAsync(source, backupPath);

        await migrator.MigrateAsync();

        await AssertHealthyAsync(source);
        Assert.Equal(original, await CaptureLegacyRowsAsync(source));
        Assert.Equal(1, await ScalarAsync(source, $"SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '{OwnershipMigration}'"));
        foreach (var table in new[] { "Students", "Teachers", "AttendanceRecords", "BehaviorRecords", "QuranRecords" })
        {
            Assert.Equal(0, await ScalarAsync(source, $"SELECT COUNT(*) FROM \"{table}\" WHERE OrganizationId IS NOT NULL"));
            Assert.Equal(1, await ScalarAsync(source,
                $"SELECT COUNT(*) FROM pragma_foreign_key_list('{table}') WHERE \"table\" = 'Organizations' AND \"from\" = 'OrganizationId' AND \"to\" = 'OrganizationId'"));
        }
        await AssertLegacySchoolAccessIsQuarantinedAsync(db);

        // Restore into a different empty file, never over the source.
        var restoredPath = scratch.File("restored.db");
        await using (var backup = await OpenAsync(backupPath, readOnly: true))
            await BackupAsync(backup, restoredPath);
        await using var restored = await OpenAsync(restoredPath, readOnly: true);
        await AssertHealthyAsync(restored);
        Assert.Equal(original, await CaptureLegacyRowsAsync(restored));
        Assert.Equal(0, await ScalarAsync(restored, $"SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '{OwnershipMigration}'"));
        Assert.Equal(0, await ScalarAsync(restored, "SELECT COUNT(*) FROM pragma_table_info('Students') WHERE name = 'OrganizationId'"));
    }

    [Fact]
    public async Task DowngradeWithoutNewConflicts_PreservesLegacyData()
    {
        using var scratch = new ScratchDirectory();
        await using var source = await OpenAsync(scratch.File("legacy.db"));
        await EnableWalAsync(source);
        await using var db = CreateContext(source);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(LegacyMigration);
        await SeedLegacyAsync(db);
        var original = await CaptureLegacyRowsAsync(source);

        await migrator.MigrateAsync();
        await migrator.MigrateAsync(LegacyMigration);

        await AssertHealthyAsync(source);
        Assert.Equal(original, await CaptureLegacyRowsAsync(source));
        Assert.Equal(0, await ScalarAsync(source, $"SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '{OwnershipMigration}'"));
        Assert.Equal(0, await ScalarAsync(source, "SELECT COUNT(*) FROM pragma_table_info('Students') WHERE name = 'OrganizationId'"));
    }

    [Fact]
    public async Task DowngradeWithDuplicateAttendance_FailsAndOriginalBackupCanBeRestored()
    {
        using var scratch = new ScratchDirectory();
        await using var source = await OpenAsync(scratch.File("legacy.db"));
        await EnableWalAsync(source);
        await using var db = CreateContext(source);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(LegacyMigration);
        await SeedLegacyAsync(db);
        var original = await CaptureLegacyRowsAsync(source);
        var backupPath = scratch.File("before-upgrade.db");
        await BackupAsync(source, backupPath);
        await migrator.MigrateAsync();

        // Simulate a separately reviewed assignment and a later scoped write
        // directly in SQL. No reconciliation workflow is implemented here.
        // The old unique index cannot represent both rows on the same date.
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE Students SET OrganizationId = 1 WHERE StudentId = 1;
            INSERT INTO AttendanceRecords (StudentId, OrganizationId, Date, Status, Notes, RecordedByUserId, CreatedAtUtc)
            VALUES (1, 1, '2026-09-01', 'PRESENT', 'new scoped example', 4, '2026-09-01 00:00:00');
            """);
        Assert.Equal(3, await ScalarAsync(source, "SELECT COUNT(*) FROM AttendanceRecords"));

        var error = await Assert.ThrowsAsync<SqliteException>(() => migrator.MigrateAsync(LegacyMigration));
        Assert.Equal(19, error.SqliteErrorCode);
        Assert.Contains("UNIQUE constraint failed", error.Message, StringComparison.OrdinalIgnoreCase);

        // The failed downgrade is not assumed atomic. Restore the pre-upgrade
        // backup to a separate file and verify that copy instead.
        var restoredPath = scratch.File("restored-after-failure.db");
        await using (var backup = await OpenAsync(backupPath, readOnly: true))
            await BackupAsync(backup, restoredPath);
        await using var restored = await OpenAsync(restoredPath, readOnly: true);
        await AssertHealthyAsync(restored);
        Assert.Equal(original, await CaptureLegacyRowsAsync(restored));
        Assert.Equal(2, await ScalarAsync(restored, "SELECT COUNT(*) FROM AttendanceRecords"));
        Assert.Equal(0, await ScalarAsync(restored, $"SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '{OwnershipMigration}'"));
    }

    private static async Task AssertLegacySchoolAccessIsQuarantinedAsync(SchoolDbContext db)
    {
        var http = new DefaultHttpContext();
        var scope = new SchoolScope(db, new HttpContextAccessor { HttpContext = http });
        foreach (var (userId, role) in new[]
        {
            (7, RoleNames.SchoolAdmin), (4, RoleNames.Teacher), (6, RoleNames.Parent)
        })
        {
            http.User = Principal(userId, role);
            Assert.Empty(await scope.Students().ToArrayAsync());
            Assert.Empty(await scope.Teachers().ToArrayAsync());
            Assert.Empty(await scope.Courses().ToArrayAsync());
            Assert.Empty(await scope.Enrollments().ToArrayAsync());
            Assert.Empty(await scope.Guardians().ToArrayAsync());
            Assert.False(await scope.CanViewStudentAsync(1));
            Assert.False(await scope.CanViewStudentAsync(2));
            Assert.Empty(await scope.Attendance().ToArrayAsync());
            Assert.Empty(await scope.Behavior().ToArrayAsync());
            Assert.Empty(await scope.Quran().ToArrayAsync());
        }

        // An unassigned student can still see their own basic profile, but not
        // Student 360 or legacy school records.
        http.User = Principal(1, RoleNames.Student);
        Assert.Single(await scope.Students().ToArrayAsync());
        Assert.False(await scope.CanViewStudentAsync(1));
        Assert.Empty(await scope.Quran().ToArrayAsync());

        http.User = Principal(9, RoleNames.SuperAdmin);
        Assert.Equal(3, await scope.Students().CountAsync());
        Assert.Equal(2, await scope.Teachers().CountAsync());
        Assert.Equal(2, await scope.Courses().CountAsync());
        Assert.Equal(3, await scope.Enrollments().CountAsync());
        Assert.Equal(2, await scope.Guardians().CountAsync());
        var overview = await new Student360Service(scope).GetAsync(1, http.User);
        Assert.NotNull(overview);
        Assert.Single(overview!.Attendance);
        Assert.Single(overview.Behavior);
        Assert.Single(overview.Quran);
    }

    private static ClaimsPrincipal Principal(int userId, string role) => new(new ClaimsIdentity(
        new[] { new Claim("UserId", userId.ToString(CultureInfo.InvariantCulture)), new Claim(ClaimTypes.Role, role) },
        authenticationType: "synthetic-rehearsal"));

    private static SchoolDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<SchoolDbContext>().UseSqlite(connection).Options);

    private static async Task<SqliteConnection> OpenAsync(string path, bool readOnly = false)
    {
        var settings = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false
        };
        var connection = new SqliteConnection(settings.ToString());
        await connection.OpenAsync();
        return connection;
    }

    private static async Task EnableWalAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL";
        Assert.Equal("wal", (await command.ExecuteScalarAsync())?.ToString(), ignoreCase: true);
        command.CommandText = "PRAGMA wal_autocheckpoint = 0";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task BackupAsync(SqliteConnection source, string destinationPath)
    {
        await using var destination = await OpenAsync(destinationPath);
        source.BackupDatabase(destination);
    }

    private static async Task AssertHealthyAsync(SqliteConnection connection)
    {
        Assert.Equal(1, await ScalarAsync(connection, "PRAGMA foreign_keys"));
        await using (var integrity = connection.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check";
            await using var results = await integrity.ExecuteReaderAsync();
            Assert.True(await results.ReadAsync());
            Assert.Equal("ok", results.GetString(0));
            Assert.False(await results.ReadAsync());
        }
        await using (var foreignKeys = connection.CreateCommand())
        {
            foreignKeys.CommandText = "PRAGMA foreign_key_check";
            await using var results = await foreignKeys.ExecuteReaderAsync();
            Assert.False(await results.ReadAsync());
        }
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<string[]> CaptureLegacyRowsAsync(SqliteConnection connection)
    {
        // Explicit columns deliberately omit the newly added school ownership
        // fields, so old values and relationships can be compared after upgrade.
        var queries = new[]
        {
            $"SELECT MigrationId, ProductVersion FROM __EFMigrationsHistory WHERE MigrationId <> '{OwnershipMigration}' ORDER BY MigrationId",
            "SELECT UserId, Username, PasswordHash, Role FROM Users ORDER BY UserId",
            "SELECT AuditLogId, UserId, Action, EntityName, EntityId, TimestampUtc FROM AuditLogs ORDER BY AuditLogId",
            "SELECT RefreshTokenId, UserId, TokenHash, ExpiresAtUtc, CreatedAtUtc, RevokedAtUtc FROM RefreshTokens ORDER BY RefreshTokenId",
            "SELECT OrganizationId, Name, IsActive, CreatedAtUtc FROM Organizations ORDER BY OrganizationId",
            "SELECT StudentId, FirstName, LastName, UserId FROM Students ORDER BY StudentId",
            "SELECT TeacherId, FirstName, LastName, SubjectSpecialty, UserId FROM Teachers ORDER BY TeacherId",
            "SELECT CourseId, CourseName, TeacherId FROM Courses ORDER BY CourseId",
            "SELECT OrganizationMembershipId, OrganizationId, UserId, IsActive, CreatedAtUtc FROM OrganizationMemberships ORDER BY OrganizationMembershipId",
            "SELECT AcademicYearId, OrganizationId, Name, StartDate, EndDate, IsActive FROM AcademicYears ORDER BY AcademicYearId",
            "SELECT TermId, AcademicYearId, Name, StartDate, EndDate FROM Terms ORDER BY TermId",
            "SELECT SchoolClassId, AcademicYearId, Name, GradeLevel FROM SchoolClasses ORDER BY SchoolClassId",
            "SELECT SectionId, SchoolClassId, TeacherId, Name FROM Sections ORDER BY SectionId",
            "SELECT EnrollmentId, StudentId, AcademicYearId, TermId, SectionId, Status, StartDate, EndDate FROM Enrollments ORDER BY EnrollmentId",
            "SELECT ParentId, UserId, FirstName, LastName, Phone, Email FROM Parents ORDER BY ParentId",
            "SELECT StudentParentId, StudentId, ParentId, Relationship, IsAuthorized FROM StudentParents ORDER BY StudentParentId",
            "SELECT AttendanceRecordId, StudentId, Date, Status, Notes, RecordedByUserId, CreatedAtUtc FROM AttendanceRecords ORDER BY AttendanceRecordId",
            "SELECT BehaviorRecordId, StudentId, Type, Category, Description, Visibility, Points, RecordedByUserId, Date, CreatedAtUtc FROM BehaviorRecords ORDER BY BehaviorRecordId",
            "SELECT QuranRecordId, StudentId, SurahName, FromAyah, ToAyah, MemorizationType, Rating, TajweedErrorsCount, TeacherNotes, Date, RecordedByUserId, CreatedAtUtc FROM QuranRecords ORDER BY QuranRecordId"
        };
        var rows = new List<string>();
        foreach (var query in queries)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = query;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var fields = Enumerable.Range(0, reader.FieldCount)
                    .Select(i => reader.IsDBNull(i) ? "<null>" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture));
                rows.Add(query + "|" + string.Join("|", fields));
            }
        }
        return rows.ToArray();
    }

    private static Task SeedLegacyAsync(SchoolDbContext db) => db.Database.ExecuteSqlRawAsync("""
        INSERT INTO Users (UserId, Username, PasswordHash, Role) VALUES
            (1, 'example-student-a', 'fixture-hash', 'STUDENT'),
            (2, 'example-student-b', 'fixture-hash', 'STUDENT'),
            (3, 'example-unowned', 'fixture-hash', 'STUDENT'),
            (4, 'example-teacher-a', 'fixture-hash', 'TEACHER'),
            (5, 'example-teacher-b', 'fixture-hash', 'TEACHER'),
            (6, 'example-parent-a', 'fixture-hash', 'PARENT'),
            (7, 'example-admin-a', 'fixture-hash', 'SCHOOL_ADMIN'),
            (8, 'example-admin-b', 'fixture-hash', 'SCHOOL_ADMIN'),
            (9, 'example-root', 'fixture-hash', 'SUPER_ADMIN');
        INSERT INTO AuditLogs (AuditLogId, UserId, Action, EntityName, EntityId, TimestampUtc) VALUES
            (1, 7, 'EXAMPLE', 'Student', '1', '2026-09-01 00:00:00');
        INSERT INTO RefreshTokens (RefreshTokenId, UserId, TokenHash, ExpiresAtUtc, CreatedAtUtc) VALUES
            (1, 7, 'synthetic-not-a-real-token-hash', '2026-12-01 00:00:00', '2026-09-01 00:00:00');
        INSERT INTO Organizations (OrganizationId, Name, IsActive, CreatedAtUtc) VALUES
            (1, 'Example A', 1, '2026-01-01 00:00:00'), (2, 'Example B', 1, '2026-01-01 00:00:00');
        INSERT INTO Students (StudentId, FirstName, LastName, UserId) VALUES
            (1, 'Example', 'A', 1), (2, 'Example', 'B', 2), (3, 'Example', 'Unowned', 3);
        INSERT INTO Teachers (TeacherId, FirstName, LastName, SubjectSpecialty, UserId) VALUES
            (1, 'Example', 'Teacher A', 'Example', 4), (2, 'Example', 'Teacher B', 'Example', 5);
        INSERT INTO Courses (CourseId, CourseName, TeacherId) VALUES (1, 'Example A', 1), (2, 'Example B', 2);
        INSERT INTO OrganizationMemberships (OrganizationId, UserId, IsActive, CreatedAtUtc) VALUES
            (1, 4, 1, '2026-01-01 00:00:00'), (2, 5, 1, '2026-01-01 00:00:00'),
            (1, 6, 1, '2026-01-01 00:00:00'), (1, 7, 1, '2026-01-01 00:00:00'),
            (2, 8, 1, '2026-01-01 00:00:00');
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
            (1, 1, 1, 1, 1, 'ACTIVE', '2026-09-01'),
            (2, 2, 2, 2, 2, 'ACTIVE', '2026-09-01'),
            (3, 2, 1, 1, 1, 'ACTIVE', '2026-09-01');
        INSERT INTO Parents (ParentId, UserId, FirstName, LastName) VALUES (1, 6, 'Example', 'Parent A');
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
        """);

    private sealed class ScratchDirectory : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"smartschool-rehearsal-{Guid.NewGuid():N}");

        public ScratchDirectory() => Directory.CreateDirectory(_path);
        public string File(string name) => Path.Combine(_path, name);
        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}
