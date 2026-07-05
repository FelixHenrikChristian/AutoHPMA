using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AutoHPMA.Capture;
using AutoHPMA.Capture.Models;
using AutoHPMA.Capture.Native;
using AutoHPMA.Contracts.Services;
using AutoHPMA.Core.Models;
using AutoHPMA.Core.Services;
using AutoHPMA.Helpers;
using AutoHPMA.Models;
using AutoHPMA.Views.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AutoHPMA.Views.TestPages;

public sealed partial class TaskCoordinateEditorPage : Page
{
    private const int AutoSaveDelayMilliseconds = 600;
    private const int OneShotCaptureTimeoutMilliseconds = 1000;
    private const int OneShotCapturePollIntervalMilliseconds = 30;

    private readonly TaskCoordinateConfigStore _coordinateStore;
    private readonly IReadOnlyList<IGameWindowProvider> _windowProviders;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _autoSaveTimer;
    private bool _isLoadingSnapshot;
    private bool _hasPendingChanges;

    public ObservableCollection<EditableTaskRegion> Regions { get; } = [];

    public ObservableCollection<EditableTaskPoint> Points { get; } = [];

    public TaskCoordinateEditorPage()
    {
        _coordinateStore = App.GetService<TaskCoordinateConfigStore>();
        _windowProviders = App.GetService<IEnumerable<IGameWindowProvider>>().ToArray();

        InitializeComponent();

        _autoSaveTimer = DispatcherQueue.CreateTimer();
        _autoSaveTimer.Interval = TimeSpan.FromMilliseconds(AutoSaveDelayMilliseconds);
        _autoSaveTimer.Tick += AutoSaveTimer_Tick;

        RegionsListView.ItemsSource = Regions;
        PointsListView.ItemsSource = Points;
        Regions.CollectionChanged += Regions_CollectionChanged;
        Points.CollectionChanged += Points_CollectionChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Regions.Count == 0 && Points.Count == 0)
        {
            LoadSnapshot();
        }
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _coordinateStore.Reload();
            LoadSnapshot();
            ShowMessage("已重新加载任务坐标配置。", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowMessage($"重新加载失败：{ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _autoSaveTimer.Stop();
        _ = SaveConfig(showSuccess: true, isAutoSave: false);
    }

    private async void SelectFileButton_Click(object sender, RoutedEventArgs e)
    {
        _autoSaveTimer.Stop();
        if (_hasPendingChanges &&
            !SaveConfig(showSuccess: false, isAutoSave: true))
        {
            return;
        }

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(file.Path))
        {
            ShowMessage("无法读取所选配置文件路径。", InfoBarSeverity.Error);
            return;
        }

        try
        {
            _coordinateStore.LoadFrom(file.Path);
            LoadSnapshot();
            ShowMessage($"已切换任务坐标配置：{file.Path}", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowMessage($"切换配置文件失败：{ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void AutoSaveTimer_Tick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        sender.Stop();
        _ = SaveConfig(showSuccess: false, isAutoSave: true);
    }

    private void Regions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UnsubscribeRegions(e.OldItems);
        SubscribeRegions(e.NewItems);
        ScheduleAutoSave();
    }

    private void Points_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UnsubscribePoints(e.OldItems);
        SubscribePoints(e.NewItems);
        ScheduleAutoSave();
    }

    private void Coordinate_PropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        ScheduleAutoSave();

    private void ScheduleAutoSave()
    {
        if (_isLoadingSnapshot)
        {
            return;
        }

        _hasPendingChanges = true;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private bool SaveConfig(bool showSuccess, bool isAutoSave)
    {
        if (!TryBuildConfig(out var config, out var error))
        {
            ShowMessage(
                isAutoSave ? $"自动保存暂停：{error}" : error,
                InfoBarSeverity.Warning);
            return false;
        }

        try
        {
            _coordinateStore.Save(config);
            _hasPendingChanges = false;
            if (showSuccess)
            {
                LoadSnapshot();
                ShowMessage("任务坐标配置已保存，并已刷新运行时配置。", InfoBarSeverity.Success);
            }
            else if (isAutoSave &&
                     EditorInfoBar.IsOpen &&
                     EditorInfoBar.Severity == InfoBarSeverity.Warning)
            {
                EditorInfoBar.IsOpen = false;
            }

            return true;
        }
        catch (Exception ex)
        {
            ShowMessage($"{(isAutoSave ? "自动保存" : "保存")}失败：{ex.Message}", InfoBarSeverity.Error);
            return false;
        }
    }

    private bool TryBuildConfig(
        out TaskCoordinateConfig config,
        out string error)
    {
        config = new TaskCoordinateConfig();
        error = string.Empty;
        var snapshot = _coordinateStore.CreateSnapshot();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var regions = new List<TaskCoordinateRegionDefinition>();
        var points = new List<TaskCoordinatePointDefinition>();

        for (var index = 0; index < Regions.Count; index++)
        {
            if (!Regions[index].TryToDefinition(out var region, out var itemError))
            {
                error = $"第 {index + 1} 个固定区域无效：{itemError}";
                return false;
            }

            if (!ids.Add(region.Id))
            {
                error = $"坐标 ID 重复：{region.Id}";
                return false;
            }

            regions.Add(region);
        }

        for (var index = 0; index < Points.Count; index++)
        {
            if (!Points[index].TryToDefinition(out var point, out var itemError))
            {
                error = $"第 {index + 1} 个固定点无效：{itemError}";
                return false;
            }

            if (!ids.Add(point.Id))
            {
                error = $"坐标 ID 重复：{point.Id}";
                return false;
            }

            points.Add(point);
        }

        config = new TaskCoordinateConfig
        {
            CanonicalWidth = snapshot.CanonicalWidth,
            CanonicalHeight = snapshot.CanonicalHeight,
            Regions = regions,
            Points = points,
        };
        return true;
    }

    private async void AddRegionButton_Click(object sender, RoutedEventArgs e)
    {
        var selection = await SelectCoordinateAsync(TaskCoordinateSelectionMode.Region, null);
        if (selection is null)
        {
            return;
        }

        Regions.Add(new EditableTaskRegion
        {
            Id = GenerateUniqueId("region"),
            X = selection.Value.X.ToString(),
            Y = selection.Value.Y.ToString(),
            Width = selection.Value.Width.ToString(),
            Height = selection.Value.Height.ToString(),
        });
    }

    private async void EditRegionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: EditableTaskRegion editableRegion })
        {
            return;
        }

        if (!editableRegion.TryToDefinition(out var region, out var error))
        {
            ShowMessage($"区域无效：{error}", InfoBarSeverity.Warning);
            return;
        }

        var initial = new TaskCoordinateSelection(
            region.X,
            region.Y,
            region.Width,
            region.Height);
        var selection = await SelectCoordinateAsync(TaskCoordinateSelectionMode.Region, initial);
        if (selection is not null)
        {
            editableRegion.Update(selection.Value);
        }
    }

    private void DeleteRegionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EditableTaskRegion region })
        {
            Regions.Remove(region);
        }
    }

    private async void AddPointButton_Click(object sender, RoutedEventArgs e)
    {
        var selection = await SelectCoordinateAsync(TaskCoordinateSelectionMode.Point, null);
        if (selection is null)
        {
            return;
        }

        Points.Add(new EditableTaskPoint
        {
            Id = GenerateUniqueId("point"),
            X = selection.Value.X.ToString(),
            Y = selection.Value.Y.ToString(),
        });
    }

    private async void EditPointButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: EditableTaskPoint editablePoint })
        {
            return;
        }

        if (!editablePoint.TryToDefinition(out var point, out var error))
        {
            ShowMessage($"固定点无效：{error}", InfoBarSeverity.Warning);
            return;
        }

        var initial = new TaskCoordinateSelection(point.X, point.Y, 0, 0);
        var selection = await SelectCoordinateAsync(TaskCoordinateSelectionMode.Point, initial);
        if (selection is not null)
        {
            editablePoint.Update(selection.Value);
        }
    }

    private void DeletePointButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EditableTaskPoint point })
        {
            Points.Remove(point);
        }
    }

    private async Task<TaskCoordinateSelection?> SelectCoordinateAsync(
        TaskCoordinateSelectionMode mode,
        TaskCoordinateSelection? initialCanonicalCoordinate)
    {
        var capture = await CaptureCoordinateFrameAsync();
        if (capture is null)
        {
            return null;
        }

        TaskCoordinateSelection? initialFrameCoordinate = initialCanonicalCoordinate is null
            ? null
            : ToFrameCoordinate(initialCanonicalCoordinate.Value, capture.Scale, capture.Frame);
        var selectionWindow = new TaskCoordinateSelectionWindow(
            capture.Frame,
            capture.SourceName,
            mode,
            initialFrameCoordinate);
        WindowPlacementHelper.CenterOnParent(selectionWindow, App.MainWindow);

        var selectedFrameCoordinate = await selectionWindow.SelectAsync();
        return selectedFrameCoordinate is null
            ? null
            : ToCanonicalCoordinate(selectedFrameCoordinate.Value, capture.Scale, mode);
    }

    private async Task<CoordinateFrame?> CaptureCoordinateFrameAsync()
    {
        var target = LocateTarget();
        if (target is null)
        {
            ShowMessage("未找到游戏窗口，请先启动游戏后再试。", InfoBarSeverity.Warning);
            return null;
        }

        if (!NativeMethods.GetWindowRect(
                target.GameWindow.Handle,
                out var gameRect) ||
            gameRect.Width <= 0)
        {
            ShowMessage("无法读取当前游戏窗口尺寸。", InfoBarSeverity.Error);
            return null;
        }

        var scale = gameRect.Width / (double)_coordinateStore.CanonicalWidth;
        if (scale <= 0)
        {
            ShowMessage("当前游戏窗口坐标缩放无效。", InfoBarSeverity.Error);
            return null;
        }

        try
        {
            var frame = await CaptureOneShotFrameAsync(target);
            if (frame is null)
            {
                ShowMessage("未获取到游戏截图，请稍后重试。", InfoBarSeverity.Warning);
                return null;
            }

            return new CoordinateFrame(frame, scale, target.DisplayName);
        }
        catch (Exception ex)
        {
            ShowMessage($"截图失败：{ex.Message}", InfoBarSeverity.Error);
            return null;
        }
    }

    private GameWindowTarget? LocateTarget()
    {
        foreach (var provider in _windowProviders)
        {
            GameWindowTarget? target;
            try
            {
                target = provider.TryLocate();
            }
            catch
            {
                continue;
            }

            if (target is not null)
            {
                return target;
            }
        }

        return null;
    }

    private static async Task<CapturedFrame?> CaptureOneShotFrameAsync(GameWindowTarget target)
    {
        if (!WindowsGraphicsCapture.IsSupported)
        {
            throw new PlatformNotSupportedException("当前系统不支持 Windows Graphics Capture。");
        }

        using var capture = ScreenCaptureFactory.Create(CaptureMethod.WindowsGraphicsCapture);
        capture.Start(target.CaptureHandle);

        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(OneShotCaptureTimeoutMilliseconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var frame = capture.TryGetFrame();
            if (frame is not null)
            {
                return frame;
            }

            await Task.Delay(OneShotCapturePollIntervalMilliseconds);
        }

        return capture.TryGetFrame();
    }

    private sealed record CoordinateFrame(
        CapturedFrame Frame,
        double Scale,
        string SourceName);

    private TaskCoordinateSelection ToCanonicalCoordinate(
        TaskCoordinateSelection frameCoordinate,
        double scale,
        TaskCoordinateSelectionMode mode)
    {
        var canonicalWidth = _coordinateStore.CanonicalWidth;
        var canonicalHeight = _coordinateStore.CanonicalHeight;
        var x = Math.Clamp(
            (int)Math.Round(frameCoordinate.X / scale),
            0,
            canonicalWidth - 1);
        var y = Math.Clamp(
            (int)Math.Round(frameCoordinate.Y / scale),
            0,
            canonicalHeight - 1);
        if (mode == TaskCoordinateSelectionMode.Point)
        {
            return new TaskCoordinateSelection(x, y, 0, 0);
        }

        var right = Math.Clamp(
            (int)Math.Round((frameCoordinate.X + frameCoordinate.Width) / scale),
            x + 1,
            canonicalWidth);
        var bottom = Math.Clamp(
            (int)Math.Round((frameCoordinate.Y + frameCoordinate.Height) / scale),
            y + 1,
            canonicalHeight);
        return new TaskCoordinateSelection(x, y, right - x, bottom - y);
    }

    private static TaskCoordinateSelection ToFrameCoordinate(
        TaskCoordinateSelection canonicalCoordinate,
        double scale,
        CapturedFrame frame)
    {
        var x = Math.Clamp(
            (int)Math.Round(canonicalCoordinate.X * scale),
            0,
            frame.Width - 1);
        var y = Math.Clamp(
            (int)Math.Round(canonicalCoordinate.Y * scale),
            0,
            frame.Height - 1);
        if (canonicalCoordinate.Width <= 0 || canonicalCoordinate.Height <= 0)
        {
            return new TaskCoordinateSelection(x, y, 0, 0);
        }

        var right = Math.Clamp(
            (int)Math.Round((canonicalCoordinate.X + canonicalCoordinate.Width) * scale),
            x + 1,
            frame.Width);
        var bottom = Math.Clamp(
            (int)Math.Round((canonicalCoordinate.Y + canonicalCoordinate.Height) * scale),
            y + 1,
            frame.Height);
        return new TaskCoordinateSelection(x, y, right - x, bottom - y);
    }

    private void LoadSnapshot()
    {
        _isLoadingSnapshot = true;
        try
        {
            foreach (var region in Regions)
            {
                region.PropertyChanged -= Coordinate_PropertyChanged;
            }

            foreach (var point in Points)
            {
                point.PropertyChanged -= Coordinate_PropertyChanged;
            }

            var snapshot = _coordinateStore.CreateSnapshot();
            ConfigFileText.Text = _coordinateStore.SourcePath;
            ToolTipService.SetToolTip(ConfigFileText, _coordinateStore.SourcePath);
            CanonicalSizeText.Text = $"{snapshot.CanonicalWidth} x {snapshot.CanonicalHeight}";

            Regions.Clear();
            foreach (var region in snapshot.Regions)
            {
                Regions.Add(EditableTaskRegion.FromDefinition(region));
            }

            Points.Clear();
            foreach (var point in snapshot.Points)
            {
                Points.Add(EditableTaskPoint.FromDefinition(point));
            }

            _hasPendingChanges = false;
        }
        finally
        {
            _isLoadingSnapshot = false;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _autoSaveTimer.Stop();
    }

    private void SubscribeRegions(System.Collections.IList? regions)
    {
        if (regions is null)
        {
            return;
        }

        foreach (EditableTaskRegion region in regions)
        {
            region.PropertyChanged += Coordinate_PropertyChanged;
        }
    }

    private void UnsubscribeRegions(System.Collections.IList? regions)
    {
        if (regions is null)
        {
            return;
        }

        foreach (EditableTaskRegion region in regions)
        {
            region.PropertyChanged -= Coordinate_PropertyChanged;
        }
    }

    private void SubscribePoints(System.Collections.IList? points)
    {
        if (points is null)
        {
            return;
        }

        foreach (EditableTaskPoint point in points)
        {
            point.PropertyChanged += Coordinate_PropertyChanged;
        }
    }

    private void UnsubscribePoints(System.Collections.IList? points)
    {
        if (points is null)
        {
            return;
        }

        foreach (EditableTaskPoint point in points)
        {
            point.PropertyChanged -= Coordinate_PropertyChanged;
        }
    }

    private string GenerateUniqueId(string prefix)
    {
        var ids = Regions
            .Select(region => region.Id)
            .Concat(Points.Select(point => point.Id))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < 10000; index++)
        {
            var candidate = $"{prefix}-{index}";
            if (!ids.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{prefix}-{DateTimeOffset.Now:yyyyMMddHHmmss}";
    }

    private void ShowMessage(string message, InfoBarSeverity severity)
    {
        EditorInfoBar.Message = message;
        EditorInfoBar.Severity = severity;
        EditorInfoBar.IsOpen = false;
        EditorInfoBar.IsOpen = true;
    }

    public sealed class EditableTaskRegion : EditableCoordinateBase
    {
        private string _width = "100";
        private string _height = "80";

        public string Width
        {
            get => _width;
            set => SetProperty(ref _width, value);
        }

        public string Height
        {
            get => _height;
            set => SetProperty(ref _height, value);
        }

        public static EditableTaskRegion FromDefinition(TaskCoordinateRegionDefinition region) =>
            new()
            {
                Id = region.Id,
                X = region.X.ToString(),
                Y = region.Y.ToString(),
                Width = region.Width.ToString(),
                Height = region.Height.ToString(),
            };

        public void Update(TaskCoordinateSelection selection)
        {
            X = selection.X.ToString();
            Y = selection.Y.ToString();
            Width = selection.Width.ToString();
            Height = selection.Height.ToString();
        }

        public bool TryToDefinition(
            out TaskCoordinateRegionDefinition region,
            out string error)
        {
            region = new TaskCoordinateRegionDefinition();
            if (!TryReadBase(out var id, out var x, out var y, out error) ||
                !TryParseCoordinate(Width, "宽", allowZero: false, out var width, out error) ||
                !TryParseCoordinate(Height, "高", allowZero: false, out var height, out error))
            {
                return false;
            }

            region = new TaskCoordinateRegionDefinition
            {
                Id = id,
                X = x,
                Y = y,
                Width = width,
                Height = height,
            };
            return true;
        }
    }

    public sealed class EditableTaskPoint : EditableCoordinateBase
    {
        public static EditableTaskPoint FromDefinition(TaskCoordinatePointDefinition point) =>
            new()
            {
                Id = point.Id,
                X = point.X.ToString(),
                Y = point.Y.ToString(),
            };

        public void Update(TaskCoordinateSelection selection)
        {
            X = selection.X.ToString();
            Y = selection.Y.ToString();
        }

        public bool TryToDefinition(
            out TaskCoordinatePointDefinition point,
            out string error)
        {
            point = new TaskCoordinatePointDefinition();
            if (!TryReadBase(out var id, out var x, out var y, out error))
            {
                return false;
            }

            point = new TaskCoordinatePointDefinition
            {
                Id = id,
                X = x,
                Y = y,
            };
            return true;
        }
    }

    public abstract class EditableCoordinateBase : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _x = "0";
        private string _y = "0";

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public string X
        {
            get => _x;
            set => SetProperty(ref _x, value);
        }

        public string Y
        {
            get => _y;
            set => SetProperty(ref _y, value);
        }

        protected bool TryReadBase(
            out string id,
            out int x,
            out int y,
            out string error)
        {
            id = Id.Trim();
            x = 0;
            y = 0;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
                error = "ID 不能为空";
                return false;
            }

            return TryParseCoordinate(X, "X", allowZero: true, out x, out error) &&
                TryParseCoordinate(Y, "Y", allowZero: true, out y, out error);
        }

        protected void SetProperty<T>(
            ref T field,
            T value,
            [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected static bool TryParseCoordinate(
            string text,
            string label,
            bool allowZero,
            out int value,
            out string error)
        {
            value = 0;
            error = string.Empty;
            if (!int.TryParse(text, out value))
            {
                error = $"{label} 必须是整数";
                return false;
            }

            if (value < 0 || (!allowZero && value == 0))
            {
                error = allowZero ? $"{label} 不能小于 0" : $"{label} 必须大于 0";
                return false;
            }

            return true;
        }
    }
}
