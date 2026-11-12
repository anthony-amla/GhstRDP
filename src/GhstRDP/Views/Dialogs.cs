using System.Windows;
using GhstRDP.ViewModels;

namespace GhstRDP.Views;

/// <summary>Themed modal dialogs. They are separate windows so they stay above the RDP sessions.</summary>
public static class Dialogs
{
    public static void Info(Window? owner, string title, string message) =>
        Show(owner, title, message, "OK", null, danger: false);

    public static void Error(Window? owner, string title, string message) =>
        Show(owner, title, message, "OK", null, danger: false);

    public static bool Confirm(Window? owner, string title, string message, string confirmText, bool danger = false) =>
        Show(owner, title, message, confirmText, "Cancelar", danger);

    static bool Show(Window? owner, string title, string message, string primary, string? secondary, bool danger)
    {
        var dialog = new DialogWindow(title, message, primary, secondary, danger);
        SetOwner(dialog, owner);
        return dialog.ShowDialog() == true;
    }

    public static bool EditServer(Window? owner, EditServerViewModel vm)
    {
        var window = new EditServerWindow(vm);
        SetOwner(window, owner);
        return window.ShowDialog() == true;
    }

    /// <summary>Asks for the vault PIN.</summary>
    /// <returns>True if the vault was unlocked.</returns>
    public static bool Pin(Window? owner, VaultViewModel vault)
    {
        var dialog = new PinDialog(vault);
        SetOwner(dialog, owner);
        return dialog.ShowDialog() == true;
    }

    public static bool EditVaultEntry(Window? owner, VaultEntryEditViewModel vm)
    {
        var window = new VaultEntryWindow(vm);
        SetOwner(window, owner);
        return window.ShowDialog() == true;
    }

    static void SetOwner(Window dialog, Window? owner)
    {
        try
        {
            if (owner is { IsLoaded: true, IsVisible: true } && !ReferenceEquals(owner, dialog))
            {
                dialog.Owner = owner;
                return;
            }
        }
        catch (InvalidOperationException)
        {
            // Owner is closing; fall back to centering on screen.
        }
        dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }
}
