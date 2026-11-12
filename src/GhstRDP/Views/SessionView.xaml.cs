using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AxMSTSCLib;
using GhstRDP.Core;
using GhstRDP.ViewModels;
using MSTSCLib;
using WinForms = System.Windows.Forms;

namespace GhstRDP.Views;

/// <summary>
/// Hosts the Windows RDP ActiveX control (mstscax) for one session tab.
/// </summary>
public partial class SessionView : UserControl, IDisposable
{
    readonly SessionViewModel _vm;
    readonly MainViewModel _main;
    readonly WinForms.Panel _panel;
    readonly WinForms.Panel _cover;
    readonly WinForms.Label _coverLabel;
    readonly DispatcherTimer _resizeTimer;
    readonly DispatcherTimer _thumbTimer;

    AxMsRdpClient9NotSafeForScripting? _rdp;
    bool _started, _closing, _closeFinished, _disposed, _loggedIn;
    bool _dynamicResize = true;
    int _resizeFailures, _lastW, _lastH;

    public SessionView(SessionViewModel vm, MainViewModel main)
    {
        InitializeComponent();
        _vm = vm;
        _main = main;
        DataContext = vm;

        _panel = new WinForms.Panel { BackColor = System.Drawing.Color.FromArgb(21, 21, 24), Dock = WinForms.DockStyle.Fill };

        // Covers the white "connecting" screen; the RDP control must stay visible underneath or it won't render.
        _cover = new WinForms.Panel { BackColor = System.Drawing.Color.FromArgb(21, 21, 24), Dock = WinForms.DockStyle.Fill, Visible = false };
        _coverLabel = new WinForms.Label
        {
            Dock = WinForms.DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            ForeColor = System.Drawing.Color.FromArgb(170, 170, 179),
            Font = new System.Drawing.Font("Segoe UI", 10f),
        };
        _cover.Controls.Add(_coverLabel);
        _panel.Controls.Add(_cover);
        RdpHost.Child = _panel;

        _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _resizeTimer.Tick += (_, _) =>
        {
            _resizeTimer.Stop();
            ApplyResize();
        };

        _thumbTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
        _thumbTimer.Tick += (_, _) => CaptureThumbnail();

        vm.ReconnectRequested += Connect;
        vm.CloseRequested += BeginClose;
        vm.FullScreenRequested += GoFullScreen;
        vm.FocusRequested += FocusRdp;
        vm.ThumbnailRequested += CaptureThumbnail;

        RdpArea.SizeChanged += (_, _) =>
        {
            if (!_loggedIn) return;
            _resizeTimer.Stop();
            _resizeTimer.Start();
        };

        Loaded += OnLoaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started) return;
        _started = true;
        Dispatcher.InvokeAsync(Connect, DispatcherPriority.Background);
    }

    uint ScaleFactor
    {
        get
        {
            if (!_main.Settings.MatchWindowsScale) return 100;
            var scale = (uint)Math.Round(VisualTreeHelper.GetDpi(this).DpiScaleX * 100);
            return Math.Clamp(scale, 100u, 500u);
        }
    }

    int _connectGeneration;

    async void Connect()
    {
        if (_disposed || _closing) return;
        var generation = ++_connectGeneration;

        try
        {
            DestroyRdp();
            _loggedIn = false;
            _dynamicResize = true;
            _resizeFailures = 0;

            var record = _vm.Server.Record;
            _vm.SetState(SessionState.Connecting, "Conectando…", _vm.Server.DisplayAddress);

            var probe = await StatusMonitor.ProbeDetailedAsync(record.Host, record.Port, TimeSpan.FromSeconds(4));
            if (generation != _connectGeneration || _disposed || _closing) return;
            if (probe == StatusMonitor.ProbeResult.Refused)
            {
                _vm.Server.SetStatus(false, 0);
                _vm.SetState(SessionState.Error, "O servidor recusou a conexão",
                    $"Nada respondendo em {_vm.Server.DisplayAddress}. Confira a porta e se a Área de Trabalho Remota está habilitada no servidor.");
                return;
            }
            if (probe == StatusMonitor.ProbeResult.NotFound)
            {
                _vm.Server.SetStatus(false, 0);
                _vm.SetState(SessionState.Error, "Endereço não encontrado",
                    $"Não foi possível encontrar {_vm.Server.DisplayAddress}. Confira o IP ou domínio.");
                return;
            }

            // The ActiveX only starts rendering if its host is visible when created; otherwise the screen stays black.
            if (!IsVisible)
            {
                _pendingStart = true;
                Log.Info($"{_vm.Server.Name}: waiting for the tab to become visible before connecting");
                return;
            }

            _vm.SetState(SessionState.Connecting, "Conectando…", _vm.Server.DisplayAddress, rdpVisible: true);
            UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (generation != _connectGeneration || _disposed || _closing) return;

            StartRdp(record);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to start the RDP session", ex);
            _vm.SetState(SessionState.Error, "Não foi possível iniciar a conexão", ex.Message);
        }
    }

    bool _pendingStart;

    void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible || !_pendingStart) return;
        _pendingStart = false;
        Dispatcher.InvokeAsync(Connect, DispatcherPriority.Background);
    }

    void StartRdp(ServerRecord record)
    {
        var settings = _main.Settings;
        try
        {
            var rdp = new AxMsRdpClient9NotSafeForScripting();
            ((ISupportInitialize)rdp).BeginInit();
            rdp.Dock = WinForms.DockStyle.Fill;
            _panel.Controls.Add(rdp);
            _coverLabel.Text = $"Conectando a {record.Name}…";
            _cover.Visible = true;
            _cover.BringToFront();
            ((ISupportInitialize)rdp).EndInit();
            rdp.CreateControl();
            _rdp = rdp;
            HookEvents(rdp);
            Log.Info($"RDP control created: visible={rdp.Visible} host={RdpHost.IsVisible} size={rdp.Width}x{rdp.Height}");

            var (w, h) = ComputeDesktopSize(record);
            _lastW = w;
            _lastH = h;

            rdp.Server = record.Host.Trim();
            if (!string.IsNullOrWhiteSpace(record.Username)) rdp.UserName = record.Username.Trim();
            if (!string.IsNullOrWhiteSpace(record.Domain)) rdp.Domain = record.Domain.Trim();
            rdp.DesktopWidth = w;
            rdp.DesktopHeight = h;
            rdp.ColorDepth = 32;
            rdp.ConnectingText = "Conectando…";
            rdp.FullScreenTitle = $"{record.Name} — Ghst RDP";

            var adv = rdp.AdvancedSettings9;
            adv.RDPPort = record.Port;
            adv.EnableCredSspSupport = true;          // NLA
            adv.AuthenticationLevel = 2;              // warn on untrusted certificate
            adv.SmartSizing = record.Display == DisplayMode.Custom;
            adv.EnableAutoReconnect = true;
            adv.MaxReconnectAttempts = 20;
            adv.keepAliveInterval = 30000;
            adv.BitmapPersistence = 1;
            adv.Compress = 1;
            adv.NetworkConnectionType = 7;            // auto-detect
            adv.BandwidthDetection = true;
            adv.RedirectClipboard = record.RedirectClipboard;
            adv.RedirectDrives = record.RedirectDrives;
            adv.RedirectPrinters = record.RedirectPrinters;
            adv.RedirectSmartCards = false;
            adv.RedirectPorts = false;
            adv.ConnectToAdministerServer = record.AdminSession;
            adv.DisplayConnectionBar = true;
            adv.ConnectionBarShowMinimizeButton = true;
            adv.ConnectionBarShowRestoreButton = true;
            adv.PinConnectionBar = false;
            adv.GrabFocusOnConnect = true;
            adv.ContainerHandledFullScreen = 0;

            var secured = rdp.SecuredSettings3;
            secured.AudioRedirectionMode = (int)record.Audio;
            secured.KeyboardHookMode = (int)settings.WinKeys;

            var password = Secrets.Unprotect(record.PasswordProtected);
            if (!string.IsNullOrEmpty(password)) adv.ClearTextPassword = password;

            var ocx = rdp.GetOcx();
            if (ocx is IMsRdpClientNonScriptable5 ns)
            {
                ns.AllowCredentialSaving = false;
                ns.PromptForCredentials = false;
                ns.AllowPromptingForCredentials = true;
                ns.NegotiateSecurityLayer = true;
                ns.ShowRedirectionWarningDialog = false;
                ns.UseMultimon = record.Multimon && record.Display == DisplayMode.FullScreen;
                ns.ConnectionBarText = record.Name;
            }

            if (ocx is IMsRdpExtendedSettings ext)
            {
                try
                {
                    object desktopScale = ScaleFactor;
                    ext.set_Property("DesktopScaleFactor", ref desktopScale);
                    object deviceScale = 100u;
                    ext.set_Property("DeviceScaleFactor", ref deviceScale);
                }
                catch (Exception ex)
                {
                    Log.Warn("Scale factor was not accepted", ex);
                }
            }

            if (record.Display == DisplayMode.FullScreen) rdp.FullScreen = true;

            Log.Info($"Connecting to {record.Name} ({_vm.Server.Address}) {w}x{h} scale {ScaleFactor}%");
            rdp.Connect();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to start the RDP session", ex);
            _vm.SetState(SessionState.Error, "Não foi possível iniciar a conexão", ex.Message);
        }
    }

    (int W, int H) ComputeDesktopSize(ServerRecord record)
    {
        if (record.Display == DisplayMode.Custom)
            return (Even(Math.Clamp(record.CustomWidth, 640, 8192)), Math.Clamp(record.CustomHeight, 480, 8192));

        var screen = WinForms.Screen.FromControl(_panel);
        if (record.Display == DisplayMode.FullScreen)
            return (Even(screen.Bounds.Width), screen.Bounds.Height);

        int w = _panel.ClientSize.Width, h = _panel.ClientSize.Height;
        if (w < 400 || h < 300)
        {
            w = (int)(screen.WorkingArea.Width * 0.8);
            h = (int)(screen.WorkingArea.Height * 0.8);
        }
        return (Even(Math.Clamp(w, 640, 8192)), Math.Clamp(h, 480, 8192));
    }

    static int Even(int v) => v & ~1;

    bool IsCurrent(object? sender) => sender != null && ReferenceEquals(sender, _rdp);

    void HookEvents(AxMsRdpClient9NotSafeForScripting rdp)
    {
        rdp.OnConnecting += (s, _) =>
        {
            if (IsCurrent(s)) _vm.SetState(SessionState.Connecting, "Conectando…", _vm.Server.DisplayAddress);
        };

        rdp.OnConnected += (s, _) =>
        {
            if (!IsCurrent(s)) return;
            HideCover();
            _vm.SetState(SessionState.Connected, "Conectado");
            Dispatcher.InvokeAsync(FocusRdp, DispatcherPriority.Background);
        };

        rdp.OnLoginComplete += (s, _) =>
        {
            if (!IsCurrent(s)) return;
            HideCover();
            _loggedIn = true;
            Log.Info($"Login complete on {_vm.Server.Name} (visible={_rdp?.Visible} host={RdpHost.IsVisible})");
            _main.MarkConnected(_vm.Server);
            _thumbTimer.Start();
            _resizeTimer.Stop();
            _resizeTimer.Start();
        };

        rdp.OnDisconnected += OnDisconnected;

        rdp.OnFatalError += (s, e) =>
        {
            if (!IsCurrent(s)) return;
            Log.Error($"RDP fatal error {e.errorCode} on {_vm.Server.Name}");
            _vm.SetState(SessionState.Error, "Erro no cliente RDP", $"Código {e.errorCode}. Tente reconectar.");
        };

        rdp.OnAutoReconnecting2 += (s, e) =>
        {
            if (!IsCurrent(s)) return;
            _vm.SetState(SessionState.Reconnecting, "Reconectando…", $"Tentativa {e.attemptCount} de {e.maxAttemptCount}");
        };

        rdp.OnAutoReconnected += (s, _) =>
        {
            if (IsCurrent(s)) _vm.SetState(SessionState.Connected, "Conectado");
        };

        rdp.OnRequestContainerMinimize += (s, _) =>
        {
            if (!IsCurrent(s)) return;
            var window = Window.GetWindow(this);
            if (window != null) window.WindowState = WindowState.Minimized;
        };

        rdp.OnLogonError += (s, e) =>
        {
            if (IsCurrent(s)) Log.Info($"Logon error {e.lError} on {_vm.Server.Name}");
        };

        rdp.OnWarning += (s, e) =>
        {
            if (IsCurrent(s)) Log.Info($"RDP warning {e.warningCode} on {_vm.Server.Name}");
        };
    }

    void HideCover()
    {
        if (!_cover.Visible) return;
        _cover.Visible = false;
        try { _rdp?.Invalidate(true); } catch { }
    }

    void OnDisconnected(object? sender, IMsTscAxEvents_OnDisconnectedEvent e)
    {
        if (!IsCurrent(sender)) return;
        _cover.Visible = false;
        _thumbTimer.Stop();
        _loggedIn = false;

        if (_closing)
        {
            // Let the ActiveX return from its own event before the tab disposes it.
            Dispatcher.BeginInvoke(FinishClose, DispatcherPriority.Background);
            return;
        }

        var reason = e.discReason;
        var description = "";
        try
        {
            description = _rdp!.GetErrorDescription((uint)reason, (uint)_rdp.ExtendedDisconnectReason);
        }
        catch
        {
        }
        Log.Info($"Disconnected from {_vm.Server.Name}: reason {reason} - {description}");

        // 1 = local disconnect, 2 = remote user, 3 = server
        if (reason is 1 or 2 or 3)
        {
            _vm.SetState(SessionState.Disconnected, "Sessão encerrada",
                string.IsNullOrWhiteSpace(description) ? "A sessão remota foi desconectada." : description);
            return;
        }

        var friendly = FriendlyError(reason);
        if (friendly is { } f)
            _vm.SetState(SessionState.Error, f.Title, $"{f.Detail}\n(código {reason})");
        else
            _vm.SetState(SessionState.Error, "Não foi possível conectar",
                string.IsNullOrWhiteSpace(description) ? $"Código {reason}." : $"{description}\n(código {reason})");
    }

    static (string Title, string Detail)? FriendlyError(int reason) => reason switch
    {
        516 or 520 => ("O servidor não respondeu",
            "Confira se ele está ligado, se o IP e a porta estão certos e se a Área de Trabalho Remota está habilitada."),
        264 or 1796 => ("Tempo esgotado",
            "O servidor demorou demais para responder. Pode ser rede instável ou servidor sobrecarregado."),
        260 or 1288 or 1540 => ("Endereço não encontrado", "O nome do servidor não foi encontrado (DNS). Confira o IP ou domínio."),
        2308 => ("A conexão caiu", "A rede caiu ou o servidor reiniciou. Tente reconectar."),
        2055 => ("Usuário ou senha incorretos", "O servidor recusou o login. Edite o servidor e confira usuário e senha."),
        2823 => ("Conta desativada", "A conta de usuário está desativada no servidor."),
        3335 => ("Conta bloqueada", "Muitas tentativas de login erradas. A conta foi bloqueada no servidor."),
        3591 => ("Conta expirada", "A conta de usuário expirou no servidor."),
        3847 or 4615 => ("Troque a senha", "O servidor exige trocar a senha antes de entrar. Faça isso pelo mstsc e depois atualize aqui."),
        _ => null,
    };

    void ApplyResize()
    {
        var rdp = _rdp;
        if (rdp == null || !_loggedIn || _disposed) return;
        if (_vm.Server.Record.Display != DisplayMode.FitWindow) return;
        if (!_dynamicResize) return;

        try
        {
            if (rdp.FullScreen) return;
        }
        catch
        {
            return;
        }

        var w = Even(_panel.ClientSize.Width);
        var h = _panel.ClientSize.Height;
        if (w < 400 || h < 300) return;
        if (w == _lastW && h == _lastH) return;

        try
        {
            var scale = ScaleFactor;
            rdp.UpdateSessionDisplaySettings((uint)w, (uint)h, (uint)w, (uint)h, 0, scale, 100);
            _lastW = w;
            _lastH = h;
            _resizeFailures = 0;
            _resizeTimer.Interval = TimeSpan.FromMilliseconds(450);
        }
        catch (Exception ex)
        {
            _resizeFailures++;
            if (_resizeFailures >= 3)
            {
                Log.Info($"Dynamic resolution unavailable on {_vm.Server.Name}; falling back to smart sizing. {ex.Message}");
                _dynamicResize = false;
                try { rdp.AdvancedSettings9.SmartSizing = true; } catch { }
            }
            else
            {
                _resizeTimer.Interval = TimeSpan.FromMilliseconds(1200);
                _resizeTimer.Start();
            }
        }
    }

    void FocusRdp()
    {
        if (_rdp == null || !_vm.ShowRdp || !IsVisible) return;
        try
        {
            RdpHost.Focus();
            _rdp.Focus();
        }
        catch
        {
        }
    }

    void GoFullScreen()
    {
        try
        {
            if (_rdp != null && _rdp.Connected == 1) _rdp.FullScreen = true;
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to enter full screen", ex);
        }
    }

    void CaptureThumbnail()
    {
        if (_rdp == null || !_loggedIn || !_main.Settings.Thumbnails || !RdpHost.IsVisible) return;
        try
        {
            if (Thumbnails.Capture(_rdp, _vm.Server.Id)) _vm.Server.LoadThumbnail();
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to capture thumbnail", ex);
        }
    }

    void BeginClose()
    {
        if (_closing) return;
        _closing = true;
        _resizeTimer.Stop();
        _thumbTimer.Stop();
        CaptureThumbnail();

        try
        {
            if (_rdp != null && _rdp.Connected != 0)
            {
                _rdp.Disconnect();
                var fallback = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                fallback.Tick += (_, _) =>
                {
                    fallback.Stop();
                    FinishClose();
                };
                fallback.Start();
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to disconnect", ex);
        }

        FinishClose();
    }

    void FinishClose()
    {
        if (_closeFinished) return;
        _closeFinished = true;
        _vm.NotifyClosed();
    }

    void DestroyRdp()
    {
        _cover.Visible = false;
        var rdp = _rdp;
        _rdp = null;
        if (rdp == null) return;
        try
        {
            if (rdp.Connected != 0) rdp.Disconnect();
        }
        catch
        {
        }
        try
        {
            _panel.Controls.Remove(rdp);
            rdp.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to dispose the RDP control", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _resizeTimer.Stop();
        _thumbTimer.Stop();
        _vm.ReconnectRequested -= Connect;
        _vm.CloseRequested -= BeginClose;
        _vm.FullScreenRequested -= GoFullScreen;
        _vm.FocusRequested -= FocusRdp;
        _vm.ThumbnailRequested -= CaptureThumbnail;
        DestroyRdp();
        try
        {
            RdpHost.Child = null;
            _panel.Dispose();
            RdpHost.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to dispose the session host", ex);
        }
    }
}
