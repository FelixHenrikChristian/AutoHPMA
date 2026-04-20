using System.Runtime.InteropServices;

using WinRT.Interop;

using WinUIEx;

namespace AutoHPMA.Helpers;

internal static class WindowPlacementHelper
{
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// 将子窗口置于父窗口工作区中央。
    /// </summary>
    public static void CenterOnParent(WindowEx child, WindowEx parent)
    {
        var parentHwnd = WindowNative.GetWindowHandle(parent);
        if (!GetWindowRect(parentHwnd, out var pr))
        {
            return;
        }

        const int defaultW = 800;
        const int defaultH = 720;
        var w = child.AppWindow.Size.Width;
        var h = child.AppWindow.Size.Height;
        if (w <= 0 || h <= 0)
        {
            w = defaultW;
            h = defaultH;
        }

        var pw = pr.Right - pr.Left;
        var ph = pr.Bottom - pr.Top;
        var x = pr.Left + (pw - w) / 2;
        var y = pr.Top + (ph - h) / 2;
        child.AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }
}
