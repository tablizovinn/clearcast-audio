using System.ComponentModel;
using System.Runtime.CompilerServices;
using CLEARCAST.Application.Abstractions;
using CLEARCAST.Core.Enums;

namespace CLEARCAST.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IProcessingModeService _processingModeService;

    private string _applicationStatus = "Application initialized";

    public MainViewModel(IProcessingModeService processingModeService)
    {
        _processingModeService = processingModeService;
    }

    public string ApplicationStatus
    {
        get => _applicationStatus;
        set
        {
            if (_applicationStatus == value)
            {
                return;
            }

            _applicationStatus = value;
            OnPropertyChanged();
        }
    }

    public ProcessingMode CurrentProcessingMode =>
        _processingModeService.CurrentMode;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
