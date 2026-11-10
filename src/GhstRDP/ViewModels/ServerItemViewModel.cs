using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GhstRDP.Core;

namespace GhstRDP.ViewModels;

public enum ServerStatus { Desconhecido, Verificando, Online, Offline }

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
        ServerKind.Dedicado => "Dedicado",
        _ => "Outro",
    };
    public bool Favorite => Record.Favorite;
    public string FavoriteGlyph => Record.Favorite ? "" : "";
    public string Notes => Record.Notes;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Record.Notes);
    public string Subtitle => HasGroup ? $"{DisplayAddress}  ·  {Group}" : DisplayAddress;

    // ---------- Modo sigiloso ----------
    bool Privacy => _owner.PrivacyMode;

    /// <summary>Endereço dentro de frases e dicas (mensagens de erro, confirmações): "IP oculto" no modo sigiloso.</summary>
    public string DisplayAddress => Privacy ? "(IP oculto)" : Address;

    /// <summary>
    /// Endereço para os cards e a lista. No modo sigiloso é um IP FALSO, que a tela mostra borrado
    /// (borrar o IP real não basta: dígitos borrados podem ser reconstruídos).
    /// </summary>
    public string ShownAddress => Privacy ? FakeAddress(Record.Host) : Address;
    public bool IsMasked => Privacy;

    static string FakeAddress(string host) =>
        System.Net.IPAddress.TryParse(host, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? "000.000.000.000"
            : "servidor.exemplo.com";

    /// <summary>Cliente e tipo em maiúsculas para as etiquetas do rodapé do card.</summary>
    public string ClientLabel => Record.Group.Trim().ToUpperInvariant();
    public string KindTag => KindLabel.ToUpperInvariant();

    /// <summary>Texto à direita do rodapé: latência quando online.</summary>
    public string FooterStatus => Status switch
    {
        ServerStatus.Online => $"{LatencyMs} ms",
        ServerStatus.Offline => "offline",
        ServerStatus.Verificando => "···",
        _ => "",
    };

    ImageSource? _pixelThumbnail;
    /// <summary>Prévia reduzida a poucos pixels (modo sigiloso): mostra só o "clima" da tela, sem texto legível.</summary>
    public ImageSource? PixelThumbnail => _pixelThumbnail;
    public bool ShowThumbnail => !HasBanner && Thumbnail != null && !Privacy;
    public bool ShowPixelThumbnail => !HasBanner && _pixelThumbnail != null && Privacy;
    public bool ShowPlaceholder => !HasBanner && Thumbnail == null;

    // ---------- Banner (logo da empresa no lugar da prévia) ----------
    ImageSource? _banner;
    public ImageSource? Banner => _banner;
    public bool HasBanner => _banner != null;
    /// <summary>Imagem larga (banner pronto) sempre cobre o card; só logos "quadradas" ficam centralizadas.</summary>
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
        ServerStatus.Verificando => "Verificando",
        _ => "—",
    };

    public string StatusTooltip => HasOpenSession
        ? "Sessão aberta no Ghst"
        : Status switch
        {
            ServerStatus.Online => $"Online · {LatencyMs} ms",
            ServerStatus.Offline => $"Sem resposta na porta {Port}",
            ServerStatus.Verificando => "Verificando…",
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
        // Versão "pixelizada" da prévia para o modo sigiloso (poucos pixels = nada legível)
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
                Log.Warn("Falha ao pixelizar prévia", ex);
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

    /// <summary>Troca o registro (após edição) e atualiza todos os bindings.</summary>
    public void Update(ServerRecord record)
    {
        var hostChanged = !string.Equals(record.Host, Record.Host, StringComparison.OrdinalIgnoreCase) || record.Port != Record.Port;
        Record = record;
        LoadBanner();
        if (hostChanged) Status = ServerStatus.Desconhecido;
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
            Log.Warn("Falha ao carregar miniatura", ex);
            Thumbnail = null;
        }
    }

    [RelayCommand] void Connect() => _owner.Connect(this);
    [RelayCommand] void ConnectEmbedded() => _owner.Connect(this, OpenMode.Embutido);
    [RelayCommand] void ConnectExternal() => _owner.Connect(this, OpenMode.Externo);
    [RelayCommand] void Edit() => _owner.EditServer(this);
    [RelayCommand] void Duplicate() => _owner.DuplicateServer(this);
    [RelayCommand] void Delete() => _owner.DeleteServer(this);
    [RelayCommand] void ToggleFavorite() => _owner.ToggleFavorite(this);
    [RelayCommand] void CopyAddress() => _owner.CopyText(Address, "Endereço copiado");
    [RelayCommand] void CopyUsername() => _owner.CopyText(UserDisplay, "Usuário copiado");
    [RelayCommand] void CopyPassword() => _owner.CopyPassword(this);
}
