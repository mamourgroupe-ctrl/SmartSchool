using Microsoft.AspNetCore.Mvc.Testing;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using SmartSchoolAPI.Authorization;

namespace SmartSchoolAPI.Tests;

public sealed class IntegrationSecurityTests
{
    private static IntegrationTestFactory NewFactory() => new();

    private static HttpClient Client(IntegrationTestFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<string> Login(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = username, Password = password });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static string Token(int userId, string role, DateTime? expires = null, string? issuer = null, string? audience = null, string? key = null)
    {
        var signingKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(key ?? IntegrationTestFactory.JwtKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(issuer ?? IntegrationTestFactory.JwtIssuer, audience ?? IntegrationTestFactory.JwtAudience, new[] { new Claim("UserId", userId.ToString()), new Claim(ClaimTypes.Role, role) }, expires: expires ?? DateTime.UtcNow.AddHours(1), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static void Bearer(HttpClient client, string token) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task Login_CorrectCredentials_Returns200AndJwtAndSuccessAudit()
    {
        using var factory = NewFactory(); factory.SeedUser("login-ok", "Correct123!", RoleNames.Student); using var client = Client(factory);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = "login-ok", Password = "Correct123!" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(); Assert.Contains("accessToken", body, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("PasswordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(factory.AuditLogs(), x => x.Action == "LOGIN_SUCCESS");
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401AndFailureAuditWithoutSecret()
    {
        using var factory = NewFactory(); factory.SeedUser("login-wrong", "Correct123!", RoleNames.Student); using var client = Client(factory);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = "login-wrong", Password = "Wrong123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var audit = string.Join("|", factory.AuditLogs().Select(x => $"{x.Action}|{x.EntityName}|{x.EntityId}")); Assert.Contains("LOGIN_FAILURE", audit); Assert.DoesNotContain("Wrong123!", audit); Assert.DoesNotContain("PasswordHash", audit, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_UnknownUser_Returns401()
    {
        using var factory = NewFactory(); using var client = Client(factory);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = "unknown-user", Password = "Correct123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ValidJwt_IsAcceptedByProtectedEndpoint()
    {
        using var factory = NewFactory(); var userId = factory.SeedStudent("jwt-valid", "Correct123!", "Valid", "Student"); using var client = Client(factory);
        Bearer(client, Token(userId, RoleNames.Student));
        var response = await client.GetAsync("/api/students");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredJwt_IsRejected() => await AssertJwtRejected(Token(1, RoleNames.Student, DateTime.UtcNow.AddMinutes(-1)));

    [Fact]
    public async Task WrongSignatureJwt_IsRejected() => await AssertJwtRejected(Token(1, RoleNames.Student, key: "wrong-key-012345678901234567890123456789"));

    [Fact]
    public async Task WrongIssuerJwt_IsRejected() => await AssertJwtRejected(Token(1, RoleNames.Student, issuer: "WrongIssuer"));

    [Fact]
    public async Task WrongAudienceJwt_IsRejected() => await AssertJwtRejected(Token(1, RoleNames.Student, audience: "WrongAudience"));

    private static async Task AssertJwtRejected(string token)
    {
        using var factory = NewFactory(); using var client = Client(factory); Bearer(client, token);
        var response = await client.GetAsync("/api/students"); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginRateLimit_Returns429AfterSixthRequest()
    {
        using var factory = NewFactory(); factory.SeedUser("rate-user", "Correct123!", RoleNames.Student); using var client = Client(factory);
        HttpResponseMessage? last = null; for (var i = 0; i < 6; i++) last = await client.PostAsJsonAsync("/api/auth/login", new { Username = "rate-user", Password = "Correct123!" });
        Assert.Equal((HttpStatusCode)429, last!.StatusCode);
    }

    [Fact]
    public async Task StudentCannotCreateStudent_ButSchoolAdminCan()
    {
        using var factory = NewFactory(); factory.SeedUser("student-role", "Correct123!", RoleNames.Student); factory.SeedUser("school-admin", "Correct123!", RoleNames.SchoolAdmin);
        using var studentClient = Client(factory); Bearer(studentClient, await Login(studentClient, "student-role", "Correct123!"));
        var forbidden = await studentClient.PostAsJsonAsync("/api/students", new { Username = "newstudent", Password = "Correct123!", FirstName = "A", LastName = "B" }); Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var adminClient = Client(factory); Bearer(adminClient, await Login(adminClient, "school-admin", "Correct123!"));
        var created = await adminClient.PostAsJsonAsync("/api/students", new { Username = "newstudent", Password = "Correct123!", FirstName = "A", LastName = "B" }); Assert.Equal(HttpStatusCode.OK, created.StatusCode);
    }

    [Fact]
    public async Task StudentResourceIsolation_ReturnsOnlyOwnRecord()
    {
        using var factory = NewFactory(); var userA = factory.SeedStudent("student-a", "Correct123!", "Only", "A"); factory.SeedStudent("student-b", "Correct123!", "Other", "B"); using var client = Client(factory);
        Bearer(client, Token(userA, RoleNames.Student)); var response = await client.GetFromJsonAsync<JsonElement[]>("/api/students");
        Assert.NotNull(response); Assert.Single(response!); Assert.Equal("Only", response![0].GetProperty("firstName").GetString());
    }

    [Fact]
    public async Task StudentsTeachersCourses_ValidateInvalidBodies()
    {
        using var factory = NewFactory(); factory.SeedUser("validation-admin", "Correct123!", RoleNames.SchoolAdmin); using var client = Client(factory); Bearer(client, await Login(client, "validation-admin", "Correct123!"));
        var student = await client.PostAsJsonAsync("/api/students", new { Username = "x", Password = "short", FirstName = "", LastName = "" }); Assert.Equal(HttpStatusCode.BadRequest, student.StatusCode);
        var teacher = await client.PostAsJsonAsync("/api/teachers", new { Username = "x", Password = "short", FirstName = "", LastName = "", SubjectSpecialty = "" }); Assert.Equal(HttpStatusCode.BadRequest, teacher.StatusCode);
        var course = await client.PostAsJsonAsync("/api/courses", new { CourseName = "", TeacherId = 0 }); Assert.Equal(HttpStatusCode.BadRequest, course.StatusCode);
    }

    [Fact]
    public async Task GlobalExceptionHandler_ReturnsGeneric500WithoutStackOrSecret()
    {
        using var factory = NewFactory(); using var client = Client(factory); var response = await client.GetAsync("/__test__/throw");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode); var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("test-secret-stack", body, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("at SmartSchoolAPI", body, StringComparison.OrdinalIgnoreCase);
    }
}
