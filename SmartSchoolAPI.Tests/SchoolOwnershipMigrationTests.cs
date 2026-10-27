using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartSchoolAPI.Data;

namespace SmartSchoolAPI.Tests;

public sealed class SchoolOwnershipMigrationTests
{
    [Fact]
    public async Task Upgrade_PreservesLegacyDataAndDoesNotGuessItsSchool()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SchoolDbContext>().UseSqlite(connection).Options;
        await using var db = new SchoolDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261009120000_AddRefreshTokens");

        // Synthetic legacy rows: one student has conflicting memberships and
        // another has no ownership evidence. None has a recorded school field.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Users (UserId, Username, PasswordHash, Role) VALUES
                (1, 'legacy-student-a', 'fixture-hash', 'STUDENT'),
                (2, 'legacy-teacher', 'fixture-hash', 'TEACHER'),
                (3, 'legacy-student-b', 'fixture-hash', 'STUDENT');
            INSERT INTO Organizations (OrganizationId, Name, IsActive, CreatedAtUtc) VALUES
                (1, 'School A', 1, '2026-01-01 00:00:00'),
                (2, 'School B', 1, '2026-01-01 00:00:00');
            INSERT INTO Students (StudentId, FirstName, LastName, UserId) VALUES
                (1, 'Example', 'Ambiguous', 1), (2, 'Example', 'Unowned', 3);
            INSERT INTO Teachers (TeacherId, FirstName, LastName, SubjectSpecialty, UserId) VALUES
                (1, 'Example', 'Teacher', 'Example', 2);
            INSERT INTO OrganizationMemberships (OrganizationId, UserId, IsActive, CreatedAtUtc) VALUES
                (1, 1, 1, '2026-01-01 00:00:00'),
                (2, 1, 1, '2026-01-01 00:00:00'),
                (1, 2, 1, '2026-01-01 00:00:00');
            INSERT INTO AttendanceRecords (AttendanceRecordId, StudentId, Date, Status, Notes, RecordedByUserId, CreatedAtUtc) VALUES
                (1, 1, '2026-09-01', 'PRESENT', 'legacy attendance', 2, '2026-09-01 00:00:00');
            INSERT INTO BehaviorRecords (BehaviorRecordId, StudentId, Type, Category, Description, Visibility, Points, RecordedByUserId, Date, CreatedAtUtc) VALUES
                (1, 1, 'POSITIVE', 'Example', 'legacy behavior', 'PARENT_VISIBLE', 1, 2, '2026-09-01', '2026-09-01 00:00:00');
            INSERT INTO QuranRecords (QuranRecordId, StudentId, SurahName, FromAyah, ToAyah, MemorizationType, Rating, TajweedErrorsCount, Date, RecordedByUserId, CreatedAtUtc) VALUES
                (1, 1, 'Example', 1, 2, 'NEW', 8, 0, '2026-09-01', 2, '2026-09-01 00:00:00');
            """);

        await migrator.MigrateAsync();

        var students = await db.Students.OrderBy(s => s.StudentId).ToArrayAsync();
        Assert.Equal(2, students.Length);
        Assert.All(students, student => Assert.Null(student.OrganizationId));
        Assert.Null((await db.Teachers.SingleAsync()).OrganizationId);
        var attendance = await db.AttendanceRecords.SingleAsync();
        var behavior = await db.BehaviorRecords.SingleAsync();
        var quran = await db.QuranRecords.SingleAsync();
        Assert.Null(attendance.OrganizationId);
        Assert.Null(behavior.OrganizationId);
        Assert.Null(quran.OrganizationId);
        Assert.Equal("legacy attendance", attendance.Notes);
        Assert.Equal("legacy behavior", behavior.Description);
        Assert.Equal("Example", quran.SurahName);
        Assert.Equal(3, await db.OrganizationMemberships.CountAsync());
    }
}
