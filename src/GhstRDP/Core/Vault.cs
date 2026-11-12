using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GhstRDP.Core;

public sealed class VaultEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Server this entry belongs to, or null for a free-text <see cref="Machine"/>.</summary>
    public Guid? ServerId { get; set; }
    public string Machine { get; set; } = "";
    public string Label { get; set; } = "";
    public string Username { get; set; } = "";

    /// <summary>AES-256-GCM with the PIN key, wrapped in DPAPI for the current Windows user.</summary>
    public string? Secret { get; set; }
    public string Notes { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public sealed class VaultData
{
    public int Version { get; set; } = 1;
    public string? PinSalt { get; set; }
    /// <summary>SHA-256 of the verifier half of the PIN derivation. The PIN itself is never stored.</summary>
    public string? PinVerifier { get; set; }
    public int Iterations { get; set; } = 310_000;
    public List<VaultEntry> Entries { get; set; } = new();
}

public enum UnlockResult { Ok, WrongPin, LockedOut }

/// <summary>
/// PIN-protected password vault. PBKDF2-SHA256 turns the PIN into 64 bytes: the first half is the
/// AES-256 key (kept in memory only while unlocked), the second half is hashed and stored as the verifier.
/// </summary>
public sealed class VaultService
{
    // Changing this makes every vault entry unreadable.
    static readonly byte[] DpapiEntropy = "GhstRDP::cofre::v1"u8.ToArray();
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    readonly object _gate = new();
    byte[]? _key;
    long _lastActivity;
    int _failedAttempts;
    long _lockedUntil;

    public VaultData Data { get; private set; } = new();
    public TimeSpan AutoLockAfter { get; set; } = TimeSpan.FromMinutes(5);

    public string FilePath => Path.Combine(Paths.RoamingDir, "vault.json");
    string BackupPath => FilePath + ".bak";

    public bool IsConfigured => !string.IsNullOrEmpty(Data.PinSalt) && !string.IsNullOrEmpty(Data.PinVerifier);
    public bool IsUnlocked => _key != null;

    /// <summary>Seconds left in the wrong-PIN lockout, or 0.</summary>
    public int LockoutSecondsLeft => (int)Math.Max(0, Math.Ceiling((_lockedUntil - Environment.TickCount64) / 1000.0));

    public event Action? StateChanged;

    public static bool IsValidPin(string? pin) => pin is { Length: >= 4 and <= 12 } && pin.All(char.IsAsciiDigit);

    public void Load()
    {
        foreach (var path in new[] { FilePath, BackupPath })
        {
            try
            {
                if (!File.Exists(path)) continue;
                var data = JsonSerializer.Deserialize<VaultData>(File.ReadAllText(path, Encoding.UTF8), Json);
                if (data == null) continue;
                data.Entries ??= new List<VaultEntry>();
                Data = data;
                if (path == BackupPath) Log.Warn("Vault restored from backup");
                return;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to read vault ({path})", ex);
            }
        }
        Data = new VaultData();
    }

    public void Save()
    {
        lock (_gate)
        {
            AtomicFile.Write(FilePath, JsonSerializer.Serialize(Data, Json), BackupPath);
        }
    }

    static (byte[] Key, byte[] VerifierHash) Derive(string pin, byte[] salt, int iterations)
    {
        var material = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, iterations, HashAlgorithmName.SHA256, 64);
        try
        {
            var key = material[..32];
            var verifierHash = SHA256.HashData(material.AsSpan(32, 32));
            return (key, verifierHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    /// <summary>Sets the PIN on first use and leaves the vault unlocked. Deliberately slow, call it off the UI thread.</summary>
    public void SetupPin(string pin)
    {
        if (!IsValidPin(pin)) throw new ArgumentException("O PIN precisa ter de 4 a 12 números.");
        if (IsConfigured) throw new InvalidOperationException("O PIN já foi definido.");

        var salt = RandomNumberGenerator.GetBytes(16);
        var (key, verifierHash) = Derive(pin, salt, Data.Iterations);
        Data.PinSalt = Convert.ToBase64String(salt);
        Data.PinVerifier = Convert.ToBase64String(verifierHash);
        Save();
        SetKey(key);
    }

    /// <summary>Checks the PIN and unlocks on success. Deliberately slow, call it off the UI thread.</summary>
    public UnlockResult Unlock(string pin)
    {
        if (!IsConfigured) return UnlockResult.WrongPin;
        if (LockoutSecondsLeft > 0) return UnlockResult.LockedOut;

        var (key, verifierHash) = Derive(pin ?? "", Convert.FromBase64String(Data.PinSalt!), Data.Iterations);
        var expected = Convert.FromBase64String(Data.PinVerifier!);
        if (CryptographicOperations.FixedTimeEquals(verifierHash, expected))
        {
            _failedAttempts = 0;
            SetKey(key);
            return UnlockResult.Ok;
        }

        CryptographicOperations.ZeroMemory(key);
        _failedAttempts++;
        if (_failedAttempts % 5 == 0)
        {
            // Lockout doubles every 5 consecutive misses: 30 s, 60 s, 120 s... capped at 15 min.
            var seconds = 30 * Math.Pow(2, _failedAttempts / 5 - 1);
            _lockedUntil = Environment.TickCount64 + (long)(Math.Min(seconds, 900) * 1000);
            Log.Warn($"Vault: {_failedAttempts} wrong PINs, locked for {seconds:0} s");
            return UnlockResult.LockedOut;
        }
        return UnlockResult.WrongPin;
    }

    void SetKey(byte[] key)
    {
        lock (_gate)
        {
            if (_key != null) CryptographicOperations.ZeroMemory(_key);
            _key = key;
            _lastActivity = Environment.TickCount64;
        }
        StateChanged?.Invoke();
    }

    public void Lock()
    {
        lock (_gate)
        {
            if (_key == null) return;
            CryptographicOperations.ZeroMemory(_key);
            _key = null;
        }
        StateChanged?.Invoke();
    }

    /// <summary>Postpones the auto-lock.</summary>
    public void Touch() => _lastActivity = Environment.TickCount64;

    public void CheckAutoLock()
    {
        if (IsUnlocked && Environment.TickCount64 - _lastActivity > AutoLockAfter.TotalMilliseconds) Lock();
    }

    public string Protect(string plain)
    {
        byte[] key;
        lock (_gate) key = _key ?? throw new InvalidOperationException("Cofre bloqueado.");
        Touch();

        var data = Encoding.UTF8.GetBytes(plain);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[data.Length];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, data, cipher, tag);
        CryptographicOperations.ZeroMemory(data);

        var blob = new byte[12 + 16 + cipher.Length];
        nonce.CopyTo(blob, 0);
        tag.CopyTo(blob, 12);
        cipher.CopyTo(blob, 28);
        return Convert.ToBase64String(ProtectedData.Protect(blob, DpapiEntropy, DataProtectionScope.CurrentUser));
    }

    public string? Unprotect(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return null;
        byte[] key;
        lock (_gate) key = _key ?? throw new InvalidOperationException("Cofre bloqueado.");
        Touch();

        try
        {
            var blob = ProtectedData.Unprotect(Convert.FromBase64String(secret), DpapiEntropy, DataProtectionScope.CurrentUser);
            var plain = new byte[blob.Length - 28];
            using (var aes = new AesGcm(key, 16)) aes.Decrypt(blob.AsSpan(0, 12), blob.AsSpan(28), blob.AsSpan(12, 16), plain);
            try { return Encoding.UTF8.GetString(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception ex)
        {
            Log.Error("Could not decrypt a vault entry", ex);
            return null;
        }
    }

    /// <summary>Wipes the vault. Used when the PIN is forgotten, since entries can't be recovered without it.</summary>
    public void Reset()
    {
        Lock();
        Data = new VaultData();
        _failedAttempts = 0;
        _lockedUntil = 0;
        Save();
        try { File.Delete(BackupPath); }
        catch (Exception ex) { Log.Warn("Failed to delete vault backup", ex); }
        StateChanged?.Invoke();
    }
}
