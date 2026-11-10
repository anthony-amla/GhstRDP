using CommunityToolkit.Mvvm.ComponentModel;
using GhstRDP.Core;

namespace GhstRDP.ViewModels;

public sealed record MachineOption(Guid? ServerId, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Cadastro/edição de uma senha do cofre.</summary>
public partial class VaultEntryEditViewModel : ObservableObject
{
    public VaultEntryEditViewModel(VaultEntry? entry, string? plainPassword, IReadOnlyList<MachineOption> machines, Guid? serverId, string? machine)
    {
        IsNew = entry == null;
        Machines = machines;
        var targetServer = entry?.ServerId ?? serverId;
        _selectedMachine = machines.FirstOrDefault(m => m.ServerId == targetServer && targetServer != null) ?? machines[0];
        _machineName = entry?.Machine ?? machine ?? "";
        _label = entry?.Label ?? "";
        _username = entry?.Username ?? "";
        _password = plainPassword ?? "";
        _notes = entry?.Notes ?? "";
    }

    public bool IsNew { get; }
    public string Title => IsNew ? "Adicionar senha" : "Editar senha";
    public IReadOnlyList<MachineOption> Machines { get; }

    [ObservableProperty] private MachineOption _selectedMachine;
    [ObservableProperty] private string _machineName;
    [ObservableProperty] private string _label;
    [ObservableProperty] private string _username;
    [ObservableProperty] private string _password;
    [ObservableProperty] private string _notes;
    [ObservableProperty] private string _error = "";

    /// <summary>"Outra máquina" escolhida: mostra o campo para digitar o nome.</summary>
    public bool IsOtherMachine => SelectedMachine.ServerId == null;
    partial void OnSelectedMachineChanged(MachineOption value) => OnPropertyChanged(nameof(IsOtherMachine));

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Username) && string.IsNullOrEmpty(Password))
            return "Preencha o usuário ou a senha.";
        if (IsOtherMachine && string.IsNullOrWhiteSpace(MachineName))
            return "Escolha um servidor ou digite o nome da máquina.";
        return null;
    }

    public void ApplyTo(VaultEntry entry, VaultService vault)
    {
        entry.ServerId = SelectedMachine.ServerId;
        entry.Machine = IsOtherMachine ? MachineName.Trim() : SelectedMachine.Name;
        entry.Label = (Label ?? "").Trim();
        entry.Username = (Username ?? "").Trim();
        entry.Secret = string.IsNullOrEmpty(Password) ? null : vault.Protect(Password);
        entry.Notes = (Notes ?? "").Trim();
        entry.UpdatedAt = DateTime.Now;
    }
}
