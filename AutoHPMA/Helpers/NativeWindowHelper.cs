using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AutoHPMA.Helpers;

/// <summary>
/// 覆盖窗口相关的 Win32 API 和工具方法
/// </summary>
/// <remarks>
/// 注意：GetWindowPosition 使用 DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)
/// 而非 GetWindowRect，以排除 DWM 不可见阴影边框，与 WindowsGraphicsCapture 的坐标系一致。
/// </remarks>
public static class NativeWindowHelper
{
    #region Win32 常量

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    #endregion

    #region Win32 API 声明

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 设置窗口为透明且鼠标穿透
    /// </summary>
    public static void SetWindowTransparent(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT);
    }

    /// <summary>
    /// 将窗口置于所有 Topmost 窗口的最上层
    /// </summary>
    public static void BringWindowToFront(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    /// <summary>
    /// 获取当前窗口的 DPI 缩放比例
    /// </summary>
    public static (double dpiX, double dpiY) GetDpiScale(Visual visual)
    {
        PresentationSource source = PresentationSource.FromVisual(visual);
        if (source?.CompositionTarget != null)
        {
            return (source.CompositionTarget.TransformFromDevice.M11,
                    source.CompositionTarget.TransformFromDevice.M22);
        }
        return (1.0, 1.0);
    }

    /// <summary>
    /// 获取目标窗口的位置（经过 DPI 缩放）
    /// </summary>
    /// <remarks>
    /// 使用 DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS) 获取不含 DWM 不可见阴影边框的窗口矩形，
    /// 与 WindowsGraphicsCapture.GetGameScreenRegion 使用的坐标系一致，避免 MaskWindow 绘制偏移。
    /// </remarks>
    public static bool GetWindowPosition(IntPtr targetHwnd, Visual referenceVisual, out double left, out double top, out double width, out double height)
    {
        left = top = width = height = 0;

        // 优先使用 DwmGetWindowAttribute 获取不含阴影边框的窗口矩形
        RECT rect;
        int hr = DwmGetWindowAttribute(targetHwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>());
        if (hr != 0)
        {
            // DWM 不可用时 fallback 到 GetWindowRect
            if (!GetWindowRect(targetHwnd, out rect))
                return false;
        }

        var (dpiX, dpiY) = GetDpiScale(referenceVisual);
        left = rect.Left * dpiX;
        top = rect.Top * dpiY;
        width = (rect.Right - rect.Left) * dpiX;
        height = (rect.Bottom - rect.Top) * dpiY;
        
        return true;
    }

    #endregion
}
