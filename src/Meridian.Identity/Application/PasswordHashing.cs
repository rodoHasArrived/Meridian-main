using System.Security.Cryptography;

namespace Meridian.Identity;

/// <summary>
/// Password hashing helper for Meridian-managed local accounts.
/// </summary>
public static class PasswordHashing
{
    public const string Algorithm = "pbkdf2-sha256";
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int DefaultIterations = 210_000;

    public static string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt, DefaultIterations);
        return string.Join(
            '$',
            Algorithm,
            DefaultIterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public static bool VerifyPassword(string password, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(passwordHash))
        {
            return false;
        }

        var parts = passwordHash.Split('$', StringSplitOptions.TrimEntries);
        if (parts.Length != 4 ||
            !parts[0].Equals(Algorithm, StringComparison.Ordinal) ||
            !int.TryParse(parts[1], out var iterations) ||
            iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedHash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length < SaltSize || expectedHash.Length < HashSize)
        {
            return false;
        }

        var actualHash = Derive(password, salt, iterations, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    public static bool IsSupportedHash(string? passwordHash)
        => !string.IsNullOrWhiteSpace(passwordHash) &&
           passwordHash.Split('$', StringSplitOptions.TrimEntries) is [var algorithm, var iterations, var salt, var hash] &&
           algorithm.Equals(Algorithm, StringComparison.Ordinal) &&
           int.TryParse(iterations, out var parsedIterations) &&
           parsedIterations > 0 &&
           IsBase64(salt) &&
           IsBase64(hash);

    private static byte[] Derive(string password, byte[] salt, int iterations, int hashSize = HashSize)
        => Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            hashSize);

    private static bool IsBase64(string value)
    {
        try
        {
            Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
