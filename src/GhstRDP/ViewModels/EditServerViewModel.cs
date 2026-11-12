using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GhstRDP.Core;

namespace GhstRDP.ViewModels;

public partial class EditServerViewModel : ObservableObject
{
    readonly ServerRecord _original;

    EditServerViewModel(ServerRecord original, bool isNew, OpenMode appDefault)
    {
        _original = original;
        IsNew = isNew;
        AppDefaultLabel = appDefault == OpenMode.External ? "Padrão (mstsc)" : "Padrão (no Ghst)";

        _name = original.Name;
        _host = original.Host;
        _port = original.Port.ToString();
        _username = original.Username;
        _domain = original.Domain;
        _password = Secrets.Unprotect(original.PasswordProtected) ?? "";
        _kind = original.Kind;
        _group = original.Group;
        _notes = original.Notes;
        _favorite = original.Favorite;
        _openMode = original.OpenMode;
        _display = original.Display;
        _customWidth = original.CustomWidth.ToString();
        _customHeight = original.CustomHeight.ToString();
        _multimon = original.Multimon;
        _redirectClipboard = original.RedirectClipboard;
        _redirectDrives = original.RedirectDrives;
        _redirectPrinters = original.RedirectPrinters;
        _audio = original.Audio;
        _adminSession = original.AdminSession;

        _bannerFill = original.BannerFill;
        _bannerPath = Banners.PathFor(original);
        _bannerPreview = Banners.Load(_bannerPath);
        // A duplicated server needs its own copy of the original's banner.
        BannerChanged = isNew && _bannerPath != null;
    }

    [ObservableProperty] private string? _bannerPath;
    [ObservableProperty] private System.Windows.Media.ImageSource? _bannerPreview;
    [ObservableProperty] private bool _bannerFill;
    [ObservableProperty] private bool _applyBannerToClient;

    public bool BannerChanged { get; private set; }
    public bool HasBanner => BannerPreview != null;
    /// <summary>Wide images always fill the card, so the fill option only applies to logos.</summary>
    public bool IsWideBanner => Banners.IsWide(BannerPreview);
    public bool CanChooseFill => HasBanner && !IsWideBanner;
    public bool ShowPreviewLogo => HasBanner && !BannerFill && !IsWideBanner;
    public bool ShowPreviewFill => HasBanner && (BannerFill || IsWideBanner);
    public bool CanApplyToClient => HasBanner && !string.IsNullOrWhiteSpace(Group);
    public string ApplyToClientText => $"Usar este banner em todos os servidores de {(Group ?? "").Trim()}";

    partial void OnBannerPathChanged(string? value) => BannerPreview = Banners.Load(value);

    partial void OnBannerPreviewChanged(System.Windows.Media.ImageSource? value)
    {
        OnPropertyChanged(nameof(HasBanner));
        OnPropertyChanged(nameof(IsWideBanner));
        OnPropertyChanged(nameof(CanChooseFill));
        OnPropertyChanged(nameof(ShowPreviewLogo));
        OnPropertyChanged(nameof(ShowPreviewFill));
        OnPropertyChanged(nameof(CanApplyToClient));
    }

    partial void OnBannerFillChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPreviewLogo));
        OnPropertyChanged(nameof(ShowPreviewFill));
    }

    partial void OnGroupChanged(string value)
    {
        OnPropertyChanged(nameof(CanApplyToClient));
        OnPropertyChanged(nameof(ApplyToClientText));
    }

    [RelayCommand]
    void ChooseBanner()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Escolher banner do card",
            Filter = Banners.FileFilter,
        };
        if (dialog.ShowDialog() != true) return;
        var error = Banners.Validate(dialog.FileName);
        if (error != null)
        {
            Error = error;
            return;
        }
        Error = "";
        BannerPath = dialog.FileName;
        BannerChanged = true;
    }

    [RelayCommand]
    void RemoveBanner()
    {
        BannerPath = null;
        BannerChanged = true;
        ApplyBannerToClient = false;
    }

    public static EditServerViewModel ForNew(OpenMode appDefault, ServerRecord? template = null) =>
        new(template ?? new ServerRecord(), true, appDefault);

    public static EditServerViewModel ForEdit(ServerRecord record, OpenMode appDefault) =>
        new(record.Clone(), false, appDefault);

    public bool IsNew { get; }
    public string Title => IsNew ? "Adicionar servidor" : "Editar servidor";

    /// <summary>Checked before showing the password in plain text; returns false to deny (e.g. vault PIN not entered).</summary>
    public Func<bool>? RevealGate { get; set; }
    public string AppDefaultLabel { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _host;
    [ObservableProperty] private string _port;
    [ObservableProperty] private string _username;
    [ObservableProperty] private string _domain;
    [ObservableProperty] private string _password;
    [ObservableProperty] private ServerKind _kind;
    [ObservableProperty] private string _group;
    [ObservableProperty] private string _notes;
    [ObservableProperty] private bool _favorite;
    [ObservableProperty] private OpenMode _openMode;
    [ObservableProperty] private DisplayMode _display;
    [ObservableProperty] private string _customWidth;
    [ObservableProperty] private string _customHeight;
    [ObservableProperty] private bool _multimon;
    [ObservableProperty] private bool _redirectClipboard;
    [ObservableProperty] private bool _redirectDrives;
    [ObservableProperty] private bool _redirectPrinters;
    [ObservableProperty] private AudioMode _audio;
    [ObservableProperty] private bool _adminSession;

    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _testResult = "";
    [ObservableProperty] private bool _testOk;

    public bool IsCustomDisplay => Display == DisplayMode.Custom;
    partial void OnDisplayChanged(DisplayMode value) => OnPropertyChanged(nameof(IsCustomDisplay));


    [RelayCommand]
    async Task TestAsync()
    {
        var (host, port, error) = ParseHost();
        if (error != null)
        {
            TestOk = false;
            TestResult = error;
            return;
        }
        TestOk = false;
        TestResult = "Testando…";
        var (online, ms) = await StatusMonitor.ProbeAsync(host, port);
        TestOk = online;
        TestResult = online ? $"Porta {port} respondeu em {ms} ms" : $"Sem resposta em {host}:{port}";
    }

    (string Host, int Port, string? Error) ParseHost()
    {
        var host = (Host ?? "").Trim();
        if (host.StartsWith("rdp://", StringComparison.OrdinalIgnoreCase)) host = host[6..];
        host = host.TrimEnd('/');
        var portText = (Port ?? "").Trim();

        // "host:port" typed into the host field (IPv4 or domain; IPv6 has more colons)
        if (host.Count(c => c == ':') == 1)
        {
            var parts = host.Split(':');
            host = parts[0];
            if (!string.IsNullOrWhiteSpace(parts[1])) portText = parts[1];
        }
        if (host.StartsWith('[') && host.Contains("]:"))
        {
            var idx = host.LastIndexOf("]:", StringComparison.Ordinal);
            portText = host[(idx + 2)..];
            host = host[1..idx];
        }

        if (string.IsNullOrWhiteSpace(host)) return ("", 0, "Informe o IP ou domínio do servidor.");
        if (host.Contains(' ')) return ("", 0, "O endereço não pode ter espaços.");
        if (!int.TryParse(string.IsNullOrWhiteSpace(portText) ? "3389" : portText, out var port) || port is < 1 or > 65535)
            return ("", 0, "Porta inválida (1 a 65535).");
        return (host, port, null);
    }

    /// <summary>Normalizes host and port, fills a missing name and checks the custom resolution.</summary>
    /// <returns>An error message to show, or null when the form is valid.</returns>
    public string? Validate()
    {
        var (host, port, error) = ParseHost();
        if (error != null) return error;
        Host = host;
        Port = port.ToString();

        if (Display == DisplayMode.Custom)
        {
            if (!int.TryParse(CustomWidth, out var w) || w is < 640 or > 8192 ||
                !int.TryParse(CustomHeight, out var h) || h is < 480 or > 8192)
                return "Resolução personalizada inválida (mínimo 640×480, máximo 8192).";
        }

        if (string.IsNullOrWhiteSpace(Name)) Name = host;
        return null;
    }

    public ServerRecord ToRecord()
    {
        var record = _original.Clone();
        record.Name = Name.Trim();
        record.Host = Host.Trim();
        record.Port = int.Parse(Port);
        record.Username = (Username ?? "").Trim();
        record.Domain = (Domain ?? "").Trim();
        record.PasswordProtected = Secrets.Protect(Password);
        record.Kind = Kind;
        record.Group = (Group ?? "").Trim();
        record.Notes = (Notes ?? "").Trim();
        record.Favorite = Favorite;
        record.OpenMode = OpenMode;
        record.Display = Display;
        if (int.TryParse(CustomWidth, out var width)) record.CustomWidth = width & ~1;
        if (int.TryParse(CustomHeight, out var height)) record.CustomHeight = height;
        record.Multimon = Multimon;
        record.RedirectClipboard = RedirectClipboard;
        record.RedirectDrives = RedirectDrives;
        record.RedirectPrinters = RedirectPrinters;
        record.Audio = Audio;
        record.AdminSession = AdminSession;
        record.BannerFill = BannerFill;
        // BannerFile is set by the caller, which copies the image under the server id.
        return record;
    }
}
