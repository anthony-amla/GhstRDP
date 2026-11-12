using System.Security.Cryptography;
using System.Text;

namespace GhstRDP.Core;

/// <summary>DPAPI encryption scoped to the current Windows user on this machine.</summary>
public static class Secrets
{
    // Changing this makes every saved password unreadable.
    static readonly byte[] Entropy = "GhstRDP::senhas::v1"u8.ToArray();

    /// <returns>Base64 ciphertext, or null for an empty input.</returns>
    public static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        var bytes = Encoding.UTF8.GetBytes(plain);
        try
        {
            var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <returns>The plain text, or null when empty or not decryptable by this user.</returns>
    public static string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return null;
        try
        {
            var encrypted = Convert.FromBase64String(protectedValue);
            var bytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception ex)
        {
            Log.Error("Could not decrypt a password (different user or machine?)", ex);
            return null;
        }
    }
}
