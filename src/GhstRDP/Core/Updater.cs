using Velopack;
using Velopack.Sources;

namespace GhstRDP.Core;

public static class AppInfo
{
    /// <summary>Repositório no GitHub de onde saem as versões (Releases). Troque aqui se o repositório mudar.</summary>
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

/// <summary>
/// Atualização via Velopack + GitHub Releases. Só funciona na versão instalada (Setup.exe);
/// rodando do build de desenvolvimento ou de um .exe solto, apenas avisa e abre a página.
/// </summary>
public sealed class Updater
{
    readonly UpdateManager _manager = new(new GithubSource(AppInfo.RepoUrl, null, false));
    UpdateInfo? _pending;

    public bool IsInstalled => _manager.IsInstalled;
    public string? PendingVersion => _pending?.TargetFullRelease.Version.ToString();

    /// <summary>Versão nova disponível no GitHub, ou null.</summary>
    public async Task<string?> CheckAsync()
    {
        if (!IsInstalled) return null;
        var info = await _manager.CheckForUpdatesAsync();
        if (info == null) return null;
        _pending = info;
        return info.TargetFullRelease.Version.ToString();
    }

    /// <summary>Baixa (o Velopack confere o hash de cada pacote).</summary>
    public async Task DownloadAsync(Action<int> progress)
    {
        if (_pending == null) throw new InvalidOperationException("Nenhuma atualização encontrada.");
        await _manager.DownloadUpdatesAsync(_pending, progress);
    }

    /// <summary>Aplica quando o app fechar (sem abrir de novo).</summary>
    public void ApplyOnExit()
    {
        if (_pending == null) return;
        _manager.WaitExitThenApplyUpdates(_pending.TargetFullRelease, silent: true, restart: false);
    }

    /// <summary>Fecha, aplica e abre a versão nova.</summary>
    public void ApplyAndRestart()
    {
        if (_pending == null) return;
        _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }
}
