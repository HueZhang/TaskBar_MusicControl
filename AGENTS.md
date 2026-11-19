# AGENTS.md — TaskbarWidget

> AI 阅读入口。精简、可操作；深入内容见 `docs/DESIGN.md`（设计与需求）与 `README.md`（人类使用说明）。
> 最后更新：2026-11-19。修改架构/约定时同步更新本文件。

## 1. 项目是什么

一个 Windows 11 任务栏小组件：把**媒体信息与控制**（SMTC）和**网速 / CPU / 内存监控**放进同一块任务栏区域。风格参考 FluentFlyout，刻意精简：无歌词、无进度条、无皮肤/历史统计/插件。仓库：`https://github.com/HueZhang/TaskBar_MusicControl.git`。

## 2. 技术栈（不要随意升级或替换）

- C# / WPF，`net10.0-windows10.0.22000.0`（WinRT SMTC 需要 windows TFM）
- WPF-UI 4.3.0（Fluent 主题/Brushes）、MicaWPF 7.1.0（保留引用）
- Dubya.WindowsMediaController 2.5.6（SMTC 封装）
- CommunityToolkit.Mvvm 8.4.2（`ObservableObject` / `[ObservableProperty]` / `[RelayCommand]`）
- Win32 P/Invoke：任务栏嵌入、定位、鼠标钩子、系统采样；`System.Windows.Forms.NotifyIcon` 做托盘
- 配置：`%APPDATA%\TaskbarWidget\settings.json`（`System.Text.Json` + 枚举字符串）

## 3. 构建 / 运行 / 发布

```bash
dotnet build TaskbarWidget.sln -c Debug
dotnet run --project src/TaskbarWidget/TaskbarWidget.csproj -c Debug
dotnet publish src/TaskbarWidget/TaskbarWidget.csproj -c Release -r win-x64 --self-contained false -o publish
```

- 程序在任务栏里，修改代码后需**先杀掉运行中的实例**再编译，否则 exe 被占用：`taskkill //F //IM TaskbarWidget.exe`
- 单实例 Mutex：`TaskbarWidget_SingleInstance`。
- Debug 产物：`src/TaskbarWidget/bin/Debug/net10.0-windows10.0.22000.0/TaskbarWidget.exe`；发布产物：`publish/TaskbarWidget.exe`。

## 4. 目录职责（能力地图，文件可能变动）

```
App.xaml / App.xaml.cs        启动、单实例、主题跟随系统、托盘菜单、悬浮控制条的创建与定位
Models/                       MediaState（含封面 BitmapImage）、SystemStats
Services/
  MediaService                SMTC 会话/属性/封面/播放控制；事件驱动，首读放线程池
  SystemMonitorService        PeriodicTimer 轮询：网速(主网卡)、CPU(GetSystemTimes)、内存(GlobalMemoryStatusEx)
  TaskbarHost                 把 WPF 窗口 SetParent 到 Shell_TrayWnd；定位；Explorer 重启恢复；降级悬浮窗
  WheelSwitchService          WH_MOUSE_LL 全局钩子：光标在组件矩形内才吞掉滚轮并切页
  ClickOutsideService         WH_MOUSE_LL 全局钩子：点击悬浮控制条以外则请求收起
  SettingsService             settings.json 读写
ViewModels/
  WidgetViewModel             聚合两页数据、ActivePage、圆点状态、MediaFlyout 打开请求、媒体命令
  SettingsViewModel           设置项（自启/位置/刷新/网卡/滚轮），保存后回调 host.SetPosition
Views/
  WidgetWindow                嵌入任务栏的主窗口：MonitorPage + MediaPage 叠放 + 右侧竖向圆点
  MonitorPage                 上传/下载一列、CPU/MEM 一列（两列两行）
  MediaPage                   封面(获取不到用默认音符图标) + 标题 + 歌手；点击请求打开控制条
  MediaFlyoutWindow           纯控制条(上一曲/播放暂停/下一曲)；滑入淡入/滑出淡出动画
  SettingsWindow              WPF-UI Fluent 设置窗口
Interop/NativeMethods.cs      所有 Win32 声明与常量（窗口、钩子、 acrylic、系统采样、默认路由）
Converters/                   速度/百分比/圆点透明度/布尔可见性/空对象可见性
Resources/app.ico             程序与托盘图标（蓝底白色播放三角）
docs/DESIGN.md                原始设计与需求（部分已过时，以本文件与代码为准）
```

## 5. 架构规则（必须遵守）

1. **事件驱动 + Dispatcher**：所有采样/钩子回调在后台线程，更新 UI 必须切回 `Dispatcher`（`WidgetViewModel` 已统一处理）；新数据路径沿用这一约定。
2. **两个全局钩子**：`WheelSwitchService` 和 `ClickOutsideService` 各装一个 `WH_MOUSE_LL`。回调里只做矩形判断和 `Dispatcher.BeginInvoke`，绝不 WPF 对象操作、不耗时；未命中一律 `CallNextHookEx`；`Uninstall` 在退出时必调。
3. **任务栏嵌入只有一条路径**：`TaskbarHost` 负责 `SetParent(Shell_TrayWnd)` + `WS_CHILD` + `HWND_TOP`（不能用 `SWP_NOZORDER`，否则被 Explorer 的 DesktopWindowContentBridge 遮住）。失败才 `Fallback()` 置顶悬浮窗。不要把主窗口改成普通窗口再嵌。
4. **首帧尺寸**：`MediaFlyoutWindow.ShowAt` 首次显示必须 `Measure/Arrange/UpdateLayout`；动画结束值用 `FillBehavior.Stop` 释放，否则每次收起都会把 `Top` 往下推 8px。
5. **位置和主题**：
   - 右侧 = 紧挨 `TrayNotifyWnd` 左侧；左侧 = 读注册表 `HKCU\...\Explorer\Advanced\TaskbarAl`：1(居中) 贴任务栏最左边缘；0(左对齐) 跟在最后一个 `Taskbar.TaskListButtonAutomationPeer` 图标后面。
   - 主题跟随系统：`ApplicationThemeManager.ApplySystemTheme()`；**不要**在 App.xaml 用 `Theme="System"`（会 XAML 解析失败）。**不要**给子窗口加 `SetWindowCompositionAttribute` 亚克力（会画方块，圆角外露直角）。
   - 无独立主题设置、无 Legacy 宽度开关、无主题下拉（已删除，别加回来）。
6. **媒体页与控制条**：媒体页只展示信息；控制按钮只在 `MediaFlyoutWindow`。Segoe Fluent Icons：上一曲 `E892`、播放 `E768`、暂停 `E769`、下一曲 `E893`。按钮无外框（自定义 ControlTemplate，只有 hover/pressed 背景）。

## 6. 编码约定

- 新服务类用 `sealed` + 显式 `Dispose()` 释放定时器/钩子/事件订阅；事件订阅在构造函数成对注册、在 `Dispose` 成对取消。
- ViewModel 用 `partial class` + `[ObservableProperty]`；命令用 `[RelayCommand] private` 方法。
- XAML 资源 key 用 PascalCase；颜色用 `#AARRGGBB`；字体图标用 `Segoe Fluent Icons`。
- 静态 P/Invoke 全部放 `NativeMethods.cs`，不要散落在业务代码里。
- 异常在边界吞掉并降级（SMTC 不可用、托盘图标缺失、钩子安装失败都不应崩溃），不要让它冒泡到 UI 线程。

## 7. 当前已完成（别重复做）

- 双页重叠 + 右侧竖向圆点 + 悬停滚轮/点击圆点切页
- 监控页两列两行（上传/下载、CPU/MEM）；主网卡默认，可指定/累加
- 媒体页封面（默认图标兜底）+ 标题歌手；点击弹出紧凑控制条，点击外部收起，带动画
- 任务栏嵌入/定位/居中判断/Explorer 重启恢复；托盘菜单仅「设置…/退出」；托盘图标
- 设置：自启、位置、刷新频率、网卡、滚轮模式；主题跟随系统
- 已发布 win-x64 非自包含版本并创建开始菜单快捷方式（固定到开始需用户手动，系统禁止程序化固定）

## 8. 不要做 / 已知坑

- ❌ 歌词、进度条/seek、音量控制、播放器切换、皮肤/字体/颜色设置、历史统计、插件、温度/GPU/硬盘。
- ❌ 不要给 `WidgetWindow` / `MediaFlyoutWindow` 加 ACRYLICBLURBEHIND（矩形背景会露直角）。
- ❌ 不要在 `App.xaml` 把 `ThemesDictionary` 设成 `System`。
- ❌ 不要用 `SWP_NOZORDER` 定位嵌入窗口。
- ⚠️ Win11 任务栏结构随版本/第三方美化工具变化；定位依赖 UI Automation ClassName `Taskbar.TaskListButtonAutomationPeer` 和注册表 `TaskbarAl`，改版本后要重新验证。
- ⚠️ CPU 用差分估算，与任务管理器差 1–3% 属预期。
- ⚠️ 自动固定到开始菜单返回 `E_ACCESSDENIED`（Windows 限制），不要再尝试程序化固定。

## 9. 验证清单（改完至少跑一遍）

1. `dotnet build TaskbarWidget.sln -c Debug` 零错误。
2. 启动后无媒体显示监控页；播放 SMTC 播放器后出现第二圆点。
3. 悬停组件滚轮切页；鼠标移开滚动不影响其他窗口。
4. 媒体页点击弹出控制条，第一次点击位置正确；点击外部收起，位置不下移。
5. 设置切到左侧：居中任务栏贴最左、左对齐图标时跟在最后图标后。
6. 托盘右键只有设置/退出；设置能保存并立即生效。
7. 退出后进程结束、全局钩子卸载。
