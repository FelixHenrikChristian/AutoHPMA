using System;

using Serilog.Events;

namespace AutoHPMA.Models;

/// <summary>
/// 单条日志记录的轻量快照，用于 UI 展示。
/// </summary>
public sealed class LogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public LogEventLevel Level { get; init; }
    public string SourceContext { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Exception { get; init; }

    /// <summary>时:分:秒.毫秒。</summary>
    public string TimeText => Timestamp.LocalDateTime.ToString("HH:mm:ss.fff");

    /// <summary>三字母缩写：INF / DBG / WRN / ERR / FTL / VRB。</summary>
    public string LevelText => Level switch
    {
        LogEventLevel.Verbose => "VRB",
        LogEventLevel.Debug => "DBG",
        LogEventLevel.Information => "INF",
        LogEventLevel.Warning => "WRN",
        LogEventLevel.Error => "ERR",
        LogEventLevel.Fatal => "FTL",
        _ => "LOG",
    };

    /// <summary>SourceContext 的短名（取最后一段）。</summary>
    public string ShortSource
    {
        get
        {
            if (string.IsNullOrEmpty(SourceContext))
            {
                return string.Empty;
            }

            var idx = SourceContext.LastIndexOf('.');
            return idx >= 0 ? SourceContext[(idx + 1)..] : SourceContext;
        }
    }
}
