using System.IO;
using System.Text;
using System.Windows;
using GhstRDP.Core;
using GhstRDP.ViewModels;
using Microsoft.Win32;

namespace GhstRDP.Views;

public partial class ExportPasswordsWindow : Window
{
    readonly ExportPasswordsViewModel _vm;
    readonly MainViewModel _main;

    public ExportPasswordsWindow(ExportPasswordsViewModel vm, MainViewModel main)
    {
        InitializeComponent();
        _vm = vm;
        _main = main;
        DataContext = vm;
        SourceInitialized += (_, _) => NativeWindow.ApplyDarkChrome(this);
    }

    void CodePassword_Changed(object sender, RoutedEventArgs e)
    {
        _vm.CodePassword = CodePasswordBox.Password;
        _vm.CodePasswordConfirm = CodePasswordConfirmBox.Password;
        _vm.Error = "";
    }

    void SelectAll_Click(object sender, RoutedEventArgs e) => _vm.SelectAll(true);

    void SelectNone_Click(object sender, RoutedEventArgs e) => _vm.SelectAll(false);

    async void Copy_Click(object sender, RoutedEventArgs e)
    {
        var text = await RenderAsync();
        if (text == null) return;

        if (_vm.IsCode)
        {
            _main.CopyText(text, "Código copiado");
            _vm.Notice = "Código copiado. Mande a senha por outro canal.";
        }
        else
        {
            _main.CopySecret(text, "Texto copiado — some da área de transferência em 30 s");
            _vm.Notice = "Texto copiado. Some da área de transferência em 30 s.";
        }
    }

    async void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Salvar exportação",
            FileName = $"ghst-rdp-senhas-{DateTime.Now:yyyyMMdd}{(_vm.IsCode ? ".ghstrdp" : ".md")}",
            Filter = _vm.IsCode ? PasswordTransfer.CodeFileFilter : PasswordTransfer.MarkdownFileFilter,
        };
        if (_vm.Validate() is { } error)
        {
            _vm.Error = error;
            return;
        }
        if (dialog.ShowDialog(this) != true) return;

        var text = await RenderAsync();
        if (text == null) return;
        try
        {
            File.WriteAllText(dialog.FileName, text, new UTF8Encoding(false));
            _vm.Notice = $"Salvo em {Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save export file", ex);
            _vm.Error = $"Não foi possível salvar: {ex.Message}";
        }
    }

    async Task<string?> RenderAsync()
    {
        _vm.Error = "";
        _vm.Notice = "";
        if (_vm.Validate() is { } error)
        {
            _vm.Error = error;
            return null;
        }

        _vm.IsBusy = true;
        try
        {
            var package = _vm.BuildPackage();
            return await Task.Run(() => _vm.Render(package));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to export passwords", ex);
            _vm.Error = "Não foi possível gerar a exportação.";
            return null;
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
