using System.Windows;
using System.Windows.Input;
using GhstRDP.Core;
using GhstRDP.ViewModels;

namespace GhstRDP.Views;

public partial class PinDialog : Window
{
    readonly VaultViewModel _vault;

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
        OkButton.IsEnabled = false;
        try
        {
            if (await _vault.UnlockAsync(PinBox.Password))
            {
                DialogResult = true;
                return;
            }
            ErrorText.Text = _vault.Error;
            ErrorText.Visibility = string.IsNullOrEmpty(_vault.Error) ? Visibility.Collapsed : Visibility.Visible;
            PinBox.Clear();
            PinBox.Focus();
        }
        finally
        {
            OkButton.IsEnabled = true;
        }
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
