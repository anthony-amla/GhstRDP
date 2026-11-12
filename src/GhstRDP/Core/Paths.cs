using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GhstRDP.Core;

public static class Paths
{
    /// <summary>Optional data folder from GHSTRDP_HOME, handy for testing without touching real data.</summary>
    static readonly string? Override = Environment.GetEnvironmentVariable("GHSTRDP_HOME");

    static bool HasOverride => !string.IsNullOrWhiteSpace(Override);

    public static string RoamingDir { get; } = HasOverride
        ? Path.Combine(Override!, "roaming")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GhstRDP");

    public static string LocalDir { get; } = HasOverride
        ? Path.Combine(Override!, "local")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GhstRDP");

    /// <summary>Single-instance key: a different data folder runs as a separate instance.</summary>
    public static string InstanceKey { get; } = HasOverride
        ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(Override!).ToLowerInvariant())))[..12]
        : "default";

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
