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

namespace SmartSchoolAPI.Tests;

public sealed class QuranIntegrationTests
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

    private static void SeedQuranRecord(IntegrationTestFactory factory, int studentId, int recordedByUserId, string memorizationType, int fromAyah, int toAyah, int rating, int tajweedErrors, string date)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        db.QuranRecords.Add(new QuranRecord
        {
            StudentId = studentId,
            SurahName = "سورة الملك",
            FromAyah = fromAyah,
            ToAyah = toAyah,
            MemorizationType = memorizationType,
            Rating = rating,
            TajweedErrorsCount = tajweedErrors,
            Date = DateOnly.Parse(date),
            RecordedByUserId = recordedByUserId
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task Teacher_CanRecordQuranProgress_ForAssignedStudent_WithAudit()
    {
        using var factory = NewFactory();
        var school = factory.SeedStructuredSchool("quran");
        using var client = Client(factory, Token(school.TeacherUserId!.Value, RoleNames.Teacher));
        var response = await client.PostAsJsonAsync($"/api/students/{school.StudentId}/quran", new
        {
            date = "2026-09-05", surahName = "سورة الملك", fromAyah = 1, toAyah = 15,
            memorizationType = "NEW", rating = 9, tajweedErrorsCount = 2, teacherNotes = "يحتاج تثبيت"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains(factory.AuditLogs(), x => x.Action == "QURAN_RECORD_CREATED");
    }

    [Fact]
    public async Task Teacher_CannotRecordQuranProgress_ForUnassignedStudent()
    {
        using var factory = NewFactory();
        var mine = factory.SeedStructuredSchool("quran-mine");
        var other = factory.SeedStructuredSchool("quran-other");
        using var client = Client(factory, Token(mine.TeacherUserId!.Value, RoleNames.Teacher));
        var response = await client.PostAsJsonAsync($"/api/students/{other.StudentId}/quran", new
        {
            date = "2026-09-05", surahName = "سورة الملك", fromAyah = 1, toAyah = 15,
            memorizationType = "NEW", rating = 9, tajweedErrorsCount = 0
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Student_CannotRecordQuranProgress()
    {
        using var factory = NewFactory();
        var school = factory.SeedStructuredSchool("quran-student");
        var studentUserId = factory.StudentUserIdFor(school.StudentId);
        using var client = Client(factory, Token(studentUserId, RoleNames.Student));
        var response = await client.PostAsJsonAsync($"/api/students/{school.StudentId}/quran", new
        {
            date = "2026-09-05", surahName = "سورة الملك", fromAyah = 1, toAyah = 15,
            memorizationType = "NEW", rating = 9, tajweedErrorsCount = 0
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RecordQuranProgress_RejectsInvalidInput()
    {
        using var factory = NewFactory();
        var school = factory.SeedStructuredSchool("quran-invalid");
        using var client = Client(factory, Token(school.TeacherUserId!.Value, RoleNames.Teacher));

        var badType = await client.PostAsJsonAsync($"/api/students/{school.StudentId}/quran", new
        {
            date = "2026-09-05", surahName = "سورة الملك", fromAyah = 1, toAyah = 15,
            memorizationType = "INVALID", rating = 9, tajweedErrorsCount = 0
        });
        Assert.Equal(HttpStatusCode.BadRequest, badType.StatusCode);

        var badRange = await client.PostAsJsonAsync($"/api/students/{school.StudentId}/quran", new
        {
            date = "2026-09-05", surahName = "سورة الملك", fromAyah = 10, toAyah = 5,
            memorizationType = "NEW", rating = 9, tajweedErrorsCount = 0
        });
        Assert.Equal(HttpStatusCode.BadRequest, badRange.StatusCode);

        var badRating = await client.PostAsJsonAsync($"/api/students/{school.StudentId}/quran", new
        {
            date = "2026-09-05", surahName = "سورة الملك", fromAyah = 1, toAyah = 15,
            memorizationType = "NEW", rating = 11, tajweedErrorsCount = 0
        });
        Assert.Equal(HttpStatusCode.BadRequest, badRating.StatusCode);
    }

    [Fact]
    public async Task Parent_CanListQuranRecords_OfLinkedChild_Only()
    {
        using var factory = NewFactory();
        var school = factory.SeedStructuredSchool("quran-parent");
        var other = factory.SeedStructuredSchool("quran-parent-other");
        SeedQuranRecord(factory, school.StudentId, school.TeacherUserId!.Value, "NEW", 1, 15, 9, 2, "2026-09-05");
        SeedQuranRecord(factory, school.StudentId, school.TeacherUserId!.Value, "REVIEW", 16, 30, 7, 1, "2026-09-06");
        var parentUserId = factory.SeedParentWithLink("quran-parent-user", school.StudentId);

        using var client = Client(factory, Token(parentUserId, RoleNames.Parent));
        var own = await client.GetFromJsonAsync<JsonElement[]>($"/api/students/{school.StudentId}/quran");
        Assert.NotNull(own);
        Assert.Equal(2, own!.Length);

        var foreign = await client.GetAsync($"/api/students/{other.StudentId}/quran");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task Summary_IsDeterministic_FromStoredRecords()
    {
        using var factory = NewFactory();
        var school = factory.SeedStructuredSchool("quran-summary");
        SeedQuranRecord(factory, school.StudentId, school.TeacherUserId!.Value, "NEW", 1, 15, 8, 2, "2026-09-05");
        SeedQuranRecord(factory, school.StudentId, school.TeacherUserId!.Value, "REVIEW", 16, 30, 10, 1, "2026-09-06");

        using var client = Client(factory, Token(school.TeacherUserId!.Value, RoleNames.Teacher));
        var response = await client.GetFromJsonAsync<JsonElement>($"/api/students/{school.StudentId}/quran/summary");
        Assert.Equal(2, response.GetProperty("totalRecords").GetInt32());
        Assert.Equal(15, response.GetProperty("newMemorizationAyahs").GetInt32());
        Assert.Equal(15, response.GetProperty("reviewAyahs").GetInt32());
        Assert.Equal(9d, response.GetProperty("averageRating").GetDouble());
        Assert.Equal(3, response.GetProperty("totalTajweedErrors").GetInt32());
        Assert.Equal("2026-09-06", response.GetProperty("lastRecordDate").GetString());
    }

    [Fact]
    public async Task Student360_Overview_IncludesQuranRecords()
    {
        using var factory = NewFactory();
        var school = factory.SeedStructuredSchool("quran-360");
        SeedQuranRecord(factory, school.StudentId, school.TeacherUserId!.Value, "NEW", 1, 15, 9, 0, "2026-09-05");

        using var client = Client(factory, Token(school.TeacherUserId!.Value, RoleNames.Teacher));
        var response = await client.GetAsync($"/api/students/{school.StudentId}/360");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("سورة الملك", body);
        Assert.Contains("quran", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task QuranEndpoints_RequireAuthentication()
    {
        using var factory = NewFactory();
        var school = factory.SeedStructuredSchool("quran-anon");
        using var anonymous = factory.CreateClient();
        var list = await anonymous.GetAsync($"/api/students/{school.StudentId}/quran");
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        var summary = await anonymous.GetAsync($"/api/students/{school.StudentId}/quran/summary");
        Assert.Equal(HttpStatusCode.Unauthorized, summary.StatusCode);
    }
}
