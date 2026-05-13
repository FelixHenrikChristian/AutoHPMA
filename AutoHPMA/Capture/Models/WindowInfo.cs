using System;

namespace AutoHPMA.Capture.Models;

/// <summary>
/// 描述一个可被截屏的可见顶层窗口。
/// </summary>
public sealed class WindowInfo : IEquatable<WindowInfo>
{
    public required IntPtr Handle { get; init; }

    public string Title { get; init; } = string.Empty;

    public string ProcessName { get; init; } = string.Empty;

    public int ProcessId { get; init; }

    /// <summary>下拉框中显示的友好名称。</summary>
    public string DisplayName =>
        string.IsNullOrEmpty(Title)
            ? $"{ProcessName} (PID {ProcessId})"
            : $"{Title} — {ProcessName}";

    public bool Equals(WindowInfo? other) => other is not null && Handle == other.Handle;

    public override bool Equals(object? obj) => obj is WindowInfo other && Equals(other);

    public override int GetHashCode() => Handle.GetHashCode();

    public override string ToString() => DisplayName;
}
