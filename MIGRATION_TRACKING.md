# AutoHPMA WinUI 3 Migration Tracking

本文档用于跟踪 `AutoHPMA-old` WPF 项目向 `AutoHPMA` WinUI 3 项目的迁移进度。后续每次迁移完成一个阶段后，请更新对应状态、记录主要文件和验证结果，方便在新的对话中继续。

## 迁移原则

- UI 可以参考旧版布局和交互，但必须使用 WinUI 3 / Windows App SDK 原生控件实现，避免引入 WPF UI 控件或 WPF 依赖。
- 功能完整性优先；迁移时允许把旧项目中过于耦合的逻辑拆成服务、模型、ViewModel 和 WinUI 页面。
- 每个阶段保持可构建、可人工验证；用户使用 Visual Studio 构建测试后再提交和推送。
- 涉及窗口句柄、截图、OCR、任务执行、全局热键、通知、更新等功能时，优先迁移可测试的基础服务，再接入 UI。
- 每步完成后更新本文档的状态、文件清单、验证结果和 commit message。

## 当前快照

- `AutoHPMA` 已是 WinUI 3 / Windows App SDK 项目，包含 Template Studio 风格的 Shell、Navigation、Settings、Logging、Hotkey、Update、Capture/Test 等基础结构。
- `AutoHPMA-old` 保留完整 WPF 项目，核心功能包括 Dashboard、Task、Log、Hotkey、Notification、Settings、Update、截图预览、遮罩窗口、任务运行时、OCR、图像处理和任务资源。
- 当前新项目中 `HomePage.xaml` 已恢复基础主流程入口；`TaskPage.xaml` 仍为空页面，需要继续恢复任务控制。
- 当前新项目已存在未提交改动，主要涉及 `App.xaml.cs`、`AutoHPMA.csproj`、Capture 目录、截图测试页、设置页、更新窗口和 `CapturePreviewWindow`。迁移时不要回退这些改动。

## 状态标记

- `[todo]` 未开始。
- `[doing]` 正在迁移。
- `[review]` 等待 Visual Studio 构建或人工验证。
- `[done]` 已完成并验证。
- `[blocked]` 受阻，需要补充信息或先完成依赖步骤。

## 分阶段计划

### 0. 建立迁移跟踪

状态：`[review]`

目标：
- 新增本迁移跟踪文档。
- 明确阶段边界、验收方式和提交消息格式。

主要文件：
- `MIGRATION_TRACKING.md`

验收点：
- 文档存在于 `AutoHPMA` 项目根目录。
- 后续对话能通过阅读本文档恢复迁移上下文。

Commit message：

```text
docs: 添加 WinUI 迁移跟踪文档

- 记录 WPF 到 WinUI 3 的分阶段迁移计划
- 约定每步状态、验收点和提交消息记录方式
```

### 1. 基线梳理与依赖收口

状态：`[doing]`

目标：
- 对比新旧项目的 NuGet 依赖、资产、配置文件和启动流程。
- 确认 WinUI 项目只保留 WinUI 可用依赖，补齐 OpenCV、OCR、ONNX、Excel、Vanara、Toast 等功能依赖。
- 把旧项目 `AppSettings` 能力映射到新项目 `ILocalSettingsService` / `SettingsKeys`，避免保留 WPF `Properties.Settings` 或 WPF UI 配置。

主要文件候选：
- `AutoHPMA/AutoHPMA.csproj`
- `AutoHPMA/App.xaml.cs`
- `AutoHPMA/Configuration/SettingsKeys.cs`
- `AutoHPMA/Services/LocalSettingsService.cs`
- `AutoHPMA-old/AutoHPMA/Config/AppSettings.cs`

验收点：
- Visual Studio 能还原依赖并构建当前 WinUI 项目。
- 新项目无新增 WPF UI 包引用。
- 旧设置项已列出迁移映射，未迁移项有明确 TODO。

Commit message：

```text
chore: 收口 WinUI 迁移基础依赖

- 对齐旧项目功能依赖和 WinUI 项目包引用
- 建立旧设置项到新本地设置服务的映射
```

### 2. 共享 WinUI 控件与页面布局规范

状态：`[todo]`

目标：
- 用 WinUI 原生控件重建旧版 `SettingRow`、任务卡片头部、卡片/行样式。
- 统一页面边距、标题、图标、分组、Expander 行为和响应式布局。
- 修正迁移页面中的中文文案、资源路径和 XAML 绑定方式。

主要文件候选：
- `AutoHPMA/Views/Controls/*`
- `AutoHPMA/Styles/*.xaml`
- `AutoHPMA/Views/SettingsPage.xaml`
- `AutoHPMA/Views/TaskPage.xaml`

验收点：
- Settings、Task、Home 等页面可复用同一套 WinUI 行控件。
- 页面不依赖 WPF `ui:*` 命名空间。
- 文案在 Visual Studio 设计器和运行时显示正常。

Commit message：

```text
feat: 重建 WinUI 页面通用控件

- 使用 WinUI 原生控件实现设置行和任务卡片结构
- 统一迁移页面的布局、图标和资源引用方式
```

### 3. Home/Dashboard 主流程迁移

状态：`[todo]`

目标：
- 将旧 `DashboardPage` 迁移为新 `HomePage`。
- 恢复启动/停止主流程、游戏窗口上下文、状态监测间隔、OCR 引擎选择、日志窗口开关、遮罩窗口开关等功能。
- 将旧 `DashboardViewModel` 中的 WPF 依赖改为 WinUI 服务、Messenger 或 DispatcherQueue。

主要文件候选：
- `AutoHPMA/Views/HomePage.xaml`
- `AutoHPMA/ViewModels/HomeViewModel.cs`
- `AutoHPMA-old/AutoHPMA/Views/Pages/DashboardPage.xaml`
- `AutoHPMA-old/AutoHPMA/ViewModels/Pages/DashboardViewModel.cs`
- `AutoHPMA-old/AutoHPMA/Services/AppContextService.cs`

验收点：
- 首页不再是空页面。
- 启动按钮状态能随运行状态变化。
- 相关设置能保存并在重启后恢复。

Commit message：

```text
feat: 迁移首页主流程控制

- 用 WinUI 控件还原 Dashboard 启动和运行状态配置
- 接入本地设置保存 OCR、日志窗口和遮罩窗口选项
```

本轮状态更新（2026-05-13）：
主要改动：
- 新增 `IAppContextService` / `AppContextService`，保存 `displayHwnd`、`gameHwnd`、状态监测间隔、OCR 引擎和当前截图器。
- 扩展 WinUI 侧窗口枚举能力，支持按进程检测 MuMu/官方客户端并查找 MuMu 子窗口。
- 将 `HomePage` 从空页面迁移为 WinUI 原生控件主流程入口，支持窗口检测、启动/停止截图器、运行选项和辅助窗口偏好保存。
验证结果：
- CLI 构建：`dotnet build .\AutoHPMA\AutoHPMA.csproj -p:Platform=x64` 通过。
- 人工验证：待 Visual Studio 启动后验证首页导航、窗口检测、启动/停止和设置重启恢复。
遗留问题：
- 悬浮日志窗口和遮罩窗口实体尚未迁移，当前首页只保存对应偏好并建立上下文。
- Task 页面仍为空，任务运行时尚未接入首页上下文。

### 4. 日志、InfoBar 与悬浮日志窗口

状态：`[todo]`

目标：
- 完成 UI 日志列表、文件日志、诊断模式和全局通知提示。
- 迁移旧 `LogWindow` 悬浮窗口能力，必要时使用 WinUIEx/WindowEx 和 Win32 句柄辅助实现。
- 保持 Serilog sink 与新 `LogPage` / `InfoBarNotificationService` 解耦。

主要文件候选：
- `AutoHPMA/Services/Logging/*`
- `AutoHPMA/Views/LogPage.xaml`
- `AutoHPMA/ViewModels/LogViewModel.cs`
- `AutoHPMA/Contracts/Services/IInfoBarNotificationService.cs`
- `AutoHPMA-old/AutoHPMA/Views/Windows/LogWindow.xaml`
- `AutoHPMA-old/AutoHPMA/Helpers/LogHelper/*`

验收点：
- 日志页能实时显示运行日志。
- 文件日志位置可打开。
- 悬浮日志窗口能跟随开关显示/隐藏。

Commit message：

```text
feat: 迁移日志显示与通知提示

- 接入 WinUI 日志页、文件日志和诊断模式
- 恢复全局提示与悬浮日志窗口显示逻辑
```

### 5. 窗口枚举、截图与预览

状态：`[review]`

目标：
- 完成 BitBlt、PrintWindow、Windows Graphics Capture 等截图路径。
- 迁移窗口枚举、截图预览窗口、截图测试页和捕获方法选择。
- 明确 `gameHwnd`、`displayHwnd`、捕获器生命周期和资源释放边界。

主要文件候选：
- `AutoHPMA/Capture/*`
- `AutoHPMA/Views/TestPages/ScreenshotTestPage.xaml`
- `AutoHPMA/ViewModels/TestPages/ScreenshotTestViewModel.cs`
- `AutoHPMA/Views/Windows/CapturePreviewWindow.xaml`
- `AutoHPMA-old/AutoHPMA/Helpers/CaptureHelper/*`
- `AutoHPMA-old/AutoHPMA/Views/Windows/ScreenshotPreviewWindow.xaml`

验收点：
- 能列出目标窗口。
- 至少一种截图方式可成功预览。
- 切换截图方式不会泄漏或锁死捕获资源。

Commit message：

```text
feat: 迁移窗口截图与预览流程

- 接入 WinUI 截图测试页和捕获预览窗口
- 统一窗口枚举、截图方法选择和捕获资源释放
```

完成时间：2026-05-13
主要改动：
- 接入 BitBlt、PrintWindow、Windows Graphics Capture 三种 WinUI 可用截图路径。
- 新增截图测试页 ViewModel、窗口枚举、截图方式选择和 WinUI 预览窗口。
- 捕获预览窗口支持实时显示、保存当前帧，并在窗口关闭时释放计时器、位图和捕获器。
验证结果：
- CLI 构建：`dotnet build .\AutoHPMA\AutoHPMA.csproj -p:Platform=x64` 通过。
- 人工验证：待 Visual Studio 验证窗口列表刷新、三种截图方式预览、保存当前帧和关闭释放。
遗留问题：
- Windows Graphics Capture、BitBlt、PrintWindow 的实际兼容性需在目标游戏窗口/MuMu 窗口上人工确认。
- 构建仍有既有 MVVMTK0045 和 XAML `Icon` 过时警告，未在本步骤处理。
实际 commit message：
```text
feat: 迁移首页主流程与截图上下文

- 接入 WinUI 首页启动/停止、窗口探测和运行设置保存
- 补齐应用上下文服务并复用截图捕获器生命周期
- 将截图预览阶段推进到可构建验证状态
```

### 6. OCR 与图像处理基础能力

状态：`[todo]`

目标：
- 迁移 RapidOCR/PaddleOCR、Tesseract、Windows OCR、模板匹配、颜色过滤、轮廓检测等基础能力。
- 把 WPF BitmapSource 相关代码改为 WinUI/WinRT/Skia/OpenCvSharp 可用的数据结构。
- 补齐 OCR 模型、任务图片、题库等资源复制配置。

主要文件候选：
- `AutoHPMA/Helpers/RecognizeHelper/*`
- `AutoHPMA/Helpers/ImageHelper/*`
- `AutoHPMA/Views/TestPages/*`
- `AutoHPMA-old/AutoHPMA/Assets/Models/OCR/*`
- `AutoHPMA-old/AutoHPMA/Assets/Tasks/*`

验收点：
- OCR 测试页能加载图片并返回识别结果。
- 模板匹配、颜色过滤、轮廓检测测试页能运行。
- 所需模型和测试资源在输出目录中存在。

Commit message：

```text
feat: 迁移 OCR 和图像处理能力

- 接入 WinUI 可用的 OCR、模板匹配和图像处理流程
- 补齐模型与任务资源的输出复制配置
```

### 7. 任务运行时与窗口交互

状态：`[todo]`

目标：
- 迁移 `IGameTask`、`BaseGameTask`、鼠标键盘模拟、窗口定位和任务取消机制。
- 去除 `Application.Current.Dispatcher`、WPF MessageBox、WPF Window 等依赖。
- 统一任务启动、停止、完成事件、错误提示和日志输出。

主要文件候选：
- `AutoHPMA/GameTask/*`
- `AutoHPMA/Helpers/WindowInteractionHelper.cs`
- `AutoHPMA/Services/AppContextService.cs`
- `AutoHPMA-old/AutoHPMA/GameTask/*`
- `AutoHPMA-old/AutoHPMA/Helpers/WindowInteractionHelper.cs`

验收点：
- 空任务或轻量测试任务能启动、停止并写入日志。
- 任务完成后 UI 状态能回到空闲。
- 任务错误不会让 WinUI 主窗口崩溃。

Commit message：

```text
feat: 迁移自动化任务运行时

- 重构任务启动、停止和完成事件到 WinUI 服务模型
- 替换 WPF Dispatcher、窗口和消息框依赖
```

### 8. Task 页面与常驻任务

状态：`[todo]`

目标：
- 迁移 `TaskPage` 和 `TaskViewModel`。
- 恢复自动社团答题、自动禁林探索、自动巫师烹饪的配置项、任务启动/停止和状态显示。
- 迁移 `CookingConfigService`、题库目录打开和菜品配置加载。

主要文件候选：
- `AutoHPMA/Views/TaskPage.xaml`
- `AutoHPMA/ViewModels/TaskViewModel.cs`
- `AutoHPMA/GameTask/Permanent/*`
- `AutoHPMA/Services/CookingConfigService.cs`
- `AutoHPMA-old/AutoHPMA/Views/Pages/TaskPage.xaml`
- `AutoHPMA-old/AutoHPMA/GameTask/Permanent/*`

验收点：
- 任务页不再为空。
- 同一时间只允许一个任务运行。
- 三个常驻任务的配置能保存，启动失败时能给出 WinUI 提示。

Commit message：

```text
feat: 迁移任务页和常驻任务控制

- 用 WinUI 控件恢复任务卡片、参数配置和运行状态
- 接入社团答题、禁林探索和烹饪任务启动逻辑
```

### 9. 限时任务迁移

状态：`[todo]`

目标：
- 迁移自动甜蜜冒险和相关资源。
- 保持与常驻任务相同的运行时、日志、截图和停止机制。

主要文件候选：
- `AutoHPMA/GameTask/Temporary/*`
- `AutoHPMA-old/AutoHPMA/GameTask/Temporary/AutoSweetAdventure.cs`
- `AutoHPMA-old/AutoHPMA/Assets/Tasks/SweetAdventure/*`

验收点：
- 甜蜜冒险任务可从任务页启动/停止。
- 所需图片资源复制到输出目录。
- 任务状态能正确回写 UI。

Commit message：

```text
feat: 迁移限时活动任务

- 接入甜蜜冒险任务和图片资源
- 复用统一任务运行时处理启动、停止和日志状态
```

### 10. 热键、通知与系统集成

状态：`[todo]`

目标：
- 完成全局热键注册、热键设置页、任务快捷控制和停止所有任务。
- 迁移 Toast 通知能力，保留通知开关和必要的权限/清单配置。
- 检查防休眠、开机自启或其他系统集成功能是否需要从旧项目补齐。

主要文件候选：
- `AutoHPMA/Services/HotkeyService.cs`
- `AutoHPMA/Views/HotkeySettingsPage.xaml`
- `AutoHPMA/ViewModels/HotkeySettingsViewModel.cs`
- `AutoHPMA/Views/NotificationSettingsPage.xaml`
- `AutoHPMA/ViewModels/NotificationSettingsViewModel.cs`
- `AutoHPMA-old/AutoHPMA/Services/KeyboardHookManager.cs`
- `AutoHPMA-old/AutoHPMA/Helpers/ToastNotificationHelper.cs`

验收点：
- 热键设置能保存、重启后恢复。
- 全局热键能触发任务切换或停止全部任务。
- 通知开关生效，通知不会重复注册。

Commit message：

```text
feat: 迁移热键和通知系统

- 接入全局热键注册、保存和任务快捷控制
- 恢复通知开关与 Toast 通知发送流程
```

### 11. 设置、条款、更新与关于页面收尾

状态：`[todo]`

目标：
- 完成设置页所有旧功能：主题、防休眠、日志文件上限、重置设置、关于链接、版本显示。
- 将旧 `TermsOfUseWindow` 迁移为 WinUI `ContentDialog` 或独立窗口。
- 完成更新检查、更新窗口、下载安装/跳转逻辑和错误提示。

主要文件候选：
- `AutoHPMA/Views/SettingsPage.xaml`
- `AutoHPMA/ViewModels/SettingsViewModel.cs`
- `AutoHPMA/Views/Dialogs/TermsOfUseDialog.xaml`
- `AutoHPMA/Views/UpdateWindow.xaml`
- `AutoHPMA/Services/UpdateService.cs`
- `AutoHPMA-old/AutoHPMA/Views/Windows/TermsOfUseWindow.xaml`
- `AutoHPMA-old/AutoHPMA/Services/UpdateService.cs`

验收点：
- 首次启动条款流程可用。
- 手动检查更新能显示结果。
- 设置页所有按钮和链接可用。

Commit message：

```text
feat: 完成设置条款和更新流程迁移

- 补齐设置页、首次使用条款和关于信息
- 完善更新检查窗口、状态提示和错误处理
```

### 12. 清理、构建与回归检查

状态：`[todo]`

目标：
- 删除迁移过程中遗留的 WPF-only 代码、无用 using、死资源和重复模型。
- 统一命名空间、文件结构、资源路径和可空性警告。
- 运行 Visual Studio 构建和关键功能人工回归。

主要文件候选：
- 全项目。

验收点：
- Visual Studio 构建通过。
- 主窗口、首页、任务页、日志页、测试页、设置页、热键页、通知页可打开。
- 截图、OCR、任务启动/停止、更新检查、设置保存、通知、热键至少完成一次人工验证。

Commit message：

```text
refactor: 清理 WinUI 迁移遗留代码

- 移除 WPF-only 依赖和迁移残留代码
- 统一资源路径、命名空间和构建警告处理
```

## 每步更新模板

完成任一阶段后，在对应阶段补充：

```text
完成时间：
主要改动：
- 
验证结果：
- Visual Studio 构建：
- 人工验证：
遗留问题：
- 
实际 commit message：
```

## 下次对话建议入口

下次继续时，优先执行：

```powershell
Get-Content D:\Learn\VisualStudio\source\repo\AutoHPMA\MIGRATION_TRACKING.md -Encoding UTF8
git status --short
```

如果第 0 步已提交，建议从第 1 步“基线梳理与依赖收口”开始；如果用户希望先看到可用界面，也可以从第 2 步和第 3 步并行推进。
