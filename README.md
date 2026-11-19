# TaskbarWidget

一个精简的 Windows 11 任务栏小组件：把**媒体信息与控制**和**网速 / CPU / 内存监控**放进同一块任务栏区域，风格参考 [FluentFlyout](https://github.com/unchihugo/FluentFlyout)，但去掉歌词、皮肤、历史统计等一切额外配置。

## 功能

- **重叠双页**：同一区域两页叠放，同一时刻只显示一页。
  - 无媒体时显示监控页。
  - 有媒体时显示媒体页；右侧两个竖向圆点指示当前页，实心 = 当前页，可点击切换。
  - 鼠标悬停在组件上滚动滚轮可切换页面（滚轮钩子只拦截组件上的滚动，不影响其他窗口）。
- **监控页**：上传 / 下载速度一列，CPU / 内存一列；网速默认取主网卡，可在设置里改为指定网卡或全部累加。
- **媒体页**：显示歌曲封面（获取不到时用默认图标）、标题、歌手；点击媒体页会在上方弹出一个紧凑的悬浮控制条，包含上一曲 / 播放暂停 / 下一曲。点击悬浮窗以外任何位置即可收起，带滑入滑出动画。
- **任务栏集成**：作为 `Shell_TrayWnd` 子窗口嵌入任务栏，可设置贴在右侧系统托盘旁或最左侧。Windows 11 任务栏图标居中时，最左侧为空，组件直接贴在最左边缘；图标左对齐时则跟在最后一个程序图标后面。
- **主题**：自动跟随 Windows 浅色 / 深色模式，无独立主题设置。
- **托盘菜单**：右键托盘图标只有「设置…」和「退出」。
- **设置**：开机自启、任务栏位置、刷新频率、网卡选择、滚轮切换方式。配置保存在 `%APPDATA%\TaskbarWidget\settings.json`。
- **单实例**：重复启动不会开第二份。

## 技术栈

- C# / WPF，`net10.0-windows10.0.22000.0`
- WPF-UI 4.3.0（Fluent 主题）、MicaWPF 7.1.0
- Dubya.WindowsMediaController 2.5.6（Windows SMTC 媒体会话与控制）
- CommunityToolkit.Mvvm 8.4.2
- 手写 Win32 P/Invoke：任务栏嵌入与定位、全局低级鼠标钩子、系统监控采样（`GetIpForwardTable` / `GetSystemTimes` / `GlobalMemoryStatusEx`）

## 构建与运行

前置条件：Windows 11、.NET 10 SDK。

```bash
git clone https://github.com/HueZhang/TaskBar_MusicControl.git
cd TaskBar_MusicControl
dotnet build TaskbarWidget.sln -c Release
```

运行：

```bash
dotnet run --project src/TaskbarWidget/TaskbarWidget.csproj -c Release
```

或启动构建产物：

```
src\TaskbarWidget\bin\Release\net10.0-windows10.0.22000.0\TaskbarWidget.exe
```

发布独立 exe：

```bash
dotnet publish src/TaskbarWidget/TaskbarWidget.csproj -c Release -r win-x64 --self-contained false
```

## 使用说明

1. 启动后托盘出现图标；组件默认嵌入任务栏右侧、紧挨系统托盘。
2. 播放任意支持 SMTC 的播放器（Spotify、浏览器、Apple Music、WMP 等），组件即显示媒体信息。
3. 鼠标悬停在组件上滚动滚轮，或点击右侧圆点，切换监控页 / 媒体页。
4. 在媒体页点击任意位置，弹出控制条；点击控制条以外位置收起。
5. 右键托盘图标 →「设置…」可调整位置、刷新频率、网卡等；「退出」关闭程序。

## 项目结构

```
TaskbarWidget.sln
src/TaskbarWidget/
├─ App.xaml / App.xaml.cs              启动、单实例、主题、托盘、悬浮控制条
├─ Models/                              MediaState、SystemStats
├─ Services/
│   ├─ MediaService.cs                  SMTC 会话、媒体属性、封面、播放控制
│   ├─ SystemMonitorService.cs          网速 / CPU / 内存采样
│   ├─ TaskbarHost.cs                   任务栏嵌入、定位、Explorer 重启恢复
│   ├─ WheelSwitchService.cs            悬停滚轮切页（WH_MOUSE_LL）
│   ├─ ClickOutsideService.cs           点击外部收起悬浮控制条
│   └─ SettingsService.cs               配置读写
├─ ViewModels/                          WidgetViewModel、SettingsViewModel
├─ Views/
│   ├─ WidgetWindow.xaml                嵌入任务栏的主窗口（两页 + 圆点）
│   ├─ MonitorPage.xaml                 网速 / CPU / 内存
│   ├─ MediaPage.xaml                   封面 + 标题 + 歌手（点击进入控制条）
│   ├─ MediaFlyoutWindow.xaml           悬浮控制条（上一曲 / 播放暂停 / 下一曲）
│   └─ SettingsWindow.xaml              设置窗口
├─ Interop/NativeMethods.cs             Win32 P/Invoke 声明
├─ Converters/                          格式化与可见性转换器
└─ Resources/app.ico                    程序 / 托盘图标
docs/DESIGN.md                          设计与需求文档
```

## 说明

- 任务栏嵌入使用非官方 Win32 方式，任务栏结构随 Windows 更新变化可能导致错位；程序在嵌入失败时会降级为贴任务栏上方的悬浮窗。
- CPU 使用率采用 `GetSystemTimes` 差分，与任务管理器可能有 1–3% 差异。
- 歌词、进度条、音量控制、皮肤、历史统计等不在本项目范围内。

## 参考

- [FluentFlyout](https://github.com/unchihugo/FluentFlyout)
- [TrafficMonitor](https://github.com/zhongyang219/TrafficMonitor)
- [Dubya.WindowsMediaController](https://github.com/DubyaDude/WindowsMediaController)
- [WPF-UI](https://github.com/lepoco/wpfui)
