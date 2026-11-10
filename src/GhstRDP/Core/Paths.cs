using System.IO;

namespace GhstRDP.Core;

public static class Paths
{
    /// <summary>
    /// Pasta alternativa (variável de ambiente GHSTRDP_HOME), útil para testar sem tocar nos dados reais.
    /// </summary>
    static readonly string? Override = Environment.GetEnvironmentVariable("GHSTRDP_HOME");

    public static string RoamingDir { get; } = string.IsNullOrWhiteSpace(Override)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GhstRDP")
        : Path.Combine(Override, "roaming");

    public static string LocalDir { get; } = string.IsNullOrWhiteSpace(Override)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GhstRDP")
        : Path.Combine(Override, "local");

    /// <summary>Sufixo para a trava de instância única: pasta de dados diferente = instância separada.</summary>
    public static string InstanceKey { get; } = string.IsNullOrWhiteSpace(Override)
        ? "default"
        : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(Override).ToLowerInvariant())))[..12];

    public static string DataFile => Path.Combine(RoamingDir, "data.json");
    public static string DataBackupFile => Path.Combine(RoamingDir, "data.json.bak");
    public static string BannersDir => Path.Combine(RoamingDir, "banners");
    public static string RdpDir => Path.Combine(LocalDir, "rdp");
    public static string ThumbsDir => Path.Combine(LocalDir, "thumbs");
    public static string LogsDir => Path.Combine(LocalDir, "logs");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(RoamingDir);
        Directory.CreateDirectory(RdpDir);
        Directory.CreateDirectory(ThumbsDir);
        Directory.CreateDirectory(LogsDir);
    }
}
