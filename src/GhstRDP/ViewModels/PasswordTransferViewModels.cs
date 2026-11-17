using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GhstRDP.Core;

namespace GhstRDP.ViewModels;

public enum ExportFormat { Code, Markdown }

public partial class TransferItemViewModel : ObservableObject
{
    public TransferItemViewModel(string label, string username)
    {
        Label = label;
        Username = username;
    }

    public string Label { get; }
    public string Username { get; }
    public bool HasUsername => !string.IsNullOrWhiteSpace(Username);
    public bool IsServerLogin { get; init; }

    /// <summary>Builds the exported login with its password decrypted (export only).</summary>
    public Func<TransferServer>? BuildServer { get; init; }

    /// <summary>Builds the exported entry with its password decrypted (export only).</summary>
    public Func<TransferEntry>? BuildEntry { get; init; }

    /// <summary>Imported login (import only).</summary>
    public TransferServer? ImportedServer { get; init; }

    /// <summary>Imported entry (import only).</summary>
    public TransferEntry? ImportedEntry { get; init; }

    /// <summary>True when something with the same login already exists locally (import only).</summary>
    public bool Exists { get; init; }

    internal TransferGroupViewModel? Group { get; set; }

    [ObservableProperty] private bool _isChecked = true;
    [ObservableProperty] private string _status = "";

    partial void OnIsCheckedChanged(bool value) => Group?.NotifyItemChanged();
}

public partial class TransferGroupViewModel : ObservableObject
{
    public TransferGroupViewModel(string name, string client, Brush? tagBackground, Brush? tagForeground, IReadOnlyList<TransferItemViewModel> items)
    {
        Name = name;
        Client = client;
        TagBackground = tagBackground;
        TagForeground = tagForeground;
        Items = items;
        foreach (var item in items) item.Group = this;
    }

    public string Name { get; }
    public string Client { get; }
    public bool HasClient => !string.IsNullOrWhiteSpace(Client);
    public Brush? TagBackground { get; }
    public Brush? TagForeground { get; }
    public IReadOnlyList<TransferItemViewModel> Items { get; }

    /// <summary>True when every item is checked, false when none is, null when mixed.</summary>
    public bool? IsChecked
    {
        get => Items.All(i => i.IsChecked) ? true : Items.Any(i => i.IsChecked) ? null : false;
        set
        {
            var check = value != false;
            foreach (var item in Items) item.IsChecked = check;
        }
    }

    internal void NotifyItemChanged() => OnPropertyChanged(nameof(IsChecked));
}

/// <summary>State of the export dialog: what to include and in which format.</summary>
public partial class ExportPasswordsViewModel : ObservableObject
{
    public ExportPasswordsViewModel(IReadOnlyList<TransferGroupViewModel> groups)
    {
        Groups = groups;
        foreach (var item in groups.SelectMany(g => g.Items))
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(TransferItemViewModel.IsChecked)) return;
                Notice = "";
                OnPropertyChanged(nameof(SelectedCount));
            };
    }

    public IReadOnlyList<TransferGroupViewModel> Groups { get; }
    public bool HasGroups => Groups.Count > 0;
    public int SelectedCount => Groups.Sum(g => g.Items.Count(i => i.IsChecked));

    [ObservableProperty] private ExportFormat _format = ExportFormat.Code;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _notice = "";

    public string CodePassword { get; set; } = "";
    public string CodePasswordConfirm { get; set; } = "";

    public bool IsCode => Format == ExportFormat.Code;

    partial void OnFormatChanged(ExportFormat value)
    {
        Error = "";
        Notice = "";
        OnPropertyChanged(nameof(IsCode));
    }

    public void SelectAll(bool check)
    {
        foreach (var group in Groups) group.IsChecked = check;
    }

    /// <returns>An error message for the user, or null when the export can run.</returns>
    public string? Validate()
    {
        if (SelectedCount == 0) return "Marque pelo menos um item para exportar.";
        if (!IsCode) return null;
        if (CodePassword.Length < PasswordTransfer.MinCodePasswordLength)
            return $"A senha do código precisa ter pelo menos {PasswordTransfer.MinCodePasswordLength} caracteres.";
        if (CodePassword != CodePasswordConfirm) return "As duas senhas do código não são iguais.";
        return null;
    }

    /// <returns>The selected machines with passwords decrypted.</returns>
    public TransferPackage BuildPackage()
    {
        var package = new TransferPackage();
        foreach (var group in Groups)
        {
            var selected = group.Items.Where(i => i.IsChecked).ToList();
            if (selected.Count == 0) continue;
            package.Machines.Add(new TransferMachine
            {
                Name = group.Name,
                Client = group.Client,
                Server = selected.FirstOrDefault(i => i.IsServerLogin)?.BuildServer?.Invoke(),
                Entries = selected.Where(i => !i.IsServerLogin && i.BuildEntry != null).Select(i => i.BuildEntry!()).ToList(),
            });
        }
        return package;
    }

    /// <summary>Produces the export text in the chosen format. The code variant is slow, call it off the UI thread.</summary>
    public string Render(TransferPackage package) =>
        IsCode ? PasswordTransfer.ToCode(package, CodePassword) : PasswordTransfer.ToMarkdown(package);
}

/// <summary>State of the import dialog: read a code, preview it, then pick what to bring in.</summary>
public partial class ImportPasswordsViewModel : ObservableObject
{
    readonly MainViewModel _main;

    public ImportPasswordsViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<TransferGroupViewModel> Groups { get; } = new();

    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isPreview;
    [ObservableProperty] private bool _replaceExisting;
    [ObservableProperty] private string _summary = "";

    public string CodePassword { get; set; } = "";

    partial void OnReplaceExistingChanged(bool value) => RefreshStatuses();

    /// <summary>Decrypts <see cref="Code"/> and fills <see cref="Groups"/> for the preview.</summary>
    public async Task<bool> ReadAsync()
    {
        Error = "";
        if (string.IsNullOrWhiteSpace(Code))
        {
            Error = "Cole o código ou abra um arquivo .ghstrdp.";
            return false;
        }
        if (string.IsNullOrEmpty(CodePassword))
        {
            Error = "Digite a senha do código.";
            return false;
        }

        IsBusy = true;
        try
        {
            var code = Code;
            var password = CodePassword;
            var package = await Task.Run(() => PasswordTransfer.FromCode(code, password));
            if (package.Machines.Count == 0)
            {
                Error = "O código não tem nenhuma senha.";
                return false;
            }
            BuildPreview(package);
            IsPreview = true;
            return true;
        }
        catch (FormatException ex)
        {
            Error = ex.Message;
            return false;
        }
        catch (CryptographicException)
        {
            Error = "Senha do código incorreta, ou o código foi alterado.";
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("Failed to read import code", ex);
            Error = "Não foi possível ler esse código.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Back()
    {
        IsPreview = false;
        Groups.Clear();
        Error = "";
    }

    void BuildPreview(TransferPackage package)
    {
        Groups.Clear();
        foreach (var machine in package.Machines)
        {
            var target = _main.FindImportTarget(machine);
            var items = new List<TransferItemViewModel>();

            if (machine.Server is { } server)
            {
                items.Add(new TransferItemViewModel($"Servidor {server.Address}", server.Username)
                {
                    IsServerLogin = true,
                    ImportedServer = server,
                    Exists = target != null,
                });
            }
            foreach (var entry in machine.Entries)
            {
                items.Add(new TransferItemViewModel(string.IsNullOrWhiteSpace(entry.Label) ? "Senha" : entry.Label, entry.Username)
                {
                    ImportedEntry = entry,
                    Exists = _main.FindVaultEntry(target?.Id, machine.Name, entry) != null,
                });
            }
            if (items.Count == 0) continue;

            var tag = string.IsNullOrWhiteSpace(machine.Client) ? -1 : _main.PeekClientColorIndex(machine.Client);
            Groups.Add(new TransferGroupViewModel(
                target?.Name ?? machine.Name, machine.Client,
                tag < 0 ? null : TagPalette.Background(tag), tag < 0 ? null : TagPalette.Foreground(tag), items));
        }
        RefreshStatuses();
    }

    void RefreshStatuses()
    {
        foreach (var item in Groups.SelectMany(g => g.Items))
        {
            item.Status = (item.IsServerLogin, item.Exists, ReplaceExisting) switch
            {
                (true, false, _) => "Servidor novo",
                (false, false, _) => "Senha nova",
                (_, true, true) => "Já existe — será substituído",
                _ => "Já existe — fica o seu",
            };
        }
        var total = Groups.Sum(g => g.Items.Count);
        var existing = Groups.Sum(g => g.Items.Count(i => i.Exists));
        Summary = existing == 0
            ? $"{total} item(ns) no código."
            : $"{total} item(ns) no código, {existing} já existe(m) aqui.";
    }

    /// <returns>The checked machines, keeping only checked logins and entries.</returns>
    public IReadOnlyList<TransferMachine> SelectedMachines() =>
        Groups.Select(g => new TransferMachine
            {
                Name = g.Name,
                Client = g.Client,
                Server = g.Items.FirstOrDefault(i => i.IsServerLogin && i.IsChecked)?.ImportedServer,
                Entries = g.Items.Where(i => !i.IsServerLogin && i.IsChecked && i.ImportedEntry != null).Select(i => i.ImportedEntry!).ToList(),
            })
            .Where(m => m.Server != null || m.Entries.Count > 0)
            .ToList();
}
