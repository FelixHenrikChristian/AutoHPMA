using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AutoHPMA.Models;

using Serilog.Core;
using Serilog.Events;

namespace AutoHPMA.Services.Logging;

/// <summary>
/// Serilog 内存环形缓冲 sink。
/// 保留最近 N 条日志用于 UI 实时展示；新增日志时触发 <see cref="EntryWritten"/>。
/// 线程安全。
/// </summary>
public sealed class InMemoryLogSink : ILogEventSink
{
    private readonly object _gate = new();
    private readonly Queue<LogEntry> _buffer;
    private readonly int _capacity;

    /// <summary>
    /// 有新日志写入时触发。回调可能发生在任意线程，订阅方需要自己切到 UI 线程。
    /// </summary>
    public event Action<LogEntry>? EntryWritten;

    public InMemoryLogSink(int capacity = 2000)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
        _buffer = new Queue<LogEntry>(capacity);
    }

    public void Emit(LogEvent logEvent)
    {
        var entry = Convert(logEvent);

        lock (_gate)
        {
            if (_buffer.Count >= _capacity)
            {
                _buffer.Dequeue();
            }
            _buffer.Enqueue(entry);
        }

        EntryWritten?.Invoke(entry);
    }

    /// <summary>
    /// 获取当前缓冲内所有日志的快照（按时间先后）。
    /// </summary>
    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate)
        {
            return _buffer.ToArray();
        }
    }

    /// <summary>
    /// 清空缓冲。不会触发事件。
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _buffer.Clear();
        }
    }

    private static LogEntry Convert(LogEvent e)
    {
        string source = string.Empty;
        if (e.Properties.TryGetValue("SourceContext", out var sc) &&
            sc is ScalarValue sv && sv.Value is string s)
        {
            source = s;
        }

        string message;
        try
        {
            message = e.RenderMessage();
        }
        catch
        {
            message = e.MessageTemplate.Text;
        }

        string? exception = null;
        if (e.Exception != null)
        {
            using var sw = new StringWriter();
            sw.Write(e.Exception.ToString());
            exception = sw.ToString();
        }

        return new LogEntry
        {
            Timestamp = e.Timestamp,
            Level = e.Level,
            SourceContext = source,
            Message = message,
            Exception = exception,
        };
    }
}
