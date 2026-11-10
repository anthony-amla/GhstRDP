using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GhstRDP.Core;
using GhstRDP.Views;

namespace GhstRDP.ViewModels;

public enum VaultState { Configurar, Bloqueado, Aberto }

/// <summary>Aba "Senhas": cofre protegido por PIN, com as senhas agrupadas por máquina.</summary>
public partial class VaultViewModel : ObservableObject
{
    readonly MainViewModel _main;
    readonly VaultService _vault;

    public VaultViewModel(MainViewModel main, VaultService vault)
    {
        _main = main;
        _vault = vault;
        _vault.StateChanged += () => Application.Current?.Dispatcher.BeginInvoke(OnVaultStateChanged);
    }

    public VaultService Service => _vault;

    public VaultState State => !_vault.IsConfigured ? VaultState.Configurar : _vault.IsUnlocked ? VaultState.Aberto : VaultState.Bloqueado;

    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _search = "";

    public ObservableCollection<VaultGroupViewModel> Groups { get; } = new();
    public bool HasGroups => Groups.Count > 0;

    partial void OnSearchChanged(string value) => Refresh();

    void OnVaultStateChanged()
    {
        OnPropertyChanged(nameof(State));
        if (_vault.IsUnlocked) Refresh();
        else
        {
            Groups.Clear();
            OnPropertyChanged(nameof(HasGroups));
        }
    }

    // ---------------- PIN ----------------

    public async Task<bool> SetupAsync(string pin, string confirm)
    {
        Error = "";
        if (!VaultService.IsValidPin(pin))
        {
            Error = "Use de 4 a 12 números.";
            return false;
        }
        if (pin != confirm)
        {
            Error = "Os dois PINs não são iguais.";
            return false;
        }
        IsBusy = true;
        try
        {
            await Task.Run(() => _vault.SetupPin(pin));
            _main.ShowToast("PIN criado — cofre aberto");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao criar PIN", ex);
            Error = ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> UnlockAsync(string pin)
    {
        Error = "";
        if (string.IsNullOrEmpty(pin)) return false;
        if (_vault.LockoutSecondsLeft > 0)
        {
            Error = $"Muitas tentativas. Tente de novo em {_vault.LockoutSecondsLeft} s.";
            return false;
        }
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _vault.Unlock(pin));
            switch (result)
            {
                case UnlockResult.Ok:
                    return true;
                case UnlockResult.Bloqueado:
                    Error = $"Muitas tentativas. Tente de novo em {_vault.LockoutSecondsLeft} s.";
                    return false;
                default:
                    Error = "PIN incorreto.";
                    return false;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand] void Lock() => _vault.Lock();

    [RelayCommand]
    void ForgotPin()
    {
        var count = _vault.Data.Entries.Count;
        var message = "Sem o PIN não dá para abrir as senhas guardadas no cofre. Para criar um PIN novo, o cofre precisa ser apagado"
                      + (count > 0 ? $" — {count} senha(s) serão perdidas." : ".")
                      + "\n\nOs logins salvos nos servidores (usuário e senha do RDP) continuam funcionando.";
        if (!Dialogs.Confirm(Application.Current.MainWindow, "Apagar o cofre?", message, "Apagar cofre", danger: true)) return;
        _vault.Reset();
        Error = "";
        OnPropertyChanged(nameof(State));
        _main.ShowToast("Cofre apagado — crie um PIN novo");
    }

    [RelayCommand] void Add() => _main.EditVaultEntry(null, null);

    // ---------------- Lista ----------------

    /// <summary>Monta os grupos: um por servidor (login do RDP + senhas do cofre) e um por máquina digitada.</summary>
    public void Refresh()
    {
        Groups.Clear();
        if (!_vault.IsUnlocked)
        {
            OnPropertyChanged(nameof(HasGroups));
            return;
        }

        var q = Search?.Trim() ?? "";
        bool Match(params string?[] values) =>
            q.Length == 0 || values.Any(v => !string.IsNullOrEmpty(v) && v.Contains(q, StringComparison.OrdinalIgnoreCase));

        var entries = _vault.Data.Entries;
        var serverIds = new HashSet<Guid>();

        foreach (var server in _main.Servers.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            serverIds.Add(server.Id);
            var groupMatches = Match(server.Name, server.Group);
            var items = new List<VaultItemViewModel>();

            if (server.HasUser || server.HasPassword)
            {
                var item = VaultItemViewModel.ForServerLogin(server, this, _main);
                if (groupMatches || Match(item.Label, item.Username)) items.Add(item);
            }
            foreach (var entry in entries.Where(e => e.ServerId == server.Id).OrderBy(e => e.Label, StringComparer.OrdinalIgnoreCase))
            {
                var item = VaultItemViewModel.ForEntry(entry, this, _main);
                if (groupMatches || Match(entry.Label, entry.Username, entry.Notes)) items.Add(item);
            }

            if (items.Count > 0) Groups.Add(VaultGroupViewModel.ForServer(server, items, _main));
        }

        // Senhas de máquinas que não estão cadastradas como servidor (ou de servidor removido)
        var loose = entries.Where(e => e.ServerId == null || !serverIds.Contains(e.ServerId.Value))
            .GroupBy(e => string.IsNullOrWhiteSpace(e.Machine) ? "Sem máquina" : e.Machine.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var group in loose)
        {
            var items = group
                .Where(e => Match(group.Key, e.Label, e.Username, e.Notes))
                .OrderBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
                .Select(e => VaultItemViewModel.ForEntry(e, this, _main))
                .ToList();
            if (items.Count > 0) Groups.Add(VaultGroupViewModel.ForMachine(group.Key, items, _main));
        }

        OnPropertyChanged(nameof(HasGroups));
    }

    internal string? RevealEntry(VaultEntry entry) => _vault.IsUnlocked ? _vault.Unprotect(entry.Secret) : null;

    internal void DeleteEntry(VaultEntry entry)
    {
        var name = string.IsNullOrWhiteSpace(entry.Label) ? entry.Username : entry.Label;
        if (!Dialogs.Confirm(Application.Current.MainWindow, "Remover senha?", $"\"{name}\" será apagada do cofre.", "Remover", danger: true)) return;
        _vault.Data.Entries.Remove(entry);
        _main.SaveVault();
        Refresh();
        _main.ShowToast("Senha removida");
    }
}

public sealed class VaultGroupViewModel
{
    VaultGroupViewModel(string title, IReadOnlyList<VaultItemViewModel> items)
    {
        Title = title;
        Items = items;
    }

    public string Title { get; }
    public string Client { get; private init; } = "";
    public bool HasClient => Client.Length > 0;
    public Brush? TagBackground { get; private init; }
    public Brush? TagForeground { get; private init; }
    public bool IsServer { get; private init; }
    public Guid? ServerId { get; private init; }
    public IReadOnlyList<VaultItemViewModel> Items { get; }
    public IRelayCommand? AddCommand { get; private init; }

    public static VaultGroupViewModel ForServer(ServerItemViewModel server, IReadOnlyList<VaultItemViewModel> items, MainViewModel main) =>
        new(server.Name, items)
        {
            Client = server.Group.Trim(),
            TagBackground = server.TagBackground,
            TagForeground = server.TagForeground,
            IsServer = true,
            ServerId = server.Id,
            AddCommand = new RelayCommand(() => main.EditVaultEntry(null, server.Id)),
        };

    public static VaultGroupViewModel ForMachine(string machine, IReadOnlyList<VaultItemViewModel> items, MainViewModel main) =>
        new(machine, items)
        {
            AddCommand = new RelayCommand(() => main.EditVaultEntry(null, null, machine)),
        };
}

public partial class VaultItemViewModel : ObservableObject
{
    readonly Func<string?> _getPassword;
    readonly MainViewModel _main;

    VaultItemViewModel(string label, string username, Func<string?> getPassword, MainViewModel main)
    {
        Label = label;
        Username = username;
        _getPassword = getPassword;
        _main = main;
    }

    public string Label { get; }
    public string Username { get; }
    public bool HasUsername => !string.IsNullOrWhiteSpace(Username);
    public string Notes { get; private init; } = "";
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public bool IsServerLogin { get; private init; }
    public bool CanDelete => !IsServerLogin;

    [ObservableProperty] private bool _isRevealed;
    [ObservableProperty] private string _passwordText = "••••••••";

    public IRelayCommand EditCommand { get; private init; } = null!;
    public IRelayCommand DeleteCommand { get; private init; } = null!;

    public static VaultItemViewModel ForServerLogin(ServerItemViewModel server, VaultViewModel vault, MainViewModel main) =>
        new("Login do RDP", server.UserDisplay, () => Secrets.Unprotect(server.Record.PasswordProtected), main)
        {
            IsServerLogin = true,
            EditCommand = new RelayCommand(() => main.EditServer(server)),
            DeleteCommand = new RelayCommand(() => { }),
        };

    public static VaultItemViewModel ForEntry(VaultEntry entry, VaultViewModel vault, MainViewModel main) =>
        new(string.IsNullOrWhiteSpace(entry.Label) ? "Senha" : entry.Label, entry.Username, () => vault.RevealEntry(entry), main)
        {
            Notes = entry.Notes,
            EditCommand = new RelayCommand(() => main.EditVaultEntry(entry, entry.ServerId)),
            DeleteCommand = new RelayCommand(() => vault.DeleteEntry(entry)),
        };

    [RelayCommand]
    void ToggleReveal()
    {
        if (IsRevealed)
        {
            IsRevealed = false;
            PasswordText = "••••••••";
            return;
        }
        var pwd = _getPassword();
        PasswordText = string.IsNullOrEmpty(pwd) ? "(sem senha)" : pwd;
        IsRevealed = true;
    }

    [RelayCommand] void CopyUser() => _main.CopyText(Username, "Usuário copiado");

    [RelayCommand]
    void CopyPassword()
    {
        var pwd = _getPassword();
        if (string.IsNullOrEmpty(pwd)) _main.ShowToast("Sem senha salva");
        else _main.CopySecret(pwd, "Senha copiada — some da área de transferência em 30 s");
    }
}
