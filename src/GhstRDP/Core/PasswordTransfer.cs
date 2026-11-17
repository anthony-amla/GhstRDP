using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GhstRDP.Core;

public sealed class TransferPackage
{
    public int Version { get; set; } = 1;
    public DateTime ExportedAt { get; set; } = DateTime.Now;
    public List<TransferMachine> Machines { get; set; } = new();
}

public sealed class TransferMachine
{
    public string Name { get; set; } = "";
    public string Client { get; set; } = "";

    /// <summary>RDP login of the machine, or null for a vault-only machine.</summary>
    public TransferServer? Server { get; set; }
    public List<TransferEntry> Entries { get; set; } = new();
}

public sealed class TransferServer
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 3389;
    public string Username { get; set; } = "";
    public string Domain { get; set; } = "";
    public string? Password { get; set; }
    public ServerKind Kind { get; set; } = ServerKind.Vps;

    public string Address => Port == 3389 ? Host : $"{Host}:{Port}";
}

public sealed class TransferEntry
{
    public string Label { get; set; } = "";
    public string Username { get; set; } = "";
    public string? Password { get; set; }
    public string Notes { get; set; } = "";
}

/// <summary>Password-protected share codes and Discord-ready text for exported passwords.</summary>
public static class PasswordTransfer
{
    public const string CodePrefix = "GHSTRDP1.";
    public const string CodeFileFilter = "Código do Ghst RDP (*.ghstrdp)|*.ghstrdp|Texto (*.txt)|*.txt|Todos os arquivos|*.*";
    public const string MarkdownFileFilter = "Markdown (*.md)|*.md|Texto (*.txt)|*.txt";
    public const int MinCodePasswordLength = 6;

    const int SaltSize = 16, NonceSize = 12, TagSize = 16, Iterations = 310_000;

    static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Encrypts a package into a single-line code. Deliberately slow, call it off the UI thread.</summary>
    /// <param name="password">Shared with the recipient separately; needed to import the code.</param>
    public static string ToCode(TransferPackage package, string password)
    {
        var plain = Compress(JsonSerializer.SerializeToUtf8Bytes(package, Json));
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = DeriveKey(password, salt);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plain, cipher, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }

        var blob = new byte[SaltSize + NonceSize + TagSize + cipher.Length];
        salt.CopyTo(blob, 0);
        nonce.CopyTo(blob, SaltSize);
        tag.CopyTo(blob, SaltSize + NonceSize);
        cipher.CopyTo(blob, SaltSize + NonceSize + TagSize);
        return CodePrefix + Base64Url(blob);
    }

    /// <summary>Decrypts a code made by <see cref="ToCode"/>. Deliberately slow, call it off the UI thread.</summary>
    /// <exception cref="FormatException">The text is not a Ghst RDP code.</exception>
    /// <exception cref="CryptographicException">Wrong password or the code was changed.</exception>
    public static TransferPackage FromCode(string code, string password)
    {
        var compact = new string(code.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (!compact.StartsWith(CodePrefix, StringComparison.Ordinal))
            throw new FormatException("Isso não é um código do Ghst RDP.");

        byte[] blob;
        try { blob = FromBase64Url(compact[CodePrefix.Length..]); }
        catch (FormatException) { throw new FormatException("O código está incompleto ou foi alterado."); }
        if (blob.Length <= SaltSize + NonceSize + TagSize) throw new FormatException("O código está incompleto.");

        var salt = blob.AsSpan(0, SaltSize).ToArray();
        var key = DeriveKey(password, salt);
        var plain = new byte[blob.Length - SaltSize - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(blob.AsSpan(SaltSize, NonceSize), blob.AsSpan(SaltSize + NonceSize + TagSize),
                blob.AsSpan(SaltSize + NonceSize, TagSize), plain);
            var json = Decompress(plain);
            var package = JsonSerializer.Deserialize<TransferPackage>(json, Json) ?? throw new FormatException("O código está vazio.");
            package.Machines ??= new List<TransferMachine>();
            foreach (var machine in package.Machines) machine.Entries ??= new List<TransferEntry>();
            return package;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <returns>Discord-flavored Markdown with addresses and passwords hidden behind spoilers.</returns>
    public static string ToMarkdown(TransferPackage package)
    {
        var sb = new StringBuilder();
        foreach (var machine in package.Machines)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("### ").Append(Escape(machine.Name));
            if (!string.IsNullOrWhiteSpace(machine.Client)) sb.Append(" · ").Append(Escape(machine.Client));
            sb.Append('\n');

            if (machine.Server is { } server)
            {
                sb.Append("\n**Login do RDP**\n");
                sb.Append("Endereço: ").Append(Spoiler(server.Address)).Append('\n');
                AppendLogin(sb, FullUser(server), server.Password);
            }

            foreach (var entry in machine.Entries)
            {
                sb.Append("\n**").Append(Escape(string.IsNullOrWhiteSpace(entry.Label) ? "Senha" : entry.Label)).Append("**\n");
                AppendLogin(sb, entry.Username, entry.Password);
                if (!string.IsNullOrWhiteSpace(entry.Notes))
                    sb.Append("-# ").Append(Escape(entry.Notes.ReplaceLineEndings(" "))).Append('\n');
            }
        }
        return sb.ToString().TrimEnd() + "\n";
    }

    static void AppendLogin(StringBuilder sb, string username, string? password)
    {
        if (!string.IsNullOrWhiteSpace(username)) sb.Append("Usuário: ").Append(Code(username)).Append('\n');
        sb.Append("Senha: ").Append(string.IsNullOrEmpty(password) ? "(sem senha)" : Spoiler(password)).Append('\n');
    }

    static string FullUser(TransferServer server) =>
        string.IsNullOrWhiteSpace(server.Domain) ? server.Username : $"{server.Domain}\\{server.Username}";

    static string Spoiler(string value) => $"||{Code(value)}||";

    // A backtick would break the code span, and pipes inside one can close the spoiler early, so escape those instead.
    static string Code(string value) =>
        value.Contains('`') || value.Contains('|') ? Escape(value) : $"`{value}`";

    static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if ("\\*_~`|>#[]()".Contains(ch)) sb.Append('\\');
            sb.Append(ch);
        }
        return sb.ToString();
    }

    static byte[] DeriveKey(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);

    static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize)) brotli.Write(data);
        return output.ToArray();
    }

    static byte[] Decompress(byte[] data)
    {
        using var input = new BrotliStream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static byte[] FromBase64Url(string text)
    {
        var base64 = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }
}
