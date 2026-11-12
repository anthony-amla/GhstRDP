using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using GhstRDP.Core;
using GhstRDP.ViewModels;

namespace GhstRDP.Views;

public partial class PinDialog : Window
{
    readonly VaultViewModel _vault;
    bool _checking;

    public PinDialog(VaultViewModel vault)
    {
        InitializeComponent();
        _vault = vault;
        SourceInitialized += (_, _) => NativeWindow.ApplyDarkChrome(this);
        Loaded += (_, _) => PinBox.Focus();
    }

    async void Ok_Click(object sender, RoutedEventArgs e) => await TryUnlock();

    async void PinBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await TryUnlock();
    }

    async Task TryUnlock()
    {
        if (_checking) return;
        _checking = true;
        OkButton.IsEnabled = false;
        try
        {
            if (await _vault.UnlockAsync(PinBox.Password))
            {
                _checking = false;
                DialogResult = true;
                return;
            }
            ShowError(_vault.Error);
        }
        catch (Exception ex)
        {
            Log.Error("PIN check failed", ex);
            ShowError("Não foi possível abrir o cofre. O arquivo pode estar danificado.");
        }
        finally
        {
            _checking = false;
            OkButton.IsEnabled = true;
        }
    }

    void ShowError(string? message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        PinBox.Clear();
        PinBox.Focus();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_checking) e.Cancel = true;
        base.OnClosing(e);
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!_checking) DialogResult = false;
    }
}
