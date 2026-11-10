using System.Windows;
using GhstRDP.Core;
using GhstRDP.ViewModels;

namespace GhstRDP.Views;

public partial class VaultEntryWindow : Window
{
    readonly VaultEntryEditViewModel _vm;
    bool _syncing;

    public VaultEntryWindow(VaultEntryEditViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        _syncing = true;
        PasswordInput.Password = vm.Password;
        _syncing = false;

        SourceInitialized += (_, _) => NativeWindow.ApplyDarkChrome(this);
        Loaded += (_, _) => LabelBox.Focus();
    }

    void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _vm.Password = PasswordInput.Password;
    }

    void Reveal_Click(object sender, RoutedEventArgs e)
    {
        var reveal = PasswordPlain.Visibility != Visibility.Visible;
        if (reveal)
        {
            PasswordPlain.Visibility = Visibility.Visible;
            PasswordInput.Visibility = Visibility.Collapsed;
            RevealButton.Content = "";
            PasswordPlain.Focus();
            PasswordPlain.CaretIndex = PasswordPlain.Text.Length;
        }
        else
        {
            _syncing = true;
            PasswordInput.Password = _vm.Password;
            _syncing = false;
            PasswordPlain.Visibility = Visibility.Collapsed;
            PasswordInput.Visibility = Visibility.Visible;
            RevealButton.Content = "";
            PasswordInput.Focus();
        }
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var error = _vm.Validate();
        if (error != null)
        {
            _vm.Error = error;
            return;
        }
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
