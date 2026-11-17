using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using GhstRDP.ViewModels;

namespace GhstRDP.Views;

public partial class VaultView : UserControl
{
    public VaultView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged old) old.PropertyChanged -= OnVmChanged;
            if (e.NewValue is INotifyPropertyChanged now) now.PropertyChanged += OnVmChanged;
        };
        IsVisibleChanged += (_, _) => FocusPinBox();
    }

    VaultViewModel? Vm => DataContext as VaultViewModel;

    void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VaultViewModel.State)) FocusPinBox();
    }

    void FocusPinBox()
    {
        if (!IsVisible || Vm == null) return;
        Dispatcher.InvokeAsync(() =>
        {
            if (Vm.State == VaultState.Setup) NewPin.Focus();
            else if (Vm.State == VaultState.Locked) UnlockPin.Focus();
        }, DispatcherPriority.Input);
    }

    void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ContextMenu is not { } menu) return;
        menu.DataContext = element.DataContext;
        menu.PlacementTarget = element;
        menu.Placement = PlacementMode.Bottom;
        menu.HorizontalOffset = 0;
        menu.Opened -= AlignMenuRight;
        menu.Opened += AlignMenuRight;
        menu.IsOpen = true;
        e.Handled = true;
    }

    static void AlignMenuRight(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu { PlacementTarget: FrameworkElement target } menu)
            menu.HorizontalOffset = target.ActualWidth - menu.ActualWidth;
    }

    async void Setup_Click(object sender, RoutedEventArgs e) => await Setup();

    async void Setup_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (ReferenceEquals(sender, NewPin)) ConfirmPin.Focus();
        else await Setup();
    }

    async Task Setup()
    {
        if (Vm == null) return;
        var ok = await Vm.SetupAsync(NewPin.Password, ConfirmPin.Password);
        if (ok)
        {
            NewPin.Clear();
            ConfirmPin.Clear();
        }
    }

    async void Unlock_Click(object sender, RoutedEventArgs e) => await Unlock();

    async void Unlock_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await Unlock();
    }

    async Task Unlock()
    {
        if (Vm == null) return;
        await Vm.UnlockAsync(UnlockPin.Password);
        UnlockPin.Clear();
        if (Vm.State == VaultState.Locked) UnlockPin.Focus();
    }
}
