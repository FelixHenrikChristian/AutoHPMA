namespace AutoHPMA.Capture.Models;

/// <summary>
/// 屏幕/窗口截屏方式。
/// </summary>
public enum CaptureMethod
{
    /// <summary>
    /// Windows Graphics Capture (Win10 1803+，支持 DirectX 加速窗口，性能最佳)。
    /// </summary>
    WindowsGraphicsCapture,

    /// <summary>
    /// GDI BitBlt (兼容性好，CPU 占用低，但无法捕获硬件加速内容)。
    /// </summary>
    BitBlt,

    /// <summary>
    /// User32 PrintWindow (可捕获被遮挡或后台窗口，性能一般)。
    /// </summary>
    PrintWindow,
}

