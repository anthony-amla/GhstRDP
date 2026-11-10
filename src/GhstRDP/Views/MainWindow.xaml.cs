using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using GhstRDP.Core;
using GhstRDP.ViewModels;

namespace GhstRDP.Views;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm;
    readonly Dictionary<SessionViewModel, SessionView> _sessionViews = new();
    bool _allowClose;
    bool _maxPressed;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        RestorePlacement();

        vm.Sessions.CollectionChanged += Sessions_CollectionChanged;
        StateChanged += (_, _) => UpdateMaximizedState();
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeWindow.ApplyDarkChrome(this);
        // Adicionado depois do WindowChrome → recebe as mensagens antes (snap layouts no botão maximizar)
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
        UpdateMaximizedState();
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    // ================= Sessões (cada aba = um SessionView vivo) =================

    void Sessions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var view in _sessionViews.Values)
            {
                view.Dispose();
                SessionHost.Children.Remove(view);
            }
            _sessionViews.Clear();
            return;
        }

        if (e.OldItems != null)
        {
            foreach (SessionViewModel session in e.OldItems)
            {
                if (!_sessionViews.Remove(session, out var view)) continue;
                view.Dispose();
                SessionHost.Children.Remove(view);
            }
        }

        if (e.NewItems != null)
        {
            foreach (SessionViewModel session in e.NewItems)
            {
                var view = new SessionView(session, _vm);
                _sessionViews[session] = view;
                SessionHost.Children.Add(view);
            }
        }
    }

    // ================= Barra de título =================

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void UpdateMaximizedState()
    {
        if (WindowState == WindowState.Maximized)
        {
            Root.Margin = NativeWindow.MaximizedOverhang(this);
            MaxButton.Content = "";
            MaxButton.ToolTip = "Restaurar";
        }
        else
        {
            Root.Margin = new Thickness(0);
            MaxButton.Content = "";
            MaxButton.ToolTip = "Maximizar";
        }
    }

    const int WM_NCHITTEST = 0x0084;
    const int WM_NCMOUSELEAVE = 0x02A2;
    const int WM_NCLBUTTONDOWN = 0x00A1;
    const int WM_NCLBUTTONUP = 0x00A2;
    const int HTMAXBUTTON = 9;

    /// <summary>Faz o Windows 11 mostrar os layouts de encaixe (snap) ao passar o mouse no maximizar.</summary>
    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                if (IsOverMaxButton(lParam))
                {
                    SetMaxVisual(_maxPressed ? "CaptionPressedBrush" : "CaptionHoverBrush");
                    handled = true;
                    return new IntPtr(HTMAXBUTTON);
                }
                SetMaxVisual(null);
                break;

            case WM_NCMOUSELEAVE:
                _maxPressed = false;
                SetMaxVisual(null);
                break;

            case WM_NCLBUTTONDOWN when wParam.ToInt64() == HTMAXBUTTON:
                _maxPressed = true;
                SetMaxVisual("CaptionPressedBrush");
                handled = true;
                return IntPtr.Zero;

            case WM_NCLBUTTONUP when wParam.ToInt64() == HTMAXBUTTON:
                if (_maxPressed) ToggleMaximize();
                _maxPressed = false;
                SetMaxVisual(null);
                handled = true;
                return IntPtr.Zero;
        }
        return IntPtr.Zero;
    }

    bool IsOverMaxButton(IntPtr lParam)
    {
        if (!MaxButton.IsVisible) return false;
        var lp = lParam.ToInt64();
        var x = unchecked((short)(lp & 0xFFFF));
        var y = unchecked((short)((lp >> 16) & 0xFFFF));
        try
        {
            var p = MaxButton.PointFromScreen(new Point(x, y));
            return p.X >= 0 && p.Y >= 0 && p.X < MaxButton.ActualWidth && p.Y < MaxButton.ActualHeight;
        }
        catch
        {
            return false;
        }
    }

    void SetMaxVisual(string? brushKey) =>
        MaxButton.Background = brushKey == null ? Brushes.Transparent : (Brush)FindResource(brushKey);

    // ================= Atalhos =================

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (ctrl && e.Key is Key.K or Key.F)
        {
            _vm.CurrentPage = Page.Servidores;
            Dispatcher.InvokeAsync(() =>
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }, DispatcherPriority.Input);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.IsKeyboardFocused && SearchBox.Text.Length > 0)
        {
            _vm.Search = "";
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.Tab && _vm.Sessions.Count > 1)
        {
            // Ctrl+Tab / Ctrl+Shift+Tab: troca de aba (quando o foco está no app, não na sessão)
            var back = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            var index = _vm.SelectedSession == null ? 0 : _vm.Sessions.IndexOf(_vm.SelectedSession);
            var next = (index + (back ? -1 : 1) + _vm.Sessions.Count) % _vm.Sessions.Count;
            _vm.SelectedSession = _vm.Sessions[next];
            _vm.CurrentPage = Page.Sessoes;
            e.Handled = true;
        }
    }

    /// <summary>Botão do meio do mouse fecha a aba, como no navegador.</summary>
    void SessionTabs_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        var element = e.OriginalSource as DependencyObject;
        while (element != null && element is not System.Windows.Controls.ListBoxItem)
            element = VisualTreeHelper.GetParent(element);
        if (element is System.Windows.Controls.ListBoxItem { DataContext: SessionViewModel session })
        {
            _vm.CloseSession(session);
            e.Handled = true;
        }
    }

    void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ContextMenu is not { } menu) return;
        menu.DataContext = element.DataContext;
        menu.PlacementTarget = element;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
        e.Handled = true;
    }

    // ================= Fechar com sessões abertas =================

    bool _closeFlowRunning;

    /// <summary>Desligamento/logoff do Windows: fecha sem perguntar.</summary>
    public void AllowCloseNow()
    {
        SavePlacement();
        _allowClose = true;
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;

        // Nunca abre diálogo dentro do evento Closing (o WPF não deixa usar uma janela "fechando" como dona).
        // Cancela e roda o fluxo logo em seguida.
        e.Cancel = true;
        if (_closeFlowRunning) return;
        _closeFlowRunning = true;
        Dispatcher.BeginInvoke(RunCloseFlow, DispatcherPriority.Normal);
    }

    void RunCloseFlow()
    {
        try
        {
            var live = _vm.Sessions.Count(s => s.IsLive);
            if (live > 0)
            {
                var message = live == 1
                    ? "Há 1 sessão aberta. Ela será desconectada — os programas no servidor continuam rodando."
                    : $"Há {live} sessões abertas. Elas serão desconectadas — os programas nos servidores continuam rodando.";
                if (!Dialogs.Confirm(this, "Fechar o Ghst RDP?", message, "Desconectar e sair"))
                {
                    _closeFlowRunning = false;
                    _vm.Update.CancelRelaunch();
                    return;
                }
            }

            SavePlacement();

            if (_vm.Sessions.Count == 0)
            {
                _allowClose = true;
                Close();
                return;
            }

            // Desconecta tudo de forma limpa e fecha quando terminar (ou após 5 s)
            _vm.SessionsChanged += () =>
            {
                if (_vm.Sessions.Count == 0) ForceClose();
            };
            foreach (var session in _vm.Sessions.ToList()) _vm.CloseSession(session);

            var fallback = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            fallback.Tick += (_, _) =>
            {
                fallback.Stop();
                ForceClose();
            };
            fallback.Start();
        }
        catch (Exception ex)
        {
            Log.Error("Falha no fluxo de fechamento; fechando mesmo assim", ex);
            ForceClose();
        }
    }

    void ForceClose()
    {
        if (_allowClose) return;
        _allowClose = true;
        foreach (var view in _sessionViews.Values)
        {
            try { view.Dispose(); }
            catch (Exception ex) { Log.Warn("Falha ao descartar sessão no fechamento", ex); }
        }
        _sessionViews.Clear();
        Dispatcher.BeginInvoke(Close);
    }

    // ================= Posição da janela =================

    void RestorePlacement()
    {
        var s = _vm.Settings;
        if (s.WindowWidth is { } w && s.WindowHeight is { } h && s.WindowLeft is { } left && s.WindowTop is { } top)
        {
            var rect = new Rect(left, top, w, h);
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (rect.IntersectsWith(screen) && w >= MinWidth && h >= MinHeight)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
                Width = w;
                Height = h;
            }
        }
        if (s.WindowMaximized) WindowState = WindowState.Maximized;
    }

    void SavePlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        var s = _vm.Settings;
        if (!bounds.IsEmpty && bounds.Width > 0)
        {
            s.WindowLeft = bounds.Left;
            s.WindowTop = bounds.Top;
            s.WindowWidth = bounds.Width;
            s.WindowHeight = bounds.Height;
        }
        s.WindowMaximized = WindowState == WindowState.Maximized;
        _vm.SaveQuiet();
    }
}
