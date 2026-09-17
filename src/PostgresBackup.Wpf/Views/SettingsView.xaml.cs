using System.ComponentModel;
using System.Windows.Controls;
using PostgresBackup.Wpf.ViewModels;

namespace PostgresBackup.Wpf.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += SettingsView_DataContextChanged;
    }

    private void SettingsView_DataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldVm)
        {
            oldVm.PropertyChanged -= Vm_PropertyChanged;
        }

        if (e.NewValue is SettingsViewModel newVm)
        {
            newVm.PropertyChanged += Vm_PropertyChanged;
            PwdBox.Password = newVm.Password;
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.Password) && sender is SettingsViewModel vm)
        {
            if (PwdBox.Password != vm.Password)
            {
                PwdBox.Password = vm.Password;
            }
        }
    }

    private void PasswordBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && sender is PasswordBox pb)
        {
            if (vm.Password != pb.Password)
            {
                vm.Password = pb.Password;
            }
        }
    }
}
