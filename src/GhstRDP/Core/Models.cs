namespace GhstRDP.Core;

public enum ServerKind { Vps, Dedicated, Other }

/// <summary>How a connection opens. <see cref="Default"/> follows <see cref="AppSettings.DefaultOpenMode"/>.</summary>
public enum OpenMode { Default, Embedded, External }

public enum DisplayMode { FitWindow, FullScreen, Custom }

/// <summary>Same values as the RDP control's AudioRedirectionMode.</summary>
public enum AudioMode { Local = 0, Remote = 1, Off = 2 }

/// <summary>Same values as the RDP control's KeyboardHookMode.</summary>
public enum WinKeysMode { Never = 0, Always = 1, FullScreenOnly = 2 }

public sealed class ServerRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 3389;
    public string Username { get; set; } = "";
    public string Domain { get; set; } = "";

    /// <summary>Password encrypted with DPAPI for the current Windows user.</summary>
    public string? PasswordProtected { get; set; }

    public ServerKind Kind { get; set; } = ServerKind.Vps;
    public string Group { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Favorite { get; set; }

    public OpenMode OpenMode { get; set; } = OpenMode.Default;
    public DisplayMode Display { get; set; } = DisplayMode.FitWindow;
    public int CustomWidth { get; set; } = 1920;
    public int CustomHeight { get; set; } = 1080;
    public bool Multimon { get; set; }

    public bool RedirectClipboard { get; set; } = true;
    public bool RedirectDrives { get; set; }
    public bool RedirectPrinters { get; set; }
    public AudioMode Audio { get; set; } = AudioMode.Local;
    public bool AdminSession { get; set; }

    /// <summary>Card image file name inside <see cref="Paths.BannersDir"/>. Takes priority over the session preview.</summary>
    public string? BannerFile { get; set; }

    /// <summary>True to cover the whole card, false to center it as a logo.</summary>
    public bool BannerFill { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastConnectedAt { get; set; }
    public int ConnectCount { get; set; }

    public ServerRecord Clone() => (ServerRecord)MemberwiseClone();
}

public sealed class AppSettings
{
    public OpenMode DefaultOpenMode { get; set; } = OpenMode.Embedded;
    public WinKeysMode WinKeys { get; set; } = WinKeysMode.FullScreenOnly;
    public bool MatchWindowsScale { get; set; } = true;
    public bool StatusCheck { get; set; } = true;
    public int StatusIntervalSeconds { get; set; } = 30;
    public bool Thumbnails { get; set; } = true;
    public bool ListView { get; set; }
    public bool NavCollapsed { get; set; }

    /// <summary>Hides IP addresses (except in edit forms) and blurs session previews.</summary>
    public bool PrivacyMode { get; set; }

    public bool AutoUpdate { get; set; } = true;

    /// <summary>Palette index per client, keyed by lowercase client name.</summary>
    public Dictionary<string, int> ClientColors { get; set; } = new();

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
}

public sealed class AppData
{
    public int Version { get; set; } = DataMigration.CurrentVersion;
    public AppSettings Settings { get; set; } = new();
    public List<ServerRecord> Servers { get; set; } = new();
}
