using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSchoolAPI.Authorization;

namespace SmartSchoolAPI.Tests;

public sealed class SafetyNetIntegrationTests
{
    [Fact]
    public async Task TwoSchools_RealLoginScopesEachRoleToItsOwnStudent()
    {
        using var factory = new IntegrationTestFactory();
        var schools = factory.SeedTwoSchools("safety-net");

        using var anonymous = factory.CreateClient();
        using var unauthenticated = await anonymous.GetAsync("/api/students");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        var cases = new[]
        {
            (schools.First.AdminUsername, schools.First.School.StudentId, schools.Second.School.StudentId),
            (schools.First.TeacherUsername, schools.First.School.StudentId, schools.Second.School.StudentId),
            (schools.First.ParentUsername, schools.First.School.StudentId, schools.Second.School.StudentId),
            (schools.First.StudentUsername, schools.First.School.StudentId, schools.Second.School.StudentId),
            (schools.Second.AdminUsername, schools.Second.School.StudentId, schools.First.School.StudentId)
        };

        foreach (var (username, ownStudentId, otherStudentId) in cases)
        {
            using var client = factory.CreateClient();
            await SignInAsync(client, username);

            var students = await client.GetFromJsonAsync<JsonElement[]>("/api/students");
            Assert.NotNull(students);
            Assert.Equal(ownStudentId, Assert.Single(students!).GetProperty("studentId").GetInt32());

            using var ownOverview = await client.GetAsync($"/api/students/{ownStudentId}/360");
            using var otherOverview = await client.GetAsync($"/api/students/{otherStudentId}/360");
            Assert.Equal(HttpStatusCode.OK, ownOverview.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, otherOverview.StatusCode);
        }
    }

    [Fact]
    public async Task SeparateFactories_DoNotShareTheirInMemoryDatabase()
    {
        using var firstFactory = new IntegrationTestFactory();
        var schools = firstFactory.SeedTwoSchools("database-isolation");
        using var firstClient = firstFactory.CreateClient();
        await SignInAsync(firstClient, schools.First.AdminUsername);

        using var secondFactory = new IntegrationTestFactory();
        secondFactory.SeedUser("fresh-super-admin", RoleNames.SuperAdmin);
        using var secondClient = secondFactory.CreateClient();
        await SignInAsync(secondClient, "fresh-super-admin");

        var emptyDatabase = await secondClient.GetFromJsonAsync<JsonElement[]>("/api/students");
        Assert.NotNull(emptyDatabase);
        Assert.Empty(emptyDatabase!);

        var firstDatabase = await firstClient.GetFromJsonAsync<JsonElement[]>("/api/students");
        Assert.NotNull(firstDatabase);
        Assert.Equal(schools.First.School.StudentId, Assert.Single(firstDatabase!).GetProperty("studentId").GetInt32());
    }

    private static async Task SignInAsync(HttpClient client, string username)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username,
            password = IntegrationTestFactory.DefaultPassword
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
