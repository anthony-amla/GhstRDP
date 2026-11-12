using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GhstRDP.Core;

namespace GhstRDP.ViewModels;

public enum ServerStatus { Unknown, Checking, Online, Offline }

public partial class ServerItemViewModel : ObservableObject
{
    readonly MainViewModel _owner;

    public ServerItemViewModel(ServerRecord record, MainViewModel owner)
    {
        Record = record;
        _owner = owner;
        LoadBanner();
        LoadThumbnail();
    }

    public ServerRecord Record { get; private set; }

    public Guid Id => Record.Id;
    public string Name => Record.Name;
    public string Host => Record.Host;
    public int Port => Record.Port;
    public string Address => Record.Port == 3389 ? Record.Host : $"{Record.Host}:{Record.Port}";
    public string UserDisplay => string.IsNullOrWhiteSpace(Record.Domain) ? Record.Username : $"{Record.Domain}\\{Record.Username}";
    public bool HasUser => !string.IsNullOrWhiteSpace(Record.Username);
    public bool HasPassword => !string.IsNullOrEmpty(Record.PasswordProtected);
    public string Group => Record.Group;
    public bool HasGroup => !string.IsNullOrWhiteSpace(Record.Group);
    public Brush TagBackground => TagPalette.Background(Math.Max(0, _owner.ClientColorIndex(Record.Group)));
    public Brush TagForeground => TagPalette.Foreground(Math.Max(0, _owner.ClientColorIndex(Record.Group)));
    public ServerKind Kind => Record.Kind;
    public string KindLabel => Record.Kind switch
    {
        ServerKind.Vps => "VPS",
        ServerKind.Dedicated => "Dedicado",
        _ => "Outro",
    };
    public bool Favorite => Record.Favorite;
    public string FavoriteGlyph => Record.Favorite ? "" : "";
    public string Notes => Record.Notes;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Record.Notes);
    public string Subtitle => HasGroup ? $"{DisplayAddress}  ·  {Group}" : DisplayAddress;

    bool Privacy => _owner.PrivacyMode;

    /// <summary>Address used inside messages and tooltips; replaced by a placeholder in privacy mode.</summary>
    public string DisplayAddress => Privacy ? "(IP oculto)" : Address;

    /// <summary>
    /// Address shown on cards and list rows. In privacy mode this is a fake address that the view blurs,
    /// since blurred real digits can still be recovered.
    /// </summary>
    public string ShownAddress => Privacy ? FakeAddress(Record.Host) : Address;
    public bool IsMasked => Privacy;

    static string FakeAddress(string host) =>
        System.Net.IPAddress.TryParse(host, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? "000.000.000.000"
            : "servidor.exemplo.com";

    public string ClientLabel => Record.Group.Trim().ToUpperInvariant();
    public string KindTag => KindLabel.ToUpperInvariant();

    public string FooterStatus => Status switch
    {
        ServerStatus.Online => $"{LatencyMs} ms",
        ServerStatus.Offline => "offline",
        ServerStatus.Checking => "···",
        _ => "",
    };

    ImageSource? _pixelThumbnail;
    /// <summary>Thumbnail downscaled to a few pixels for privacy mode, so no text is legible.</summary>
    public ImageSource? PixelThumbnail => _pixelThumbnail;
    public bool ShowThumbnail => !HasBanner && Thumbnail != null && !Privacy;
    public bool ShowPixelThumbnail => !HasBanner && _pixelThumbnail != null && Privacy;
    public bool ShowPlaceholder => !HasBanner && Thumbnail == null;

    ImageSource? _banner;
    public ImageSource? Banner => _banner;
    public bool HasBanner => _banner != null;
    // Wide images always fill the card; only squarish logos are centered.
    bool BannerCovers => Record.BannerFill || Banners.IsWide(_banner);
    public bool ShowBannerLogo => HasBanner && !BannerCovers;
    public bool ShowBannerFill => HasBanner && BannerCovers;

    void LoadBanner() => _banner = Banners.Load(Banners.PathFor(Record));

    public void RefreshPrivacy()
    {
        OnPropertyChanged(nameof(DisplayAddress));
        OnPropertyChanged(nameof(ShownAddress));
        OnPropertyChanged(nameof(IsMasked));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(Tooltip));
        OnPropertyChanged(nameof(ShowThumbnail));
        OnPropertyChanged(nameof(ShowPixelThumbnail));
    }

    public string LastConnectedText
    {
        get
        {
            if (Record.LastConnectedAt is not { } at) return "Nunca conectado";
            var diff = DateTime.Now - at;
            if (diff.TotalMinutes < 1) return "Conectado agora";
            if (diff.TotalMinutes < 60) return $"Há {(int)diff.TotalMinutes} min";
            if (diff.TotalHours < 24) return $"Há {(int)diff.TotalHours} h";
            if (diff.TotalDays < 30) return $"Há {(int)diff.TotalDays} d";
            return at.ToString("dd/MM/yyyy");
        }
    }

    public string Tooltip
    {
        get
        {
            var lines = new List<string> { Name, DisplayAddress };
            if (HasUser) lines.Add($"Usuário: {UserDisplay}");
            lines.Add(LastConnectedText);
            if (HasNotes) lines.Add(Notes);
            return string.Join("\n", lines);
        }
    }

    [ObservableProperty] private ServerStatus _status;
    [ObservableProperty] private int _latencyMs;
    [ObservableProperty] private ImageSource? _thumbnail;
    [ObservableProperty] private bool _hasOpenSession;

    public bool HasThumbnail => Thumbnail != null;

    public string StatusText => Status switch
    {
        ServerStatus.Online => $"{LatencyMs} ms",
        ServerStatus.Offline => "Offline",
        ServerStatus.Checking => "Verificando",
        _ => "—",
    };

    public string StatusTooltip => HasOpenSession
        ? "Sessão aberta no Ghst"
        : Status switch
        {
            ServerStatus.Online => $"Online · {LatencyMs} ms",
            ServerStatus.Offline => $"Sem resposta na porta {Port}",
            ServerStatus.Checking => "Verificando…",
            _ => "Status desconhecido",
        };

    partial void OnStatusChanged(ServerStatus value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusTooltip));
        OnPropertyChanged(nameof(FooterStatus));
    }

    partial void OnLatencyMsChanged(int value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusTooltip));
        OnPropertyChanged(nameof(FooterStatus));
    }

    partial void OnHasOpenSessionChanged(bool value) => OnPropertyChanged(nameof(StatusTooltip));

    partial void OnThumbnailChanged(ImageSource? value)
    {
        _pixelThumbnail = null;
        if (value is BitmapSource bmp && bmp.PixelWidth > 0)
        {
            try
            {
                var scale = 28.0 / bmp.PixelWidth;
                var small = new TransformedBitmap(bmp, new ScaleTransform(scale, scale));
                small.Freeze();
                _pixelThumbnail = small;
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to pixelate thumbnail", ex);
            }
        }
        OnPropertyChanged(nameof(HasThumbnail));
        OnPropertyChanged(nameof(PixelThumbnail));
        OnPropertyChanged(nameof(ShowThumbnail));
        OnPropertyChanged(nameof(ShowPixelThumbnail));
        OnPropertyChanged(nameof(ShowPlaceholder));
    }

    public void SetStatus(bool online, int ms)
    {
        LatencyMs = ms;
        Status = online ? ServerStatus.Online : ServerStatus.Offline;
        _owner.NotifyStatusChanged();
    }

    /// <summary>Replaces the record after an edit and refreshes every binding.</summary>
    public void Update(ServerRecord record)
    {
        var hostChanged = !string.Equals(record.Host, Record.Host, StringComparison.OrdinalIgnoreCase) || record.Port != Record.Port;
        Record = record;
        LoadBanner();
        if (hostChanged) Status = ServerStatus.Unknown;
        OnPropertyChanged(string.Empty);
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(LastConnectedText));
        OnPropertyChanged(nameof(Tooltip));
    }

    public void LoadThumbnail()
    {
        try
        {
            var path = Thumbnails.PathFor(Id);
            if (!File.Exists(path))
            {
                Thumbnail = null;
                return;
            }
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = 480;
            image.EndInit();
            image.Freeze();
            Thumbnail = image;
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to load thumbnail", ex);
            Thumbnail = null;
        }
    }

    [RelayCommand] void Connect() => _owner.Connect(this);
    [RelayCommand] void ConnectEmbedded() => _owner.Connect(this, OpenMode.Embedded);
    [RelayCommand] void ConnectExternal() => _owner.Connect(this, OpenMode.External);
    [RelayCommand] void Edit() => _owner.EditServer(this);
    [RelayCommand] void Duplicate() => _owner.DuplicateServer(this);
    [RelayCommand] void Delete() => _owner.DeleteServer(this);
    [RelayCommand] void ToggleFavorite() => _owner.ToggleFavorite(this);
    [RelayCommand] void CopyAddress() => _owner.CopyText(Address, "Endereço copiado");
    [RelayCommand] void CopyUsername() => _owner.CopyText(UserDisplay, "Usuário copiado");
    [RelayCommand] void CopyPassword() => _owner.CopyPassword(this);
}
