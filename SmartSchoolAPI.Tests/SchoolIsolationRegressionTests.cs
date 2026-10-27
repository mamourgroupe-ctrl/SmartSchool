using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SmartSchoolAPI.Authorization;
using SmartSchoolAPI.Data;
using SmartSchoolAPI.Models;

namespace SmartSchoolAPI.Tests;

public sealed class SchoolIsolationRegressionTests
{
    [Fact]
    public async Task SchoolAdmin_CannotEnrollAStudentOwnedByAnotherSchool()
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("cross-enrollment");
        var target = StructureFor(factory, schools.First.School.OrganizationId);
        using var admin = Client(factory, schools.First.AdminUserId, RoleNames.SchoolAdmin);
        admin.DefaultRequestHeaders.Add("X-School-Id", schools.Second.School.OrganizationId.ToString());

        using var response = await EnrollAsync(admin, schools.Second.School.StudentId, target);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        Assert.False(await db.Enrollments.AnyAsync(e => e.StudentId == schools.Second.School.StudentId && e.TermId == target.TermId));
    }

    [Fact]
    public async Task SchoolAdmin_CannotEnrollOwnStudentInForeignSection()
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("cross-section");
        var own = StructureFor(factory, schools.First.School.OrganizationId);
        var foreign = StructureFor(factory, schools.Second.School.OrganizationId);
        using var admin = Client(factory, schools.First.AdminUserId, RoleNames.SchoolAdmin);

        using var response = await admin.PostAsJsonAsync("/api/enrollments", new
        {
            studentId = schools.First.School.StudentId,
            academicYearId = own.AcademicYearId,
            termId = own.TermId,
            sectionId = foreign.SectionId,
            startDate = "2026-09-01"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SchoolAdmin_CannotClaimAnotherSchoolsNewUnenrolledStudent()
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("unregistered-student");
        using var owner = Client(factory, schools.Second.AdminUserId, RoleNames.SchoolAdmin);
        using var attacker = Client(factory, schools.First.AdminUserId, RoleNames.SchoolAdmin);
        using var created = await owner.PostAsJsonAsync("/api/students", new
        {
            username = "new-student-for-second-school",
            password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)),
            firstName = "Example",
            lastName = "Student",
            organizationId = schools.First.School.OrganizationId
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var studentId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("studentId").GetInt32();

        using var response = await EnrollAsync(attacker, studentId, StructureFor(factory, schools.First.School.OrganizationId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_CannotAssignLegacyUnownedStudentWithUnverifiedRecords()
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("unowned-history");
        var userId = factory.SeedStudent("legacy-unowned-student", Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)), "Example", "Student");
        int studentId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
            studentId = await db.Students.Where(s => s.UserId == userId).Select(s => s.StudentId).SingleAsync();
            db.AttendanceRecords.Add(new AttendanceRecord { StudentId = studentId, Date = new DateOnly(2026, 10, 11), Status = "PRESENT", RecordedByUserId = schools.Second.TeacherUserId });
            await db.SaveChangesAsync();
        }
        var rootId = factory.SeedUser("unowned-root", Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)), RoleNames.SuperAdmin);
        using var root = Client(factory, rootId, RoleNames.SuperAdmin);

        using var response = await EnrollAsync(root, studentId, StructureFor(factory, schools.First.School.OrganizationId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var overview = await root.GetAsync($"/api/students/{studentId}/360");
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
    }

    [Fact]
    public async Task SchoolAdmin_CannotMakeForeignTeacherAMemberOfOwnSchool()
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("cross-member");
        using var admin = Client(factory, schools.First.AdminUserId, RoleNames.SchoolAdmin);

        using var response = await admin.PostAsJsonAsync($"/api/organizations/{schools.First.School.OrganizationId}/members", new
        {
            userId = schools.Second.TeacherUserId
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        Assert.False(await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == schools.First.School.OrganizationId && m.UserId == schools.Second.TeacherUserId));
    }

    [Fact]
    public async Task SchoolAdmin_CannotAssignForeignTeacherToOwnSection()
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("foreign-section-teacher");
        var first = StructureFor(factory, schools.First.School.OrganizationId);
        using var admin = Client(factory, schools.First.AdminUserId, RoleNames.SchoolAdmin);

        using var response = await admin.PostAsJsonAsync($"/api/classes/{first.ClassId}/sections", new
        {
            name = "Unauthorised section",
            teacherId = schools.Second.School.TeacherId
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("360")]
    [InlineData("quran")]
    [InlineData("quran/summary")]
    public async Task LegacyCrossSchoolEnrollment_DoesNotGrantAccessToForeignStudentRecords(string suffix)
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("ambiguous-records");
        using var owner = Client(factory, schools.Second.TeacherUserId, RoleNames.Teacher);
        foreach (var kind in new[] { "attendance", "behavior", "quran" })
        {
            using var created = await RecordAsync(owner, schools.Second.School.StudentId, kind);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }
        using var ownerRead = await owner.GetAsync($"/api/students/{schools.Second.School.StudentId}/{suffix}");
        Assert.Equal(HttpStatusCode.OK, ownerRead.StatusCode);
        AddConflictingEnrollment(factory, schools.Second.School.StudentId, StructureFor(factory, schools.First.School.OrganizationId));
        using var admin = Client(factory, schools.First.AdminUserId, RoleNames.SchoolAdmin);

        using var response = await admin.GetAsync($"/api/students/{schools.Second.School.StudentId}/{suffix}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("attendance")]
    [InlineData("behavior")]
    [InlineData("quran")]
    public async Task LegacyCrossSchoolEnrollment_DoesNotGrantWriteAccessToForeignRecords(string kind)
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("ambiguous-writes");
        AddConflictingEnrollment(factory, schools.Second.School.StudentId, StructureFor(factory, schools.First.School.OrganizationId));
        using var admin = Client(factory, schools.First.AdminUserId, RoleNames.SchoolAdmin);

        using var response = await RecordAsync(admin, schools.Second.School.StudentId, kind);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LegacyCrossSchoolEnrollment_DoesNotExposeForeignStudentInList()
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("ambiguous-list");
        AddConflictingEnrollment(factory, schools.Second.School.StudentId, StructureFor(factory, schools.First.School.OrganizationId));
        using var admin = Client(factory, schools.First.AdminUserId, RoleNames.SchoolAdmin);

        var students = await admin.GetFromJsonAsync<JsonElement[]>("/api/students");

        Assert.NotNull(students);
        Assert.DoesNotContain(students!, s => s.GetProperty("studentId").GetInt32() == schools.Second.School.StudentId);
    }

    [Fact]
    public async Task LegacyCrossSchoolGuardianLink_DoesNotGrantAccessToForeignStudent()
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("foreign-guardian");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
            var parentId = await db.Parents.Where(p => p.UserId == schools.First.ParentUserId).Select(p => p.ParentId).SingleAsync();
            db.StudentParents.Add(new StudentParent { StudentId = schools.Second.School.StudentId, ParentId = parentId, Relationship = "Example", IsAuthorized = true });
            await db.SaveChangesAsync();
        }
        using var parent = Client(factory, schools.First.ParentUserId, RoleNames.Parent);

        using var own = await parent.GetAsync($"/api/students/{schools.First.School.StudentId}/360");
        using var foreign = await parent.GetAsync($"/api/students/{schools.Second.School.StudentId}/360");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task LegacyCrossSchoolGuardianLink_DoesNotExposeForeignGuardianInOverview()
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("foreign-guardian-list");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
            var parentId = await db.Parents.Where(p => p.UserId == schools.First.ParentUserId).Select(p => p.ParentId).SingleAsync();
            db.StudentParents.Add(new StudentParent { StudentId = schools.Second.School.StudentId, ParentId = parentId, Relationship = "Example", IsAuthorized = true });
            await db.SaveChangesAsync();
        }
        using var otherAdmin = Client(factory, schools.Second.AdminUserId, RoleNames.SchoolAdmin);

        var overview = await otherAdmin.GetStringAsync($"/api/students/{schools.Second.School.StudentId}/360");

        Assert.DoesNotContain(schools.First.ParentUsername, overview);
        Assert.Contains(schools.Second.ParentUsername, overview);
    }

    [Fact]
    public async Task LegacyRecordsWithoutVerifiedSchool_AreQuarantinedButPreservedForSuperAdmin()
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("unverified-legacy-records");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
            var studentId = schools.First.School.StudentId;
            var foreignRecorder = schools.Second.TeacherUserId;
            db.AttendanceRecords.Add(new AttendanceRecord { StudentId = studentId, Date = new DateOnly(2026, 10, 11), Status = "ABSENT", Notes = "unverified-attendance", RecordedByUserId = foreignRecorder });
            db.BehaviorRecords.Add(new BehaviorRecord { StudentId = studentId, Date = new DateOnly(2026, 10, 11), Type = "POSITIVE", Category = "Example", Description = "unverified-behavior", Visibility = "PARENT_VISIBLE", RecordedByUserId = foreignRecorder });
            db.QuranRecords.Add(new QuranRecord { StudentId = studentId, Date = new DateOnly(2026, 10, 11), SurahName = "unverified-quran", FromAyah = 1, ToAyah = 2, MemorizationType = "NEW", Rating = 8, RecordedByUserId = foreignRecorder });
            await db.SaveChangesAsync();
        }
        using var admin = Client(factory, schools.First.AdminUserId, RoleNames.SchoolAdmin);
        var overview = await admin.GetStringAsync($"/api/students/{schools.First.School.StudentId}/360");
        var records = await admin.GetFromJsonAsync<JsonElement[]>($"/api/students/{schools.First.School.StudentId}/quran");
        Assert.DoesNotContain("unverified-attendance", overview);
        Assert.DoesNotContain("unverified-behavior", overview);
        Assert.DoesNotContain("unverified-quran", overview);
        Assert.NotNull(records);
        Assert.Empty(records!);

        var rootId = factory.SeedUser("unverified-root", Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)), RoleNames.SuperAdmin);
        using var root = Client(factory, rootId, RoleNames.SuperAdmin);
        var rootOverview = await root.GetStringAsync($"/api/students/{schools.First.School.StudentId}/360");
        Assert.Contains("unverified-attendance", rootOverview);
        Assert.Contains("unverified-behavior", rootOverview);
        Assert.Contains("unverified-quran", rootOverview);
    }

    [Fact]
    public async Task RecordsWithMismatchedSchoolAndStudent_AreHiddenFromBothSchools()
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("record-school-mismatch");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
            db.AttendanceRecords.Add(new AttendanceRecord
            {
                OrganizationId = schools.First.School.OrganizationId, StudentId = schools.Second.School.StudentId,
                Date = new DateOnly(2026, 10, 13), Status = "ABSENT", Notes = "mismatched-record",
                RecordedByUserId = schools.First.TeacherUserId
            });
            await db.SaveChangesAsync();
        }
        using var firstAdmin = Client(factory, schools.First.AdminUserId, RoleNames.SchoolAdmin);
        using var secondAdmin = Client(factory, schools.Second.AdminUserId, RoleNames.SchoolAdmin);

        using var foreign = await firstAdmin.GetAsync($"/api/students/{schools.Second.School.StudentId}/360");
        var own = await secondAdmin.GetStringAsync($"/api/students/{schools.Second.School.StudentId}/360");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.DoesNotContain("mismatched-record", own);
    }

    [Fact]
    public async Task LegacyUnverifiedAttendance_DoesNotBlockSchoolScopedAttendance()
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("unverified-attendance-conflict");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
            db.AttendanceRecords.Add(new AttendanceRecord
            {
                StudentId = schools.First.School.StudentId, Date = new DateOnly(2026, 10, 10), Status = "ABSENT",
                Notes = "legacy-unverified", RecordedByUserId = schools.Second.TeacherUserId
            });
            await db.SaveChangesAsync();
        }
        using var admin = Client(factory, schools.First.AdminUserId, RoleNames.SchoolAdmin);

        using var created = await RecordAsync(admin, schools.First.School.StudentId, "attendance");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var overview = await admin.GetStringAsync($"/api/students/{schools.First.School.StudentId}/360");
        Assert.DoesNotContain("legacy-unverified", overview);
        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        Assert.Equal(2, await verificationDb.AttendanceRecords.CountAsync(r => r.StudentId == schools.First.School.StudentId));
    }

    [Fact]
    public async Task AdminWithMultipleSchools_CannotChooseNewStudentScopeFromClient()
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("ambiguous-creator");
        factory.AddMembership(schools.Second.School.OrganizationId, schools.First.AdminUserId);
        using var admin = Client(factory, schools.First.AdminUserId, RoleNames.SchoolAdmin);

        using var response = await admin.PostAsJsonAsync("/api/students", new
        {
            username = "ambiguous-new-student",
            password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)),
            firstName = "Example",
            lastName = "Student",
            organizationId = schools.Second.School.OrganizationId
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SchoolAdminCanCreateTeacherAndCourseInOwnSchool()
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("create-teacher-scope");
        using var admin = Client(factory, schools.First.AdminUserId, RoleNames.SchoolAdmin);
        using var created = await admin.PostAsJsonAsync("/api/teachers", new
        {
            username = "new-teacher-for-first-school",
            password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)),
            firstName = "Example",
            lastName = "Teacher",
            subjectSpecialty = "Example"
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var teacherId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("teacherId").GetInt32();

        using var course = await admin.PostAsJsonAsync("/api/courses", new { courseName = "Example", teacherId });

        Assert.Equal(HttpStatusCode.OK, course.StatusCode);
    }

    [Fact]
    public async Task NewStudentRecords_AlwaysUseAuthenticatedSchool_NotClientSchool()
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("record-owner");
        using var admin = Client(factory, schools.First.AdminUserId, RoleNames.SchoolAdmin);
        var studentId = schools.First.School.StudentId;
        var forgedOrganization = schools.Second.School.OrganizationId;
        using var attendance = await admin.PostAsJsonAsync($"/api/students/{studentId}/attendance", new { date = "2026-10-12", status = "PRESENT", organizationId = forgedOrganization });
        using var behavior = await admin.PostAsJsonAsync($"/api/students/{studentId}/behavior", new { date = "2026-10-12", type = "POSITIVE", category = "Example", description = "Example", visibility = "INTERNAL", points = 1, organizationId = forgedOrganization });
        using var quran = await admin.PostAsJsonAsync($"/api/students/{studentId}/quran", new { date = "2026-10-12", surahName = "Example", fromAyah = 1, toAyah = 2, memorizationType = "NEW", rating = 8, tajweedErrorsCount = 0, organizationId = forgedOrganization });
        Assert.Equal(HttpStatusCode.Created, attendance.StatusCode);
        Assert.Equal(HttpStatusCode.Created, behavior.StatusCode);
        Assert.Equal(HttpStatusCode.Created, quran.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        var expected = schools.First.School.OrganizationId;
        Assert.Equal(expected, (await db.AttendanceRecords.SingleAsync()).OrganizationId);
        Assert.Equal(expected, (await db.BehaviorRecords.SingleAsync()).OrganizationId);
        Assert.Equal(expected, (await db.QuranRecords.SingleAsync()).OrganizationId);
    }

    [Fact]
    public async Task LegacyCrossSchoolTeacherMembership_DoesNotExposeForeignTeacherOrCourses()
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("ambiguous-teacher");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
            db.OrganizationMemberships.Add(new OrganizationMembership
            {
                OrganizationId = schools.First.School.OrganizationId,
                UserId = schools.Second.TeacherUserId
            });
            db.Courses.Add(new Course { CourseName = "Foreign course", TeacherId = schools.Second.School.TeacherId!.Value });
            await db.SaveChangesAsync();
        }
        using var admin = Client(factory, schools.First.AdminUserId, RoleNames.SchoolAdmin);

        var teachers = await admin.GetFromJsonAsync<JsonElement[]>("/api/teachers");
        var courses = await admin.GetFromJsonAsync<JsonElement[]>("/api/courses");

        Assert.NotNull(teachers);
        Assert.NotNull(courses);
        Assert.DoesNotContain(teachers!, t => t.GetProperty("teacherId").GetInt32() == schools.Second.School.TeacherId);
        Assert.DoesNotContain(courses!, c => c.GetProperty("teacherId").GetInt32() == schools.Second.School.TeacherId);
    }

    [Fact]
    public async Task LegacyCrossSchoolTeacherMembership_CannotBeUsedToCreateForeignCourse()
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("ambiguous-course");
        factory.AddMembership(schools.First.School.OrganizationId, schools.Second.TeacherUserId);
        using var admin = Client(factory, schools.First.AdminUserId, RoleNames.SchoolAdmin);

        using var response = await admin.PostAsJsonAsync("/api/courses", new
        {
            courseName = "Unauthorised course",
            teacherId = schools.Second.School.TeacherId
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(RoleNames.SchoolAdmin)]
    [InlineData(RoleNames.Teacher)]
    [InlineData(RoleNames.Parent)]
    [InlineData(RoleNames.Student)]
    public async Task RolesCannotReadAnotherSchoolsStudent(string role)
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("role-isolation");
        var userId = role switch
        {
            RoleNames.SchoolAdmin => schools.First.AdminUserId,
            RoleNames.Teacher => schools.First.TeacherUserId,
            RoleNames.Parent => schools.First.ParentUserId,
            RoleNames.Student => schools.First.StudentUserId,
            _ => throw new InvalidOperationException()
        };
        using var client = Client(factory, userId, role);
        client.DefaultRequestHeaders.Add("X-School-Id", schools.Second.School.OrganizationId.ToString());

        using var overview = await client.GetAsync($"/api/students/{schools.Second.School.StudentId}/360?schoolId={schools.Second.School.OrganizationId}");
        using var quran = await client.GetAsync($"/api/students/{schools.Second.School.StudentId}/quran");

        Assert.Equal(HttpStatusCode.NotFound, overview.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, quran.StatusCode);
    }

    [Fact]
    public async Task SchoolAdmin_CannotModifyForeignSchoolResources()
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("foreign-write");
        var other = StructureFor(factory, schools.Second.School.OrganizationId);
        using var admin = Client(factory, schools.First.AdminUserId, RoleNames.SchoolAdmin);

        using var classroom = await admin.PostAsJsonAsync($"/api/academic-years/{other.AcademicYearId}/classes", new { name = "Foreign class" });
        using var section = await admin.PostAsJsonAsync($"/api/classes/{other.ClassId}/sections", new { name = "Foreign section" });
        using var attendance = await RecordAsync(admin, schools.Second.School.StudentId, "attendance");
        using var behavior = await RecordAsync(admin, schools.Second.School.StudentId, "behavior");
        using var quran = await RecordAsync(admin, schools.Second.School.StudentId, "quran");
        using var course = await admin.PostAsJsonAsync("/api/courses", new { courseName = "Foreign course", teacherId = schools.Second.School.TeacherId });

        foreach (var response in new[] { classroom, section, attendance, behavior, quran, course })
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_CanReadBothSchoolsWithoutClientSuppliedSchoolScope()
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("root-scope");
        var rootId = factory.SeedUser("scope-root", Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)), RoleNames.SuperAdmin);
        using var root = Client(factory, rootId, RoleNames.SuperAdmin);

        using var first = await root.GetAsync($"/api/students/{schools.First.School.StudentId}/360");
        using var second = await root.GetAsync($"/api/students/{schools.Second.School.StudentId}/360");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    private static HttpClient Client(IntegrationTestFactory factory, int userId, string role)
    {
        var claims = new[] { new Claim("UserId", userId.ToString()), new Claim(ClaimTypes.Role, role) };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(IntegrationTestFactory.JwtKey)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(IntegrationTestFactory.JwtIssuer, IntegrationTestFactory.JwtAudience, claims,
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: credentials);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        return client;
    }

    private static SchoolStructure StructureFor(IntegrationTestFactory factory, int organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        var year = db.AcademicYears.Single(y => y.OrganizationId == organizationId);
        var term = db.Terms.Single(t => t.AcademicYearId == year.AcademicYearId);
        var classroom = db.SchoolClasses.Single(c => c.AcademicYearId == year.AcademicYearId);
        var section = db.Sections.Single(s => s.SchoolClassId == classroom.SchoolClassId);
        return new SchoolStructure(year.AcademicYearId, term.TermId, classroom.SchoolClassId, section.SectionId);
    }

    private static void AddConflictingEnrollment(IntegrationTestFactory factory, int studentId, SchoolStructure otherSchool)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        db.Enrollments.Add(new Enrollment
        {
            StudentId = studentId,
            AcademicYearId = otherSchool.AcademicYearId,
            TermId = otherSchool.TermId,
            SectionId = otherSchool.SectionId,
            Status = "ACTIVE",
            StartDate = new DateOnly(2026, 9, 1)
        });
        db.SaveChanges();
    }

    private static Task<HttpResponseMessage> EnrollAsync(HttpClient client, int studentId, SchoolStructure target) =>
        client.PostAsJsonAsync("/api/enrollments", new
        {
            studentId,
            academicYearId = target.AcademicYearId,
            termId = target.TermId,
            sectionId = target.SectionId,
            startDate = "2026-09-01"
        });

    private static Task<HttpResponseMessage> RecordAsync(HttpClient client, int studentId, string kind) => kind switch
    {
        "attendance" => client.PostAsJsonAsync($"/api/students/{studentId}/attendance", new { date = "2026-10-10", status = "PRESENT" }),
        "behavior" => client.PostAsJsonAsync($"/api/students/{studentId}/behavior", new { date = "2026-10-10", type = "POSITIVE", category = "Example", description = "Example", visibility = "INTERNAL", points = 1 }),
        "quran" => client.PostAsJsonAsync($"/api/students/{studentId}/quran", new { date = "2026-10-10", surahName = "Example", fromAyah = 1, toAyah = 2, memorizationType = "NEW", rating = 8, tajweedErrorsCount = 0 }),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private sealed record SchoolStructure(int AcademicYearId, int TermId, int ClassId, int SectionId);
}
