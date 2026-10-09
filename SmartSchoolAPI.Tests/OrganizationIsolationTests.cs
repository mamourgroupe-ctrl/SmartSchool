using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SmartSchoolAPI.Authorization;

namespace SmartSchoolAPI.Tests;

public sealed class OrganizationIsolationTests
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
    public async Task SchoolAdminCannotCreateCourse_ForTeacherInOtherOrganization()
    {
        using var factory = NewFactory();
        var mine = factory.SeedStructuredSchool("course-iso");
        var other = factory.SeedStructuredSchool("course-iso-other");
        var adminUserId = factory.SeedUser("course-iso-admin", RoleNames.SchoolAdmin);
        factory.AddMembership(mine.OrganizationId, adminUserId);

        using var client = Client(factory, Token(adminUserId, RoleNames.SchoolAdmin));
        var response = await client.PostAsJsonAsync("/api/courses", new { CourseName = "Math", TeacherId = other.TeacherId!.Value });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SchoolAdminCanCreateCourse_ForOwnOrganizationTeacher()
    {
        using var factory = NewFactory();
        var mine = factory.SeedStructuredSchool("course-ok");
        var adminUserId = factory.SeedUser("course-ok-admin", RoleNames.SchoolAdmin);
        factory.AddMembership(mine.OrganizationId, adminUserId);

        using var client = Client(factory, Token(adminUserId, RoleNames.SchoolAdmin));
        var response = await client.PostAsJsonAsync("/api/courses", new { CourseName = "Math", TeacherId = mine.TeacherId!.Value });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdminCanCreateCourse_ForAnyTeacher()
    {
        using var factory = NewFactory();
        var school = factory.SeedStructuredSchool("course-root");
        var rootUserId = factory.SeedUser("course-root-admin", RoleNames.SuperAdmin);

        using var client = Client(factory, Token(rootUserId, RoleNames.SuperAdmin));
        var response = await client.PostAsJsonAsync("/api/courses", new { CourseName = "Math", TeacherId = school.TeacherId!.Value });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
