using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GhstRDP.Core;
using GhstRDP.Views;

namespace GhstRDP.ViewModels;

public enum Page { Servidores, Sessoes, Senhas, Configuracoes }

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
        // Fecha o cofre sozinho depois de 5 min sem uso
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

        // Atualiza textos relativos ("Há 5 min") e o resumo
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

        CurrentPage = Page.Servidores;
    }

    public AppSettings Settings => _store.Data.Settings;

    // ================= Coleções =================
    public ObservableCollection<ServerItemViewModel> Servers { get; } = new();
    public ListCollectionView ServersView { get; }
    public ObservableCollection<ServerItemViewModel> Favorites { get; } = new();
    public ObservableCollection<ServerItemViewModel> Recents { get; } = new();
    public ObservableCollection<SessionViewModel> Sessions { get; } = new();

    // ================= Navegação =================
    [ObservableProperty] private Page _currentPage;
    [ObservableProperty] private bool _isNavExpanded;
    [ObservableProperty] private bool _isListView;

    partial void OnCurrentPageChanged(Page value)
    {
        if (value == Page.Sessoes) SelectedSession?.RequestFocus();
        if (value == Page.Senhas) Vault.Refresh();
    }

    // ================= Atualização =================
    public UpdateViewModel Update { get; }

    // ================= Cofre de senhas =================
    public VaultViewModel Vault { get; }

    /// <summary>
    /// Se o cofre tem PIN, pede o PIN antes de mostrar/copiar senhas (inclusive as dos servidores).
    /// Sem PIN configurado, libera como antes.
    /// </summary>
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
            Log.Error("Falha ao salvar o cofre", ex);
            Dialogs.Error(Application.Current.MainWindow, "Não foi possível salvar o cofre", ex.Message);
        }
    }

    /// <summary>Adicionar (entry null) ou editar uma senha do cofre.</summary>
    public void EditVaultEntry(VaultEntry? entry, Guid? serverId, string? machine = null)
    {
        if (!EnsureVaultUnlocked()) return;
        if (!_vaultService.IsConfigured)
        {
            CurrentPage = Page.Senhas;
            ShowToast("Crie um PIN para usar o cofre");
            return;
        }

        var machines = new List<MachineOption> { new(null, "Outra máquina (digitar nome)") };
        machines.AddRange(Servers.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Select(s => new MachineOption(s.Id, s.Name)));

        var plain = entry == null ? null : _vaultService.Unprotect(entry.Secret);
        var editor = new VaultEntryEditViewModel(entry, plain, machines, serverId, machine);
        if (!Dialogs.EditVaultEntry(Application.Current.MainWindow, editor)) return;

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
            Log.Error("Falha ao guardar senha no cofre", ex);
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

    // ================= Busca e filtros =================
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private TypeFilter _typeFilter;
    [ObservableProperty] private StatusFilter _statusFilter;
    [ObservableProperty] private bool _favoritesOnly;
    [ObservableProperty] private string? _clientFilter;
    [ObservableProperty] private int _filteredCount;

    public ObservableCollection<ClientFilter> Clients { get; } = new();
    public bool HasClients => Clients.Count > 0;
    public bool HasVps => Servers.Any(s => s.Kind == ServerKind.Vps);
    public bool HasDedicado => Servers.Any(s => s.Kind == ServerKind.Dedicado);
    public bool HasOutro => Servers.Any(s => s.Kind == ServerKind.Outro);

    public bool HasActiveFilter => !string.IsNullOrWhiteSpace(Search) || TypeFilter != TypeFilter.Todos
                                   || StatusFilter != StatusFilter.Todos || FavoritesOnly || ClientFilter != null;

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
        TypeFilter = TypeFilter.Todos;
        StatusFilter = StatusFilter.Todos;
        FavoritesOnly = false;
        ClientFilter = null;
    }

    bool FilterServer(object obj)
    {
        if (obj is not ServerItemViewModel s) return false;

        if (TypeFilter != TypeFilter.Todos)
        {
            var kind = TypeFilter switch { TypeFilter.Vps => ServerKind.Vps, TypeFilter.Dedicado => ServerKind.Dedicado, _ => ServerKind.Outro };
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

    /// <summary>Remonta os chips de cliente (com contagem e cor) e os tipos disponíveis.</summary>
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
        OnPropertyChanged(nameof(HasDedicado));
        OnPropertyChanged(nameof(HasOutro));
    }

    /// <summary>
    /// Cor do cliente na paleta. Na primeira vez escolhe a cor menos usada (clientes diferentes
    /// ficam com cores diferentes) e salva, para nunca mudar depois.
    /// </summary>
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

    /// <summary>Recalcula favoritos, recentes, chips e contagens.</summary>
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
    /// <summary>Início sem favoritos nem recentes: mostra todos os servidores.</summary>
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

    // ================= Sessões =================
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
        var mode = forced ?? (item.Record.OpenMode == OpenMode.Padrao ? Settings.DefaultOpenMode : item.Record.OpenMode);
        if (mode == OpenMode.Externo) OpenExternal(item);
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
            Log.Error("Falha ao abrir mstsc", ex);
            Dialogs.Error(Application.Current.MainWindow, "Não foi possível abrir o mstsc", ex.Message);
        }
    }

    void OpenEmbedded(ServerItemViewModel item)
    {
        var existing = Sessions.FirstOrDefault(s => ReferenceEquals(s.Server, item));
        if (existing != null)
        {
            SelectedSession = existing;
            CurrentPage = Page.Sessoes;
            if (existing.CanReconnect) existing.ReconnectCommand.Execute(null);
            return;
        }

        var session = new SessionViewModel(item, this);
        Sessions.Add(session);
        item.HasOpenSession = true;
        CurrentPage = Page.Sessoes;
        SelectedSession = session;
    }

    public void CloseSession(SessionViewModel session) => session.RequestClose();

    /// <summary>Chamado pela view quando a sessão terminou de desconectar.</summary>
    internal void OnSessionClosed(SessionViewModel session)
    {
        var index = Sessions.IndexOf(session);
        if (index < 0) return;
        var wasSelected = ReferenceEquals(SelectedSession, session);
        Sessions.RemoveAt(index);
        session.Server.HasOpenSession = Sessions.Any(s => ReferenceEquals(s.Server, session.Server));
        if (wasSelected)
            SelectedSession = Sessions.Count == 0 ? null : Sessions[Math.Min(index, Sessions.Count - 1)];
        if (Sessions.Count == 0 && CurrentPage == Page.Sessoes) CurrentPage = Page.Servidores;
        SessionsChanged?.Invoke();
    }

    /// <summary>Disparado quando a lista de sessões muda por fechamento (usado no fechamento do app).</summary>
    public event Action? SessionsChanged;

    public void MarkConnected(ServerItemViewModel item)
    {
        item.Record.LastConnectedAt = DateTime.Now;
        item.Record.ConnectCount++;
        item.RefreshTexts();
        SaveQuiet();
        RefreshDerived();
    }

    // ================= CRUD =================
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
        var groups = GroupNames();
        var editor = item == null
            ? EditServerViewModel.ForNew(groups, Settings.DefaultOpenMode, template)
            : EditServerViewModel.ForEdit(item.Record, groups, Settings.DefaultOpenMode);
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
            var idx = _store.Data.Servers.FindIndex(s => s.Id == record.Id);
            if (idx >= 0) _store.Data.Servers[idx] = record;
            else _store.Data.Servers.Add(record);
            item.Update(record);
            if (Settings.StatusCheck) _ = _status.CheckAsync(item);
            ShowToast("Alterações salvas");
        }

        if (editor.ApplyBannerToClient && record.BannerFile != null) ApplyBannerToClient(record);

        Save();
        RebuildChips();
        RefreshDerived();
    }

    /// <summary>Copia (ou remove) o banner escolhido no cadastro para a pasta de banners.</summary>
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
            Log.Error("Falha ao salvar o banner", ex);
            Dialogs.Error(Application.Current.MainWindow, "Não foi possível salvar o banner", ex.Message);
        }
    }

    /// <summary>Usa o mesmo banner em todos os servidores do mesmo cliente.</summary>
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
                Log.Warn($"Falha ao copiar banner para {other.Name}", ex);
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

        // Senhas do cofre ligadas a esse servidor ficam guardadas com o nome dele
        var orphans = _vaultService.Data.Entries.Where(e => e.ServerId == item.Id).ToList();
        foreach (var e in orphans)
        {
            e.ServerId = null;
            e.Machine = item.Name;
        }
        if (orphans.Count > 0) SaveVault();

        // Só remove a credencial do Windows se nenhum outro servidor usa o mesmo host
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

    // ================= Clipboard =================
    DispatcherTimer? _clipboardClear;

    public void CopyText(string text, string toast)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (TrySetClipboard(text)) ShowToast(toast);
    }

    public void CopyPassword(ServerItemViewModel item)
    {
        if (!item.HasPassword)
        {
            ShowToast("Este servidor não tem senha salva");
            return;
        }
        if (!EnsureVaultUnlocked()) return;
        var pwd = Secrets.Unprotect(item.Record.PasswordProtected);
        if (string.IsNullOrEmpty(pwd))
        {
            ShowToast("Este servidor não tem senha salva");
            return;
        }
        CopySecret(pwd, "Senha copiada — some da área de transferência em 30 s");
    }

    /// <summary>Copia um segredo e limpa a área de transferência depois de 30 s (se ainda for ele).</summary>
    public void CopySecret(string pwd, string toast)
    {
        if (!TrySetClipboard(pwd)) return;
        ShowToast(toast);

        _clipboardClear?.Stop();
        _clipboardClear = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clipboardClear.Tick += (_, _) =>
        {
            _clipboardClear?.Stop();
            try
            {
                if (Clipboard.ContainsText() && Clipboard.GetText() == pwd) Clipboard.Clear();
            }
            catch
            {
                // clipboard ocupado por outro app
            }
        };
        _clipboardClear.Start();
    }

    static bool TrySetClipboard(string text)
    {
        for (var i = 0; i < 5; i++)
        {
            try
            {
                Clipboard.SetDataObject(text, false);
                return true;
            }
            catch
            {
                Thread.Sleep(40);
            }
        }
        return false;
    }

    // ================= Toast =================
    [ObservableProperty] private string _toastText = "";
    [ObservableProperty] private bool _isToastVisible;

    public void ShowToast(string text)
    {
        ToastText = text;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ================= Configurações =================
    public OpenMode DefaultOpenMode
    {
        get => Settings.DefaultOpenMode;
        set
        {
            if (value == OpenMode.Padrao || Settings.DefaultOpenMode == value) return;
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
            else foreach (var s in Servers) s.Status = ServerStatus.Desconhecido;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
            SaveQuiet();
        }
    }

    /// <summary>Modo sigiloso: IPs ocultos na tela (menos na edição) e prévias pixelizadas.</summary>
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
                // Mensagens de sessão já na tela também perdem o IP
                foreach (var session in Sessions)
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
        catch (Exception ex) { Log.Warn("Falha ao abrir pasta", ex); }
    }

    [RelayCommand]
    void OpenLogsFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.LogsDir}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Falha ao abrir pasta", ex); }
    }

    [RelayCommand]
    async Task RefreshStatusAsync()
    {
        if (!Settings.StatusCheck) return;
        ShowToast("Verificando servidores…");
        await _status.CheckAllAsync();
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Chamado pelo StatusMonitor via item.SetStatus → atualiza o resumo de vez em quando.</summary>
    DispatcherTimer? _statusRefresh;

    public void NotifyStatusChanged()
    {
        OnPropertyChanged(nameof(Summary));
        if (StatusFilter == StatusFilter.Todos) return;
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

    // ================= Persistência =================
    void Save()
    {
        try
        {
            _store.Save();
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao salvar dados", ex);
            Dialogs.Error(Application.Current.MainWindow, "Não foi possível salvar", ex.Message);
        }
    }

    public void SaveQuiet()
    {
        try { _store.Save(); }
        catch (Exception ex) { Log.Error("Falha ao salvar dados", ex); }
    }

    public void Dispose()
    {
        _status.Dispose();
        _clock.Stop();
        _vaultTimer.Stop();
        _vaultService.Lock();
    }
}
