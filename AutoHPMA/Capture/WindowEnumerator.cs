using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using AutoHPMA.Capture.Models;
using AutoHPMA.Capture.Native;

namespace AutoHPMA.Capture;

/// <summary>
/// 列出当前可见的、有标题的顶层窗口，用于截屏目标选择。
/// </summary>
public static class WindowEnumerator
{
    public static IReadOnlyList<WindowInfo> EnumerateVisibleWindows(IntPtr selfWindow = default)
    {
        var list = new List<WindowInfo>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;
            if (hWnd == selfWindow) return true;

            var length = NativeMethods.GetWindowTextLength(hWnd);
            if (length <= 0) return true;

            var sb = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
            string processName;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                processName = p.ProcessName;
            }
            catch
            {
                return true; // 进程已退出
            }

            list.Add(new WindowInfo
            {
                Handle = hWnd,
                Title = title,
                ProcessName = processName,
                ProcessId = (int)pid,
            });
            return true;
        }, IntPtr.Zero);

        return list.OrderBy(w => w.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<WindowInfo> EnumerateChildWindows(WindowInfo parentWindow)
    {
        ArgumentNullException.ThrowIfNull(parentWindow);

        var list = new List<WindowInfo>();
        NativeMethods.EnumChildWindows(parentWindow.Handle, (hWnd, _) =>
        {
            var length = NativeMethods.GetWindowTextLength(hWnd);
            var title = string.Empty;
            if (length > 0)
            {
                var sb = new StringBuilder(length + 1);
                NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
                title = sb.ToString();
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = $"[子窗口 0x{hWnd.ToInt64():X}]";
            }

            list.Add(new WindowInfo
            {
                Handle = hWnd,
                Title = title,
                ProcessName = $"{parentWindow.ProcessName} (子窗口)",
                ProcessId = parentWindow.ProcessId,
            });

            return true;
        }, IntPtr.Zero);

        return list.OrderBy(w => w.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
