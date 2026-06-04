using System.Runtime.InteropServices;
using System.ComponentModel;
using AutoHPMA.Core.Contracts.Services;
using AutoHPMA.Core.Models;

namespace AutoHPMA.Core.Services;

public sealed class WindowInteractionService : IWindowInteractionService
{
    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private static readonly IntPtr MkLButton = new(0x0001);

    public async Task ExecuteAsync(IntPtr hWnd, MouseActionOptions options, CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        ArgumentNullException.ThrowIfNull(options);
        ValidateHandle(hWnd);
        ValidateOptions(options);

        for (var i = 0; i < options.RepeatCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (options.ActionType)
            {
                case MouseActionType.Click:
                    await ClickOnceAsync(hWnd, options.X, options.Y, 100, cancellationToken);
                    break;

                case MouseActionType.Drag:
                    await DragOnceAsync(hWnd, options, cancellationToken);
                    break;

                case MouseActionType.LongPress:
                    await ClickOnceAsync(hWnd, options.X, options.Y, options.DurationMilliseconds, cancellationToken);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(options.ActionType), options.ActionType, "Unsupported mouse action type.");
            }

            if (i < options.RepeatCount - 1)
            {
                await DelayIfPositiveAsync(options.RepeatIntervalMilliseconds, cancellationToken);
            }
        }
    }

    public bool TrySetForegroundWindow(IntPtr hWnd)
    {
        EnsureWindows();
        ValidateHandle(hWnd);
        return SetForegroundWindow(hWnd);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Window interaction is only supported on Windows.");
        }
    }

    private static void ValidateHandle(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            throw new ArgumentException("Target window handle cannot be zero.", nameof(hWnd));
        }

        if (!IsWindow(hWnd))
        {
            throw new InvalidOperationException("Target window no longer exists.");
        }
    }

    private static void ValidateOptions(MouseActionOptions options)
    {
        ValidateCoordinate(options.X, nameof(options.X));
        ValidateCoordinate(options.Y, nameof(options.Y));
        ValidateCoordinate(options.EndX, nameof(options.EndX));
        ValidateCoordinate(options.EndY, nameof(options.EndY));

        if (options.DurationMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.DurationMilliseconds), "Duration cannot be negative.");
        }

        if (options.RepeatCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.RepeatCount), "Repeat count must be greater than zero.");
        }

        if (options.RepeatIntervalMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.RepeatIntervalMilliseconds), "Repeat interval cannot be negative.");
        }
    }

    private static void ValidateCoordinate(int value, string parameterName)
    {
        if (value is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Mouse coordinates must fit in a Win32 message lParam.");
        }
    }

    private static async Task ClickOnceAsync(IntPtr hWnd, int x, int y, int holdDurationMilliseconds, CancellationToken cancellationToken)
    {
        var lParam = MakeLParam(x, y);
        SendWindowMessage(hWnd, WmMouseMove, IntPtr.Zero, lParam);
        SendWindowMessage(hWnd, WmLButtonDown, MkLButton, lParam);

        try
        {
            await DelayIfPositiveAsync(holdDurationMilliseconds, cancellationToken);
        }
        finally
        {
            SendWindowMessage(hWnd, WmLButtonUp, IntPtr.Zero, lParam);
        }
    }

    private static async Task DragOnceAsync(IntPtr hWnd, MouseActionOptions options, CancellationToken cancellationToken)
    {
        var startLParam = MakeLParam(options.X, options.Y);
        var endLParam = MakeLParam(options.EndX, options.EndY);
        var currentLParam = startLParam;

        PostWindowMessage(hWnd, WmLButtonDown, MkLButton, startLParam);

        try
        {
            await DelayIfPositiveAsync(50, cancellationToken);

            const int steps = 30;
            var stepDelay = Math.Max(options.DurationMilliseconds / steps, 1);
            var stepX = (options.EndX - options.X) / (double)steps;
            var stepY = (options.EndY - options.Y) / (double)steps;

            for (var i = 1; i <= steps; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var currentX = (int)Math.Round(options.X + (stepX * i));
                var currentY = (int)Math.Round(options.Y + (stepY * i));
                currentLParam = MakeLParam(currentX, currentY);

                PostWindowMessage(hWnd, WmMouseMove, MkLButton, currentLParam);
                await DelayIfPositiveAsync(stepDelay, cancellationToken);
            }

            PostWindowMessage(hWnd, WmLButtonUp, IntPtr.Zero, endLParam);
            await DelayIfPositiveAsync(50, cancellationToken);
        }
        catch
        {
            PostWindowMessage(hWnd, WmLButtonUp, IntPtr.Zero, currentLParam);
            throw;
        }
    }

    private static async Task DelayIfPositiveAsync(int milliseconds, CancellationToken cancellationToken)
    {
        if (milliseconds > 0)
        {
            await Task.Delay(milliseconds, cancellationToken);
        }
    }

    private static IntPtr MakeLParam(int x, int y)
    {
        return (IntPtr)((y << 16) | (x & 0xFFFF));
    }

    private static void SendWindowMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
        => SendMessage(hWnd, message, wParam, lParam);

    private static void PostWindowMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (!PostMessage(hWnd, message, wParam, lParam))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
