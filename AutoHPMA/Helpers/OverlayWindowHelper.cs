using System.Runtime.InteropServices;
using AutoHPMA.Capture.Native;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace AutoHPMA.Helpers;

internal static class OverlayWindowHelper
{
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const uint DWMWCP_DONOTROUND = 1;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const long BORDERLESS_STYLE_MASK =
        0x00C00000L | // WS_CAPTION
        0x00040000L | // WS_THICKFRAME
        0x00020000L | // WS_MINIMIZEBOX
        0x00010000L | // WS_MAXIMIZEBOX
        0x00080000L;  // WS_SYSMENU
    private const long WS_EX_TRANSPARENT = 0x00000020;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_LAYERED = 0x00080000;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int WM_ERASEBKGND = 0x0014;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_DWMCOMPOSITIONCHANGED = 0x031E;
    private const int HTTRANSPARENT = -1;

    private static readonly object BoundsGate = new();
    private static readonly Dictionary<IntPtr, RectInt32> LastOverlayBounds = [];
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly UIntPtr SubclassId = new(0x4148504D);

    public static void ApplyOverlayChrome(WindowEx window)
    {
        window.SystemBackdrop = new TransparentTintBackdrop(Color.FromArgb(0, 0, 0, 0));
        window.ExtendsContentIntoTitleBar = true;
        window.IsAlwaysOnTop = true;

        if (window.AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            presenter = OverlappedPresenter.Create();
            window.AppWindow.SetPresenter(presenter);
        }

        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        window.AppWindow.IsShownInSwitchers = false;

        ApplyTransparentOverlayStyles(GetWindowHandle(window), show: false);
    }

    public static IDisposable InstallMessageHook(WindowEx window)
    {
        var hwnd = GetWindowHandle(window);
        if (hwnd == IntPtr.Zero)
        {
            return EmptyDisposable.Instance;
        }

        var hook = new WindowMessageHook(hwnd);
        hook.Attach();
        return hook;
    }

    public static void BringToTop(WindowEx window)
    {
        var hwnd = GetWindowHandle(window);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        SetTopMost(hwnd);
    }

    public static void ShowNoActivate(WindowEx window)
    {
        var hwnd = GetWindowHandle(window);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        ApplyTransparentOverlayStyles(hwnd);
        SetTopMost(hwnd);
    }

    public static void HideOverlay(WindowEx window)
    {
        var hwnd = GetWindowHandle(window);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
    }

    public static IntPtr GetWindowHandle(WindowEx? window) =>
        window is null ? IntPtr.Zero : WindowNative.GetWindowHandle(window);

    private static void ApplyTransparentOverlayStyles(IntPtr hwnd, bool show = true)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        RemoveStandardWindowFrame(hwnd);
        AddExtendedStyles(hwnd, WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        RefreshWindowFrame(hwnd, show);
        TryDisableDwmFrame(hwnd);
    }

    private static void RemoveStandardWindowFrame(IntPtr hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        _ = SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style & ~BORDERLESS_STYLE_MASK));
    }

    private static void AddExtendedStyles(IntPtr hwnd, long styles)
    {
        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        _ = SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle | styles));
    }

    private static void RefreshWindowFrame(IntPtr hwnd, bool show)
    {
        var flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED;
        if (show)
        {
            flags |= SWP_SHOWWINDOW;
        }

        _ = SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, flags);
    }

    private static void SetTopMost(IntPtr hwnd)
    {
        _ = SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private static void TryDisableDwmFrame(IntPtr hwnd)
    {
        try
        {
            var cornerPreference = DWMWCP_DONOTROUND;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, Marshal.SizeOf<uint>());

            var borderColor = DWMWA_COLOR_NONE;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, Marshal.SizeOf<uint>());
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    public static bool TryMoveToWindow(WindowEx overlay, IntPtr targetHwnd, int offsetX = 0, int offsetY = 0)
    {
        if (!TryGetWindowBounds(targetHwnd, out var bounds))
        {
            return false;
        }

        var newBounds = new RectInt32(bounds.Left + offsetX, bounds.Top + offsetY, 0, 0);
        if (ShouldApplyBounds(overlay, newBounds))
        {
            overlay.AppWindow.Move(new PointInt32(newBounds.X, newBounds.Y));
        }

        return true;
    }

    public static bool TryFitToWindow(WindowEx overlay, IntPtr targetHwnd, int offsetX = 0, int offsetY = 0)
    {
        if (!TryGetWindowBounds(targetHwnd, out var bounds))
        {
            return false;
        }

        var newBounds = new RectInt32(
            bounds.Left + offsetX,
            bounds.Top + offsetY,
            Math.Max(1, bounds.Width),
            Math.Max(1, bounds.Height));
        if (ShouldApplyBounds(overlay, newBounds))
        {
            overlay.AppWindow.MoveAndResize(newBounds);
        }

        return true;
    }

    private static bool ShouldApplyBounds(WindowEx overlay, RectInt32 bounds)
    {
        var hwnd = GetWindowHandle(overlay);
        if (hwnd == IntPtr.Zero)
        {
            return true;
        }

        lock (BoundsGate)
        {
            if (LastOverlayBounds.TryGetValue(hwnd, out var previous) &&
                previous.X == bounds.X &&
                previous.Y == bounds.Y &&
                previous.Width == bounds.Width &&
                previous.Height == bounds.Height)
            {
                return false;
            }

            LastOverlayBounds[hwnd] = bounds;
            return true;
        }
    }

    public static bool TryGetWindowBounds(IntPtr targetHwnd, out NativeMethods.RECT bounds)
    {
        var hr = DwmGetWindowAttribute(
            targetHwnd,
            DWMWA_EXTENDED_FRAME_BOUNDS,
            out bounds,
            Marshal.SizeOf<NativeMethods.RECT>());

        if (hr == 0)
        {
            return true;
        }

        return NativeMethods.GetWindowRect(targetHwnd, out bounds);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        out NativeMethods.RECT pvAttribute,
        int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref uint pvAttribute,
        int cbAttribute);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr hwnd,
        SubclassProc subclassProc,
        UIntPtr subclassId,
        UIntPtr refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        IntPtr hwnd,
        SubclassProc subclassProc,
        UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    private delegate IntPtr SubclassProc(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr refData);

    private sealed class WindowMessageHook : IDisposable
    {
        private readonly IntPtr _hwnd;
        private readonly SubclassProc _subclassProc;
        private bool _attached;

        public WindowMessageHook(IntPtr hwnd)
        {
            _hwnd = hwnd;
            _subclassProc = WndProc;
        }

        public void Attach()
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            _attached = SetWindowSubclass(_hwnd, _subclassProc, SubclassId, UIntPtr.Zero);
        }

        public void Dispose()
        {
            if (!_attached)
            {
                return;
            }

            _ = RemoveWindowSubclass(_hwnd, _subclassProc, SubclassId);
            _attached = false;
        }

        private IntPtr WndProc(
            IntPtr hwnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            UIntPtr subclassId,
            UIntPtr refData)
        {
            if (message == WM_ERASEBKGND)
            {
                return new IntPtr(1);
            }

            if (message == WM_NCHITTEST)
            {
                return new IntPtr(HTTRANSPARENT);
            }

            if (message == WM_DWMCOMPOSITIONCHANGED)
            {
                TryDisableDwmFrame(hwnd);
            }

            return DefSubclassProc(hwnd, message, wParam, lParam);
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        private EmptyDisposable()
        {
        }

        public void Dispose()
        {
        }
    }
}
