using System.Security.Cryptography;
using System.Text;

namespace SmartSchoolAPI.Security;

public static class RefreshTokenService
{
    public static (string Token, string Hash) Generate()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        return (token, Hash(token));
    }

    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
