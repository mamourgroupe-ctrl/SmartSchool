using Microsoft.Extensions.DependencyInjection;
using SmartSchoolAPI.Authorization;
using SmartSchoolAPI.Data;
using SmartSchoolAPI.Security;

namespace SmartSchoolAPI.Tests;

/// <summary>
/// Unit tests for the single application-wide definition of "the same username" (P0-B).
/// </summary>
public sealed class UsernameRulesTests
{
    [Theory]
    [InlineData("  spaced  ", "spaced")]
    [InlineData("no-spaces", "no-spaces")]
    [InlineData("\ttabbed\t", "tabbed")]
    [InlineData("", "")]
    public void Normalize_RemovesSurroundingWhitespace(string input, string expected) =>
        Assert.Equal(expected, UsernameRules.Normalize(input));

    [Fact]
    public void Normalize_TreatsNullOrEmptyAsEmpty() => Assert.Equal(string.Empty, UsernameRules.Normalize(null));

    [Theory]
    [InlineData("  Ahmed  ", "ahmed")]
    [InlineData("AHMED", "ahmed")]
    [InlineData("ahmed", "ahmed")]
    public void ComparisonKey_FoldsWhitespaceAndCase(string input, string expected) =>
        Assert.Equal(expected, UsernameRules.ComparisonKey(input));

    [Fact]
    public void ComparisonKey_AgreesWithTheThrottleKey()
    {
        // The throttle groups attempts by the same key, which is what makes a case variant of a
        // taken account count as the same account instead of a free extra guess.
        var throttle = new LoginAttemptThrottle(new TestTimeProvider(), new LoginThrottleOptions { FailureLimit = 5 });

        for (var attempt = 0; attempt < 5; attempt++) throttle.RecordFailure("  MiXeD-Case  ");

        Assert.True(throttle.IsBlocked("mixed-case", out var retryAfterSeconds));
        Assert.InRange(retryAfterSeconds, 1, 60);
    }

    [Theory]
    [InlineData("ab", false)]
    [InlineData(" ab ", false)]
    [InlineData("abc", true)]
    [InlineData(" a-b_c ", true)]
    public void TryNormalize_RejectsShortNamesAfterTrimming(string input, bool expected)
    {
        var accepted = UsernameRules.TryNormalize(input, out var normalized, out var error);

        Assert.Equal(expected, accepted);
        Assert.Equal(input.Trim(), normalized);
        Assert.Equal(expected, error is null);
    }

    [Fact]
    public void TryNormalize_AcceptsTheBoundaryLengthsAndRejectsBeyond()
    {
        Assert.True(UsernameRules.TryNormalize(new string('u', UsernameRules.MaxLength), out _, out _));

        Assert.False(UsernameRules.TryNormalize(new string('u', UsernameRules.MaxLength + 1), out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public async Task IsTakenAsync_SeesCaseAndWhitespaceVariants()
    {
        using var factory = new IntegrationTestFactory();
        factory.SeedUser("Taken-User", "Correct123!", RoleNames.SchoolAdmin);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();

        Assert.True(await UsernameRules.IsTakenAsync(db, " taken-user "));
        Assert.True(await UsernameRules.IsTakenAsync(db, "TAKEN-USER"));
        Assert.True(await UsernameRules.IsTakenAsync(db, "Taken-User"));
        Assert.False(await UsernameRules.IsTakenAsync(db, "another-user"));
    }

    [Fact]
    public async Task IsTakenAsync_TreatsBlankInputAsAvailable()
    {
        using var factory = new IntegrationTestFactory();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();

        Assert.False(await UsernameRules.IsTakenAsync(db, "   "));
        Assert.False(await UsernameRules.IsTakenAsync(db, null));
    }
}
