using System;
using AutoHPMA.Capture.Native;

namespace AutoHPMA.Capture;

/// <summary>
/// 基于 <c>PrintWindow</c> 的窗口捕获，仅截取客户区，并尝试启用 PW_RENDERFULLCONTENT 以兼容硬件加速窗口。
/// </summary>
public sealed class PrintWindowCapture : GdiWindowCaptureBase
{
    protected override bool TryGetCaptureSize(IntPtr hWnd, out int width, out int height)
    {
        if (NativeMethods.GetClientRect(hWnd, out var rect))
        {
            width = rect.Width;
            height = rect.Height;
            return true;
        }

        width = height = 0;
        return false;
    }

    protected override IntPtr AcquireSourceDC(IntPtr hWnd) => NativeMethods.GetDC(hWnd);

    protected override void ReleaseSourceDC(IntPtr hWnd, IntPtr hdc)
    {
        if (hdc != IntPtr.Zero)
        {
            NativeMethods.ReleaseDC(hWnd, hdc);
        }
    }

    protected override bool RenderToMemoryDC(IntPtr hWnd, IntPtr hdcSrc, IntPtr hdcMem, int width, int height)
    {
        // 优先尝试 PW_RENDERFULLCONTENT，失败时降级到仅 PW_CLIENTONLY。
        var flags = NativeMethods.PW_CLIENTONLY | NativeMethods.PW_RENDERFULLCONTENT;
        if (NativeMethods.PrintWindow(hWnd, hdcMem, flags))
        {
            return true;
        }

        return NativeMethods.PrintWindow(hWnd, hdcMem, NativeMethods.PW_CLIENTONLY);
    }
}
