using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SmartSchoolAPI.Data;
using SmartSchoolAPI.Models;
using SmartSchoolAPI.Security;
using SmartSchoolAPI.Services;

namespace SmartSchoolAPI.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController(SchoolDbContext context, IConfiguration configuration) : ControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginDto request)
    {
        var user = await context.Users.SingleOrDefaultAsync(u => u.Username == request.Username);
        var valid = user is not null && !string.IsNullOrWhiteSpace(user.PasswordHash) && PasswordService.Verify(request.Password, user.PasswordHash);
        if (!valid)
        {
            await WriteAuditAsync(user?.UserId, "LOGIN_FAILURE", "User", user?.UserId.ToString());
            return Unauthorized(new { success = false, message = "Invalid username or password." });
        }

        var (refreshToken, refreshHash) = RefreshTokenService.Generate();
        context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user!.UserId,
            TokenHash = refreshHash,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(RefreshTokenLifetimeDays())
        });
        await WriteAuditAsync(user!.UserId, "LOGIN_SUCCESS", "User", user.UserId.ToString());
        return Ok(new LoginResponse { AccessToken = GenerateJwtToken(user!), RefreshToken = refreshToken, User = new UserDto { UserId = user!.UserId, Username = user!.Username, Role = user!.Role } });
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("refresh")]
    public async Task<IActionResult> Refresh(RefreshTokenDto request)
    {
        var hash = RefreshTokenService.Hash(request.RefreshToken);
        var stored = await context.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash);
        if (stored is null || stored.RevokedAtUtc != null || stored.ExpiresAtUtc <= DateTime.UtcNow)
        {
            await WriteAuditAsync(null, "TOKEN_REFRESH_FAILURE", "RefreshToken", null);
            return Unauthorized(new { success = false, message = "Invalid or expired refresh token." });
        }

        var user = await context.Users.SingleOrDefaultAsync(u => u.UserId == stored.UserId);
        if (user is null)
        {
            await WriteAuditAsync(null, "TOKEN_REFRESH_FAILURE", "RefreshToken", null);
            return Unauthorized(new { success = false, message = "Invalid or expired refresh token." });
        }

        // Rotate: revoke the presented token and issue a fresh token pair.
        stored.RevokedAtUtc = DateTime.UtcNow;
        var (refreshToken, refreshHash) = RefreshTokenService.Generate();
        context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.UserId,
            TokenHash = refreshHash,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(RefreshTokenLifetimeDays())
        });
        await WriteAuditAsync(user.UserId, "TOKEN_REFRESHED", "User", user.UserId.ToString());
        return Ok(new LoginResponse { AccessToken = GenerateJwtToken(user), RefreshToken = refreshToken, User = new UserDto { UserId = user.UserId, Username = user.Username, Role = user.Role } });
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (!Stage1AccessService.TryUserId(User, out var userId))
            return Unauthorized(new { success = false, message = "Invalid token." });

        var activeTokens = await context.RefreshTokens.Where(x => x.UserId == userId && x.RevokedAtUtc == null).ToListAsync();
        foreach (var token in activeTokens) token.RevokedAtUtc = DateTime.UtcNow;
        await WriteAuditAsync(userId, "LOGOUT", "User", userId.ToString());
        return Ok(new { success = true });
    }

    private int RefreshTokenLifetimeDays() =>
        int.TryParse(configuration["Jwt:RefreshTokenLifetimeDays"], out var days) && days > 0 ? days : 7;

    private async Task WriteAuditAsync(int? userId, string action, string entityName, string? entityId)
    {
        context.AuditLogs.Add(new AuditLog { UserId = userId, Action = action, EntityName = entityName, EntityId = entityId, TimestampUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();
    }

    private string GenerateJwtToken(User user)
    {
        var jwt = configuration.GetSection("Jwt");
        var key = jwt["Key"] ?? Environment.GetEnvironmentVariable("SMARTSCHOOL_JWT_KEY") ?? throw new InvalidOperationException("JWT key is not configured.");
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, user.Username), new Claim("UserId", user.UserId.ToString()), new Claim(ClaimTypes.Role, user.Role), new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()) };
        var token = new JwtSecurityToken(jwt["Issuer"], jwt["Audience"], claims, expires: DateTime.UtcNow.AddHours(2), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public sealed class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public UserDto User { get; set; } = new();
}

public sealed class UserDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public sealed class LoginDto
{
    [Required, MinLength(3)] public string Username { get; set; } = string.Empty;
    [Required, MinLength(8)] public string Password { get; set; } = string.Empty;
}

public sealed class RefreshTokenDto
{
    [Required, MinLength(10)] public string RefreshToken { get; set; } = string.Empty;
}
