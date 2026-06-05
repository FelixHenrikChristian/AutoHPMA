using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using AutoHPMA.Helpers;
using AutoHPMA.Models;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Serilog.Events;
using Windows.Foundation;
using Windows.UI;

namespace AutoHPMA.Views.Windows;

public sealed partial class LogOverlayWindow : WindowEx, INotifyPropertyChanged
{
    private const int MaxLogCount = 100;
    private const double MarqueeViewWidth = 230;
    private const double MarqueePauseSeconds = 1.5;
    private static readonly Regex MarkupRegex = new(@"\[(?<name>[A-Za-z]+)\](?<text>.*?)\[/\k<name>\]", RegexOptions.Compiled);

    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _timeTimer;
    private readonly IDisposable _messageHook;
    private bool _isClosed;
    private bool _showMarquee = true;
    private bool _showDebugLogs;
    private string _timeNow = string.Empty;
    private string _currentGameState = "空闲";

    public LogOverlayWindow()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        InitializeComponent();

        RootHost.DataContext = this;
        OverlayWindowHelper.ApplyOverlayChrome(this);
        _messageHook = OverlayWindowHelper.InstallMessageHook(this);

        _timeTimer = _dispatcher.CreateTimer();
        _timeTimer.Interval = TimeSpan.FromSeconds(1);
        _timeTimer.Tick += OnTimeTimerTick;
        _timeTimer.Start();

        TimeNow = DateTime.Now.ToString("HH:mm:ss");

        LoggingHelper.LogBuffer.EntryWritten += OnEntryWritten;
        Closed += OnClosed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public bool ShowMarquee
    {
        get => _showMarquee;
        set => _showMarquee = value;
    }

    public bool ShowDebugLogs
    {
        get => _showDebugLogs;
        set => _showDebugLogs = value;
    }

    public string TimeNow
    {
        get => _timeNow;
        private set
        {
            if (_timeNow == value)
            {
                return;
            }

            _timeNow = value;
            OnPropertyChanged(nameof(TimeNow));
        }
    }

    public string CurrentGameState
    {
        get => _currentGameState;
        private set
        {
            if (_currentGameState == value)
            {
                return;
            }

            _currentGameState = value;
            OnPropertyChanged(nameof(CurrentGameState));
        }
    }

    public void LoadSnapshot()
    {
        Entries.Clear();

        foreach (var entry in LoggingHelper.LogBuffer.Snapshot())
        {
            if (PassesFilter(entry))
            {
                Entries.Add(entry);
            }
        }

        TrimEntries();
        ScrollToBottom();
    }

    public void SetGameState(string state) => CurrentGameState = string.IsNullOrWhiteSpace(state) ? "空闲" : state;

    public void DeleteLastLogMessage()
    {
        if (Entries.Count > 0)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }
    }

    public void RefreshPosition(IntPtr hWnd, int offsetX = 0, int offsetY = 0)
    {
        if (OverlayWindowHelper.TryMoveToWindow(this, hWnd, offsetX, offsetY))
        {
            ScrollToBottom();
        }
    }

    public void BringOverlayToFront() => OverlayWindowHelper.BringToTop(this);

    private void OnEntryWritten(LogEntry entry)
    {
        if (_isClosed || !PassesFilter(entry))
        {
            return;
        }

        _dispatcher.TryEnqueue(() =>
        {
            if (_isClosed)
            {
                return;
            }

            Entries.Add(entry);
            TrimEntries();
            ScrollToBottom();
        });
    }

    private bool PassesFilter(LogEntry entry) =>
        _showDebugLogs ||
        entry.Level is not (LogEventLevel.Verbose or LogEventLevel.Debug);

    private void TrimEntries()
    {
        while (Entries.Count > MaxLogCount)
        {
            Entries.RemoveAt(0);
        }
    }

    private void ScrollToBottom()
    {
        if (LogListView.Items.Count > 0)
        {
            LogListView.ScrollIntoView(LogListView.Items[^1]);
        }
    }

    private void OnTimeTimerTick(DispatcherQueueTimer sender, object args) =>
        TimeNow = DateTime.Now.ToString("HH:mm:ss");

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _isClosed = true;
        Closed -= OnClosed;
        LoggingHelper.LogBuffer.EntryWritten -= OnEntryWritten;
        _messageHook.Dispose();
        _timeTimer.Stop();
        _timeTimer.Tick -= OnTimeTimerTick;
    }

    private void OnMessageTextLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock textBlock || textBlock.DataContext is not LogEntry entry)
        {
            return;
        }

        ApplyFormattedMessage(textBlock, entry.Message);

        if (!ShowMarquee)
        {
            Canvas.SetLeft(textBlock, 0);
            return;
        }

        if (textBlock.Parent is Canvas canvas)
        {
            StartMarquee(textBlock, canvas);
        }
    }

    private void OnMessageClipHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        if (element.FindName("MarqueeCanvas") is Canvas canvas)
        {
            canvas.Width = e.NewSize.Width;
        }

        element.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height),
        };
    }

    private static void ApplyFormattedMessage(TextBlock textBlock, string message)
    {
        textBlock.Inlines.Clear();

        var last = 0;
        foreach (Match match in MarkupRegex.Matches(message))
        {
            if (match.Index > last)
            {
                textBlock.Inlines.Add(new Run { Text = message[last..match.Index] });
            }

            var run = new Run { Text = match.Groups["text"].Value };
            if (TryCreateBrush(match.Groups["name"].Value, out var brush))
            {
                run.Foreground = brush;
            }

            textBlock.Inlines.Add(run);
            last = match.Index + match.Length;
        }

        if (last < message.Length)
        {
            textBlock.Inlines.Add(new Run { Text = message[last..] });
        }
    }

    private static bool TryCreateBrush(string name, out SolidColorBrush brush)
    {
        Color? color = name.ToLowerInvariant() switch
        {
            "yellow" => Color.FromArgb(0xFF, 0xFA, 0xCC, 0x15),
            "lime" => Color.FromArgb(0xFF, 0x84, 0xCC, 0x16),
            "aquamarine" => Color.FromArgb(0xFF, 0x7F, 0xFF, 0xD4),
            "red" => Color.FromArgb(0xFF, 0xF8, 0x71, 0x71),
            "green" => Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E),
            "blue" => Color.FromArgb(0xFF, 0x60, 0xA5, 0xFA),
            _ => default,
        };

        if (color is not { } resolvedColor)
        {
            brush = new SolidColorBrush(Colors.Transparent);
            return false;
        }

        brush = new SolidColorBrush(resolvedColor);
        return true;
    }

    private static void StartMarquee(TextBlock textBlock, Canvas canvas)
    {
        textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var hostWidth = canvas.Parent is FrameworkElement host ? host.ActualWidth : 0;
        var viewWidth = hostWidth > 0 ? hostWidth : canvas.ActualWidth > 0 ? canvas.ActualWidth : MarqueeViewWidth;
        var textWidth = textBlock.DesiredSize.Width;
        if (textWidth <= viewWidth)
        {
            Canvas.SetLeft(textBlock, 0);
            return;
        }

        var scrollSeconds = Math.Max(2, (textWidth - viewWidth) / 60.0);
        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            From = 0,
            To = viewWidth - textWidth,
            BeginTime = TimeSpan.FromSeconds(MarqueePauseSeconds),
            Duration = new Duration(TimeSpan.FromSeconds(scrollSeconds)),
            EnableDependentAnimation = true,
        };

        Storyboard.SetTarget(animation, textBlock);
        Storyboard.SetTargetProperty(animation, "(Canvas.Left)");
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) =>
        {
            var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(MarqueePauseSeconds);
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Canvas.SetLeft(textBlock, 0);
                StartMarquee(textBlock, canvas);
            };
            timer.Start();
        };
        storyboard.Begin();
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
