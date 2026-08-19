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

public sealed class Stage1IntegrationTests
{
    [Fact]
    public async Task SuperAdmin_CanCreateInstitutionStructureAndEnrollment_WithAuditTrail()
    {
        using var factory = NewFactory();
        var rootUserId = SeedUser(factory, "stage1-root", RoleNames.SuperAdmin);
        var studentUserId = factory.SeedStudent("stage1-student", "Correct123!", "Stage", "Student");
        var teacher = SeedTeacher(factory, "stage1-teacher", "Stage", "Teacher");
        var studentId = StudentIdFor(factory, studentUserId);
        using var client = Client(factory, Token(rootUserId, RoleNames.SuperAdmin));

        var organizationId = await CreatedId(client.PostAsJsonAsync("/api/organizations", new { name = "Stage One School" }), "organizationId");
        var membership = await client.PostAsJsonAsync($"/api/organizations/{organizationId}/members", new { userId = teacher.UserId });
        Assert.Equal(HttpStatusCode.Created, membership.StatusCode);

        var academicYearId = await CreatedId(client.PostAsJsonAsync($"/api/organizations/{organizationId}/academic-years", new
        {
            name = "2026-2027", startDate = "2026-09-01", endDate = "2027-06-30", isActive = true
        }), "academicYearId");
        var termId = await CreatedId(client.PostAsJsonAsync($"/api/academic-years/{academicYearId}/terms", new
        {
            name = "Term 1", startDate = "2026-09-01", endDate = "2026-12-31"
        }), "termId");
        var classId = await CreatedId(client.PostAsJsonAsync($"/api/academic-years/{academicYearId}/classes", new
        {
            name = "Class A", gradeLevel = "Grade 5"
        }), "schoolClassId");
        var sectionId = await CreatedId(client.PostAsJsonAsync($"/api/classes/{classId}/sections", new
        {
            name = "Section A", teacherId = teacher.TeacherId
        }), "sectionId");

        var enrollment = await client.PostAsJsonAsync("/api/enrollments", new
        {
            studentId, academicYearId, termId, sectionId, startDate = "2026-09-01"
        });
        Assert.Equal(HttpStatusCode.Created, enrollment.StatusCode);
        Assert.Contains(factory.AuditLogs(), x => x.Action == "STAGE1_ENROLLMENT_CREATED");
        Assert.Contains(factory.AuditLogs(), x => x.Action == "STAGE1_ORGANIZATION_CREATED");
    }

    [Fact]
    public async Task SchoolAdminAndTeacher_AreIsolatedToTheirOrganizationAndAssignedSection()
    {
        using var factory = NewFactory();
        var first = SeedStructuredSchool(factory, "first", true);
        var second = SeedStructuredSchool(factory, "second", false);
        var schoolAdminId = SeedUser(factory, "first-admin", RoleNames.SchoolAdmin);
        AddMembership(factory, first.OrganizationId, schoolAdminId);

        using var adminClient = Client(factory, Token(schoolAdminId, RoleNames.SchoolAdmin));
        var crossOrganization = await adminClient.PostAsJsonAsync($"/api/organizations/{second.OrganizationId}/academic-years", new
        {
            name = "No Access", startDate = "2026-09-01", endDate = "2027-06-30", isActive = true
        });
        Assert.Equal(HttpStatusCode.NotFound, crossOrganization.StatusCode);

        using var teacherClient = Client(factory, Token(first.TeacherUserId!.Value, RoleNames.Teacher));
        var ownAttendance = await teacherClient.PostAsJsonAsync($"/api/students/{first.StudentId}/attendance", new
        {
            date = "2026-09-02", status = "PRESENT", notes = "On time"
        });
        Assert.Equal(HttpStatusCode.Created, ownAttendance.StatusCode);

        var crossSection = await teacherClient.PostAsJsonAsync($"/api/students/{second.StudentId}/attendance", new
        {
            date = "2026-09-02", status = "PRESENT", notes = "Must not be recorded"
        });
        Assert.Equal(HttpStatusCode.NotFound, crossSection.StatusCode);
        Assert.Contains(factory.AuditLogs(), x => x.Action == "STAGE1_ATTENDANCE_RECORDED" && x.EntityId == $"{first.StudentId}:2026-09-02");
    }

    [Fact]
    public async Task Guardian_CanViewOnlyLinkedStudent_AndInternalBehaviorIsNotExposed()
    {
        using var factory = NewFactory();
        var school = SeedStructuredSchool(factory, "guardian", false);
        var rootUserId = SeedUser(factory, "guardian-root", RoleNames.SuperAdmin);
        AddMembership(factory, school.OrganizationId, rootUserId);
        SeedBehavior(factory, school.StudentId, "INTERNAL", "Private incident");
        SeedBehavior(factory, school.StudentId, "PARENT_VISIBLE", "Visible praise");

        using var rootClient = Client(factory, Token(rootUserId, RoleNames.SuperAdmin));
        var guardianResponse = await rootClient.PostAsJsonAsync($"/api/students/{school.StudentId}/guardians", new
        {
            username = "guardian-parent", password = "Correct123!", firstName = "Guardian", lastName = "Parent",
            relationship = "Father", phone = "555-0100", email = "guardian@example.test", isAuthorized = true
        });
        Assert.Equal(HttpStatusCode.Created, guardianResponse.StatusCode);
        var guardianUserId = ParentUserId(factory, "guardian-parent");

        using var parentClient = Client(factory, Token(guardianUserId, RoleNames.Parent));
        var ownOverview = await parentClient.GetAsync($"/api/students/{school.StudentId}/360");
        Assert.Equal(HttpStatusCode.OK, ownOverview.StatusCode);
        var ownBody = await ownOverview.Content.ReadAsStringAsync();
        Assert.Contains("Visible praise", ownBody);
        Assert.DoesNotContain("Private incident", ownBody);
        Assert.DoesNotContain("555-0100", ownBody);
        Assert.DoesNotContain("guardian@example.test", ownBody);

        var other = SeedStructuredSchool(factory, "guardian-other", false);
        var otherOverview = await parentClient.GetAsync($"/api/students/{other.StudentId}/360");
        Assert.Equal(HttpStatusCode.NotFound, otherOverview.StatusCode);
        Assert.Contains(factory.AuditLogs(), x => x.Action == "STAGE1_GUARDIAN_LINKED");
    }

    [Fact]
    public async Task Student360_RequiresAuthentication_AndRejectsInvalidAttendanceState()
    {
        using var factory = NewFactory();
        var school = SeedStructuredSchool(factory, "validation", true);
        using var anonymous = factory.CreateClient();
        var unauthorized = await anonymous.GetAsync($"/api/students/{school.StudentId}/360");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var teacherClient = Client(factory, Token(school.TeacherUserId!.Value, RoleNames.Teacher));
        var invalidStatus = await teacherClient.PostAsJsonAsync($"/api/students/{school.StudentId}/attendance", new
        {
            date = "2026-09-03", status = "INVALID", notes = "Invalid state"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatus.StatusCode);
    }

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

    private static async Task<int> CreatedId(Task<HttpResponseMessage> responseTask, string propertyName)
    {
        using var response = await responseTask;
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty(propertyName).GetInt32();
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

    private static (int TeacherId, int UserId) SeedTeacher(IntegrationTestFactory factory, string username, string firstName, string lastName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        var user = new User { Username = username, PasswordHash = PasswordService.Hash("Correct123!"), Role = RoleNames.Teacher };
        db.Users.Add(user);
        db.SaveChanges();
        var teacher = new Teacher { UserId = user.UserId, FirstName = firstName, LastName = lastName, SubjectSpecialty = "General" };
        db.Teachers.Add(teacher);
        db.SaveChanges();
        return (teacher.TeacherId, user.UserId);
    }

    private static int StudentIdFor(IntegrationTestFactory factory, int userId)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SchoolDbContext>().Students.Single(x => x.UserId == userId).StudentId;
    }

    private static void AddMembership(IntegrationTestFactory factory, int organizationId, int userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = organizationId, UserId = userId });
        db.SaveChanges();
    }

    private static void SeedBehavior(IntegrationTestFactory factory, int studentId, string visibility, string description)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        db.BehaviorRecords.Add(new BehaviorRecord
        {
            StudentId = studentId, Type = "POSITIVE", Category = "Conduct", Description = description,
            Visibility = visibility, Points = 1, Date = new DateOnly(2026, 9, 1), RecordedByUserId = 0
        });
        db.SaveChanges();
    }

    private static int ParentUserId(IntegrationTestFactory factory, string username)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        return db.Users.Single(x => x.Username == username).UserId;
    }

    private static SchoolSeed SeedStructuredSchool(IntegrationTestFactory factory, string prefix, bool withTeacher)
    {
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
