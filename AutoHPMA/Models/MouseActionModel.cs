using System.Text.Json.Serialization;
using AutoHPMA.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace AutoHPMA.Models;

public partial class MouseActionModel : ObservableObject
{
    [ObservableProperty]
    public partial MouseActionType ActionType { get; set; } = MouseActionType.Click;

    [ObservableProperty]
    public partial string Description { get; set; } = "动作";

    [ObservableProperty]
    public partial double X { get; set; } = 200;

    [ObservableProperty]
    public partial double Y { get; set; } = 200;

    [ObservableProperty]
    public partial double EndX { get; set; } = 400;

    [ObservableProperty]
    public partial double EndY { get; set; } = 400;

    [ObservableProperty]
    public partial double Duration { get; set; } = 500;

    [ObservableProperty]
    public partial double Interval { get; set; } = 500;

    [ObservableProperty]
    public partial double Times { get; set; } = 1;

    public MouseActionModel()
    {
    }

    public MouseActionModel(MouseActionType actionType, string description = "动作")
    {
        ActionType = actionType;
        Description = description == "动作"
            ? actionType switch
            {
                MouseActionType.Click => "点击动作",
                MouseActionType.Drag => "拖拽动作",
                MouseActionType.LongPress => "长按动作",
                _ => "动作",
            }
            : description;
    }

    [JsonIgnore]
    public string ActionTypeName => ActionType switch
    {
        MouseActionType.Click => "点击",
        MouseActionType.Drag => "拖拽",
        MouseActionType.LongPress => "长按",
        _ => "未知",
    };

    [JsonIgnore]
    public string ParameterSummary => ActionType switch
    {
        MouseActionType.Click => $"({Coordinate(X)},{Coordinate(Y)}) @{NonNegative(Interval, 60_000)}ms ×{Positive(Times, 1_000)}",
        MouseActionType.Drag => $"({Coordinate(X)},{Coordinate(Y)})→({Coordinate(EndX)},{Coordinate(EndY)}) {NonNegative(Duration, 60_000)}ms @{NonNegative(Interval, 60_000)}ms ×{Positive(Times, 1_000)}",
        MouseActionType.LongPress => $"({Coordinate(X)},{Coordinate(Y)}) {NonNegative(Duration, 60_000)}ms @{NonNegative(Interval, 60_000)}ms ×{Positive(Times, 1_000)}",
        _ => string.Empty,
    };

    [JsonIgnore]
    public Brush ActionForeground => ActionType switch
    {
        MouseActionType.Click => new SolidColorBrush(ColorHelper.FromArgb(255, 59, 130, 246)),
        MouseActionType.Drag => new SolidColorBrush(ColorHelper.FromArgb(255, 34, 197, 94)),
        MouseActionType.LongPress => new SolidColorBrush(ColorHelper.FromArgb(255, 249, 115, 22)),
        _ => new SolidColorBrush(Colors.Gray),
    };

    [JsonIgnore]
    public Brush ActionBackground => ActionType switch
    {
        MouseActionType.Click => new SolidColorBrush(ColorHelper.FromArgb(31, 59, 130, 246)),
        MouseActionType.Drag => new SolidColorBrush(ColorHelper.FromArgb(31, 34, 197, 94)),
        MouseActionType.LongPress => new SolidColorBrush(ColorHelper.FromArgb(31, 249, 115, 22)),
        _ => new SolidColorBrush(ColorHelper.FromArgb(31, 128, 128, 128)),
    };

    [JsonIgnore]
    public Visibility EndPointVisibility =>
        ActionType == MouseActionType.Drag ? Visibility.Visible : Visibility.Collapsed;

    [JsonIgnore]
    public Visibility DurationVisibility =>
        ActionType is MouseActionType.Drag or MouseActionType.LongPress ? Visibility.Visible : Visibility.Collapsed;

    public MouseActionOptions ToOptions()
        => new()
        {
            ActionType = ActionType,
            X = Coordinate(X),
            Y = Coordinate(Y),
            EndX = Coordinate(EndX),
            EndY = Coordinate(EndY),
            DurationMilliseconds = NonNegative(Duration, 60_000),
            RepeatIntervalMilliseconds = NonNegative(Interval, 60_000),
            RepeatCount = Positive(Times, 1_000),
        };

    public static MouseActionModel CreateClick(int x, int y, int interval = 500, int times = 1)
        => new(MouseActionType.Click)
        {
            X = x,
            Y = y,
            Interval = interval,
            Times = times,
        };

    public static MouseActionModel CreateDrag(int startX, int startY, int endX, int endY, int duration = 500, int interval = 500, int times = 1)
        => new(MouseActionType.Drag)
        {
            X = startX,
            Y = startY,
            EndX = endX,
            EndY = endY,
            Duration = duration,
            Interval = interval,
            Times = times,
        };

    public static MouseActionModel CreateLongPress(int x, int y, int duration = 1000, int interval = 500, int times = 1)
        => new(MouseActionType.LongPress)
        {
            X = x,
            Y = y,
            Duration = duration,
            Interval = interval,
            Times = times,
        };

    private static int Coordinate(double value)
        => Bounded(value, 0, ushort.MaxValue);

    private static int NonNegative(double value, int max)
        => Bounded(value, 0, max);

    private static int Positive(double value, int max)
        => Bounded(value, 1, max);

    private static int Bounded(double value, int min, int max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return min;
        }

        return Math.Clamp((int)Math.Round(value), min, max);
    }

    private void NotifyActionTypePropertiesChanged()
    {
        OnPropertyChanged(nameof(ActionTypeName));
        OnPropertyChanged(nameof(ActionForeground));
        OnPropertyChanged(nameof(ActionBackground));
        OnPropertyChanged(nameof(EndPointVisibility));
        OnPropertyChanged(nameof(DurationVisibility));
        OnPropertyChanged(nameof(ParameterSummary));
    }

    private void NotifyParameterSummaryChanged()
        => OnPropertyChanged(nameof(ParameterSummary));

    partial void OnActionTypeChanged(MouseActionType value)
        => NotifyActionTypePropertiesChanged();

    partial void OnXChanged(double value)
        => NotifyParameterSummaryChanged();

    partial void OnYChanged(double value)
        => NotifyParameterSummaryChanged();

    partial void OnEndXChanged(double value)
        => NotifyParameterSummaryChanged();

    partial void OnEndYChanged(double value)
        => NotifyParameterSummaryChanged();

    partial void OnDurationChanged(double value)
        => NotifyParameterSummaryChanged();

    partial void OnIntervalChanged(double value)
        => NotifyParameterSummaryChanged();

    partial void OnTimesChanged(double value)
        => NotifyParameterSummaryChanged();
}

public sealed class MouseActionList
{
    public string Version { get; set; } = "1.0";

    public List<MouseActionModel> Actions { get; set; } = [];
}
