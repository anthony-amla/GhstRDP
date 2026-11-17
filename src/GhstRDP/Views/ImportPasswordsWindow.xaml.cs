using System.IO;
using System.Windows;
using System.Windows.Input;
using GhstRDP.Core;
using GhstRDP.ViewModels;
using Microsoft.Win32;

namespace GhstRDP.Views;

public partial class ImportPasswordsWindow : Window
{
    const long MaxFileBytes = 2 * 1024 * 1024;

    readonly ImportPasswordsViewModel _vm;

    public ImportPasswordsWindow(ImportPasswordsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        SourceInitialized += (_, _) => NativeWindow.ApplyDarkChrome(this);
        Loaded += (_, _) => CodeBox.Focus();
    }

    void CodePassword_Changed(object sender, RoutedEventArgs e) => _vm.CodePassword = CodePasswordBox.Password;

    async void CodePassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await _vm.ReadAsync();
    }

    void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Abrir exportação", Filter = PasswordTransfer.CodeFileFilter };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > MaxFileBytes)
            {
                _vm.Error = "Esse arquivo é grande demais para ser uma exportação do Ghst RDP.";
                return;
            }
            _vm.Code = File.ReadAllText(dialog.FileName).Trim();
            _vm.Error = "";
            CodePasswordBox.Focus();
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to open import file", ex);
            _vm.Error = $"Não foi possível abrir o arquivo: {ex.Message}";
        }
    }

    async void Read_Click(object sender, RoutedEventArgs e) => await _vm.ReadAsync();

    void Back_Click(object sender, RoutedEventArgs e) => _vm.Back();

    void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedMachines().Count == 0)
        {
            _vm.Error = "Marque pelo menos um item para importar.";
            return;
        }
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsBusy) DialogResult = false;
    }
}
