using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartSchoolAPI.Data;
using SmartSchoolAPI.Models;
using SmartSchoolAPI.Security;

namespace SmartSchoolAPI.Tests;

public sealed class IntegrationTestFactory : WebApplicationFactory<Program>
{
    public const string JwtKey = "integration-test-key-012345678901234567890123456789";
    public const string JwtIssuer = "SmartSchoolAPI";
    public const string JwtAudience = "SmartSchoolClients";
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("SMARTSCHOOL_JWT_KEY", JwtKey);
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Key", JwtKey);
        builder.UseSetting("Jwt:Issuer", JwtIssuer);
        builder.UseSetting("Jwt:Audience", JwtAudience);
        builder.ConfigureServices(services =>
        {
            _connection.Open();
            services.RemoveAll<DbContextOptions<SchoolDbContext>>();
            services.RemoveAll<SchoolDbContext>();
            services.AddSingleton(_connection);
            services.AddDbContext<SchoolDbContext>(options => options.UseSqlite(_connection));
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<SchoolDbContext>().Database.EnsureCreated();
        });
    }

    public int SeedUser(string username, string password, string role, int? userId = null)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        if (!db.Users.Any(u => u.Username == username))
        {
            var user = new User { Username = username, PasswordHash = PasswordService.Hash(password), Role = role };
            db.Users.Add(user);
            db.SaveChanges();
            return user.UserId;
        }
        return db.Users.Single(u => u.Username == username).UserId;
    }

    public int SeedStudent(string username, string password, string firstName, string lastName)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        var user = new User { Username = username, PasswordHash = PasswordService.Hash(password), Role = SmartSchoolAPI.Authorization.RoleNames.Student };
        db.Users.Add(user);
        db.SaveChanges();
        var student = new Student { UserId = user.UserId, FirstName = firstName, LastName = lastName };
        db.Students.Add(student);
        db.SaveChanges();
        return user.UserId;
    }

    public List<AuditLog> AuditLogs()
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SchoolDbContext>().AuditLogs.AsNoTracking().ToList();
    }

    public int SeedUser(string username, string role) => SeedUser(username, "Correct123!", role);

    public void AddMembership(int organizationId, int userId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = organizationId, UserId = userId });
        db.SaveChanges();
    }

    public int SeedParentWithLink(string username, int studentId, bool isAuthorized = true)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        var user = new User { Username = username, PasswordHash = PasswordService.Hash("Correct123!"), Role = SmartSchoolAPI.Authorization.RoleNames.Parent };
        db.Users.Add(user);
        db.SaveChanges();
        var parent = new Parent { UserId = user.UserId, FirstName = "Parent", LastName = username };
        db.Parents.Add(parent);
        db.SaveChanges();
        db.StudentParents.Add(new StudentParent { StudentId = studentId, ParentId = parent.ParentId, Relationship = "Father", IsAuthorized = isAuthorized });
        db.SaveChanges();
        return user.UserId;
    }

    public int StudentUserIdFor(int studentId)
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SchoolDbContext>().Students.Single(x => x.StudentId == studentId).UserId;
    }

    public SchoolSeed SeedStructuredSchool(string prefix, bool withTeacher = true)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        var organization = new Organization { Name = $"{prefix}-organization" };
        db.Organizations.Add(organization);
        var studentUser = new User { Username = $"{prefix}-student", PasswordHash = PasswordService.Hash("Correct123!"), Role = SmartSchoolAPI.Authorization.RoleNames.Student };
        db.Users.Add(studentUser);
        db.SaveChanges();
        var student = new Student { UserId = studentUser.UserId, FirstName = "Student", LastName = prefix };
        db.Students.Add(student);
        var academicYear = new AcademicYear { OrganizationId = organization.OrganizationId, Name = $"{prefix}-year", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true };
        db.AcademicYears.Add(academicYear);
        db.SaveChanges();
        var term = new Term { AcademicYearId = academicYear.AcademicYearId, Name = "Term 1", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 12, 31) };
        var classroom = new SchoolClass { AcademicYearId = academicYear.AcademicYearId, Name = "Class A", GradeLevel = "5" };
        db.AddRange(term, classroom);
        db.SaveChanges();

        int? teacherUserId = null;
        int? teacherId = null;
        if (withTeacher)
        {
            var teacherUser = new User { Username = $"{prefix}-teacher", PasswordHash = PasswordService.Hash("Correct123!"), Role = SmartSchoolAPI.Authorization.RoleNames.Teacher };
            db.Users.Add(teacherUser);
            db.SaveChanges();
            var teacher = new Teacher { UserId = teacherUser.UserId, FirstName = "Teacher", LastName = prefix, SubjectSpecialty = "General" };
            db.Teachers.Add(teacher);
            db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = organization.OrganizationId, UserId = teacherUser.UserId });
            db.SaveChanges();
            teacherUserId = teacherUser.UserId;
            teacherId = teacher.TeacherId;
        }

        var section = new Section { SchoolClassId = classroom.SchoolClassId, Name = "Section A", TeacherId = teacherId };
        db.Sections.Add(section);
        db.SaveChanges();
        db.Enrollments.Add(new Enrollment
        {
            StudentId = student.StudentId, AcademicYearId = academicYear.AcademicYearId, TermId = term.TermId,
            SectionId = section.SectionId, Status = "ACTIVE", StartDate = new DateOnly(2026, 9, 1)
        });
        db.SaveChanges();
        return new SchoolSeed(organization.OrganizationId, student.StudentId, teacherUserId, teacherId);
    }

    public sealed record SchoolSeed(int OrganizationId, int StudentId, int? TeacherUserId, int? TeacherId);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _connection.Dispose();
        base.Dispose(disposing);
    }
}
