# AGENTS.md

目标。保持这个 Windows 11 任务栏组件小而可验证。只改满足需求所需的最少代码。在真实运行的应用里证明改动有效，再报告完成。

本文件优先于 `docs/DESIGN.md`。设计文档写于开发前，部分条目已过时。用户说明见 `README.md`。

## 产品范围

一个任务栏区域，两页叠放。监控页显示上传、下载、CPU、内存。媒体页显示封面、标题、歌手。点击媒体页在上方弹出控制条，含上一曲、播放暂停、下一曲。右侧两个竖点指示当前页，可点击。鼠标悬停时滚轮切页。

明确不做。歌词、进度条、seek、音量、皮肤、字体颜色设置、历史统计、插件、温度、GPU、硬盘。这是边界，不是待办。

## 技术栈

C# 与 WPF。`net10.0-windows10.0.22000.0`。WinRT SMTC 需要这个 TFM，不要降级。

NuGet 依赖四个。WPF-UI 4.3.0 提供 Fluent 主题和画刷。MicaWPF 7.1.0 保留引用，主界面不依赖它。Dubya.WindowsMediaController 2.5.6 封装 SMTC。CommunityToolkit.Mvvm 8.4.2 提供 `ObservableObject` 和 `RelayCommand`。

系统交互用手写 P/Invoke。托盘用 `System.Windows.Forms.NotifyIcon`。配置存 `%APPDATA%\TaskbarWidget\settings.json`，枚举按字符串序列化。

## 功能地图

从用户可见功能反查入口、代码和预期结果。改完按同一路径走一遍。

| 功能 | 到达路径 | 代码 | 预期结果 |
| --- | --- | --- | --- |
| 监控页 | 启动且无媒体播放 | `Views/MonitorPage.xaml`、`Services/SystemMonitorService.cs` | 两列两行。上传下载一列，CPU 内存一列。每秒刷新 |
| 媒体页 | 用 SMTC 播放器放歌，悬停组件滚动或点右点 | `Services/MediaService.cs`、`Views/MediaPage.xaml`、`Services/WheelSwitchService.cs` | 第二点可用。封面显示，取不到时显示默认音符 |
| 控制条 | 点击媒体页 | `Views/MediaFlyoutWindow.xaml`、`Services/ClickOutsideService.cs` | 从下方滑入。三个按钮。点击外部滑出。重复开合位置不下移 |
| 左侧放置 | 托盘设置，位置选左侧 | `Services/TaskbarHost.cs` | 居中任务栏贴最左边缘。左对齐任务栏跟在最后一个程序图标后 |
| 设置 | 托盘右键，设置 | `Views/SettingsWindow.xaml`、`ViewModels/SettingsViewModel.cs` | 保存后立即生效，无需重启 |
| 退出 | 托盘右键，退出 | `App.xaml.cs` | 进程结束，全局钩子卸载 |

## 架构不变量

每条都来自一次实际失败。改动前先读证据，不要凭直觉推翻。

| 不变量 | 证据 | 位置 |
| --- | --- | --- |
| 嵌入只走 `TaskbarHost`。`SetParent` 到 `Shell_TrayWnd`，设 `WS_CHILD`，用 `HWND_TOP` | 用 `SWP_NOZORDER` 时 Explorer 的 DesktopWindowContentBridge 盖住组件。`WS_EX_LAYERED` 会拒绝嵌入，需在 SourceInitialized 清掉 | `Services/TaskbarHost.cs` |
| 两个窗口都不调用 `SetWindowCompositionAttribute` | 亚克力策略画的是矩形，圆角外露出直角。改用圆角半透明 `Border` | `Views/WidgetWindow.xaml.cs`、`Views/MediaFlyoutWindow.xaml.cs` |
| 没有主题下拉。主题来自 `ApplicationThemeManager.ApplySystemTheme()` | `ThemesDictionary Theme="System"` 抛 XamlParseException | `App.xaml.cs` |
| 控制条首次显示先 `Measure/Arrange/UpdateLayout`，动画用 `FillBehavior.Stop` | 首次布局前尺寸为 0，兜底用了旧尺寸 260x120 导致错位。`HoldEnd` 占住 `Top`，每次收起下移 8px | `Views/MediaFlyoutWindow.xaml.cs` |
| 两个 `WH_MOUSE_LL` 钩子。回调只做矩形判断和 `Dispatcher.BeginInvoke` | 回调里做 WPF 操作会拖慢全局输入。退出必须 `Uninstall` | `Services/WheelSwitchService.cs`、`Services/ClickOutsideService.cs` |
| 左右判断读注册表 `TaskbarAl` | 居中时 `TaskbarFrame` 横跨整条任务栏，跟图标走会把组件放到时钟上 | `Services/TaskbarHost.cs` |
| UI 更新统一在 `WidgetViewModel` 切回 Dispatcher | 采样在 `PeriodicTimer`，钩子在输入线程 | `ViewModels/WidgetViewModel.cs` |
| 程序化固定到开始菜单不可行 | 实测返回 `E_ACCESSDENIED`。快捷方式已建，固定需用户手动 | `App.xaml.cs` 之外的安装步骤 |

## 工作规则

诊断前先读相关实现。没有运行验证的解释标为假设，不要写成结论。

修 bug 前先复现。改完重跑同样步骤。复现被环境挡住就说明缺什么，不要假装验证过。

必须运行应用。编译通过只能证明代码能编译，证明不了组件嵌进任务栏，也证明不了控制条位置正确。优先删除，再考虑增加。选择能解决问题的最小改动。

加新辅助函数前先找现有规范路径。发现过期示例和调用方，同一次改动里一起更新。

同一个纠正出现第二次，说明环境有问题。把它编码成检查、类型或结构，而不是再写一段文字。文字规则排在最后。

可逆工作直接做，做完报告结果。不可逆写入需要明确授权。被邀请加范围时可以说不，判断比附和重要。

## 验证

重建前先结束进程，exe 被占用会导致链接失败。

```
taskkill //F //IM TaskbarWidget.exe
dotnet build TaskbarWidget.sln -c Debug
dotnet run --project src/TaskbarWidget/TaskbarWidget.csproj -c Debug
```

发布用下面这条。

```
dotnet publish src/TaskbarWidget/TaskbarWidget.csproj -c Release -r win-x64 --self-contained false -o publish
```

按功能地图逐项走。以下是必查项。

1. 无媒体时监控页可见，第二点禁用。
2. 悬停滚轮切页。鼠标移开后滚动不影响其他窗口。
3. 控制条第一次点击位置正确，重复开合不下移，点击外部收起。
4. 左侧设置在居中任务栏贴最左，不压时钟。
5. 托盘只有设置和退出。设置保存后立刻生效。
6. 退出后进程结束。

## 证据与边界

【实测】本机构建通过。发布 exe 可运行。开始菜单快捷方式已创建。程序化固定返回拒绝访问。

【实测】本机 UI Automation 枚举显示 `Taskbar.TaskListButtonAutomationPeer` 按钮右边界在 x=1115，`TaskbarAl` 为 1。定位逻辑依赖这个类名。

【推断】第三方任务栏美化工具可能改掉自动化类名或窗口层级，导致左侧定位失效。没有验证过。

【未验证】多显示器、任务栏自动隐藏、任务栏在顶部或左侧。不要在这些场景下声称可用。

## 完成报告格式

结果。实际运行的检查。可访问的证据。仍未验证的部分。简短，与任务规模相称。观察到的结果和推断分开写。别人报告的结果和自己检查的结果分开写。
