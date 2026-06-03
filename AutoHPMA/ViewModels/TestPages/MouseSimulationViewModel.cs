using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using AutoHPMA.Capture;
using AutoHPMA.Capture.Models;
using AutoHPMA.Contracts.Services;
using AutoHPMA.Core.Contracts.Services;
using AutoHPMA.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AutoHPMA.ViewModels.TestPages;

public partial class MouseSimulationViewModel : ObservableObject
{
    private const int PreExecutionDelayMilliseconds = 3000;

    private readonly IWindowInteractionService _windowInteractionService;
    private readonly IInfoBarNotificationService _infoBar;

    public MouseSimulationViewModel(
        IWindowInteractionService windowInteractionService,
        IInfoBarNotificationService infoBar)
    {
        _windowInteractionService = windowInteractionService;
        _infoBar = infoBar;

        AvailableWindows = [];
        AvailableChildWindows = [];
        MouseActions = [];
        MouseActions.CollectionChanged += OnMouseActionsChanged;

        RefreshWindowList();
        MouseActions.Add(MouseActionModel.CreateClick(200, 200));
    }

    public ObservableCollection<WindowInfo> AvailableWindows { get; }

    public ObservableCollection<WindowInfo> AvailableChildWindows { get; }

    public ObservableCollection<MouseActionModel> MouseActions { get; }

    public Visibility ChildWindowsVisibility =>
        HasChildWindows ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EditorVisibility =>
        SelectedMouseAction is null ? Visibility.Collapsed : Visibility.Visible;

    [ObservableProperty]
    public partial WindowInfo? SelectedClickWindow { get; set; }

    [ObservableProperty]
    public partial WindowInfo? SelectedClickChildWindow { get; set; }

    [ObservableProperty]
    public partial bool HasChildWindows { get; set; }

    [ObservableProperty]
    public partial MouseActionModel? SelectedMouseAction { get; set; }

    [ObservableProperty]
    public partial bool IsExecuting { get; set; }

    [RelayCommand]
    private void RefreshWindowList()
    {
        var previousParentHandle = SelectedClickWindow?.Handle ?? IntPtr.Zero;
        var previousChildHandle = SelectedClickChildWindow?.Handle ?? IntPtr.Zero;

        AvailableWindows.Clear();
        AvailableChildWindows.Clear();
        SelectedClickChildWindow = null;
        HasChildWindows = false;

        foreach (var window in WindowEnumerator.EnumerateVisibleWindows())
        {
            AvailableWindows.Add(window);
        }

        SelectedClickWindow = previousParentHandle == IntPtr.Zero
            ? null
            : AvailableWindows.FirstOrDefault(w => w.Handle == previousParentHandle);

        if (previousChildHandle != IntPtr.Zero)
        {
            SelectedClickChildWindow = AvailableChildWindows.FirstOrDefault(w => w.Handle == previousChildHandle);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteAllActions))]
    private async Task ExecuteAllActionsAsync()
    {
        if (SelectedClickWindow is null || SelectedClickWindow.Handle == IntPtr.Zero)
        {
            ShowError("请先选择要执行的窗口");
            return;
        }

        if (MouseActions.Count == 0)
        {
            ShowError("操作列表为空，请先添加动作");
            return;
        }

        var targetHwnd = GetEffectiveClickWindowHandle();
        if (targetHwnd == IntPtr.Zero)
        {
            ShowError("目标窗口句柄无效");
            return;
        }

        IsExecuting = true;

        try
        {
            _ = _windowInteractionService.TrySetForegroundWindow(SelectedClickWindow.Handle);
            await Task.Delay(PreExecutionDelayMilliseconds);

            for (var i = 0; i < MouseActions.Count; i++)
            {
                var action = MouseActions[i];
                await _windowInteractionService.ExecuteAsync(targetHwnd, action.ToOptions());

                if (i < MouseActions.Count - 1)
                {
                    await Task.Delay(action.ToOptions().RepeatIntervalMilliseconds);
                }
            }

            ShowSuccess($"已完成 {MouseActions.Count} 个动作");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"执行鼠标动作失败：{ex}");
            ShowError($"执行失败：{ex.Message}");
        }
        finally
        {
            IsExecuting = false;
        }
    }

    [RelayCommand]
    public void AddClickAction()
    {
        var action = MouseActionModel.CreateClick(200, 200);
        MouseActions.Add(action);
        SelectedMouseAction = action;
    }

    [RelayCommand]
    public void AddDragAction()
    {
        var action = MouseActionModel.CreateDrag(200, 200, 400, 400);
        MouseActions.Add(action);
        SelectedMouseAction = action;
    }

    [RelayCommand]
    public void AddLongPressAction()
    {
        var action = MouseActionModel.CreateLongPress(200, 200);
        MouseActions.Add(action);
        SelectedMouseAction = action;
    }

    [RelayCommand]
    public void RemoveMouseAction(MouseActionModel? action)
    {
        if (action is null)
        {
            return;
        }

        var removedSelected = ReferenceEquals(SelectedMouseAction, action);
        _ = MouseActions.Remove(action);
        if (removedSelected)
        {
            SelectedMouseAction = null;
        }
    }

    [RelayCommand]
    public void MoveActionUp(MouseActionModel? action)
    {
        if (action is null)
        {
            return;
        }

        var index = MouseActions.IndexOf(action);
        if (index > 0)
        {
            MouseActions.Move(index, index - 1);
        }
    }

    [RelayCommand]
    public void MoveActionDown(MouseActionModel? action)
    {
        if (action is null)
        {
            return;
        }

        var index = MouseActions.IndexOf(action);
        if (index >= 0 && index < MouseActions.Count - 1)
        {
            MouseActions.Move(index, index + 1);
        }
    }

    [RelayCommand]
    public void ClearAllActions()
    {
        MouseActions.Clear();
        SelectedMouseAction = null;
    }

    [RelayCommand]
    public void ClearSelection()
    {
        SelectedMouseAction = null;
    }

    [RelayCommand]
    private async Task ExportActionsAsync()
    {
        if (MouseActions.Count == 0)
        {
            ShowError("操作列表为空，无法导出");
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "mouse_actions",
        };
        picker.FileTypeChoices.Add("JSON 文件", [".json"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            var actionList = new MouseActionList
            {
                Version = "1.0",
                Actions = MouseActions.ToList(),
            };
            var json = JsonSerializer.Serialize(actionList, CreateJsonOptions());
            await FileIO.WriteTextAsync(file, json);
            ShowSuccess($"已成功导出 {MouseActions.Count} 个动作");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"导出鼠标动作失败：{ex}");
            ShowError($"导出失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ImportActionsAsync()
    {
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

        try
        {
            var json = await FileIO.ReadTextAsync(file);
            var actionList = JsonSerializer.Deserialize<MouseActionList>(json, CreateJsonOptions());

            if (actionList?.Actions is null || actionList.Actions.Count == 0)
            {
                ShowError("文件中没有有效的动作数据");
                return;
            }

            MouseActions.Clear();
            foreach (var action in actionList.Actions)
            {
                MouseActions.Add(action);
            }

            SelectedMouseAction = null;
            ShowSuccess($"已成功导入 {MouseActions.Count} 个动作");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"导入鼠标动作失败：{ex}");
            ShowError($"导入失败：{ex.Message}");
        }
    }

    private bool CanExecuteAllActions()
        => !IsExecuting && SelectedClickWindow is not null && MouseActions.Count > 0;

    private IntPtr GetEffectiveClickWindowHandle()
        => SelectedClickChildWindow?.Handle ?? SelectedClickWindow?.Handle ?? IntPtr.Zero;

    private void RefreshChildWindowList()
    {
        var previousChildHandle = SelectedClickChildWindow?.Handle ?? IntPtr.Zero;
        AvailableChildWindows.Clear();
        SelectedClickChildWindow = null;

        if (SelectedClickWindow is null)
        {
            HasChildWindows = false;
            return;
        }

        foreach (var childWindow in WindowEnumerator.EnumerateChildWindows(SelectedClickWindow))
        {
            AvailableChildWindows.Add(childWindow);
        }

        HasChildWindows = AvailableChildWindows.Count > 0;
        SelectedClickChildWindow = AvailableChildWindows.FirstOrDefault(w => w.Handle == previousChildHandle);
    }

    private void ShowError(string message)
        => _infoBar.Show(InfoBarSeverity.Error, "错误", message);

    private void ShowSuccess(string message)
        => _infoBar.Show(InfoBarSeverity.Success, "成功", message);

    private static JsonSerializerOptions CreateJsonOptions()
        => new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

    private void OnMouseActionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => ExecuteAllActionsCommand.NotifyCanExecuteChanged();

    partial void OnSelectedClickWindowChanged(WindowInfo? value)
    {
        RefreshChildWindowList();
        ExecuteAllActionsCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasChildWindowsChanged(bool value)
        => OnPropertyChanged(nameof(ChildWindowsVisibility));

    partial void OnSelectedMouseActionChanged(MouseActionModel? value)
        => OnPropertyChanged(nameof(EditorVisibility));

    partial void OnIsExecutingChanged(bool value)
        => ExecuteAllActionsCommand.NotifyCanExecuteChanged();
}
