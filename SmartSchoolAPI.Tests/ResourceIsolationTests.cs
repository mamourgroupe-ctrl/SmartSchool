using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SmartSchoolAPI.Authorization;
using SmartSchoolAPI.Data;
using SmartSchoolAPI.Models;
using SmartSchoolAPI.Security;

namespace SmartSchoolAPI.Tests;

public sealed class ResourceIsolationTests
{
    private static IntegrationTestFactory NewFactory() => new();

    private static HttpClient Client(IntegrationTestFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string Token(int userId, string role)
    {
        var claims = new[] { new Claim("UserId", userId.ToString()), new Claim(ClaimTypes.Role, role) };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(IntegrationTestFactory.JwtKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(IntegrationTestFactory.JwtIssuer, IntegrationTestFactory.JwtAudience, claims, expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task StudentCannotCreateCourse_ButSchoolAdminCan()
    {
        using var factory = NewFactory();
        var school = SeedStructuredSchool(factory, "course", true);
        var adminUserId = SeedUser(factory, "course-admin", RoleNames.SchoolAdmin);
        AddMembership(factory, school.OrganizationId, adminUserId);
        var studentUserId = factory.SeedStudent("course-student", "Correct123!", "Course", "Student");

        using var studentClient = Client(factory, Token(studentUserId, RoleNames.Student));
        var forbidden = await studentClient.PostAsJsonAsync("/api/courses", new { CourseName = "Math", TeacherId = school.TeacherId!.Value });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var adminClient = Client(factory, Token(adminUserId, RoleNames.SchoolAdmin));
        var created = await adminClient.PostAsJsonAsync("/api/courses", new { CourseName = "Math", TeacherId = school.TeacherId!.Value });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
    }

    [Fact]
    public async Task SchoolAdminSeesOnlyStudentsInOwnOrganization()
    {
        using var factory = NewFactory();
        var mine = SeedStructuredSchool(factory, "admin-iso", true);
        SeedStructuredSchool(factory, "admin-iso-other", true);
        var adminUserId = SeedUser(factory, "admin-iso-user", RoleNames.SchoolAdmin);
        AddMembership(factory, mine.OrganizationId, adminUserId);

        using var client = Client(factory, Token(adminUserId, RoleNames.SchoolAdmin));
        var response = await client.GetFromJsonAsync<JsonElement[]>("/api/students");
        Assert.NotNull(response);
        Assert.Single(response!);
        Assert.Equal(mine.StudentId, response![0].GetProperty("studentId").GetInt32());
    }

    [Fact]
    public async Task TeacherSeesOnlyStudentsInAssignedSections()
    {
        using var factory = NewFactory();
        var mine = SeedStructuredSchool(factory, "teacher-iso", true);
        SeedStructuredSchool(factory, "teacher-iso-other", true);

        using var client = Client(factory, Token(mine.TeacherUserId!.Value, RoleNames.Teacher));
        var response = await client.GetFromJsonAsync<JsonElement[]>("/api/students");
        Assert.NotNull(response);
        Assert.Single(response!);
        Assert.Equal(mine.StudentId, response![0].GetProperty("studentId").GetInt32());
    }

    [Fact]
    public async Task ParentSeesOnlyLinkedAuthorizedStudents()
    {
        using var factory = NewFactory();
        var school = SeedStructuredSchool(factory, "parent-iso", true);
        SeedStructuredSchool(factory, "parent-iso-other", true);
        var parentUserId = SeedParentWithLink(factory, "parent-iso-user", school.StudentId, isAuthorized: true);

        using var client = Client(factory, Token(parentUserId, RoleNames.Parent));
        var response = await client.GetFromJsonAsync<JsonElement[]>("/api/students");
        Assert.NotNull(response);
        Assert.Single(response!);
        Assert.Equal(school.StudentId, response![0].GetProperty("studentId").GetInt32());
    }

    [Fact]
    public async Task ParentDoesNotSeeUnauthorizedOrUnlinkedStudents()
    {
        using var factory = NewFactory();
        var school = SeedStructuredSchool(factory, "parent-auth", true);
        SeedStructuredSchool(factory, "parent-auth-other", true);
        var parentUserId = SeedParentWithLink(factory, "parent-auth-user", school.StudentId, isAuthorized: false);

        using var client = Client(factory, Token(parentUserId, RoleNames.Parent));
        var response = await client.GetFromJsonAsync<JsonElement[]>("/api/students");
        Assert.NotNull(response);
        Assert.Empty(response!);
    }

    [Fact]
    public async Task UnsupportedRoleGetsEmptyStudentList()
    {
        using var factory = NewFactory();
        factory.SeedStudent("sup-student", "Correct123!", "Sup", "Student");
        var supervisorUserId = SeedUser(factory, "supervisor-user", RoleNames.Supervisor);

        using var client = Client(factory, Token(supervisorUserId, RoleNames.Supervisor));
        var response = await client.GetFromJsonAsync<JsonElement[]>("/api/students");
        Assert.NotNull(response);
        Assert.Empty(response!);
    }

    [Fact]
    public async Task TeacherSeesOnlyOwnTeacherRecord()
    {
        using var factory = NewFactory();
        var mine = SeedStructuredSchool(factory, "teacher-list", true);
        SeedStructuredSchool(factory, "teacher-list-other", true);

        using var client = Client(factory, Token(mine.TeacherUserId!.Value, RoleNames.Teacher));
        var response = await client.GetFromJsonAsync<JsonElement[]>("/api/teachers");
        Assert.NotNull(response);
        Assert.Single(response!);
        Assert.Equal(mine.TeacherId!.Value, response![0].GetProperty("teacherId").GetInt32());
    }

    [Fact]
    public async Task ParentSeesOnlyTeachersOfLinkedChildren()
    {
        using var factory = NewFactory();
        var school = SeedStructuredSchool(factory, "parent-teachers", true);
        SeedStructuredSchool(factory, "parent-teachers-other", true);
        var parentUserId = SeedParentWithLink(factory, "parent-teachers-user", school.StudentId, isAuthorized: true);

        using var client = Client(factory, Token(parentUserId, RoleNames.Parent));
        var response = await client.GetFromJsonAsync<JsonElement[]>("/api/teachers");
        Assert.NotNull(response);
        Assert.Single(response!);
        Assert.Equal(school.TeacherId!.Value, response![0].GetProperty("teacherId").GetInt32());
    }

    [Fact]
    public async Task StudentSeesOnlyTeachersOfOwnSections()
    {
        using var factory = NewFactory();
        var mine = SeedStructuredSchool(factory, "student-teachers", true);
        SeedStructuredSchool(factory, "student-teachers-other", true);
        var studentUserId = StudentUserIdFor(factory, mine.StudentId);

        using var client = Client(factory, Token(studentUserId, RoleNames.Student));
        var response = await client.GetFromJsonAsync<JsonElement[]>("/api/teachers");
        Assert.NotNull(response);
        Assert.Single(response!);
        Assert.Equal(mine.TeacherId!.Value, response![0].GetProperty("teacherId").GetInt32());
    }

    private static int SeedUser(IntegrationTestFactory factory, string username, string role)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        var user = new User { Username = username, PasswordHash = PasswordService.Hash("Correct123!"), Role = role };
        db.Users.Add(user);
        db.SaveChanges();
        return user.UserId;
    }

    private static void AddMembership(IntegrationTestFactory factory, int organizationId, int userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = organizationId, UserId = userId });
        db.SaveChanges();
    }

    private static int SeedParentWithLink(IntegrationTestFactory factory, string username, int studentId, bool isAuthorized)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        var user = new User { Username = username, PasswordHash = PasswordService.Hash("Correct123!"), Role = RoleNames.Parent };
        db.Users.Add(user);
        db.SaveChanges();
        var parent = new Parent { UserId = user.UserId, FirstName = "Parent", LastName = username };
        db.Parents.Add(parent);
        db.SaveChanges();
        db.StudentParents.Add(new StudentParent { StudentId = studentId, ParentId = parent.ParentId, Relationship = "Father", IsAuthorized = isAuthorized });
        db.SaveChanges();
        return user.UserId;
    }

    private static int StudentUserIdFor(IntegrationTestFactory factory, int studentId)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SchoolDbContext>().Students.Single(x => x.StudentId == studentId).UserId;
    }

    private static SchoolSeed SeedStructuredSchool(IntegrationTestFactory factory, string prefix, bool withTeacher)
    {
        // Mirrors the structured seeding used by Stage1IntegrationTests.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        var organization = new Organization { Name = $"{prefix}-organization" };
        db.Organizations.Add(organization);
        var studentUser = new User { Username = $"{prefix}-student", PasswordHash = PasswordService.Hash("Correct123!"), Role = RoleNames.Student };
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
            var teacherUser = new User { Username = $"{prefix}-teacher", PasswordHash = PasswordService.Hash("Correct123!"), Role = RoleNames.Teacher };
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

    private sealed record SchoolSeed(int OrganizationId, int StudentId, int? TeacherUserId, int? TeacherId);
}
