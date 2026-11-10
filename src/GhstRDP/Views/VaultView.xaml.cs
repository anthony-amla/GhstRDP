using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
            if (Vm.State == VaultState.Configurar) NewPin.Focus();
            else if (Vm.State == VaultState.Bloqueado) UnlockPin.Focus();
        }, DispatcherPriority.Input);
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
        if (Vm.State == VaultState.Bloqueado) UnlockPin.Focus();
    }
}
