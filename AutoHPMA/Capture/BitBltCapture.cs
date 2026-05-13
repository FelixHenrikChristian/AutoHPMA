using System;
using AutoHPMA.Capture.Native;

namespace AutoHPMA.Capture;

/// <summary>
/// 基于 GDI <c>BitBlt</c> 的窗口捕获，抓取整个窗口区域 (含非客户区)。
/// </summary>
public sealed class BitBltCapture : GdiWindowCaptureBase
{
    protected override bool TryGetCaptureSize(IntPtr hWnd, out int width, out int height)
    {
        if (NativeMethods.GetWindowRect(hWnd, out var rect))
        {
            width = rect.Width;
            height = rect.Height;
            return true;
        }

        width = height = 0;
        return false;
    }

    protected override IntPtr AcquireSourceDC(IntPtr hWnd) => NativeMethods.GetWindowDC(hWnd);

    protected override void ReleaseSourceDC(IntPtr hWnd, IntPtr hdc)
    {
        if (hdc != IntPtr.Zero)
        {
            NativeMethods.ReleaseDC(hWnd, hdc);
        }
    }

    protected override bool RenderToMemoryDC(IntPtr hWnd, IntPtr hdcSrc, IntPtr hdcMem, int width, int height)
        => NativeMethods.BitBlt(hdcMem, 0, 0, width, height, hdcSrc, 0, 0, NativeMethods.SRCCOPY);
}
