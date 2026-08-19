using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SmartSchoolAPI.Data;
using SmartSchoolAPI.Models;
using SmartSchoolAPI.Security;

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

        await WriteAuditAsync(user!.UserId, "LOGIN_SUCCESS", "User", user.UserId.ToString());
        return Ok(new LoginResponse { AccessToken = GenerateJwtToken(user), User = new UserDto { UserId = user.UserId, Username = user.Username, Role = user.Role } });
    }

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
