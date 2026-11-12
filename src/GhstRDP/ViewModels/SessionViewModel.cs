using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GhstRDP.Core;

namespace GhstRDP.ViewModels;

public enum SessionState { Connecting, Connected, Reconnecting, Disconnected, Error }

/// <summary>An embedded RDP session shown as a tab. SessionView owns the ActiveX control and reports its state here.</summary>
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

    [ObservableProperty] private SessionState _state = SessionState.Connecting;
    [ObservableProperty] private string _statusTitle = "Conectando…";
    [ObservableProperty] private string _statusDetail = "";
    [ObservableProperty] private bool _isSelected;

    /// <summary>
    /// Whether the RDP area is visible. It must be visible before the control is created and connected:
    /// an ActiveX created in a hidden window connects but renders a black screen.
    /// </summary>
    [ObservableProperty] private bool _rdpVisible;

    public bool ShowRdp => RdpVisible;
    public bool ShowStatusPanel => !RdpVisible;
    public bool IsBusy => State == SessionState.Connecting;

    partial void OnRdpVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowRdp));
        OnPropertyChanged(nameof(ShowStatusPanel));
    }
    public bool CanReconnect => State is SessionState.Disconnected or SessionState.Error;
    public bool IsError => State == SessionState.Error;
    public bool IsLive => State is SessionState.Connected or SessionState.Reconnecting or SessionState.Connecting;

    public bool ShowTabSpinner => State is SessionState.Connecting or SessionState.Reconnecting;
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

    /// <param name="rdpVisible">Only used while <see cref="SessionState.Connecting"/>; other states set it automatically.</param>
    public void SetState(SessionState state, string title, string detail = "", bool? rdpVisible = null)
    {
        StatusTitle = title;
        StatusDetail = detail;
        State = state;
        RdpVisible = state switch
        {
            SessionState.Connected or SessionState.Reconnecting => true,
            SessionState.Disconnected or SessionState.Error => false,
            _ => rdpVisible ?? RdpVisible,
        };
    }

    public event Action? ReconnectRequested;
    public event Action? CloseRequested;
    public event Action? FullScreenRequested;
    public event Action? FocusRequested;
    public event Action? ThumbnailRequested;

    [RelayCommand] void Reconnect() => ReconnectRequested?.Invoke();
    [RelayCommand] void Close() => _owner.CloseSession(this);
    [RelayCommand] void FullScreen() => FullScreenRequested?.Invoke();
    [RelayCommand] void Edit() => _owner.EditServer(Server);
    [RelayCommand] void OpenExternal() => _owner.Connect(Server, OpenMode.External);

    internal void RequestFocus() => FocusRequested?.Invoke();
    internal void RequestThumbnail() => ThumbnailRequested?.Invoke();

    /// <summary>Asks the view to disconnect; it calls <see cref="NotifyClosed"/> when done.</summary>
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
