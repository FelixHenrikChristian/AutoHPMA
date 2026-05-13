using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using AutoHPMA.Capture;
using AutoHPMA.Capture.Models;
using AutoHPMA.Views.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoHPMA.ViewModels.TestPages;

public partial class ScreenshotTestViewModel : ObservableObject
{
    public ScreenshotTestViewModel()
    {
        CaptureMethods = new ObservableCollection<CaptureMethod>(
            (CaptureMethod[])Enum.GetValues(typeof(CaptureMethod)));
        Windows = new ObservableCollection<WindowInfo>();
        RefreshWindows();
    }

    public ObservableCollection<CaptureMethod> CaptureMethods { get; }

    public ObservableCollection<WindowInfo> Windows { get; }

    [ObservableProperty]
    private CaptureMethod _selectedMethod = CaptureMethod.WindowsGraphicsCapture;

    [ObservableProperty]
    private WindowInfo? _selectedWindow;

    [RelayCommand]
    private void RefreshWindows()
    {
        var previousHandle = SelectedWindow?.Handle ?? IntPtr.Zero;
        Windows.Clear();
        foreach (var w in WindowEnumerator.EnumerateVisibleWindows())
        {
            Windows.Add(w);
        }

        SelectedWindow = Windows.FirstOrDefault(w => w.Handle == previousHandle) ?? Windows.FirstOrDefault();
    }

    [RelayCommand]
    private void StartPreview()
    {
        if (SelectedWindow is null || SelectedWindow.Handle == IntPtr.Zero)
        {
            return;
        }

        if (SelectedMethod == CaptureMethod.WindowsGraphicsCapture && !WindowsGraphicsCapture.IsSupported)
        {
            return;
        }

        try
        {
            var window = new CapturePreviewWindow(SelectedMethod, SelectedWindow);
            window.Activate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"启动截图预览失败：{ex}");
        }
    }
}
