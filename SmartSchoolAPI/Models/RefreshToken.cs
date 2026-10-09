namespace SmartSchoolAPI.Models;

public sealed class RefreshToken
{
    public long RefreshTokenId { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    // SHA-256 hex of the token; the raw token is never stored.
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAtUtc { get; set; }
}
