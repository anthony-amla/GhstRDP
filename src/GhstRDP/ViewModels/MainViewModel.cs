using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GhstRDP.Core;
using GhstRDP.Views;

namespace GhstRDP.ViewModels;

public enum Page { Servers, Sessions, Passwords, Settings }

public partial class MainViewModel : ObservableObject, IDisposable
{
    readonly DataStore _store;
    readonly Dispatcher _ui;
    readonly StatusMonitor _status;
    readonly DispatcherTimer _clock;
    readonly DispatcherTimer _toastTimer;
    readonly DispatcherTimer _vaultTimer;
    readonly VaultService _vaultService = new();

    public MainViewModel(DataStore store, Dispatcher ui)
    {
        _store = store;
        _ui = ui;

        _vaultService.Load();
        Vault = new VaultViewModel(this, _vaultService);
        Update = new UpdateViewModel(this);
        _vaultTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(15) };
        _vaultTimer.Tick += (_, _) => _vaultService.CheckAutoLock();
        _vaultTimer.Start();

        foreach (var record in store.Data.Servers)
            Servers.Add(new ServerItemViewModel(record, this));

        ServersView = new ListCollectionView(Servers) { Filter = FilterServer };
        ServersView.SortDescriptions.Add(new SortDescription(nameof(ServerItemViewModel.Name), ListSortDirection.Ascending));

        Sessions.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SessionCount));
            OnPropertyChanged(nameof(HasSessions));
            OnPropertyChanged(nameof(Summary));
        };

        _isListView = Settings.ListView;
        _isNavExpanded = !Settings.NavCollapsed;

        RebuildChips();
        RefreshDerived();

        _status = new StatusMonitor(() => Servers.ToList(), ui)
        {
            Enabled = Settings.StatusCheck,
            Interval = TimeSpan.FromSeconds(Math.Clamp(Settings.StatusIntervalSeconds, 10, 600)),
        };
        _status.Start();

        _clock = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMinutes(1) };
        _clock.Tick += (_, _) =>
        {
            foreach (var s in Servers) s.RefreshTexts();
            OnPropertyChanged(nameof(Greeting));
        };
        _clock.Start();

        _toastTimer = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromSeconds(2.6) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        CurrentPage = Page.Servers;
    }

    public AppSettings Settings => _store.Data.Settings;

    public ObservableCollection<ServerItemViewModel> Servers { get; } = new();
    public ListCollectionView ServersView { get; }
    public ObservableCollection<ServerItemViewModel> Favorites { get; } = new();
    public ObservableCollection<ServerItemViewModel> Recents { get; } = new();
    public ObservableCollection<SessionViewModel> Sessions { get; } = new();

    [ObservableProperty] private Page _currentPage;
    [ObservableProperty] private bool _isNavExpanded;
    [ObservableProperty] private bool _isListView;

    partial void OnCurrentPageChanged(Page value)
    {
        if (value == Page.Sessions) SelectedSession?.RequestFocus();
        if (value == Page.Passwords) Vault.Refresh();
    }

    public UpdateViewModel Update { get; }
    public VaultViewModel Vault { get; }

    /// <summary>
    /// Asks for the vault PIN before revealing or copying a password, if a PIN is configured.
    /// </summary>
    /// <returns>True when access is allowed (no PIN set, already unlocked, or the PIN was entered).</returns>
    public bool EnsureVaultUnlocked()
    {
        if (!_vaultService.IsConfigured) return true;
        if (_vaultService.IsUnlocked)
        {
            _vaultService.Touch();
            return true;
        }
        return Dialogs.Pin(Application.Current.MainWindow, Vault);
    }

    public void SaveVault()
    {
        try { _vaultService.Save(); }
        catch (Exception ex)
        {
            Log.Error("Failed to save the vault", ex);
            Dialogs.Error(Application.Current.MainWindow, "Não foi possível salvar o cofre", ex.Message);
        }
    }

    /// <summary>Opens the editor to add a vault entry (<paramref name="entry"/> null) or edit an existing one.</summary>
    /// <param name="serverId">Server preselected in the editor, if any.</param>
    /// <param name="machine">Free-text machine name preselected when there is no linked server.</param>
    public void EditVaultEntry(VaultEntry? entry, Guid? serverId, string? machine = null)
    {
        if (!EnsureVaultUnlocked()) return;
        if (!_vaultService.IsConfigured)
        {
            CurrentPage = Page.Passwords;
            ShowToast("Crie um PIN para usar o cofre");
            return;
        }

        var machines = new List<MachineOption> { new(null, "Outra máquina (digitar nome)") };
        machines.AddRange(Servers.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Select(s => new MachineOption(s.Id, s.Name)));

        var plain = entry == null ? null : _vaultService.Unprotect(entry.Secret);
        var editor = new VaultEntryEditViewModel(entry, plain, machines, serverId, machine);
        if (!Dialogs.EditVaultEntry(Application.Current.MainWindow, editor)) return;
        if (!EnsureVaultUnlocked()) return;

        try
        {
            var target = entry ?? new VaultEntry();
            editor.ApplyTo(target, _vaultService);
            if (entry == null) _vaultService.Data.Entries.Add(target);
            SaveVault();
            Vault.Refresh();
            ShowToast(entry == null ? "Senha guardada no cofre" : "Senha atualizada");
        }
        catch (Exception ex)
        {
            Log.Error("Failed to store vault entry", ex);
            Dialogs.Error(Application.Current.MainWindow, "Não foi possível guardar a senha", ex.Message);
        }
    }

    partial void OnIsNavExpandedChanged(bool value)
    {
        Settings.NavCollapsed = !value;
        SaveQuiet();
    }

    partial void OnIsListViewChanged(bool value)
    {
        Settings.ListView = value;
        SaveQuiet();
    }

    [RelayCommand] void ToggleNav() => IsNavExpanded = !IsNavExpanded;
    [RelayCommand] void ShowGrid() => IsListView = false;
    [RelayCommand] void ShowList() => IsListView = true;
    [RelayCommand] void GoTo(Page page) => CurrentPage = page;

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private TypeFilter _typeFilter;
    [ObservableProperty] private StatusFilter _statusFilter;
    [ObservableProperty] private bool _favoritesOnly;
    [ObservableProperty] private string? _clientFilter;
    [ObservableProperty] private int _filteredCount;

    public ObservableCollection<ClientFilter> Clients { get; } = new();
    public bool HasClients => Clients.Count > 0;
    public bool HasVps => Servers.Any(s => s.Kind == ServerKind.Vps);
    public bool HasDedicated => Servers.Any(s => s.Kind == ServerKind.Dedicated);
    public bool HasOther => Servers.Any(s => s.Kind == ServerKind.Other);

    public bool HasActiveFilter => !string.IsNullOrWhiteSpace(Search) || TypeFilter != TypeFilter.All
                                   || StatusFilter != StatusFilter.All || FavoritesOnly || ClientFilter != null;

    public string ResultText => FilteredCount == Servers.Count
        ? (Servers.Count == 1 ? "1 servidor" : $"{Servers.Count} servidores")
        : $"{FilteredCount} de {Servers.Count}";

    partial void OnSearchChanged(string value) => RefreshView();
    partial void OnTypeFilterChanged(TypeFilter value) => RefreshView();
    partial void OnStatusFilterChanged(StatusFilter value) => RefreshView();
    partial void OnFavoritesOnlyChanged(bool value) => RefreshView();

    partial void OnClientFilterChanged(string? value)
    {
        foreach (var c in Clients) c.IsSelected = string.Equals(c.Name, value, StringComparison.OrdinalIgnoreCase);
        RefreshView();
    }

    [RelayCommand]
    void ToggleClient(string name) =>
        ClientFilter = string.Equals(ClientFilter, name, StringComparison.OrdinalIgnoreCase) ? null : name;

    [RelayCommand] void ToggleFavoritesOnly() => FavoritesOnly = !FavoritesOnly;

    [RelayCommand]
    void ClearFilters()
    {
        Search = "";
        TypeFilter = TypeFilter.All;
        StatusFilter = StatusFilter.All;
        FavoritesOnly = false;
        ClientFilter = null;
    }

    bool FilterServer(object obj)
    {
        if (obj is not ServerItemViewModel s) return false;

        if (TypeFilter != TypeFilter.All)
        {
            var kind = TypeFilter switch { TypeFilter.Vps => ServerKind.Vps, TypeFilter.Dedicated => ServerKind.Dedicated, _ => ServerKind.Other };
            if (s.Kind != kind) return false;
        }
        if (StatusFilter == StatusFilter.Online && s.Status != ServerStatus.Online) return false;
        if (StatusFilter == StatusFilter.Offline && s.Status != ServerStatus.Offline) return false;
        if (FavoritesOnly && !s.Favorite) return false;
        if (ClientFilter != null && !string.Equals(s.Group.Trim(), ClientFilter, StringComparison.OrdinalIgnoreCase)) return false;

        var q = Search?.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        return s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
               || s.Host.Contains(q, StringComparison.OrdinalIgnoreCase)
               || s.Group.Contains(q, StringComparison.OrdinalIgnoreCase)
               || s.Record.Username.Contains(q, StringComparison.OrdinalIgnoreCase)
               || s.Notes.Contains(q, StringComparison.OrdinalIgnoreCase)
               || s.KindLabel.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    void RefreshView()
    {
        ServersView.Refresh();
        FilteredCount = ServersView.Count;
        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(HasActiveFilter));
    }

    void RebuildChips()
    {
        Clients.Clear();
        foreach (var g in GroupNames())
        {
            var index = ClientColorIndex(g);
            Clients.Add(new ClientFilter(g, Servers.Count(s => string.Equals(s.Group.Trim(), g, StringComparison.OrdinalIgnoreCase)),
                TagPalette.Background(index), TagPalette.Foreground(index), TagPalette.Dot(index))
            {
                IsSelected = string.Equals(g, ClientFilter, StringComparison.OrdinalIgnoreCase),
            });
        }
        if (ClientFilter != null && !Clients.Any(c => c.IsSelected)) ClientFilter = null;
        OnPropertyChanged(nameof(HasClients));
        OnPropertyChanged(nameof(HasVps));
        OnPropertyChanged(nameof(HasDedicated));
        OnPropertyChanged(nameof(HasOther));
    }

    /// <summary>
    /// Palette color for a client. The first time a client is seen it gets the least used color,
    /// which is persisted so it stays stable.
    /// </summary>
    /// <returns>Index into <see cref="TagPalette"/>, or -1 when the client name is empty.</returns>
    public int ClientColorIndex(string? client)
    {
        var key = (client ?? "").Trim().ToLowerInvariant();
        if (key.Length == 0) return -1;
        var map = Settings.ClientColors;
        if (map.TryGetValue(key, out var index)) return index;

        var usage = new int[TagPalette.Count];
        foreach (var used in map.Values) usage[((used % TagPalette.Count) + TagPalette.Count) % TagPalette.Count]++;
        var preferred = TagPalette.HashIndex(key);
        index = Enumerable.Range(0, TagPalette.Count)
            .OrderBy(i => usage[i])
            .ThenBy(i => i == preferred ? 0 : 1)
            .ThenBy(i => i)
            .First();
        map[key] = index;
        SaveQuiet();
        return index;
    }

    public IReadOnlyList<string> GroupNames() => Servers
        .Select(s => s.Group.Trim())
        .Where(g => g.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
        .ToList();

    void RefreshDerived()
    {
        Favorites.Clear();
        foreach (var s in Servers.Where(s => s.Favorite).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            Favorites.Add(s);

        Recents.Clear();
        foreach (var s in Servers.Where(s => s.Record.LastConnectedAt != null)
                     .OrderByDescending(s => s.Record.LastConnectedAt).Take(8))
            Recents.Add(s);

        RefreshView();
        OnPropertyChanged(nameof(HasServers));
        OnPropertyChanged(nameof(HasFavorites));
        OnPropertyChanged(nameof(HasRecents));
        OnPropertyChanged(nameof(ShowAllOnHome));
        OnPropertyChanged(nameof(ServerCount));
        OnPropertyChanged(nameof(Summary));
    }

    public bool HasServers => Servers.Count > 0;
    public bool HasFavorites => Favorites.Count > 0;
    public bool HasRecents => Recents.Count > 0;
    /// <summary>True when the home page has no favorites or recents, so it lists every server instead.</summary>
    public bool ShowAllOnHome => HasServers && !HasFavorites && !HasRecents;
    public int ServerCount => Servers.Count;
    public int SessionCount => Sessions.Count;
    public bool HasSessions => Sessions.Count > 0;

    public string Greeting
    {
        get
        {
            var h = DateTime.Now.Hour;
            return h < 12 ? "Bom dia" : h < 18 ? "Boa tarde" : "Boa noite";
        }
    }

    public string Summary
    {
        get
        {
            var online = Servers.Count(s => s.Status == ServerStatus.Online);
            var parts = new List<string> { Servers.Count == 1 ? "1 servidor" : $"{Servers.Count} servidores" };
            if (Settings.StatusCheck && Servers.Count > 0) parts.Add($"{online} online");
            if (Sessions.Count > 0) parts.Add(Sessions.Count == 1 ? "1 sessão aberta" : $"{Sessions.Count} sessões abertas");
            return string.Join("  ·  ", parts);
        }
    }

    [ObservableProperty] private SessionViewModel? _selectedSession;

    partial void OnSelectedSessionChanging(SessionViewModel? oldValue, SessionViewModel? newValue)
    {
        oldValue?.RequestThumbnail();
    }

    partial void OnSelectedSessionChanged(SessionViewModel? value)
    {
        foreach (var s in Sessions) s.IsSelected = ReferenceEquals(s, value);
        value?.RequestFocus();
    }

    public void Connect(ServerItemViewModel item, OpenMode? forced = null)
    {
        var mode = forced ?? (item.Record.OpenMode == OpenMode.Default ? Settings.DefaultOpenMode : item.Record.OpenMode);
        if (mode == OpenMode.External) OpenExternal(item);
        else OpenEmbedded(item);
    }

    void OpenExternal(ServerItemViewModel item)
    {
        try
        {
            MstscLauncher.Launch(item.Record, Secrets.Unprotect(item.Record.PasswordProtected), Settings);
            MarkConnected(item);
            ShowToast($"Abrindo {item.Name} no mstsc");
        }
        catch (Exception ex)
        {
            Log.Error("Failed to launch mstsc", ex);
            Dialogs.Error(Application.Current.MainWindow, "Não foi possível abrir o mstsc", ex.Message);
        }
    }

    void OpenEmbedded(ServerItemViewModel item)
    {
        var existing = Sessions.FirstOrDefault(s => ReferenceEquals(s.Server, item));
        if (existing != null)
        {
            SelectedSession = existing;
            CurrentPage = Page.Sessions;
            if (existing.CanReconnect) existing.ReconnectCommand.Execute(null);
            return;
        }

        var session = new SessionViewModel(item, this);
        Sessions.Add(session);
        item.HasOpenSession = true;
        CurrentPage = Page.Sessions;
        SelectedSession = session;
    }

    public void CloseSession(SessionViewModel session) => session.RequestClose();

    /// <summary>Called by the session view once the connection has fully shut down.</summary>
    internal void OnSessionClosed(SessionViewModel session)
    {
        var index = Sessions.IndexOf(session);
        if (index < 0) return;
        var wasSelected = ReferenceEquals(SelectedSession, session);
        Sessions.RemoveAt(index);
        session.Server.HasOpenSession = Sessions.Any(s => ReferenceEquals(s.Server, session.Server));
        if (wasSelected)
            SelectedSession = Sessions.Count == 0 ? null : Sessions[Math.Min(index, Sessions.Count - 1)];
        if (Sessions.Count == 0 && CurrentPage == Page.Sessions) CurrentPage = Page.Servers;
        SessionsChanged?.Invoke();
    }

    /// <summary>Raised after a closed session is removed from <see cref="Sessions"/>.</summary>
    public event Action? SessionsChanged;

    public void MarkConnected(ServerItemViewModel item)
    {
        item.Record.LastConnectedAt = DateTime.Now;
        item.Record.ConnectCount++;
        item.RefreshTexts();
        SaveQuiet();
        RefreshDerived();
    }

    [RelayCommand]
    void AddServer() => OpenEditor(null, null);

    public void EditServer(ServerItemViewModel item) => OpenEditor(item, null);

    public void DuplicateServer(ServerItemViewModel item)
    {
        var copy = item.Record.Clone();
        copy.Id = Guid.NewGuid();
        copy.Name = item.Name + " (cópia)";
        copy.CreatedAt = DateTime.Now;
        copy.LastConnectedAt = null;
        copy.ConnectCount = 0;
        copy.Favorite = false;
        OpenEditor(null, copy);
    }

    void OpenEditor(ServerItemViewModel? item, ServerRecord? template)
    {
        var editor = item == null
            ? EditServerViewModel.ForNew(Settings.DefaultOpenMode, template)
            : EditServerViewModel.ForEdit(item.Record, Settings.DefaultOpenMode);
        editor.RevealGate = EnsureVaultUnlocked;

        if (!Dialogs.EditServer(Application.Current.MainWindow, editor)) return;

        var record = editor.ToRecord();
        ApplyBanner(record, editor);
        if (item == null)
        {
            _store.Data.Servers.Add(record);
            var vm = new ServerItemViewModel(record, this);
            Servers.Add(vm);
            if (Settings.StatusCheck) _ = _status.CheckAsync(vm);
            ShowToast($"{record.Name} adicionado");
        }
        else
        {
            var previous = item.Record;
            var idx = _store.Data.Servers.FindIndex(s => s.Id == record.Id);
            if (idx >= 0) _store.Data.Servers[idx] = record;
            else _store.Data.Servers.Add(record);
            item.Update(record);
            DropStaleCredential(previous, record);
            if (Settings.StatusCheck) _ = _status.CheckAsync(item);
            ShowToast("Alterações salvas");
        }

        if (editor.ApplyBannerToClient && record.BannerFile != null) ApplyBannerToClient(record);

        Save();
        RebuildChips();
        RefreshDerived();
    }

    /// <summary>Removes the mstsc credential saved for the old login once host, user or password changed.</summary>
    void DropStaleCredential(ServerRecord previous, ServerRecord current)
    {
        var sameLogin = string.Equals(previous.Host.Trim(), current.Host.Trim(), StringComparison.OrdinalIgnoreCase)
            && previous.Username == current.Username
            && previous.Domain == current.Domain
            && previous.PasswordProtected == current.PasswordProtected;
        if (sameLogin) return;
        if (Servers.Any(s => s.Id != current.Id && string.Equals(s.Host.Trim(), previous.Host.Trim(), StringComparison.OrdinalIgnoreCase))) return;
        CredentialStore.DeleteRdp(previous.Host);
    }

    void ApplyBanner(ServerRecord record, EditServerViewModel editor)
    {
        if (!editor.BannerChanged) return;
        try
        {
            if (editor.BannerPath == null)
            {
                Banners.Delete(record);
                record.BannerFile = null;
            }
            else
            {
                record.BannerFile = Banners.Save(record.Id, editor.BannerPath);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save banner", ex);
            Dialogs.Error(Application.Current.MainWindow, "Não foi possível salvar o banner", ex.Message);
        }
    }

    void ApplyBannerToClient(ServerRecord source)
    {
        var sourcePath = Banners.PathFor(source);
        if (sourcePath == null) return;
        var client = source.Group.Trim();
        var count = 0;
        foreach (var other in Servers.Where(s => s.Id != source.Id && string.Equals(s.Group.Trim(), client, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            try
            {
                var record = other.Record.Clone();
                record.BannerFile = Banners.Save(record.Id, sourcePath);
                record.BannerFill = source.BannerFill;
                var idx = _store.Data.Servers.FindIndex(s => s.Id == record.Id);
                if (idx >= 0) _store.Data.Servers[idx] = record;
                other.Update(record);
                count++;
            }
            catch (Exception ex)
            {
                Log.Warn($"Failed to copy banner to {other.Name}", ex);
            }
        }
        if (count > 0) ShowToast($"Banner aplicado em {count + 1} servidores de {client}");
    }

    public void DeleteServer(ServerItemViewModel item)
    {
        var open = Sessions.Where(s => ReferenceEquals(s.Server, item)).ToList();
        var message = $"\"{item.Name}\" ({item.DisplayAddress}) será removido do Ghst RDP, junto com a senha salva.";
        if (open.Count > 0) message += "\n\nA sessão aberta com esse servidor será desconectada.";
        if (!Dialogs.Confirm(Application.Current.MainWindow, "Remover servidor?", message, "Remover", danger: true)) return;

        foreach (var s in open) CloseSession(s);

        _store.Data.Servers.RemoveAll(s => s.Id == item.Id);
        Servers.Remove(item);

        var orphans = _vaultService.Data.Entries.Where(e => e.ServerId == item.Id).ToList();
        foreach (var e in orphans)
        {
            e.ServerId = null;
            e.Machine = item.Name;
        }
        if (orphans.Count > 0) SaveVault();

        // Windows credentials are keyed by host, so another server may still rely on it.
        if (!Servers.Any(s => string.Equals(s.Host, item.Host, StringComparison.OrdinalIgnoreCase)))
            CredentialStore.DeleteRdp(item.Host);
        MstscLauncher.DeleteRdpFile(item.Id);
        Thumbnails.Delete(item.Id);
        Banners.Delete(item.Record);

        Save();
        RebuildChips();
        RefreshDerived();
        ShowToast($"{item.Name} removido");
    }

    public void ToggleFavorite(ServerItemViewModel item)
    {
        var record = item.Record.Clone();
        record.Favorite = !record.Favorite;
        var idx = _store.Data.Servers.FindIndex(s => s.Id == record.Id);
        if (idx >= 0) _store.Data.Servers[idx] = record;
        item.Update(record);
        Save();
        RebuildChips();
        RefreshDerived();
    }

    DispatcherTimer? _clipboardClearTimer;

    public void CopyText(string text, string toast)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (TrySetClipboard(text, isSecret: false)) ShowToast(toast);
    }

    public void CopyPassword(ServerItemViewModel item)
    {
        if (!item.HasPassword)
        {
            ShowToast("Este servidor não tem senha salva");
            return;
        }
        if (!EnsureVaultUnlocked()) return;
        var password = Secrets.Unprotect(item.Record.PasswordProtected);
        if (string.IsNullOrEmpty(password))
        {
            ShowToast("Este servidor não tem senha salva");
            return;
        }
        CopySecret(password, "Senha copiada — some da área de transferência em 30 s");
    }

    /// <summary>Copies a secret and clears the clipboard after 30 s, if it still holds that secret.</summary>
    /// <param name="toast">Message shown once the copy succeeds.</param>
    public void CopySecret(string secret, string toast)
    {
        if (!TrySetClipboard(secret, isSecret: true)) return;
        ShowToast(toast);

        _clipboardClearTimer?.Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                if (Clipboard.ContainsText() && Clipboard.GetText() == secret) Clipboard.Clear();
            }
            catch
            {
                // clipboard locked by another process
            }
        };
        _clipboardClearTimer = timer;
        timer.Start();
    }

    static bool TrySetClipboard(string text, bool isSecret)
    {
        var data = new DataObject();
        data.SetText(text);
        if (isSecret)
        {
            // Keeps passwords out of Win+V history and cloud clipboard sync.
            data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
            data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
            data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
        }

        for (var i = 0; i < 5; i++)
        {
            try
            {
                Clipboard.SetDataObject(data, false);
                return true;
            }
            catch
            {
                Thread.Sleep(40);
            }
        }
        return false;
    }

    [ObservableProperty] private string _toastText = "";
    [ObservableProperty] private bool _isToastVisible;

    public void ShowToast(string text)
    {
        ToastText = text;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    public OpenMode DefaultOpenMode
    {
        get => Settings.DefaultOpenMode;
        set
        {
            if (value == OpenMode.Default || Settings.DefaultOpenMode == value) return;
            Settings.DefaultOpenMode = value;
            OnPropertyChanged();
            SaveQuiet();
        }
    }

    public WinKeysMode WinKeys
    {
        get => Settings.WinKeys;
        set
        {
            if (Settings.WinKeys == value) return;
            Settings.WinKeys = value;
            OnPropertyChanged();
            SaveQuiet();
        }
    }

    public bool MatchWindowsScale
    {
        get => Settings.MatchWindowsScale;
        set
        {
            Settings.MatchWindowsScale = value;
            OnPropertyChanged();
            SaveQuiet();
        }
    }

    public bool StatusCheck
    {
        get => Settings.StatusCheck;
        set
        {
            Settings.StatusCheck = value;
            _status.Enabled = value;
            if (value) _ = _status.CheckAllAsync();
            else foreach (var s in Servers) s.Status = ServerStatus.Unknown;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
            SaveQuiet();
        }
    }

    /// <summary>Hides server addresses everywhere except the editor and pixelates session thumbnails.</summary>
    public bool PrivacyMode
    {
        get => Settings.PrivacyMode;
        set
        {
            if (Settings.PrivacyMode == value) return;
            Settings.PrivacyMode = value;
            OnPropertyChanged();
            foreach (var s in Servers) s.RefreshPrivacy();
            if (value)
            {
                foreach (var session in Sessions.Where(s => !string.IsNullOrEmpty(s.Server.Address)))
                    session.StatusDetail = session.StatusDetail.Replace(session.Server.Address, session.Server.DisplayAddress);
            }
            ShowToast(value ? "Modo sigiloso ligado — IPs ocultos" : "Modo sigiloso desligado");
            SaveQuiet();
        }
    }

    [RelayCommand] void TogglePrivacy() => PrivacyMode = !PrivacyMode;

    public bool ThumbnailsEnabled
    {
        get => Settings.Thumbnails;
        set
        {
            Settings.Thumbnails = value;
            if (!value) Thumbnails.DeleteAll();
            OnPropertyChanged();
            SaveQuiet();
        }
    }

    public string DataFolder => Paths.RoamingDir;
    public string AppVersion => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    [RelayCommand]
    void OpenDataFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.RoamingDir}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Failed to open data folder", ex); }
    }

    [RelayCommand]
    void OpenLogsFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.LogsDir}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Failed to open logs folder", ex); }
    }

    [RelayCommand]
    async Task RefreshStatusAsync()
    {
        if (!Settings.StatusCheck) return;
        ShowToast("Verificando servidores…");
        await _status.CheckAllAsync();
        OnPropertyChanged(nameof(Summary));
    }

    DispatcherTimer? _statusRefresh;

    /// <summary>
    /// Called when a server's status changes. Updates the summary and, when filtering by status,
    /// refreshes the list after a short debounce.
    /// </summary>
    public void NotifyStatusChanged()
    {
        OnPropertyChanged(nameof(Summary));
        if (StatusFilter == StatusFilter.All) return;
        _statusRefresh ??= CreateStatusRefreshTimer();
        _statusRefresh.Stop();
        _statusRefresh.Start();
    }

    DispatcherTimer CreateStatusRefreshTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RefreshView();
        };
        return timer;
    }

    void Save()
    {
        try
        {
            _store.Save();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save data", ex);
            Dialogs.Error(Application.Current.MainWindow, "Não foi possível salvar", ex.Message);
        }
    }

    /// <summary>Saves without showing an error dialog; failures are only logged.</summary>
    public void SaveQuiet()
    {
        try { _store.Save(); }
        catch (Exception ex) { Log.Error("Failed to save data", ex); }
    }

    public void Dispose()
    {
        _status.Dispose();
        _clock.Stop();
        _vaultTimer.Stop();
        _vaultService.Lock();
    }
}
