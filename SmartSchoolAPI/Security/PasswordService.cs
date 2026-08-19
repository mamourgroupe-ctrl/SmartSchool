using System.Security.Cryptography;
namespace SmartSchoolAPI.Security;
public static class PasswordService
{
    private const int Iterations = 210000;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
                return string.Join((char)36, "PBKDF2", "SHA256", Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(key));
    }
    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split((char)36);
                if (parts.Length != 5 || parts[0] != "PBKDF2" || parts[1] != "SHA256" || !int.TryParse(parts[2], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}
