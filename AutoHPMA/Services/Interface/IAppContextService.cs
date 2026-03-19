using AutoHPMA.Helpers.CaptureHelper;
using AutoHPMA.Views.Windows;
using System.ComponentModel;

namespace AutoHPMA.Services.Interface;

public interface IAppContextService : INotifyPropertyChanged
{
    nint DisplayHwnd { get; set; }

    nint GameHwnd { get; set; }

    LogWindow LogWindow { get; set; }

    MaskWindow MaskWindow { get; set; }

    WindowsGraphicsCapture Capture { get; set; }

    int StateMonitorInterval { get; set; }
}
