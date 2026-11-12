using System.Windows;
using GhstRDP.Core;

namespace GhstRDP.Views;

public partial class DialogWindow : Window
{
    public DialogWindow(string title, string message, string primary, string? secondary, bool danger)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primary;

        if (secondary == null)
        {
            ButtonsGrid.Children.Remove(SecondaryButton);
            ButtonsGrid.Columns = 2;
            ButtonsGrid.Children.Insert(0, new FrameworkElement());
            PrimaryButton.Margin = new Thickness(4, 0, 0, 0);
            PrimaryButton.IsCancel = true;
        }
        else
        {
            SecondaryButton.Content = secondary;
        }

        if (danger) PrimaryButton.Style = (Style)FindResource("Btn.Danger");

        SourceInitialized += (_, _) => NativeWindow.ApplyDarkChrome(this);
    }

    void Primary_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    void Secondary_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
