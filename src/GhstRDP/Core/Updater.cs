using Velopack;
using Velopack.Sources;

namespace GhstRDP.Core;

public static class AppInfo
{
    /// <summary>GitHub repository whose Releases feed the updater.</summary>
    public const string Repo = "GhosttyE/GhstRDP";

    public static string RepoUrl => $"https://github.com/{Repo}";
    public static string ReleasesPage => $"{RepoUrl}/releases";

    public static Version Current
    {
        get
        {
            var v = typeof(AppInfo).Assembly.GetName().Version ?? new Version(1, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }
}

/// <summary>Velopack updates from GitHub Releases. Only works in an installed copy (Setup.exe).</summary>
public sealed class Updater
{
    readonly UpdateManager _manager = new(new GithubSource(AppInfo.RepoUrl, null, false));
    UpdateInfo? _pending;

    public bool IsInstalled => _manager.IsInstalled;
    public string? PendingVersion => _pending?.TargetFullRelease.Version.ToString();

    /// <returns>The newer version available, or null.</returns>
    public async Task<string?> CheckAsync()
    {
        if (!IsInstalled) return null;
        var info = await _manager.CheckForUpdatesAsync();
        if (info == null) return null;
        _pending = info;
        return info.TargetFullRelease.Version.ToString();
    }

    /// <param name="progress">Download progress from 0 to 100.</param>
    public async Task DownloadAsync(Action<int> progress)
    {
        if (_pending == null) throw new InvalidOperationException("Nenhuma atualização encontrada.");
        await _manager.DownloadUpdatesAsync(_pending, progress);
    }

    /// <summary>Installs the downloaded update after the app exits, without relaunching it.</summary>
    public void ApplyOnExit()
    {
        if (_pending == null) return;
        _manager.WaitExitThenApplyUpdates(_pending.TargetFullRelease, silent: true, restart: false);
    }

    /// <summary>Exits, installs the downloaded update and starts the new version.</summary>
    public void ApplyAndRestart()
    {
        if (_pending == null) return;
        _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }
}
