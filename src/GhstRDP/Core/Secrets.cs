using System.Security.Cryptography;
using System.Text;

namespace GhstRDP.Core;

/// <summary>
/// Criptografia de senhas com DPAPI (escopo do usuário atual do Windows).
/// Só o mesmo usuário, neste mesmo PC, consegue descriptografar.
/// </summary>
public static class Secrets
{
    static readonly byte[] Entropy = "GhstRDP::senhas::v1"u8.ToArray();

    public static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        var bytes = Encoding.UTF8.GetBytes(plain);
        try
        {
            var enc = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(enc);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public static string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return null;
        try
        {
            var enc = Convert.FromBase64String(protectedValue);
            var bytes = ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception ex)
        {
            Log.Error("Não foi possível descriptografar uma senha (outro usuário/PC?)", ex);
            return null;
        }
    }
}
