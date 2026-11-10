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
            Log.Error("Exceção não tratada (domínio)", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Warn("Task com exceção não observada", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
        Log.Info("Ghst RDP iniciando");

        var store = new DataStore();
        store.Load();

        _main = new MainViewModel(store, Dispatcher);
        var window = new MainWindow(_main);
        MainWindow = window;
        window.Show();

        _single.ActivationRequested += () => Dispatcher.BeginInvoke(window.BringToFront);
        SessionEnding += (_, _) => window.AllowCloseNow();

        if (store.Warning != null)
            Dialogs.Info(window, "Dados recuperados", store.Warning);
    }

    void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Exceção não tratada (UI)", e.Exception);
        e.Handled = true;
        try
        {
            Dialogs.Error(MainWindow, "Algo deu errado",
                "O Ghst RDP encontrou um erro e continuou funcionando.\n\n" + e.Exception.Message +
                $"\n\nDetalhes no log: {Log.CurrentFile}");
        }
        catch
        {
            // não deixa o handler de erro derrubar o app
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _main?.Update.ApplyPendingOnExit(); }
        catch (Exception ex) { Log.Error("Falha ao aplicar atualização", ex); }
        _main?.Dispose();
        _single?.Dispose();
        Log.Info("Ghst RDP encerrado");
        base.OnExit(e);
    }
}
