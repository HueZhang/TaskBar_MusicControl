# Taskbar Widget · 设计文档（v1.0）

> **状态：已开发（v1.0 实现完成）。** 代码位于 `src/TaskbarWidget/`。
> 目标：一个精简的 Windows 11 任务栏小组件 —— 只做「媒体控制 + 系统监控」，视觉风格对齐 FluentFlyout（Fluent 2 / Mica / 明暗主题 / 平滑动画），去掉歌词、皮肤、历史统计、插件、温度等一切额外配置。

---

## 0. 已确认的需求与决策

| # | 决策 | 内容 |
|---|---|---|
| 1 | **UI 形态** | 两个 widget **重叠堆叠**在同一块任务栏区域，同一时刻只显示最上层一个。无媒体时默认显示「监控页」；有媒体时下层是「媒体页」，底部显示两个圆点（实心 = 当前页）。**只有鼠标悬停在组件上时，滚轮才能切换上层页**；鼠标不在组件上时滚轮行为不受影响。 |
| 2 | **媒体页内容** | **不要封面**（占用控件区域太大）。两页必须**一样大、内容密度均衡**，避免一页零散一页拥挤：两页都用「两行信息 + 底部圆点」的相同结构，媒体页用文字/按钮填满，不靠图片撑版面。 |
| 3 | **任务栏形态** | 参考 FluentFlyout：真嵌入任务栏（`Shell_TrayWnd` 子窗口），带失败降级。 |
| 4 | **设置项** | 保留（精简后的少量配置，见 §3.4）。 |
| 5 | **CPU** | 用 `GetSystemTimes`（好实现、零依赖，允许与任务管理器差 1–3%）。 |
| 6 | **进度条** | 不做（只读进度、seek 都不做）。 |
| 7 | **网速** | 只取「主要的一个网卡」（默认路由网卡），并**加成配置项**：默认 = 主网卡，可手动指定 / 累加全部。 |
| 8 | **待定项** | 按 §9 的「推荐」执行：滚轮任意方向 toggle 两页；圆点可点击；点击封面/歌名无动作（现已无封面）；Legacy 宽度开关保留但默认关；无媒体时第二圆点显示但置灰禁用；工程名 `TaskbarWidget.sln`。 |

### 明确不做（精简边界）
- ❌ 歌词（含同步歌词）、封面、进度条/时长/seek。
- ❌ 点击弹出 Flyout（媒体页即组件本身）。
- ❌ TrafficMonitor 的：皮肤系统、字体/颜色自定义、历史流量统计、报警阈值、插件系统、温度/GPU/硬盘、网络详情页、文本自定义。
- ❌ FluentFlyout 的其他 flyout：音量/混音器、Up Next、Lock Keys、音频可视化器、锁定键。
- ❌ 随机/重复、播放器手动切换、多显示器布局适配（v1）。
- ❌ MSIX / 商店打包（先做绿色单文件 exe）。

---

## 1. 调研结论（已验证的事实）

| 事项 | 结论 | 依据 |
|---|---|---|
| FluentFlyout 技术栈 | **WPF**（`FluentFlyoutWPF` 工程：Classes / Controls / Models / Pages / ViewModels / Windows）+ WPF-UI(Fluent 2) + MicaWPF + Dubya.WindowsMediaController + CommunityToolkit.Mvvm 等 | GitHub 仓库目录与 README「Credits」 |
| 媒体信息来源 | Windows **SMTC**，`GlobalSystemMediaTransportControlsSessionManager` | Microsoft Learn、FluentFlyout FAQ |
| 控制方法 | `TryPlayAsync / TryPauseAsync / TryTogglePlayPauseAsync / TrySkipNextAsync / TrySkipPreviousAsync` 等 | Microsoft Learn `GlobalSystemMediaTransportControlsSession` 方法表 |
| 媒体属性 | `TryGetMediaPropertiesAsync()` → Title / Artist / AlbumTitle / Thumbnail；事件 `MediaPropertiesChanged / PlaybackInfoChanged` | Microsoft Learn |
| 封装库 | `Dubya.WindowsMediaController` 2.5.6，支持 .NET 6+ / `net6.0-windows10.0.22000.0` 起 | NuGet 页面 |
| SMTC 可用性 | unpackaged 桌面应用（交互式会话）可调用；Windows 11 起仅**服务 / SYSTEM / 非交互会话**被禁止 | dotnet/runtime#84293、Microsoft Q&A |
| 任务栏嵌入 | FluentFlyout 官方博客明确写「taskbar widgets **inject themselves into the taskbar** and rely on accurate width and spacing information」；Win32 `SetParent` 挂到 `Shell_TrayWnd` 子窗口 | FluentFlyout 博客「How to Use FluentFlyout with WindHawk」；Microsoft Learn `SetParent` |
| 网速 | `GetIfTable2` → `MIB_IF_ROW2.InOctets / OutOctets`（累计字节，差分求速率），`FreeMibTable` 释放 | Microsoft Learn |
| 「主网卡」 | `GetIpForwardTable` → `MIB_IPFORWARDROW`：`dwForwardDest == 0.0.0.0` 是默认路由，取**最小 `dwForwardMetric1`** 那条（Windows 用最低 metric 选路由），其 `dwForwardIfIndex` 即主网卡；`MIB_IF_ROW2` 同时带 `InterfaceIndex` / `Alias`，可对上 | Microsoft Learn `GetIpForwardTable` / `MIB_IPFORWARDROW` / `MIB_IF_ROW2` |
| CPU | `GetSystemTimes`（idle/kernel/user FILETIME 差分） | Microsoft Learn |
| 内存 | `GlobalMemoryStatusEx` → `dwMemoryLoad` / `ullTotalPhys` / ullAvailPhys` | Microsoft Learn |
| **滚轮消息** | ⚠️ `WM_MOUSEWHEEL` **发给焦点窗口**，不是光标下的窗口；`DefWindowProc` 会沿父链冒泡。组件是 Explorer 的子窗口、通常没有焦点，所以普通 WPF `MouseWheel` 事件收不到 | Microsoft Learn `WM_MOUSEWHEEL` |
| **悬停+滚轮方案** | `SetWindowsHookEx(WH_MOUSE_LL, LowLevelMouseProc)` 全局低级钩子（**不需要注入 dll**）：`MSLLHOOKSTRUCT.pt` 给光标屏幕坐标、`mouseData` 高字给滚轮 delta（±120）；判断光标是否落在组件矩形内，是则切页并 `return 非零` 吞掉该消息，否则 `CallNextHookEx` 放行。回调必须尽快返回 | Microsoft Learn `LowLevelMouseProc` / `MSLLHOOKSTRUCT` / `SetWindowsHookEx` |
| WPF-UI 4.x XAML | `xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"`；App.xaml 合并 `<ui:ThemesDictionary Theme="Light"/>` 与 `<ui:ControlsDictionary/>`；运行时改主题用 `ApplicationThemeManager.Apply(...)` | lepoco/wpfui 仓库 `src/Wpf.Ui.Gallery/App.xaml`、官方文档 |
| 本机环境 | .NET SDK 8.0.133 / 9.0.101 / **10.0.403**；git 2.45.2；Windows 11 Pro 10.0.26200；nuget.org 可达（HTTP 200） | 本机实测 |

---

## 2. 技术栈（对齐 FluentFlyout）

| 层 | 选择 | 版本 / 依据 |
|---|---|---|
| 框架 | **C# / WPF**，`net10.0-windows10.0.22000.0` | 与 FluentFlyout 同栈；本机有 .NET 10 SDK；WinRT SMTC 需要 windows TFM |
| Fluent 控件/主题 | **WPF-UI (lepoco)** | NuGet `WPF-UI` **4.3.0**（含 `net10.0-windows7.0` 目标），XAML 命名空间见 §1 |
| 背景 | **MicaWPF** | NuGet `MicaWPF` **7.1.0**（含 `net10.0-windows7.0`），FluentFlyout 同款；`MicaWindow` 在 Win10 降级纯色 |
| 媒体 | **Dubya.WindowsMediaController** | NuGet **2.5.6**（含 `net10.0-windows10.0.22000.0` 目标），FluentFlyout 同款封装 |
| MVVM | **CommunityToolkit.Mvvm** | NuGet **8.4.2**（`ObservableObject` / `RelayCommand`） |
| 系统监控 | **手写 P/Invoke**（`iphlpapi.dll` / `kernel32.dll`），零第三方库 | 只要 3 个数 |
| 任务栏嵌入 | 手写 Win32：`FindWindow("Shell_TrayWnd")` → `FindWindowEx` 定位子窗口 → `SetParent` + WS_CHILD → `SetWindowPos` 定位 | 对齐 FluentFlyout 注入方式（非官方 API，见 §9 风险） |
| 悬停滚轮 | **`SetWindowsHookEx(WH_MOUSE_LL)`** 低级钩子 + `MSLLHOOKSTRUCT` | 解决「滚轮发给焦点窗口」的必选手段 |
| 托盘 | `System.Windows.Forms.NotifyIcon`（`UseWindowsForms=true`）或 WPF-UI Tray | 简单可靠 |
| 单实例 / 配置存储 | `Mutex`；`settings.json` 存 `%APPDATA%\TaskbarWidget\`，用 `System.Text.Json` | 精简 |
| 打包 | `dotnet publish` 单文件 exe | 先不做 MSIX |

> 依赖仅 4 个 NuGet 包，其余全为系统 API / BCL。版本已通过 NuGet flat-container 接口核对存在且含 net10.0-windows 目标。

---

## 3. UI 设计

### 3.1 模型：两块等大、等密度的重叠页

同一块任务栏矩形内放**两个等大的层**（同一个 WPF 窗口里用 `Grid` 重叠 + `Panel.ZIndex` / `Opacity` 控制，避免真的开两个子窗口），Z 序只有两态。**两页刻意采用相同的「两行内容 + 底部圆点」结构，宽度、高度、字号一致**，保证视觉密度均衡：

```
页 0 · 监控页（无媒体时默认上层）      页 1 · 媒体页（有媒体时可切上来）
┌──────────────────────────────┐      ┌──────────────────────────────┐
│ ↑ 128 KB/s    ↓ 2.4 MB/s     │      │ ♪  Blinding Lights           │
│ CPU 23%       MEM 61%        │      │    The Weeknd   ⏮  ⏯  ⏭      │
│           ●  ○               │      │           ○  ●               │
└──────────────────────────────┘      └──────────────────────────────┘
```

- **无封面**：媒体页第一行用一个小音符字形 + 歌名，第二行歌手 + 三个控制按钮；与监控页的「两行标签+数值」占据同样的两行空间，不出现一页空一页挤。
- **实心圆点 = 当前上层页**；空心 = 另一页。无媒体时媒体页不可用 → 第二点置灰/禁用，滚轮不切换。
- 切换动画：上层淡出 + 下层淡入（约 150ms），圆点同步。
- 高度贴合任务栏（约 32–40px @100% DPI）；**等宽数字（tabular numerals）+ 固定列宽**，避免每秒刷新抖动。
- 歌名过长：省略号 + hover tooltip 显示全名（不做跑马灯，省动画开销）。
- Fluent 2 观感：明暗跟随系统、Mica/Acrylic、圆角、亚克力按钮、系统强调色、平滑淡入淡出。

### 3.2 切换规则（重点）

```
if (鼠标光标在组件矩形内 && 收到 WM_MOUSEWHEEL && 两页都可用)
        切换 Z 序（推荐：任意方向滚动都 toggle 两页）
        吞掉该滚轮消息（不让它传给后面的窗口）
else
        CallNextHookEx 放行 —— 不影响桌面/浏览器/任务栏本身的滚动
```

- 鼠标**不在组件上**时，滚轮完全不干预（用户明确要求「只有鼠标在其上」）。
- 判断"在组件上"：`MSLLHOOKSTRUCT.pt`（屏幕坐标，per-monitor aware）与组件窗口实际屏幕矩形做命中；DPI 用 `PerMonitorV2`，坐标与矩形换算到同一坐标系。
- 钩子只在组件可见时安装；降级为悬浮窗时同样适用；退出/隐藏时 `UnhookWindowsHookEx`。
- 回调**只做矩形判断 + 投递一个切页动作到 UI 线程**，不做耗时操作，避免拖慢全局输入。
- 圆点本身也可点击切页（推荐），方便触控板/无滚轮场景。

### 3.3 媒体页交互
- `⏮ / ⏯ / ⏭` 直接调用 SMTC；依据 `PlaybackInfo.Controls` 把不支持的按钮置灰。
- 点击文字区域：无动作（推荐）。
- 不再有 Flyout 弹出卡片 —— 媒体信息与控制全部在任务栏这一层里完成。

### 3.4 托盘与设置项（保留，但压到最少）

托盘右键菜单：`显示/隐藏组件` · `设置…` · `关于` · `退出`。

`SettingsWindow`（WPF-UI Fluent 控件）只放这些键，存 `%APPDATA%\TaskbarWidget\settings.json`：

| 分组 | 键 | 默认值 | 说明 |
|---|---|---|---|
| 常规 | 开机自启 | 关 | 注册表 Run 项 / 启动文件夹 |
| 常规 | 主题 | 跟随系统 | 跟随 / 浅色 / 深色 |
| 监控 | 刷新频率 | 1 秒 | 1s / 2s |
| 监控 | **网卡选择** | **主网卡（自动）** | `主网卡(自动)` / `指定网卡(下拉)` / `全部累加`；下拉数据来自 `GetIfTable2` 的 `Alias` 列表 |
| 交互 | 滚轮切换方向 | 任意方向切换 | 或「上=监控 / 下=媒体」 |
| 高级 | Legacy 宽度模式 | 关 | 任务栏错位时手动开启（对齐 FluentFlyout 同名开关） |

> 这就是「排除 TrafficMonitor 额外配置」的边界：没有皮肤、字体、颜色、单位、报警、历史统计、文本自定义、插件、温度/GPU/硬盘、网络详情页。

---

## 4. 架构与模块划分

```
TaskbarWidget.sln
└─ src/TaskbarWidget/                 (单工程，刻意不拆多项目)
   ├─ App.xaml / App.xaml.cs         启动、单实例、WPF-UI 主题字典、托盘
   ├─ Models/
   │   ├─ MediaState.cs              Title, Artist, Album, IsPlaying,
   │   │                             CanPlayPause/Next/Previous
   │   └─ SystemStats.cs             UploadBps, DownloadBps, CpuPercent, MemPercent
   ├─ Services/
   │   ├─ IMediaService.cs           → MediaService（封装 WindowsMediaController）
   │   │    事件 StateChanged; 方法 PlayPauseAsync/NextAsync/PreviousAsync
   │   ├─ ISystemMonitorService.cs  → SystemMonitorService（PeriodicTimer 轮询）
   │   │    ├─ NetworkSpeedSampler  GetIpForwardTable(选主网卡) + GetIfTable2 差分
   │   │    ├─ CpuSampler           GetSystemTimes 差分
   │   │    └─ MemorySampler        GlobalMemoryStatusEx
   │   ├─ ITaskbarHost.cs           → TaskbarHost（嵌入 / 定位 / DPI / explorer 重启恢复）
   │   ├─ WheelSwitchService.cs     WH_MOUSE_LL 钩子：命中组件矩形 → 切页；否则放行
   │   └─ SettingsService.cs        settings.json（§3.4 的键）
   ├─ ViewModels/
   │   ├─ WidgetViewModel.cs        聚合两页数据 + ActivePage + 圆点状态 + SwitchPageCommand
   │   ├─ MonitorPageViewModel.cs
   │   └─ MediaPageViewModel.cs     按钮 RelayCommand → MediaService
   ├─ Views/
   │   ├─ WidgetWindow.xaml         无边框、WS_CHILD；Grid 内叠放两页 + PageDots 控件
   │   │   ├─ MonitorPage.xaml     两行网速/CPU/内存
   │   │   ├─ MediaPage.xaml       歌名/歌手 + ⏮ ⏯ ⏭（无封面）
   │   │   └─ PageDots.xaml        两个圆点（实心=当前页）
   │   ├─ SettingsWindow.xaml       §3.4 设置
   │   └─ TrayIcon.cs
   ├─ Interop/NativeMethods.cs      FindWindow(Ex) / SetParent / SetWindowPos /
   │                                GetIfTable2 / FreeMibTable / GetIpForwardTable /
   │                                GetSystemTimes / GlobalMemoryStatusEx /
   │                                SetWindowsHookEx / CallNextHookEx / UnhookWindowsHookEx /
   │                                GetWindowRect / DwmSetWindowAttribute …
   ├─ Converters/                   速度格式化、百分比、布尔→可见性
   └─ Assets/                       图标、Segoe Fluent Icons 字形
```

### 数据流（单向、事件驱动）
```
SMTC 事件 ──► MediaService ──► MediaState ─┐
                                          ├─► WidgetViewModel ─► MonitorPage / MediaPage (绑定)
Win32 轮询 ─► SystemMonitorService ─► Stats┘        ▲          └─► PageDots (ActivePage)
                                                     │
WH_MOUSE_LL ─► WheelSwitchService ─► SwitchPageCommand┘（命中矩形才触发）
用户点按钮 ─► RelayCommand ─► MediaService.Try*Async() ─► SMTC 回推新状态
```
- 所有 UI 更新经 `Dispatcher` 切回 UI 线程；采样在后台 `PeriodicTimer`；钩子回调只做矩形判断 + 快速投递。
- SMTC 事件驱动，不轮询；系统监控才按 1s/2s 定时。

---

## 5. 关键实现要点

### 5.1 媒体（SMTC）
- `MediaManager.StartAsync()`；订阅 `OnAnySessionOpened/Closed`、`OnFocusedSessionChanged`、`OnAnyMediaPropertyChanged`、`OnAnyPlaybackStateChanged`。
- 用 `GetFocusedSession()` 自动跟随系统认定的会话；不做手动切换播放器。
- 属性变化 → `TryGetMediaPropertiesAsync()` 取 Title/Artist/AlbumTitle，**不取 Thumbnail**（无封面）；`GetPlaybackInfo()` 取状态与 `Controls` 可用性。
- 控制统一走 `Try*Async`，返回 bool 判断是否成功；不支持的按钮置灰。
- 兼容性：Spotify / WMP 全支持；Chromium/Firefox/Apple Music 基础播放/切歌可用。
- 兜底：SMTC `RequestAsync()` 若抛异常（权限/会话问题）→ 媒体页保持「无媒体」状态，不影响监控页与程序退出。

### 5.2 网速：主网卡
1. **选网卡**（每次采样做，成本极低，网卡热插拔自动跟随）：
   - 调 `GetIpForwardTable`，遍历 `MIB_IPFORWARDROW`，筛 `dwForwardDest == 0.0.0.0` 的默认路由，取 **`dwForwardMetric1` 最小**那条的 `dwForwardIfIndex`；多条并列取第一条。
   - 用 `dwForwardIfIndex` 去 `GetIfTable2` 结果里匹配 `MIB_IF_ROW2.InterfaceIndex`。
   - 配置覆盖：
     - `主网卡(自动)`（默认）→ 上述逻辑；
     - `指定网卡` → 用户在下拉里选中的那块（按 `InterfaceLuid` / `Alias` 记忆，因为 `InterfaceIndex` 可能变化）；
     - `全部累加` → 全部网卡求和口径。
   - 找不到默认路由（纯内网/断网）→ 该次采样显示 `0 B/s`，不崩、不显示负数。
2. **差分**：`download = ΔInOctets / Δt`，`upload = ΔOutOctets / Δt`；`Δt` 用 `Stopwatch.GetTimestamp()` 单调时钟；计数器回绕/网卡重置导致 `Δ < 0` → 本次记 0。
3. `FreeMibTable` 每次调用后释放。
4. 格式化：`0`→`0 B/s`，`<1024`→`x B/s`，`<1MB`→`x.x KB/s`，否则 `x.x MB/s`。

### 5.3 CPU
```
usage = 1 - Δidle / (Δkernel + Δuser)      // kernel 含 idle
```
零依赖、好实现，允许与任务管理器差 1–3%（已确认可接受）。

### 5.4 内存
`GlobalMemoryStatusEx` → `dwMemoryLoad` 直接给百分比；tooltip 给 `已用/总量 GB`。

### 5.5 任务栏嵌入（对齐 FluentFlyout，最需要小心）
1. `FindWindow("Shell_TrayWnd", null)` → `FindWindowEx` 找 `ReBarWindow32` / `MSTaskSwWClass` 等子容器，取可用矩形。
2. 组件窗口 `WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS`，清 `WS_POPUP`，`SetParent(widgetHwnd, trayHwnd)`，再 `SetWindowPos` 定位到托盘区左侧的空闲位置。
   - 微软注明 `SetParent` 不自动改样式：必须手动清/设 WS_CHILD/WS_POPUP；并注意 DPI awareness 一致（用 `PerMonitorV2`）。
3. 定位（v1 简单版）：以任务栏矩形为基准，固定宽度（约 200–220px），放在通知区左侧；随任务栏位置（上/下/左/右）与高度自适应。`Legacy 宽度模式` 用于错位时手动切换。
4. **Explorer 重启**：监听 `RegisterWindowMessage("TaskbarCreated")`，重新查找 HWND 并重新 `SetParent`。
5. 显示/DPI/自动隐藏变化：监听 `WM_SETTINGCHANGE` / `WM_DISPLAYCHANGE`，重算位置；组件矩形缓存供钩子命中判断使用（位置一变就更新）。
6. **失败降级**：嵌入失败自动降级为「贴任务栏上方的桌面悬浮窗（置顶、无边框）」，功能与滚轮切换完全保留。
7. 已知坑（FluentFlyout 博客/changelog）：注入依赖任务栏的尺寸/间距信息，WindHawk / StartAllBack 等会导致裁剪、错位、遮挡图标；小任务栏/顶部/左侧布局、DPI、z-order 都需适配。v1 **只做固定宽度 + 降级 + Legacy 开关**，不做全布局矩阵。

### 5.6 悬停 + 滚轮切换
- 由于 `WM_MOUSEWHEEL` 发给**焦点窗口**（组件作为 Explorer 子窗口通常无焦点），普通 WPF `MouseWheel` 不可靠 → 用 `WH_MOUSE_LL` 低级钩子（全局但**不注入 dll**）。
- 钩子回调：`wParam == WM_MOUSEWHEEL` 时读 `MSLLHOOKSTRUCT.pt` 与组件屏幕矩形比较；
  - 命中且两页可用 → `Dispatcher.BeginInvoke(SwitchPage)`，**返回非零吞掉消息**；
  - 未命中 → `CallNextHookEx` 放行，用户其它窗口的滚动不受影响。
- 回调保持极短；微软建议低级钩子尽快返回。
- 组件根容器用 `Background=Transparent` 保证 WPF 内部按钮/圆点可点（`{x:Null}` 会穿透命中测试）；钩子命中用的是 Win32 窗口矩形，不受此项影响。
- 退出/隐藏组件时 `UnhookWindowsHookEx`；异常时也要保证卸载，避免残留全局钩子。

---

## 6. 里程碑（确认后执行）

| # | 内容 | 可验证产物 |
|---|---|---|
| M1 | 骨架：WPF 工程 + WPF-UI/Mica 主题 + 托盘 + 单实例 + 空组件窗口（两页占位 + 圆点） | 托盘可退出，窗口显示占位内容 |
| M2 | **系统监控**：主网卡选择 + Network/Cpu/Memory 三个采样器 + 监控页两行显示 + 速度格式化 | 看视频/下载时数字跳动，与任务管理器量级一致；设置里能切网卡 |
| M3 | **媒体页**：MediaService + 歌名/歌手（无封面）+ ⏮⏯⏭ + 有/无媒体时页可用性 | 打开 Spotify/浏览器播放，能显示信息并切歌暂停 |
| M4 | **重叠切换**：Z 序切换 + 圆点 + 淡入淡出动画 + `WH_MOUSE_LL` 悬停滚轮（含未命中放行、消息吞掉、卸载钩子） | 鼠标在组件上滚一下换页；在桌面上滚动不受影响；两页密度一致 |
| M5 | **任务栏嵌入 + 降级路径** + Explorer 重启恢复 + DPI + Legacy 宽度开关 | 组件真正长在任务栏上；杀 explorer 后能自动回来 |
| M6 | 极简设置、开机自启、主题跟随、`dotnet publish` 单文件 exe | 一个可直接运行的 exe |

顺序 **M2/M3 先行、M4 次之、M5 最后**：先在普通悬浮窗里把功能与切换做对，再处理最容易踩坑的任务栏注入。

---

## 7. 风险与对策

| 风险 | 影响 | 对策 |
|---|---|---|
| 全局低级鼠标钩子影响系统输入 / 被安全软件关注 | 拖慢滚轮、误拦截 | 回调只做矩形判断 + 快速投递；未命中一律 `CallNextHookEx`；程序退出必卸载；只在组件可见时安装 |
| 任务栏结构随 Win11 更新/美化工具变化 | 组件错位、遮挡图标 | 固定宽度简单定位 + 失败降级悬浮窗 + `Legacy 宽度模式` 开关；不承诺所有第三方任务栏 |
| 注入属非官方手法 | 偶发失效 | 只用标准 Win32，不 hook/注入 dll 到 Explorer；提供降级 |
| SMTC 焦点在多播放器间跳变 | 显示突然切到别的 app | 跟随 `GetFocusedSession()`（系统判定），不做自定义优先级 |
| SMTC 权限/会话异常 | 媒体页空白 | try/catch 兜底，不影响监控页与退出 |
| 部分浏览器不支持上一曲 | 按钮无效 | 依据 `PlaybackInfo.Controls` 置灰；命令返回 false 静默 |
| 主网卡判定在多网卡/VPN 下选到虚拟网卡 | 网速是 VPN 口径 | 设置里可手动指定或改「全部累加」；默认最低 metric 默认路由 |
| `InterfaceIndex` 非持久（禁用再启用会变） | 手动指定的网卡失效 | 按 `InterfaceLuid` / `Alias` 记忆，每次采样重新匹配；失效回退到自动 |
| CPU 与任务管理器差 1–3% | 数值略偏 | 已确认接受（好实现优先） |
| 1s 刷新文字宽度抖动 | 视觉跳动 | 等宽数字 + 固定列宽 |
| Mica 在 Win10 / 关闭透明效果时不可用 | 背景变纯色 | MicaWPF 自动降级纯色，符合 Fluent 行为 |

---

## 8. 验收要点（开发完成后自检）
1. 无媒体时只显示监控页，第二圆点置灰；播放任意 SMTC 播放器后第二圆点可用。
2. 鼠标在组件上滚轮 → 两页切换且圆点同步；鼠标移开后滚轮不影响其它窗口。
3. 两页宽高一致、内容密度接近（无封面，媒体页不显拥挤）。
4. 网速/CPU/内存数值与任务管理器/下载工具量级一致；设置里切网卡生效。
5. ⏮/⏯/⏭ 真实控制播放器；不支持的按钮置灰。
6. 任务栏嵌入成功；杀掉 explorer 后自动恢复；嵌入失败时自动降级悬浮窗且功能完整。
7. 明暗主题跟随系统；开机自启可开关；单实例；托盘可退出；退出后无残留全局钩子。

---

## 9. 待定项（已按「推荐」定稿，无需再确认）
1. 滚轮语义：**任意方向滚动都 toggle 两页**。
2. 圆点：**可点击**切页（触控板/无滚轮也能切）。
3. 点击媒体页文字：无动作（封面已去除）。
4. **Legacy 宽度开关**：保留但默认关。
5. 无媒体时第二圆点：显示但置灰禁用。
6. 工程命名：`TaskbarWidget` / `TaskbarWidget.sln`。

---

## 10. 参考来源

- FluentFlyout：https://github.com/unchihugo/FluentFlyout ；工程目录 https://github.com/unchihugo/FluentFlyout/tree/master/FluentFlyoutWPF
- FluentFlyout FAQ：https://fluentflyout.com/faq （SMTC 支持范围）
- FluentFlyout 博客（任务栏注入 / WindHawk 兼容 / Legacy 宽度）：https://fluentflyout.com/blog/how-to-use-with-windhawk
- FluentFlyout Changelog（任务栏布局、DPI、z-order、滚轮改音量）：https://fluentflyout.com/changelog
- TrafficMonitor：https://github.com/zhongyang219/TrafficMonitor （功能裁剪依据）
- TrafficMonitor Help（CPU 取值方式）：https://github.com/zhongyang219/TrafficMonitor/blob/master/Help_en-us.md
- Microsoft Learn：`GlobalSystemMediaTransportControlsSession` / `SessionManager` / `RequestAsync`；`GetIfTable2` / `MIB_IF_ROW2`；`GetIpForwardTable` / `MIB_IPFORWARDROW`；`GetSystemTimes`；`GlobalMemoryStatusEx`；`SetParent`；`WM_MOUSEWHEEL`；`LowLevelMouseProc` / `MSLLHOOKSTRUCT` / `SetWindowsHookEx`
- WPF-UI：`https://github.com/lepoco/wpfui`（App.xaml 示例 `src/Wpf.Ui.Gallery/App.xaml`）、`https://wpfui.lepo.co/documentation/themes.html`
- NuGet flat-container 版本核对：`WPF-UI 4.3.0`、`MicaWPF 7.1.0`、`Dubya.WindowsMediaController 2.5.6`、`CommunityToolkit.Mvvm 8.4.2`
- SMTC unpackaged 限制：https://github.com/dotnet/runtime/issues/84293
- .NET 全局鼠标钩子注意事项（中文）：https://blog.walterlv.com/post/add-global-windows-hook-in-dotnet.html
- 本机环境实测：.NET SDK 8/9/10、git 2.45.2、Windows 11 Pro 10.0.26200
