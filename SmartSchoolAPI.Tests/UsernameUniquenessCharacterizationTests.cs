using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartSchoolAPI.Authorization;

namespace SmartSchoolAPI.Tests;

/// <summary>
/// P0-B (inspection phase): these tests PIN the current, vulnerable behaviour so the gap is
/// proven by execution instead of argued in prose. They are deliberately written so that they
/// pass today — a green run is the evidence that the defect exists.
///
/// ⚠ When the username-uniqueness guard lands (see docs/engineering/P0_B_USERNAME_UNIQUENESS.md),
/// every assertion below must be INVERTED (duplicates rejected with 409 instead of accepted, and
/// login must no longer return 500). This file should then be deleted or converted; it must not
/// be kept as an endorsement of the current behaviour.
///
/// No unique index, migration or data change is applied by this test file: everything runs
/// against an isolated in-memory SQLite database created by <see cref="IntegrationTestFactory"/>.
/// </summary>
public sealed class UsernameUniquenessCharacterizationTests
{
    private const string Password = "Correct123!";

    private static HttpClient Client(IntegrationTestFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        return client;
    }

    private static async Task<string> Login(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = username, Password = password });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static async Task<HttpClient> AdminClient(IntegrationTestFactory factory, string username)
    {
        factory.SeedUser(username, Password, RoleNames.SchoolAdmin);
        var client = Client(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Login(client, username, Password));
        return client;
    }

    private static Task<HttpResponseMessage> CreateStudent(HttpClient client, string username) =>
        client.PostAsJsonAsync("/api/students", new { Username = username, Password = Password, FirstName = "New", LastName = "Student" });

    [Fact]
    public async Task ExactDuplicateUsername_IsCurrentlyAccepted()
    {
        using var factory = new IntegrationTestFactory();
        using var admin = await AdminClient(factory, "dup-admin");

        using var first = await CreateStudent(admin, "duplicate-user");
        using var second = await CreateStudent(admin, "duplicate-user");

        // CURRENT behaviour: both creations succeed and two rows share one username.
        // P0-B fix: the second must be rejected with 409 Conflict.
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(2, factory.Usernames().Count(name => name == "duplicate-user"));
    }

    [Fact]
    public async Task CaseVariantUsername_IsCurrentlyAccepted()
    {
        using var factory = new IntegrationTestFactory();
        using var admin = await AdminClient(factory, "case-admin");

        using var first = await CreateStudent(admin, "Case-Variant");
        using var second = await CreateStudent(admin, "case-variant");

        // CURRENT behaviour: usernames differing only by case are distinct accounts.
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(2, factory.Usernames().Count(name => name.Equals("case-variant", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task WhitespaceVariantUsername_IsCurrentlyAcceptedAndStoredUnchanged()
    {
        using var factory = new IntegrationTestFactory();
        using var admin = await AdminClient(factory, "ws-admin");

        using var padded = await CreateStudent(admin, "  ws-variant  ");
        using var trimmed = await CreateStudent(admin, "ws-variant");

        // CURRENT behaviour: the create path does not trim, so the padded username is stored as
        // sent and becomes a third account. (The guardians path does trim — the paths disagree.)
        Assert.Equal(HttpStatusCode.OK, padded.StatusCode);
        Assert.Equal(HttpStatusCode.OK, trimmed.StatusCode);
        Assert.Contains("  ws-variant  ", factory.Usernames());
        Assert.Contains("ws-variant", factory.Usernames());
    }

    [Fact]
    public async Task ConcurrentCreates_WithTheSameUsername_BothCurrentlySucceed()
    {
        using var factory = new IntegrationTestFactory();
        using var firstClient = await AdminClient(factory, "race-admin");
        using var secondClient = Client(factory);
        secondClient.DefaultRequestHeaders.Authorization = firstClient.DefaultRequestHeaders.Authorization;

        var responses = await Task.WhenAll(
            CreateStudent(firstClient, "race-user"),
            CreateStudent(secondClient, "race-user"));

        // The key claim: nothing rejects the second concurrent create with the same username.
        // (The test host shares one in-memory SQLite connection, so the writes may be serialized
        // by the provider; the assertion below is therefore about the absence of a guard rather
        // than about how many rows the provider managed to commit.)
        Assert.DoesNotContain(responses, response => response.StatusCode == HttpStatusCode.Conflict);

        var created = responses.Count(response => response.StatusCode == HttpStatusCode.OK);
        Assert.True(created >= 1, "expected at least one create to succeed");
        Assert.Equal(created, factory.Usernames().Count(name => name == "race-user"));

        // P0-B fix: exactly one create must succeed and the other must return 409 Conflict.
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public async Task Login_WithDifferentCaseThanStored_CurrentlyFails()
    {
        using var factory = new IntegrationTestFactory();
        factory.SeedUser("Case-Sensitive", Password, RoleNames.Student);
        using var client = Client(factory);

        using var differentCase = await client.PostAsJsonAsync("/api/auth/login", new { Username = "case-sensitive", Password });
        using var exactCase = await client.PostAsJsonAsync("/api/auth/login", new { Username = "Case-Sensitive", Password });

        // CURRENT behaviour: the lookup is case-sensitive (SQLite BINARY comparison), so the
        // throttle (which normalizes case) and the login lookup disagree on what "the same
        // username" means. P0-B must define one normalization policy for both.
        Assert.Equal(HttpStatusCode.Unauthorized, differentCase.StatusCode);
        Assert.Equal(HttpStatusCode.OK, exactCase.StatusCode);
    }

    [Fact]
    public async Task ExactDuplicateUsers_MakeLoginReturn500()
    {
        using var factory = new IntegrationTestFactory();
        using var admin = await AdminClient(factory, "crash-admin");

        using var first = await CreateStudent(admin, "crash-user");
        using var second = await CreateStudent(admin, "crash-user");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var client = Client(factory);
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { Username = "crash-user", Password });

        // CURRENT behaviour: AuthController uses SingleOrDefaultAsync, which throws on two
        // matching rows; the global handler turns that into a 500 (not 401).
        // P0-B fix: credentials must resolve to a single account and return 401/200 as usual.
        Assert.Equal(HttpStatusCode.InternalServerError, login.StatusCode);
        var body = await login.Content.ReadAsStringAsync();
        Assert.Contains("An unexpected error occurred", body);
        Assert.DoesNotContain("SmartSchoolAPI", body);
    }

    [Fact]
    public async Task GuardiansPath_IsTheOnlyCreatePathThatRejectsDuplicates()
    {
        using var factory = new IntegrationTestFactory();
        var school = factory.SeedStructuredSchool("guard-dup");
        var adminUserId = factory.SeedUser("guard-admin", Password, RoleNames.SchoolAdmin);
        factory.AddMembership(school.OrganizationId, adminUserId);
        using var admin = await AdminClient(factory, "guard-admin");

        using var response = await admin.PostAsJsonAsync($"/api/students/{school.StudentId}/guardians", new
        {
            username = "guard-dup-student",
            password = Password,
            firstName = "Guardian",
            lastName = "Duplicate",
            relationship = "Father"
        });

        // CURRENT behaviour (and correct): only the guardians path checks uniqueness.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
