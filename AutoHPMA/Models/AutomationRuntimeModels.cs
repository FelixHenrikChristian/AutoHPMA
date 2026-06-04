using AutoHPMA.Capture.Models;

namespace AutoHPMA.Models;

public enum GameClientKind
{
    MumuSimulator,
    OfficialLauncher,
}

public sealed class GameWindowTarget
{
    public required GameClientKind ClientKind { get; init; }

    public required string ProviderName { get; init; }

    public required WindowInfo DisplayWindow { get; init; }

    public required WindowInfo GameWindow { get; init; }

    public IntPtr CaptureHandle => DisplayWindow.Handle != IntPtr.Zero
        ? DisplayWindow.Handle
        : GameWindow.Handle;

    public IntPtr ForegroundHandle => DisplayWindow.Handle != IntPtr.Zero
        ? DisplayWindow.Handle
        : GameWindow.Handle;

    public string DisplayName => $"{ProviderName} - {GameWindow.DisplayName}";
}

public sealed class AutomationRuntimeOptions
{
    public bool LogWindowEnabled { get; init; } = true;

    public bool LogWindowMarqueeEnabled { get; init; } = true;

    public bool ShowDebugLogs { get; init; }

    public bool MaskWindowEnabled { get; init; } = true;

    public bool MaskWindowShowTextLabels { get; init; } = true;

    public int StateMonitorInterval { get; init; } = 200;

    public string SelectedOcrEngine { get; init; } = "PaddleOCR";
}

public sealed class AutomationRuntimeStartResult
{
    private AutomationRuntimeStartResult(
        bool succeeded,
        string title,
        string message,
        GameWindowTarget? target,
        Exception? exception)
    {
        Succeeded = succeeded;
        Title = title;
        Message = message;
        Target = target;
        Exception = exception;
    }

    public bool Succeeded { get; }

    public string Title { get; }

    public string Message { get; }

    public GameWindowTarget? Target { get; }

    public Exception? Exception { get; }

    public static AutomationRuntimeStartResult Success(GameWindowTarget target) =>
        new(true, "启动成功", $"已启动截图器：{target.DisplayName}", target, null);

    public static AutomationRuntimeStartResult AlreadyRunning(GameWindowTarget? target) =>
        new(true, "已在运行", target is null ? "截图器已经启动。" : $"截图器已经启动：{target.DisplayName}", target, null);

    public static AutomationRuntimeStartResult Failure(string title, string message, Exception? exception = null) =>
        new(false, title, message, null, exception);
}
