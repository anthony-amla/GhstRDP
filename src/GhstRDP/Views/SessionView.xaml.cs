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
/// Hospeda o controle RDP do Windows (mstscax, o mesmo motor do mstsc) dentro do app.
/// Cada aba tem sua própria instância; trocar de aba só esconde, nunca recria (não derruba a sessão).
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

        // Cobre a tela branca de "Conectando…" do controle RDP. É um irmão por cima do controle:
        // o controle continua visível e ativo por baixo (escondido ele não renderiza).
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
        // Espera o layout terminar para saber o tamanho real da área
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

    // ===================== Conexão =====================

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
            _vm.SetState(SessionState.Conectando, "Conectando…", _vm.Server.DisplayAddress);

            // Teste rápido: porta fechada ou DNS errado falham na hora, sem esperar o timeout do RDP
            var probe = await StatusMonitor.ProbeDetailedAsync(record.Host, record.Port, TimeSpan.FromSeconds(4));
            if (generation != _connectGeneration || _disposed || _closing) return;
            if (probe == StatusMonitor.ProbeResult.Recusado)
            {
                _vm.Server.SetStatus(false, 0);
                _vm.SetState(SessionState.Erro, "O servidor recusou a conexão",
                    $"Nada respondendo em {_vm.Server.DisplayAddress}. Confira a porta e se a Área de Trabalho Remota está habilitada no servidor.");
                return;
            }
            if (probe == StatusMonitor.ProbeResult.NaoEncontrado)
            {
                _vm.Server.SetStatus(false, 0);
                _vm.SetState(SessionState.Erro, "Endereço não encontrado",
                    $"Não foi possível encontrar {_vm.Server.DisplayAddress}. Confira o IP ou domínio.");
                return;
            }

            // O controle RDP só ativa a renderização se a janela dele estiver visível na hora de criar.
            // Se a aba/página estiver escondida agora, espera ela aparecer.
            if (!IsVisible)
            {
                _pendingStart = true;
                Log.Info($"{_vm.Server.Name}: aguardando a aba ficar visível para conectar");
                return;
            }

            _vm.SetState(SessionState.Conectando, "Conectando…", _vm.Server.DisplayAddress, rdpVisible: true);
            UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (generation != _connectGeneration || _disposed || _closing) return;

            StartRdp(record);
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao iniciar a sessão RDP", ex);
            _vm.SetState(SessionState.Erro, "Não foi possível iniciar a conexão", ex.Message);
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
            Log.Info($"Controle RDP criado: visível={rdp.Visible} host={RdpHost.IsVisible} tamanho={rdp.Width}x{rdp.Height}");

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
            adv.AuthenticationLevel = 2;              // avisa se o certificado não for confiável
            adv.SmartSizing = record.Display == DisplayMode.Personalizada;
            adv.EnableAutoReconnect = true;
            adv.MaxReconnectAttempts = 20;
            adv.keepAliveInterval = 30000;            // evita queda por inatividade em NAT/firewall
            adv.BitmapPersistence = 1;
            adv.Compress = 1;
            adv.NetworkConnectionType = 7;            // detectar automaticamente
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
                ns.AllowPromptingForCredentials = true;   // se a senha estiver errada/ausente, o Windows pede
                ns.NegotiateSecurityLayer = true;
                ns.ShowRedirectionWarningDialog = false;
                ns.UseMultimon = record.Multimon && record.Display == DisplayMode.TelaCheia;
                ns.ConnectionBarText = record.Name; // barra da tela cheia mostra o nome, não o IP
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
                    Log.Warn("Servidor/cliente não aceitou fator de escala", ex);
                }
            }

            if (record.Display == DisplayMode.TelaCheia) rdp.FullScreen = true;

            Log.Info($"Conectando em {record.Name} ({_vm.Server.Address}) {w}x{h} escala {ScaleFactor}%");
            rdp.Connect();
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao iniciar a sessão RDP", ex);
            _vm.SetState(SessionState.Erro, "Não foi possível iniciar a conexão", ex.Message);
        }
    }

    (int W, int H) ComputeDesktopSize(ServerRecord record)
    {
        if (record.Display == DisplayMode.Personalizada)
            return (Even(Math.Clamp(record.CustomWidth, 640, 8192)), Math.Clamp(record.CustomHeight, 480, 8192));

        var screen = WinForms.Screen.FromControl(_panel);
        if (record.Display == DisplayMode.TelaCheia)
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
            if (IsCurrent(s)) _vm.SetState(SessionState.Conectando, "Conectando…", _vm.Server.DisplayAddress);
        };

        rdp.OnConnected += (s, _) =>
        {
            if (!IsCurrent(s)) return;
            HideCover();
            _vm.SetState(SessionState.Conectado, "Conectado");
            Dispatcher.InvokeAsync(FocusRdp, DispatcherPriority.Background);
        };

        rdp.OnLoginComplete += (s, _) =>
        {
            if (!IsCurrent(s)) return;
            HideCover();
            _loggedIn = true;
            Log.Info($"Login concluído em {_vm.Server.Name} (visível={_rdp?.Visible} host={RdpHost.IsVisible})");
            _main.MarkConnected(_vm.Server);
            _thumbTimer.Start();
            // a área pode ter mudado de tamanho durante o login
            _resizeTimer.Stop();
            _resizeTimer.Start();
        };

        rdp.OnDisconnected += OnDisconnected;

        rdp.OnFatalError += (s, e) =>
        {
            if (!IsCurrent(s)) return;
            Log.Error($"Erro fatal RDP {e.errorCode} em {_vm.Server.Name}");
            _vm.SetState(SessionState.Erro, "Erro no cliente RDP", $"Código {e.errorCode}. Tente reconectar.");
        };

        rdp.OnAutoReconnecting2 += (s, e) =>
        {
            if (!IsCurrent(s)) return;
            _vm.SetState(SessionState.Reconectando, "Reconectando…", $"Tentativa {e.attemptCount} de {e.maxAttemptCount}");
        };

        rdp.OnAutoReconnected += (s, _) =>
        {
            if (IsCurrent(s)) _vm.SetState(SessionState.Conectado, "Conectado");
        };

        rdp.OnRequestContainerMinimize += (s, _) =>
        {
            if (!IsCurrent(s)) return;
            var window = Window.GetWindow(this);
            if (window != null) window.WindowState = WindowState.Minimized;
        };

        rdp.OnLogonError += (s, e) =>
        {
            if (IsCurrent(s)) Log.Info($"Logon error {e.lError} em {_vm.Server.Name}");
        };

        rdp.OnWarning += (s, e) =>
        {
            if (IsCurrent(s)) Log.Info($"Aviso RDP {e.warningCode} em {_vm.Server.Name}");
        };
    }

    void HideCover()
    {
        if (!_cover.Visible) return;
        _cover.Visible = false;
        try { _rdp?.Invalidate(true); } catch { /* ignore */ }
    }

    void OnDisconnected(object? sender, IMsTscAxEvents_OnDisconnectedEvent e)
    {
        if (!IsCurrent(sender)) return;
        _cover.Visible = false;
        _thumbTimer.Stop();
        _loggedIn = false;

        if (_closing)
        {
            FinishClose();
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
            // sem descrição
        }
        Log.Info($"Desconectado de {_vm.Server.Name}: motivo {reason} — {description}");

        // 1 = desconexão local, 2 = pelo usuário remoto, 3 = pelo servidor
        if (reason is 1 or 2 or 3)
        {
            _vm.SetState(SessionState.Desconectado, "Sessão encerrada",
                string.IsNullOrWhiteSpace(description) ? "A sessão remota foi desconectada." : description);
            return;
        }

        var friendly = FriendlyError(reason);
        if (friendly is { } f)
            _vm.SetState(SessionState.Erro, f.Title, $"{f.Detail}\n(código {reason})");
        else
            _vm.SetState(SessionState.Erro, "Não foi possível conectar",
                string.IsNullOrWhiteSpace(description) ? $"Código {reason}." : $"{description}\n(código {reason})");
    }

    /// <summary>Mensagens em português para os códigos de desconexão mais comuns do RDP.</summary>
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

    // ===================== Tamanho dinâmico =====================

    void ApplyResize()
    {
        var rdp = _rdp;
        if (rdp == null || !_loggedIn || _disposed) return;
        if (_vm.Server.Record.Display != DisplayMode.AjustarJanela) return;
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
        }
        catch (Exception ex)
        {
            _resizeFailures++;
            if (_resizeFailures >= 3)
            {
                // Servidor sem suporte a resolução dinâmica: escala a imagem
                Log.Info($"Resolução dinâmica indisponível em {_vm.Server.Name}; usando ajuste de imagem. {ex.Message}");
                _dynamicResize = false;
                try { rdp.AdvancedSettings9.SmartSizing = true; } catch { /* ignore */ }
            }
            else
            {
                _resizeTimer.Interval = TimeSpan.FromMilliseconds(1200);
                _resizeTimer.Start();
            }
        }
    }

    // ===================== Ações =====================

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
            // ignore
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
            Log.Warn("Falha ao entrar em tela cheia", ex);
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
            Log.Warn("Falha ao capturar miniatura", ex);
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
                // Se o evento de desconexão não vier, fecha mesmo assim
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
            Log.Warn("Falha ao desconectar", ex);
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
            // ignore
        }
        try
        {
            _panel.Controls.Remove(rdp);
            rdp.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Falha ao descartar controle RDP", ex);
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
            Log.Warn("Falha ao descartar host da sessão", ex);
        }
    }
}
