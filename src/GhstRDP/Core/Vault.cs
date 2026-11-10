using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GhstRDP.Core;

public sealed class VaultEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Servidor ao qual a senha pertence (null = máquina digitada em <see cref="Machine"/>).</summary>
    public Guid? ServerId { get; set; }
    public string Machine { get; set; } = "";
    public string Label { get; set; } = "";
    public string Username { get; set; } = "";

    /// <summary>Senha criptografada: AES-256-GCM com a chave do PIN e, por cima, DPAPI do usuário do Windows.</summary>
    public string? Secret { get; set; }
    public string Notes { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public sealed class VaultData
{
    public int Version { get; set; } = 1;
    public string? PinSalt { get; set; }
    /// <summary>SHA-256 da metade "verificador" do PBKDF2 do PIN. O PIN em si nunca é salvo.</summary>
    public string? PinVerifier { get; set; }
    public int Iterations { get; set; } = 310_000;
    public List<VaultEntry> Entries { get; set; } = new();
}

public enum UnlockResult { Ok, PinErrado, Bloqueado }

/// <summary>
/// Cofre de senhas protegido por PIN.
/// PIN → PBKDF2-SHA256 (310 mil iterações, salt aleatório) → 64 bytes:
///   32 primeiros = chave AES-256 das senhas (só fica na memória enquanto desbloqueado)
///   32 últimos   = verificador (salvo só o SHA-256 dele, para conferir o PIN)
/// Cada senha: AES-256-GCM (chave do PIN) e depois DPAPI (usuário do Windows).
/// </summary>
public sealed class VaultService
{
    static readonly byte[] DpapiEntropy = "GhstRDP::cofre::v1"u8.ToArray();
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    readonly object _gate = new();
    byte[]? _key;
    DateTime _lastActivity;
    int _failedAttempts;
    DateTime _lockedUntil = DateTime.MinValue;

    public VaultData Data { get; private set; } = new();
    public TimeSpan AutoLockAfter { get; set; } = TimeSpan.FromMinutes(5);

    public string FilePath => Path.Combine(Paths.RoamingDir, "vault.json");
    string BackupPath => FilePath + ".bak";

    public bool IsConfigured => !string.IsNullOrEmpty(Data.PinSalt) && !string.IsNullOrEmpty(Data.PinVerifier);
    public bool IsUnlocked => _key != null;

    /// <summary>Segundos que ainda faltam do bloqueio por PIN errado (0 = liberado).</summary>
    public int LockoutSecondsLeft => Math.Max(0, (int)Math.Ceiling((_lockedUntil - DateTime.Now).TotalSeconds));

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
                if (path == BackupPath) Log.Warn("Cofre restaurado do backup");
                return;
            }
            catch (Exception ex)
            {
                Log.Error($"Falha ao ler o cofre ({path})", ex);
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

    /// <summary>Primeiro uso: define o PIN e já deixa o cofre aberto. Lento de propósito (rodar fora da UI).</summary>
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

    /// <summary>Confere o PIN. Lento de propósito (rodar fora da UI).</summary>
    public UnlockResult Unlock(string pin)
    {
        if (!IsConfigured) return UnlockResult.PinErrado;
        if (LockoutSecondsLeft > 0) return UnlockResult.Bloqueado;

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
            // 30 s, 60 s, 120 s... a cada 5 erros seguidos
            var seconds = 30 * Math.Pow(2, _failedAttempts / 5 - 1);
            _lockedUntil = DateTime.Now.AddSeconds(Math.Min(seconds, 900));
            Log.Warn($"Cofre: {_failedAttempts} PINs errados, bloqueado por {seconds:0} s");
            return UnlockResult.Bloqueado;
        }
        return UnlockResult.PinErrado;
    }

    void SetKey(byte[] key)
    {
        lock (_gate)
        {
            if (_key != null) CryptographicOperations.ZeroMemory(_key);
            _key = key;
            _lastActivity = DateTime.Now;
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

    /// <summary>Marca uso (adia o bloqueio automático).</summary>
    public void Touch() => _lastActivity = DateTime.Now;

    public void CheckAutoLock()
    {
        if (IsUnlocked && DateTime.Now - _lastActivity > AutoLockAfter) Lock();
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
            Log.Error("Não foi possível abrir uma senha do cofre", ex);
            return null;
        }
    }

    /// <summary>Troca o PIN e recriptografa todas as senhas. Precisa estar desbloqueado.</summary>
    public void ChangePin(string newPin)
    {
        if (!IsValidPin(newPin)) throw new ArgumentException("O PIN precisa ter de 4 a 12 números.");
        if (!IsUnlocked) throw new InvalidOperationException("Cofre bloqueado.");

        var plains = Data.Entries.Select(e => (e, Unprotect(e.Secret))).ToList();
        var salt = RandomNumberGenerator.GetBytes(16);
        var (key, verifierHash) = Derive(newPin, salt, Data.Iterations);
        SetKey(key);
        foreach (var (entry, plain) in plains)
            entry.Secret = plain == null ? null : Protect(plain);
        Data.PinSalt = Convert.ToBase64String(salt);
        Data.PinVerifier = Convert.ToBase64String(verifierHash);
        Save();
    }

    /// <summary>Esqueceu o PIN: apaga o cofre inteiro (as senhas não têm como ser recuperadas sem o PIN).</summary>
    public void Reset()
    {
        Lock();
        Data = new VaultData();
        _failedAttempts = 0;
        _lockedUntil = DateTime.MinValue;
        Save();
        StateChanged?.Invoke();
    }
}
