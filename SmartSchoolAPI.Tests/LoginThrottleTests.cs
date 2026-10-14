using System.Net;
using System.Net.Http.Json;
using SmartSchoolAPI.Authorization;
using SmartSchoolAPI.Security;

namespace SmartSchoolAPI.Tests;

/// <summary>
/// P0-A: failed login attempts on a single account are throttled for a short, self-clearing
/// window. There is intentionally no permanent lock, so an attacker cannot disable an account.
/// </summary>
public sealed class LoginThrottleTests
{
    private static LoginAttemptThrottle Create(TestTimeProvider time, int failureLimit = 5) =>
        new(time, new LoginThrottleOptions { FailureLimit = failureLimit, Window = TimeSpan.FromMinutes(1) });

    [Fact]
    public void BelowThreshold_IsNotBlocked()
    {
        var throttle = Create(new TestTimeProvider());

        for (var i = 0; i < 4; i++) throttle.RecordFailure("student-a");

        Assert.False(throttle.IsBlocked("student-a", out var retryAfter));
        Assert.Equal(0, retryAfter);
    }

    [Fact]
    public void AtThreshold_IsBlockedAndReportsRetryAfterWithinTheWindow()
    {
        var throttle = Create(new TestTimeProvider());

        for (var i = 0; i < 5; i++) throttle.RecordFailure("student-a");

        Assert.True(throttle.IsBlocked("student-a", out var retryAfter));
        Assert.InRange(retryAfter, 1, 60);
    }

    [Fact]
    public void SuccessfulLogin_ResetsTrackedFailures()
    {
        var throttle = Create(new TestTimeProvider());
        for (var i = 0; i < 5; i++) throttle.RecordFailure("student-a");
        Assert.True(throttle.IsBlocked("student-a", out _));

        throttle.Reset("student-a");

        Assert.False(throttle.IsBlocked("student-a", out var retryAfter));
        Assert.Equal(0, retryAfter);
    }

    [Fact]
    public void WindowRollover_ClearsTheThrottle_WithoutAnyPermanentLock()
    {
        var time = new TestTimeProvider();
        var throttle = Create(time);
        for (var i = 0; i < 5; i++) throttle.RecordFailure("student-a");
        Assert.True(throttle.IsBlocked("student-a", out _));

        time.Advance(TimeSpan.FromMinutes(1));

        Assert.False(throttle.IsBlocked("student-a", out _));
    }

    [Fact]
    public void ThrottlingOneAccount_DoesNotAffectAnother()
    {
        var throttle = Create(new TestTimeProvider());
        for (var i = 0; i < 5; i++) throttle.RecordFailure("student-a");

        Assert.True(throttle.IsBlocked("student-a", out _));
        Assert.False(throttle.IsBlocked("student-b", out _));
    }

    [Fact]
    public void Keys_AreTrimmedAndCaseInsensitive()
    {
        var throttle = Create(new TestTimeProvider());
        for (var i = 0; i < 5; i++) throttle.RecordFailure("  Student-A  ");

        Assert.True(throttle.IsBlocked("student-a", out _));
        Assert.True(throttle.IsBlocked("STUDENT-A", out _));
    }

    [Fact]
    public void OverlongIdentifiers_AreBounded()
    {
        var throttle = Create(new TestTimeProvider());
        var first = new string('a', 5000) + "-one";
        var second = new string('a', 5000) + "-two";

        for (var i = 0; i < 5; i++) throttle.RecordFailure(first);

        // Identifier length is capped, so both inputs share one bounded counter.
        Assert.True(throttle.IsBlocked(second, out _));
    }

    [Fact]
    public async Task DistributedFailures_AreThrottledPerAccount_AndReleaseAfterTheWindow()
    {
        using var factory = new IntegrationTestFactory();
        factory.SeedUser("throttle-user", "Correct123!", RoleNames.Student);

        // Five different clients (different quotas) fail once each on the same account.
        for (var i = 0; i < 5; i++)
        {
            using var attacker = factory.CreateClient();
            attacker.DefaultRequestHeaders.Add(TestClientIdentityProvider.HeaderName, $"attacker-{i}");
            using var failure = await attacker.PostAsJsonAsync(
                "/api/auth/login", new { Username = "throttle-user", Password = "Wrong123!" });
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        using var nextAttacker = factory.CreateClient();
        nextAttacker.DefaultRequestHeaders.Add(TestClientIdentityProvider.HeaderName, "attacker-final");
        using var throttled = await nextAttacker.PostAsJsonAsync(
            "/api/auth/login", new { Username = "throttle-user", Password = "Wrong123!" });
        Assert.Equal((HttpStatusCode)429, throttled.StatusCode);
        Assert.True(throttled.Headers.TryGetValues("Retry-After", out var retryAfter));
        Assert.NotEmpty(retryAfter!);
    }

    /// <summary>
    /// P0-A review regression test: throttling must never lock the account owner out. Before the
    /// fix the throttle was evaluated before credential verification, so five wrong passwords per
    /// minute from rotating addresses denied the owner access indefinitely and for free.
    /// </summary>
    [Fact]
    public async Task ThrottledAccount_OwnerWithCorrectPassword_CanStillAuthenticate()
    {
        using var factory = new IntegrationTestFactory();
        factory.SeedUser("lockout-user", "Correct123!", RoleNames.Student);

        for (var i = 0; i < 5; i++)
        {
            using var attacker = factory.CreateClient();
            attacker.DefaultRequestHeaders.Add(TestClientIdentityProvider.HeaderName, $"lockout-attacker-{i}");
            using var failure = await attacker.PostAsJsonAsync(
                "/api/auth/login", new { Username = "lockout-user", Password = "Wrong123!" });
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        using var prober = factory.CreateClient();
        prober.DefaultRequestHeaders.Add(TestClientIdentityProvider.HeaderName, "lockout-prober");
        using var stillThrottled = await prober.PostAsJsonAsync(
            "/api/auth/login", new { Username = "lockout-user", Password = "Wrong123!" });
        Assert.Equal((HttpStatusCode)429, stillThrottled.StatusCode);

        using var owner = factory.CreateClient();
        owner.DefaultRequestHeaders.Add(TestClientIdentityProvider.HeaderName, "lockout-owner");
        using var success = await owner.PostAsJsonAsync(
            "/api/auth/login", new { Username = "lockout-user", Password = "Correct123!" });
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
    }

    /// <summary>
    /// The window is evaluated against the injected clock, which also proves the test host really
    /// uses the frozen <c>TimeProvider</c> (with the system clock the last attempt would be 429).
    /// </summary>
    [Fact]
    public async Task FailedAttempts_AreForgottenAfterTheWindow()
    {
        using var factory = new IntegrationTestFactory();
        factory.SeedUser("window-user", "Correct123!", RoleNames.Student);

        for (var i = 0; i < 6; i++)
        {
            using var prober = factory.CreateClient();
            prober.DefaultRequestHeaders.Add(TestClientIdentityProvider.HeaderName, $"window-probe-{i}");
            using var response = await prober.PostAsJsonAsync(
                "/api/auth/login", new { Username = "window-user", Password = "Wrong123!" });
            Assert.Equal(i < 5 ? HttpStatusCode.Unauthorized : (HttpStatusCode)429, response.StatusCode);
        }

        factory.Time.Advance(TimeSpan.FromMinutes(1));

        using var afterWindow = factory.CreateClient();
        afterWindow.DefaultRequestHeaders.Add(TestClientIdentityProvider.HeaderName, "window-after");
        using var wrongAgain = await afterWindow.PostAsJsonAsync(
            "/api/auth/login", new { Username = "window-user", Password = "Wrong123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongAgain.StatusCode);
    }

    [Fact]
    public void TrackedAccounts_AreHardCapped()
    {
        const int capacity = 50;
        var throttle = new LoginAttemptThrottle(
            new TestTimeProvider(),
            new LoginThrottleOptions { FailureLimit = 5, Window = TimeSpan.FromMinutes(1), MaxTrackedAccounts = capacity });

        for (var i = 0; i < 500; i++) throttle.RecordFailure($"flooded-account-{i}");

        Assert.Equal(capacity, throttle.TrackedAccountCount);

        // Accounts that are already tracked keep working while the table is full.
        for (var i = 0; i < 5; i++) throttle.RecordFailure("flooded-account-0");
        Assert.True(throttle.IsBlocked("flooded-account-0", out _));
    }
}
