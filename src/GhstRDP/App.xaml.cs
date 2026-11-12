using System.Windows;
using System.Windows.Threading;
using GhstRDP.Core;
using GhstRDP.ViewModels;
using GhstRDP.Views;

namespace GhstRDP;

public partial class App : Application
{
    SingleInstance? _single;
    MainViewModel? _main;

    protected override void OnStartup(StartupEventArgs e)
    {
        _single = SingleInstance.TryAcquire();
        if (_single == null)
        {
            SingleInstance.SignalExisting();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled exception (domain)", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Warn("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
        Log.Info("Ghst RDP starting");

        DataStore store;
        Views.MainWindow window;
        try
        {
            store = new DataStore();
            store.Load();
            _main = new MainViewModel(store, Dispatcher);
            window = new MainWindow(_main);
        }
        catch (Exception ex)
        {
            Log.Error("Startup failed", ex);
            MessageBox.Show($"O Ghst RDP não conseguiu iniciar.\n\n{ex.Message}\n\nDetalhes no log: {Log.CurrentFile}",
                "Ghst RDP", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        MainWindow = window;
        window.Show();

        _single.ActivationRequested += () => Dispatcher.BeginInvoke(() =>
        {
            if (window.IsLoaded) window.BringToFront();
        });
        SessionEnding += (_, _) => window.AllowCloseNow();

        if (store.Warning != null)
            Dialogs.Info(window, "Dados recuperados", store.Warning);
    }

    void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled exception (UI)", e.Exception);
        e.Handled = true;
        try
        {
            Dialogs.Error(MainWindow, "Algo deu errado",
                "O Ghst RDP encontrou um erro e continuou funcionando.\n\n" + e.Exception.Message +
                $"\n\nDetalhes no log: {Log.CurrentFile}");
        }
        catch (Exception ex)
        {
            Log.Error("Failed to show the error dialog", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _main?.Update.ApplyPendingOnExit(); }
        catch (Exception ex) { Log.Error("Failed to apply pending update", ex); }
        _main?.Dispose();
        _single?.Dispose();
        Log.Info("Ghst RDP exited");
        base.OnExit(e);
    }
}
