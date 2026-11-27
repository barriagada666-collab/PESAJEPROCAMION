using System.Globalization;
using System.Security.Cryptography;

namespace PesajeProCamion.Domain.Security;

/// <summary>Hash seguro PBKDF2-SHA256 con soporte de migración transparente desde contraseñas legacy en texto plano.</summary>
public static class PasswordHasher
{
    private const string Prefix = "PBKDF2";
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int DefaultIterations = 100_000;

    public static string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        string normalized = password.Trim().ToUpperInvariant();
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(normalized, salt, DefaultIterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Prefix}${DefaultIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool VerifyPassword(string enteredPassword, string storedHashOrPlaintext, out bool needsRehash)
    {
        needsRehash = false;
        if (string.IsNullOrWhiteSpace(enteredPassword) || string.IsNullOrWhiteSpace(storedHashOrPlaintext))
        {
            return false;
        }

        string normalizedInput = enteredPassword.Trim().ToUpperInvariant();
        if (storedHashOrPlaintext.StartsWith($"{Prefix}$", StringComparison.Ordinal))
        {
            string[] parts = storedHashOrPlaintext.Split('$');
            if (parts.Length != 4 ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int iterations))
            {
                return false;
            }

            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expectedKey = Convert.FromBase64String(parts[3]);
            byte[] actualKey = Rfc2898DeriveBytes.Pbkdf2(normalizedInput, salt, iterations, HashAlgorithmName.SHA256, expectedKey.Length);
            bool match = CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
            needsRehash = match && iterations < DefaultIterations;
            return match;
        }

        // Legacy plaintext comparison (constant-time on normalized UTF8 bytes) and flag automatic upgrade to PBKDF2
        byte[] a = System.Text.Encoding.UTF8.GetBytes(normalizedInput);
        byte[] b = System.Text.Encoding.UTF8.GetBytes(storedHashOrPlaintext.Trim().ToUpperInvariant());
        if (a.Length != b.Length)
        {
            return false;
        }

        bool legacyMatch = CryptographicOperations.FixedTimeEquals(a, b);
        if (legacyMatch)
        {
            needsRehash = true;
        }

        return legacyMatch;
    }
}
