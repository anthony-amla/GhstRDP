using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GhstRDP.Core;

namespace GhstRDP.ViewModels;

public enum SessionState { Conectando, Conectado, Reconectando, Desconectado, Erro }

/// <summary>Uma sessão RDP embutida (uma aba). A view (SessionView) controla o ActiveX e reporta o estado aqui.</summary>
public partial class SessionViewModel : ObservableObject
{
    readonly MainViewModel _owner;

    public SessionViewModel(ServerItemViewModel server, MainViewModel owner)
    {
        Server = server;
        _owner = owner;
    }

    public ServerItemViewModel Server { get; }
    public MainViewModel Owner => _owner;
    public string Title => Server.Name;

    [ObservableProperty] private SessionState _state = SessionState.Conectando;
    [ObservableProperty] private string _statusTitle = "Conectando…";
    [ObservableProperty] private string _statusDetail = "";
    [ObservableProperty] private bool _isSelected;

    /// <summary>
    /// Área do RDP visível. Precisa estar visível ANTES de criar/conectar o controle:
    /// o ActiveX criado com a janela escondida conecta mas não renderiza (tela preta).
    /// </summary>
    [ObservableProperty] private bool _rdpVisible;

    public bool ShowRdp => RdpVisible;
    public bool ShowStatusPanel => !RdpVisible;
    public bool IsBusy => State == SessionState.Conectando;

    partial void OnRdpVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowRdp));
        OnPropertyChanged(nameof(ShowStatusPanel));
    }
    public bool CanReconnect => State is SessionState.Desconectado or SessionState.Erro;
    public bool IsError => State == SessionState.Erro;
    public bool IsLive => State is SessionState.Conectado or SessionState.Reconectando or SessionState.Conectando;

    /// <summary>Ícone da aba: carregando enquanto conecta, aviso em erro, monitor no resto.</summary>
    public bool ShowTabSpinner => State is SessionState.Conectando or SessionState.Reconectando;
    public bool ShowTabIcon => !ShowTabSpinner && !IsError;

    partial void OnStateChanged(SessionState value)
    {
        OnPropertyChanged(nameof(ShowTabSpinner));
        OnPropertyChanged(nameof(ShowTabIcon));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanReconnect));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(IsLive));
    }

    /// <param name="rdpVisible">Só usado em <see cref="SessionState.Conectando"/>; nos outros estados é automático.</param>
    public void SetState(SessionState state, string title, string detail = "", bool? rdpVisible = null)
    {
        StatusTitle = title;
        StatusDetail = detail;
        State = state;
        RdpVisible = state switch
        {
            SessionState.Conectado or SessionState.Reconectando => true,
            SessionState.Desconectado or SessionState.Erro => false,
            _ => rdpVisible ?? RdpVisible,
        };
    }

    // Pedidos para a view (que é dona do controle RDP)
    public event Action? ReconnectRequested;
    public event Action? CloseRequested;
    public event Action? FullScreenRequested;
    public event Action? FocusRequested;
    public event Action? ThumbnailRequested;

    [RelayCommand] void Reconnect() => ReconnectRequested?.Invoke();
    [RelayCommand] void Close() => _owner.CloseSession(this);
    [RelayCommand] void FullScreen() => FullScreenRequested?.Invoke();
    [RelayCommand] void Edit() => _owner.EditServer(Server);
    [RelayCommand] void OpenExternal() => _owner.Connect(Server, OpenMode.Externo);

    internal void RequestFocus() => FocusRequested?.Invoke();
    internal void RequestThumbnail() => ThumbnailRequested?.Invoke();

    /// <summary>Pede para a view desconectar; ela chama <see cref="NotifyClosed"/> quando terminar.</summary>
    internal void RequestClose()
    {
        if (CloseRequested == null) NotifyClosed();
        else CloseRequested.Invoke();
    }

    bool _closedNotified;

    internal void NotifyClosed()
    {
        if (_closedNotified) return;
        _closedNotified = true;
        _owner.OnSessionClosed(this);
    }
}
