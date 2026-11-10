using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GhstRDP.Core;

namespace GhstRDP.ViewModels;

/// <summary>Atualização automática (Velopack + GitHub Releases).</summary>
public partial class UpdateViewModel : ObservableObject
{
    readonly MainViewModel _main;
    readonly Updater _updater = new();
    readonly DispatcherTimer _timer;
    bool _busy;

    public UpdateViewModel(MainViewModel main)
    {
        _main = main;
        // Primeira verificação 20 s depois de abrir; depois a cada 6 h
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = TimeSpan.FromHours(6);
            if (AutoUpdate) await CheckAsync(silent: true);
        };
        _timer.Start();
        if (!_updater.IsInstalled) _status = "Versão não instalada (desenvolvimento): a atualização automática funciona na versão instalada pelo Setup.";
    }

    public string CurrentVersion => AppInfo.Current.ToString();

    public bool AutoUpdate
    {
        get => _main.Settings.AutoUpdate;
        set
        {
            if (_main.Settings.AutoUpdate == value) return;
            _main.Settings.AutoUpdate = value;
            OnPropertyChanged();
            _main.SaveQuiet();
            if (value) _ = CheckAsync(silent: true);
        }
    }

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private string _readyVersion = "";

    bool _restartRequested;

    [RelayCommand]
    async Task CheckNow()
    {
        if (!_updater.IsInstalled)
        {
            OpenUrl(AppInfo.ReleasesPage);
            return;
        }
        await CheckAsync(silent: false);
    }

    async Task CheckAsync(bool silent)
    {
        if (_busy || IsReady || !_updater.IsInstalled) return;
        _busy = true;
        try
        {
            Status = "Procurando atualização…";
            var version = await _updater.CheckAsync();
            if (version == null)
            {
                Status = $"Você está na versão mais nova ({CurrentVersion}).";
                return;
            }
            await _updater.DownloadAsync(p => Application.Current.Dispatcher.BeginInvoke(() => Status = $"Baixando a versão {version}… {p}%"));
            ReadyVersion = version;
            IsReady = true;
            Status = $"Versão {version} pronta. Ela entra quando você fechar o app, ou clique em \"Atualizar\".";
            _main.ShowToast($"Atualização {version} pronta");
        }
        catch (Exception ex)
        {
            Log.Warn("Falha ao procurar/baixar atualização", ex);
            Status = silent ? "" : $"Não foi possível atualizar: {ex.Message}";
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Fecha o app (pedindo confirmação se houver sessões) e abre a versão nova.</summary>
    [RelayCommand]
    void RestartNow()
    {
        if (!IsReady) return;
        _restartRequested = true;
        Application.Current.MainWindow?.Close();
    }

    /// <summary>O usuário cancelou o fechamento: não reabre depois.</summary>
    public void CancelRelaunch() => _restartRequested = false;

    [RelayCommand] void OpenReleases() => OpenUrl(AppInfo.ReleasesPage);

    /// <summary>Chamado no OnExit do app: aplica a versão baixada.</summary>
    public void ApplyPendingOnExit()
    {
        if (!IsReady) return;
        if (_restartRequested) _updater.ApplyAndRestart();
        else _updater.ApplyOnExit();
    }

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Falha ao abrir o navegador", ex); }
    }
}
