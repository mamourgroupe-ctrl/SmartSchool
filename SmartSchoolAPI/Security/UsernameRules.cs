using Microsoft.EntityFrameworkCore;
using SmartSchoolAPI.Data;

namespace SmartSchoolAPI.Security;

/// <summary>
/// The single definition of what "the same username" means.
///
/// Before this type existed the application carried three different answers: the login lookup
/// compared case-sensitively and without trimming, the guardians creation path compared
/// case-sensitively but trimmed, and <see cref="LoginAttemptThrottle"/> compared
/// case-insensitively after trimming. Every creation path now calls these helpers, so a new
/// account cannot be registered under a name that collides with an existing one.
///
/// Policy (see docs/engineering/P0_B_USERNAME_UNIQUENESS.md):
/// - stored form  : trailing/leading whitespace removed (<see cref="Normalize"/>);
/// - comparison   : stored form, case-folded with the invariant culture
///                  (<see cref="ComparisonKey"/>), which is exactly how the login throttle
///                  already grouped attempts.
///
/// The comparison is deliberately done in .NET rather than in SQL: SQLite folds case for ASCII
/// only, so a SQL-side comparison would answer differently from the throttle for non-ASCII
/// usernames. The users table is small in a single-school deployment; the scalable form of the
/// same rule is the maintained normalized column plus a unique index described in the P0-B
/// migration plan (not applied).
/// </summary>
public static class UsernameRules
{
    /// <summary>Minimum length of the stored (trimmed) username. Matches the request DTOs.</summary>
    public const int MinLength = 3;

    /// <summary>Maximum length of the stored (trimmed) username. Matches the guardians command contract.</summary>
    public const int MaxLength = 80;

    /// <summary>Stored form: surrounding whitespace removed.</summary>
    public static string Normalize(string? username) => (username ?? string.Empty).Trim();

    /// <summary>Comparison form: stored form, case-folded with the invariant culture.</summary>
    public static string ComparisonKey(string? username) => Normalize(username).ToLowerInvariant();

    /// <summary>
    /// Validates and normalizes a requested username. The length rules are applied to the trimmed
    /// value, so a padded short name such as <c>" ab "</c> is rejected instead of being stored
    /// as a two-character account.
    /// </summary>
    public static bool TryNormalize(string? username, out string normalized, out string? error)
    {
        normalized = Normalize(username);
        if (normalized.Length < MinLength)
        {
            error = $"Username must be at least {MinLength} characters long.";
            return false;
        }

        if (normalized.Length > MaxLength)
        {
            error = $"Username must be at most {MaxLength} characters long.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// True when any stored account has the same comparison key, i.e. the same username after
    /// trimming and case folding.
    /// </summary>
    public static async Task<bool> IsTakenAsync(SchoolDbContext db, string? username, CancellationToken cancellationToken = default)
    {
        var key = ComparisonKey(username);
        if (key.Length == 0) return false;

        var stored = await db.Users.AsNoTracking().Select(u => u.Username).ToListAsync(cancellationToken);
        return stored.Any(name => ComparisonKey(name) == key);
    }
}
