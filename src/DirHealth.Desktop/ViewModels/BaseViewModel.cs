using CommunityToolkit.Mvvm.ComponentModel;

namespace DirHealth.Desktop.ViewModels;

public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "";
}
