**[English](README_EN.md) | 中文**

<div align=center>
  <h1 align="center">
  <img src="https://github.com/FelixHenrikChristian/AutoHPMA/blob/master/AutoHPMA/Assets/hpma.png" width=50%>
  <br/>
  <a href="https://autohpma.top">AutoHPMA</a>
  </h1>
</div>

<div align=center>
  <img src="https://img.shields.io/badge/build-passing-brightgreen">
  <img src="https://img.shields.io/github/v/release/FelixHenrikChristian/AutoHPMA">
  <img src="https://img.shields.io/github/license/FelixHenrikChristian/AutoHPMA">
  <img src="https://img.shields.io/github/downloads/FelixHenrikChristian/AutoHPMA/total">
  <img src="https://img.shields.io/github/stars/FelixHenrikChristian/AutoHPMA">
</div>

<br>

**AutoHPMA** 是一款基于 C# 和 WPF 开发的桌面工具，专为《哈利波特：魔法觉醒》设计，旨在为游戏玩家实现一些简单的自动化功能。

<br>

## 功能

- **自动社团答题**：自动重复参与社团答题活动，OCR识别题目选项，匹配题库作答。
- **自动禁林探索**：自动完成指定次数禁林探索任务，结算页面自动点赞队友。
- **自动巫师烹饪**：支持多种菜谱的自动烹饪流程。用户可自定义或添加菜谱配置。
- **自动甜蜜冒险**：针对限时活动“甜蜜冒险”实现自动化，自动匹配、自动推进回合、自动结算。

<br>

## 特性

- 日志系统：游戏窗口覆盖日志显示，同时支持本地日志记录
- 遮罩窗口：可自定义显示的实时遮罩窗口，显示匹配结果
- 热键绑定：用户可自定义热键，通过快捷键快速启用各项功能
- 通知功能：支持原生 Windows 通知，实时推送运行结果
<br>

## 安装

### 环境要求

- Windows 10 或更高版本
- .NET 8.0 或以上

### 安装步骤

1. 前往[Releases](https://github.com/FelixHenrikChristian/AutoHPMA/releases)获取最新发布版本。
2. 下载并双击 `AutoHPMA-Setup.exe` 启动安装程序。
3. 按提示完成安装，可自定义安装路径和开机启动设置。
4. 若首次启动提示缺少 .NET 运行时，请前往 [Microsoft .NET 官网](https://dotnet.microsoft.com/download) 安装对应版本。

<br>

## 使用

1. 启动 `AutoHPMA.exe`
2. 在主页点击"启动"按钮绑定游戏窗口
3. 前往"任务"页面开启所需功能

更详细的功能说明、使用教程、常见问题解答等，请访问 [官方文档](https://autohpma.top)。

<br>

## 注意事项

- **仅适配 MuMu 模拟器**，推荐分辨率 1280×720。其余分辨率可能截图异常。
- 请将游戏画质设置为默认"标准"画质。
- 脚本运行期间请勿最小化游戏窗口，否则可能导致窗口异常置顶。
- 本工具为个人开发，仅供学习与交流使用，风险自负。

<br>

## 贡献

欢迎反馈和贡献！如果您有改进 AutoHPMA 的建议或希望贡献代码，请随时在 GitHub 页面上提出问题或创建拉取请求。

<br>

## 许可证

本项目根据 [GPL-3.0 许可证](https://github.com/FelixHenrikChristian/AutoHPMA/blob/master/LICENSE) 授权发布 - 有关详情，请参阅 LICENSE 文件。

