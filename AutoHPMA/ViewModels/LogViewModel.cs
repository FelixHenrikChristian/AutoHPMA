using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;

using Microsoft.Extensions.Logging;

using AutoHPMA.Helpers;
using AutoHPMA.Models;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.UI.Dispatching;

using Serilog.Events;

namespace AutoHPMA.ViewModels;

public partial class LogViewModel : ObservableRecipient
{
    private const int MaxDisplayEntries = 2000;

    private readonly ILogger<LogViewModel> _logger;

    public LogViewModel(ILogger<LogViewModel> logger)
    {
        _logger = logger;
    }

    private DispatcherQueue? _dispatcher;
    private bool _subscribed;

    /// <summary>
    /// 过滤后、实际显示的日志条目。
    /// </summary>
    public ObservableCollection<LogEntry> Entries { get; } = new();

    [ObservableProperty] private bool _showDebug = true;
    [ObservableProperty] private bool _showInformation = true;
    [ObservableProperty] private bool _showWarning = true;
    [ObservableProperty] private bool _showError = true;

    [ObservableProperty] private string _searchText = string.Empty;

    /// <summary>
    /// 页面激活时调用：记录 UI 调度队列、加载历史、订阅后续事件。
    /// </summary>
    public void Attach(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;

        Entries.Clear();
        foreach (var entry in LoggingHelper.LogBuffer.Snapshot())
        {
            if (PassesFilter(entry))
            {
                Entries.Add(entry);
            }
        }

        if (!_subscribed)
        {
            LoggingHelper.LogBuffer.EntryWritten += OnEntryWritten;
            _subscribed = true;
        }
    }

    /// <summary>
    /// 页面离开时调用：取消订阅，避免内存泄漏与无用回调。
    /// </summary>
    public void Detach()
    {
        if (_subscribed)
        {
            LoggingHelper.LogBuffer.EntryWritten -= OnEntryWritten;
            _subscribed = false;
        }
        _dispatcher = null;
    }

    private void OnEntryWritten(LogEntry entry)
    {
        var dq = _dispatcher;
        if (dq == null)
        {
            return;
        }

        dq.TryEnqueue(() =>
        {
            if (!PassesFilter(entry))
            {
                return;
            }

            Entries.Add(entry);
            while (Entries.Count > MaxDisplayEntries)
            {
                Entries.RemoveAt(0);
            }
        });
    }

    private bool PassesFilter(LogEntry entry)
    {
        if (!IsLevelEnabled(entry.Level))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText;
            if (entry.Message.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                entry.SourceContext.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsLevelEnabled(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => ShowDebug,
        LogEventLevel.Debug => ShowDebug,
        LogEventLevel.Information => ShowInformation,
        LogEventLevel.Warning => ShowWarning,
        LogEventLevel.Error => ShowError,
        LogEventLevel.Fatal => ShowError,
        _ => true,
    };

    partial void OnShowDebugChanged(bool value) => RebuildFromBuffer();
    partial void OnShowInformationChanged(bool value) => RebuildFromBuffer();
    partial void OnShowWarningChanged(bool value) => RebuildFromBuffer();
    partial void OnShowErrorChanged(bool value) => RebuildFromBuffer();
    partial void OnSearchTextChanged(string value) => RebuildFromBuffer();

    private void RebuildFromBuffer()
    {
        var dq = _dispatcher;
        if (dq == null)
        {
            return;
        }

        dq.TryEnqueue(() =>
        {
            Entries.Clear();
            foreach (var entry in LoggingHelper.LogBuffer.Snapshot())
            {
                if (PassesFilter(entry))
                {
                    Entries.Add(entry);
                    if (Entries.Count >= MaxDisplayEntries)
                    {
                        break;
                    }
                }
            }
        });
    }

    [RelayCommand]
    private void Clear()
    {
        // 只清 UI 列表；文件日志与内存缓冲保留用于排障。
        Entries.Clear();
    }

    /// <summary>
    /// 打开当天的日志文件。若当天日志尚未生成，退回到日志目录。
    /// </summary>
    [RelayCommand]
    private void OpenLogFile()
    {
        try
        {
            Directory.CreateDirectory(LoggingHelper.LogDirectory);

            // Serilog 滚动文件命名格式：app-YYYYMMDD.log
            var todayFile = Path.Combine(
                LoggingHelper.LogDirectory,
                $"app-{DateTime.Now:yyyyMMdd}.log");

            var target = File.Exists(todayFile) ? todayFile : LoggingHelper.LogDirectory;

            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开日志文件失败");
        }
    }
}

