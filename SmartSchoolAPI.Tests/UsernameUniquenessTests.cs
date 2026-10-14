using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartSchoolAPI.Authorization;

namespace SmartSchoolAPI.Tests;

/// <summary>
/// P0-B contract: every account-creation path accepts exactly the same usernames and refuses a
/// name that already exists under the shared comparison (trim + invariant case fold), and login
/// never answers 500 when legacy duplicate rows exist.
///
/// These scenarios were previously covered by UsernameUniquenessCharacterizationTests, which
/// documented the vulnerable behaviour by asserting that duplicates, case variants and padded
/// variants were all accepted (and that login returned 500). That file was replaced by this one
/// once the application-level guard landed; the pre-fix results are recorded in
/// docs/engineering/P0_B_USERNAME_UNIQUENESS.md.
///
/// No unique index, migration or data change is involved: every test runs against the isolated
/// in-memory SQLite database created by <see cref="IntegrationTestFactory"/>.
/// </summary>
public sealed class UsernameUniquenessTests
{
    private const string Password = "Correct123!";

    private static HttpClient Client(IntegrationTestFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

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

    private static Task<HttpResponseMessage> CreateTeacher(HttpClient client, string username) =>
        client.PostAsJsonAsync("/api/teachers", new { Username = username, Password = Password, FirstName = "New", LastName = "Teacher", SubjectSpecialty = "Maths" });

    [Fact]
    public async Task ExactDuplicateUsername_IsRejected()
    {
        using var factory = new IntegrationTestFactory();
        using var admin = await AdminClient(factory, "dup-admin");

        using var first = await CreateStudent(admin, "duplicate-user");
        using var second = await CreateStudent(admin, "duplicate-user");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(1, factory.Usernames().Count(name => name == "duplicate-user"));
    }

    [Fact]
    public async Task CaseVariantUsername_IsRejected()
    {
        using var factory = new IntegrationTestFactory();
        using var admin = await AdminClient(factory, "case-admin");

        using var first = await CreateStudent(admin, "Case-Variant");
        using var second = await CreateStudent(admin, "case-variant");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(1, factory.Usernames().Count(name => name.Equals("case-variant", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task WhitespaceVariantUsername_IsRejectedAndStoredTrimmed()
    {
        using var factory = new IntegrationTestFactory();
        using var admin = await AdminClient(factory, "ws-admin");

        using var padded = await CreateStudent(admin, "  ws-variant  ");
        using var trimmed = await CreateStudent(admin, "ws-variant");

        Assert.Equal(HttpStatusCode.OK, padded.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, trimmed.StatusCode);
        Assert.Contains("ws-variant", factory.Usernames());
        Assert.DoesNotContain("  ws-variant  ", factory.Usernames());
    }

    [Fact]
    public async Task TeacherPath_AppliesTheSameRulesAsTheStudentPath()
    {
        using var factory = new IntegrationTestFactory();
        using var admin = await AdminClient(factory, "cross-admin");

        using var teacher = await CreateTeacher(admin, "cross-path-user");
        Assert.Equal(HttpStatusCode.OK, teacher.StatusCode);

        using var duplicateStudent = await CreateStudent(admin, "cross-path-user");
        using var duplicateTeacher = await CreateTeacher(admin, "CROSS-PATH-USER");

        Assert.Equal(HttpStatusCode.Conflict, duplicateStudent.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicateTeacher.StatusCode);
        Assert.Equal(1, factory.Usernames().Count(name => name.Equals("cross-path-user", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task GuardianPath_AppliesTheSameRulesAsTheOtherCreatePaths()
    {
        using var factory = new IntegrationTestFactory();
        var school = factory.SeedStructuredSchool("guard-rules");
        var adminUserId = factory.SeedUser("guard-rules-admin", Password, RoleNames.SchoolAdmin);
        factory.AddMembership(school.OrganizationId, adminUserId);
        using var admin = await AdminClient(factory, "guard-rules-admin");

        async Task<HttpResponseMessage> LinkGuardian(string username) =>
            await admin.PostAsJsonAsync($"/api/students/{school.StudentId}/guardians", new
            {
                username,
                password = Password,
                firstName = "Guardian",
                lastName = "Rules",
                relationship = "Father"
            });

        using var created = await LinkGuardian("guard-rules-parent");
        using var duplicate = await LinkGuardian("GUARD-RULES-PARENT");
        using var takenByStudent = await CreateStudent(admin, "guard-rules-parent");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, takenByStudent.StatusCode);
    }

    [Fact]
    public async Task ShortUsernameAfterTrimming_IsRejectedOnEveryCreatePath()
    {
        using var factory = new IntegrationTestFactory();
        var school = factory.SeedStructuredSchool("short-rules");
        var adminUserId = factory.SeedUser("short-rules-admin", Password, RoleNames.SchoolAdmin);
        factory.AddMembership(school.OrganizationId, adminUserId);
        using var admin = await AdminClient(factory, "short-rules-admin");

        using var student = await CreateStudent(admin, " ab ");
        using var teacher = await CreateTeacher(admin, " ab ");
        using var guardian = await admin.PostAsJsonAsync($"/api/students/{school.StudentId}/guardians", new
        {
            username = " ab ",
            password = Password,
            firstName = "Guardian",
            lastName = "Short",
            relationship = "Father"
        });

        Assert.Equal(HttpStatusCode.BadRequest, student.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, teacher.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, guardian.StatusCode);
        Assert.DoesNotContain(factory.Usernames(), name => name.Trim().Length < 3);
    }

    [Fact]
    public async Task OverLongUsername_IsRejectedOnEveryCreatePath()
    {
        using var factory = new IntegrationTestFactory();
        var school = factory.SeedStructuredSchool("long-rules");
        var adminUserId = factory.SeedUser("long-rules-admin", Password, RoleNames.SchoolAdmin);
        factory.AddMembership(school.OrganizationId, adminUserId);
        using var admin = await AdminClient(factory, "long-rules-admin");

        var tooLong = new string('u', 81);
        using var student = await CreateStudent(admin, tooLong);
        using var teacher = await CreateTeacher(admin, tooLong);
        using var guardian = await admin.PostAsJsonAsync($"/api/students/{school.StudentId}/guardians", new
        {
            username = tooLong,
            password = Password,
            firstName = "Guardian",
            lastName = "Long",
            relationship = "Father"
        });

        Assert.Equal(HttpStatusCode.BadRequest, student.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, teacher.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, guardian.StatusCode);
    }

    [Fact]
    public async Task ConcurrentCreates_OfAnAlreadyTakenName_AreBothRejected()
    {
        using var factory = new IntegrationTestFactory();
        using var firstClient = await AdminClient(factory, "taken-admin");
        factory.SeedUser("taken-user", Password, RoleNames.Student);

        using var secondClient = Client(factory);
        secondClient.DefaultRequestHeaders.Authorization = firstClient.DefaultRequestHeaders.Authorization;

        using var first = await CreateStudent(firstClient, "taken-user");
        using var second = await CreateStudent(secondClient, "taken-user");

        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(1, factory.Usernames().Count(name => name == "taken-user"));
    }

    [Fact]
    public async Task ConcurrentCreates_OfAFreshName_NeverFail_AndStoredRowsMatchSuccesses()
    {
        using var factory = new IntegrationTestFactory();
        using var firstClient = await AdminClient(factory, "race-admin");

        using var secondClient = Client(factory);
        secondClient.DefaultRequestHeaders.Authorization = firstClient.DefaultRequestHeaders.Authorization;

        var responses = await Task.WhenAll(
            CreateStudent(firstClient, "race-user"),
            CreateStudent(secondClient, "race-user"));

        // The application-level guard cannot be atomic without the database constraint, and the
        // test host shares a single in-memory SQLite connection (writes may serialize), so the
        // assertions are: every answer is a valid outcome, nothing crashes with 500, and exactly
        // one row exists per successful create. The unique index in the migration plan is what
        // makes this airtight under real concurrency.
        Assert.DoesNotContain(responses, response => response.StatusCode == HttpStatusCode.InternalServerError);
        Assert.All(responses, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));

        var created = responses.Count(response => response.StatusCode == HttpStatusCode.OK);
        Assert.True(created >= 1, "expected at least one create to succeed");
        Assert.Equal(created, factory.Usernames().Count(name => name == "race-user"));

        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public async Task LegacyDuplicateAccounts_Answer401InsteadOf500()
    {
        using var factory = new IntegrationTestFactory();
        factory.SeedDuplicateAccounts("legacy-duplicate", Password, RoleNames.Student);
        Assert.Equal(2, factory.Usernames().Count(name => name == "legacy-duplicate"));

        using var client = Client(factory);
        using var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new { Username = "legacy-duplicate", Password = "Wrong123!" });
        using var correctPassword = await client.PostAsJsonAsync("/api/auth/login", new { Username = "legacy-duplicate", Password });

        // Before P0-B the lookup used SingleOrDefaultAsync, which threw and produced a 500 that
        // both locked the owner out and revealed that the account existed.
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.OK, correctPassword.StatusCode);
    }

    [Fact]
    public async Task LoginLookup_RemainsExactMatchUntilTheNormalizationMigration()
    {
        using var factory = new IntegrationTestFactory();
        factory.SeedUser("Exact-Login", Password, RoleNames.Student);
        using var client = Client(factory);

        using var differentCase = await client.PostAsJsonAsync("/api/auth/login", new { Username = "exact-login", Password });
        using var exactCase = await client.PostAsJsonAsync("/api/auth/login", new { Username = "Exact-Login", Password });

        // Deliberate deferral (documented in docs/engineering/P0_B_USERNAME_UNIQUENESS.md): the
        // login lookup is unchanged in this phase because making it case-insensitive before the
        // duplicate scan would introduce ambiguity between legacy rows that differ only by case.
        // New accounts can no longer be registered under such a colliding name.
        Assert.Equal(HttpStatusCode.Unauthorized, differentCase.StatusCode);
        Assert.Equal(HttpStatusCode.OK, exactCase.StatusCode);
    }
}
