# GhostSlacking 技术架构设计

> 版本：V0.1 设计基线
> 平台：Windows 10/11  x64  
> 技术路线：C# + .NET + Avalonia/FluentAvalonia UI + Win32 P/Invoke
> 渲染路线：`SetWindowRgn` 内容裁剪 + 非抓屏 Windows Composition 外扩羽化

## 1. 文档目的

本文将 GhostSlacking 的产品设计转换为可实施的技术边界和运行模型，重点回答：

- 怎样选择并识别目标窗口；
- 怎样让窗口进入 Ghost 状态并在 Peek 时只显示光标附近区域；
- 怎样保证移动、缩放、最小化、关闭和异常退出时可恢复；
- 怎样处理 Win32 坐标、DPI、多显示器和权限差异；
- 哪些能力属于 V0.1，哪些风险留给后续版本。

本文不是对所有 Windows 窗口类型的兼容性承诺。当前实现以普通顶层桌面窗口为目标，自动化测试已覆盖核心领域逻辑，但真实窗口、DPI、权限和图形设备兼容性仍需按手工矩阵验收。

## 2. 目标与非目标

### 2.1 目标

当前 V0.1 实现基线包含以下闭环：

1. 托盘程序启动，不显示常驻主窗口；
2. 用户通过快捷键进入 Window Picker，点击一个普通顶层窗口；
3. 保存必要的原始窗口状态后，目标窗口进入 Ghost 状态；
4. 按配置的 Peek 模式（默认按下切换，也支持按住显示），且光标位于目标窗口原始屏幕区域内时，显示随光标移动的 Reveal 区域；
5. Reveal 区域尽量保留目标窗口的正常鼠标点击和滚动行为；
6. 松开 Peek Key 后立即回到 Ghost；
7. 用户可以通过 Restore 快捷键或托盘菜单恢复原窗口；
8. 目标窗口关闭、程序退出或操作失败时，不遗留不可恢复的窗口修改。

### 2.2 非目标

V0.1 不包含：

- 多窗口同时 Ghost；
- 云同步、账户、插件和复杂规则系统；
- OCR、内容识别、截图录制或远程控制；
- 抓屏、窗口内容复制、定制 GPU Shader、实时克隆和复杂动画；
- 对 DirectX 独占窗口、受保护内容或所有管理员窗口的兼容承诺；
- “防录屏”“防监控”或安全级别的隐私保证；
- 通过注入、Hook 目标进程或修改目标应用业务逻辑来实现功能。

## 3. 设计原则

### 3.1 单一修改入口

只有 `VisibilityEngine` 可以修改目标窗口的 region、style、DWM 属性或 visibility。其他模块只产生意图或状态事件，不能直接调用这些 Win32 修改 API。

### 3.2 先保存、后修改

任何第一次修改目标窗口前，`RecoveryManager` 必须完成原始状态快照。快照失败时不进入 Ghost 状态。

### 3.3 可恢复优先于功能完整

异常路径应尽量导向 `RestoreAll()`；正常退出是否恢复由 `RestoreOnExit` 设置决定。恢复操作应幂等：重复调用不会把窗口置于更坏状态。

### 3.4 V0.1 保持简单

主业务在一个 Avalonia UI/消息线程上运行，使用 Input 优先级定时器更新光标和窗口几何。非 Reveal 状态保持约 16.7ms（60 Hz）；Reveal 根据鼠标所在显示器的当前刷新率和用户上限动态调整，最高 120 Hz。低级键盘/鼠标 Hook 仅用于 Picker 输入拦截，Composition overlay 仅用于可选视觉层。两者都不注入目标进程，也不改变恢复契约。

## 4. 系统上下文

```text
┌──────────────────────┐       Win32 User/GDI/DWM API       ┌─────────────────────┐
│ GhostSlacking.App.exe│ ─────────────────────────────────> │ Windows 桌面窗口系统 │
│ Avalonia Tray Host   │                                    └─────────┬───────────┘
└─────────┬────────────┘                                              │
          │                                                           │ HWND
          │ 用户输入                                                   ▼
┌─────────▼────────────┐                                    ┌─────────────────────┐
│ 键盘、鼠标、显示器、托盘 │                                    │ 目标应用窗口         │
└──────────────────────┘                                    │ Chrome/微信/记事本等 │
                                                            └─────────────────────┘

        ┌──────────────────────┐
        │ GhostSlacking.Watchdog.exe │ ← 心跳/恢复协议 → GhostSlacking.App.exe
        └──────────────────────┘
```

外部依赖仅限于 Windows 原生窗口系统、用户输入和本地文件系统。V0.1 不需要网络服务、数据库或目标应用 SDK。

## 5. 运行时结构

当前采用三个分层项目、一个独立 Watchdog 项目和两个测试项目：

```text
src/
  GhostSlacking.App/          Avalonia 托盘、生命周期、设置和提示 UI
  GhostSlacking.Core/         状态机、领域模型、服务接口
  GhostSlacking.Platform/     Win32 P/Invoke 和 Windows 适配器
  GhostSlacking.Watchdog/     独立异常恢复进程
tests/
  GhostSlacking.Core.Tests/
  GhostSlacking.App.Tests/
```

`GhostSlacking.Watchdog` 通过项目引用共享 Core/Platform 的恢复协议和 Win32 适配；App 的构建和发布会将 Watchdog 的运行文件复制到同一输出目录。当前没有独立的 `Platform.Tests` 或 `ManualTests` 项目，手工验收以文档测试矩阵执行。

### 5.1 依赖方向

```text
App ───────────────► Core ─────────────► 抽象接口
 │                    ▲
 └──────────────► Platform ─────────────┘

Watchdog ───────────► Platform/Recovery 协议
```

`Core` 不引用 UI 框架；`Platform` 实现 `Core` 定义的接口并承载无界面的 Win32 HWND；`App` 负责组合依赖、Avalonia 托盘菜单、消息循环和用户反馈。

## 6. 核心领域模型

```csharp
GhostWindowProfile
{
    nint Hwnd;
    uint ProcessId;
    string? ProcessName;
    WindowSnapshot Original;
    RevealSettings Reveal;
}

WindowSnapshot
{
    Rectangle ScreenBounds;
    bool WasVisible;
    bool WasMinimized;
    byte[]? OriginalRegionData;
    WindowStyleSnapshot Styles;
    WindowPlacementSnapshot Placement;
    string ProcessStartIdentity;
    int? OriginalSystemBackdropType;
}

RevealSettings
{
    int DiameterPx;       // 默认 144，以物理屏幕像素表达
    int SoftEdgeWidthPx;  // 默认 16
    RevealBlurLevel Blur; // 默认 Low
    RevealShape Shape;    // Circle / Rectangle / RoundedRectangle
    PeekTrigger Trigger;  // Hold 或 Toggle
}
```

实际实现不应长期持有来自 Win32 的裸资源句柄。region 数据应尽早复制为托管数据或序列化快照；原生 `HRGN` 的生命周期必须由 `SafeHandle` 或明确的 `try/finally` 管理。

## 7. 核心模块

### 7.1 `WindowPicker`

职责：把用户在屏幕上点击的位置解析为可操作的顶层目标窗口。

主要流程：

1. 开始拾取，显示轻量提示或高亮边框；
2. 根据光标位置调用 `WindowFromPoint`；
3. 沿父子关系使用 `GetAncestor(hwnd, GA_ROOT)` 获取顶层窗口；
4. 过滤自身窗口、桌面窗口、任务栏、不可见窗口和不适合作为目标的系统窗口；
5. 使用 `GetWindowRect`、`GetWindowThreadProcessId` 获取基础信息；
6. 在用户点击时输出 `TargetWindow`，失败则取消而不修改窗口。

Picker 不应默认选择当前前台窗口，因为产品交互是“指向并点击目标”。应明确处理目标窗口在拾取期间销毁的竞态。

### 7.2 `WindowTracker`

职责：维护目标 HWND 的生存性和几何信息。

当前使用轻量定时检查，不使用全局窗口事件 Hook。Idle、Ghost 和恢复路径保持约 60 Hz（16.7ms）；Reveal 通过 `MonitorFromPoint`、`GetMonitorInfo` 和 `EnumDisplaySettingsEx` 跟随光标显示器刷新率，并受 Auto/60/90/120 FPS 上限约束。Picker 的鼠标/键盘低级 Hook 是独立的输入拦截路径。每次检查：

- `IsWindow(hwnd)` 是否仍有效；
- `GetWindowRect` 是否变化；
- 是否最小化、隐藏或进入其他显示状态；
- 进程 ID 是否仍与快照一致；
- 光标是否位于目标窗口的最新屏幕区域。

窗口失效时发布 `TargetClosed` 或 `TargetInvalid`，由协调器进入恢复/清理路径。窗口移动或尺寸改变时通知 `VisibilityEngine` 重新计算区域。

### 7.3 `TriggerEngine`

职责：接收快捷键、Peek Key 和光标状态，输出无副作用的意图事件。

当前事件意图包括：

```text
PickRequested
GhostToggleRequested
PeekPressed
PeekReleased
RestoreRequested
EmergencyRestoreRequested
CursorChanged
SettingsRequested / ExitRequested
RevealDiameterIncreaseRequested / RevealDiameterDecreaseRequested
```

切换快捷键使用 `RegisterHotKey`/`WM_HOTKEY`。Hold-to-Peek 需要知道按键按下和抬起状态，V0.1 可采用低级键盘 Hook 或受控的键状态轮询；输入层必须提供去抖和重复事件抑制。TriggerEngine 不直接调用 `SetWindowRgn`。

鼠标位置和 Peek 按键状态使用 `GetCursorPos`/键状态的定时轮询；`WH_MOUSE_LL` 和 `WH_KEYBOARD_LL` 当前仅在 Picker 活跃期间用于选择点击和 Esc 取消拦截。

### 7.4 `VisibilityEngine`

职责：将领域状态转换为对目标窗口的最小、可恢复的 Win32 修改。

它是窗口 region、style、DWM 属性和显示/隐藏操作的唯一入口。当前实现的主要路径是：

```text
Normal  → Ghost       保存快照后使用 SW_HIDE，并应用临时恢复所需的窗口属性
Ghost   → Reveal      显示目标并应用所选形状的 region；可选显示 Composition 外环
Reveal  → Ghost       隐藏目标并移除 Reveal visual
Ghost   → Visible     临时完整显示目标，但保留恢复资料
Ghost   → Normal      清除 GhostSlacking 修改并恢复快照
```

核心可见性使用 `SW_HIDE` + `SetWindowRgn`，不使用整窗 alpha。Composition overlay 是 Reveal 外环的独立视觉层，失败时回退到硬边 region。

### 7.5 `RegionEngine`

职责：根据目标窗口最新屏幕矩形、光标屏幕坐标和设置生成窗口本地坐标的圆形、矩形或圆角矩形区域。

```text
cursorScreen = GetCursorPos()
windowScreen = GetWindowRect(hwnd)
centerLocal  = cursorScreen - windowScreen.TopLeft
radius       = diameter / 2
region       = RegionEngine.CreateReveal(
                  centerLocal, diameter, shape, cornerRadius)
SetWindowRgn(hwnd, region, true)
```

需要注意：

- 圆形区域应与窗口矩形求交，避免无意义的大区域；
- 区域坐标必须与 `SetWindowRgn` 所要求的窗口本地坐标一致；
- 成功传给 `SetWindowRgn` 后，region 句柄的所有权由系统接管；失败时才由调用方释放；
- 旧 region 的更新频率应与光标变化绑定，光标未变时不重复创建；
- `SetWindowRgn` 负责可靠的二值裁剪；首次显示保留显示前后两次 region 应用、同步重绘和 placement 校验，连续移动只应用并校验一次增量 region；
- 启用羽化时，目标内容 region 外扩到设置宽度的 70%，独立的鼠标穿透 Composition overlay 在原始清晰 Reveal 外侧实时模糊并淡出，不改变目标窗口输入。

### 7.6 `RecoveryManager`

职责：保存、校验和恢复 GhostSlacking 修改过的窗口状态。

第一次修改前至少保存：

- HWND 和目标进程 ID；
- 初始屏幕矩形、可见状态和最小化状态；
- 原始 window region（无 region 也要记录）；
- 与本功能有关的 style/ex-style、完整 `WINDOWPLACEMENT`、进程启动身份和相关 DWM 属性；
- 快照版本、创建时间和恢复原因。

恢复路径包括：手动 Restore、切换目标、目标进程退出、正常退出、应用退出事件、未处理异常和 Watchdog 恢复。恢复操作需逐项记录结果，即使其中一项失败也继续尝试其他项。

### 7.7 `TrayHost` 与设置 UI

Avalonia `TrayIcon` 承载常驻入口。托盘菜单至少包含：

```text
Pick Window
Toggle Ghost
Restore Window
Restore All Windows
Settings
Exit
```

设置窗口使用 Avalonia + FluentAvalonia 控件，暴露 Peek Key、最大 Peek 帧率、Reveal Diameter、直径快捷键与步长、软边宽度、Reveal 形状、Peek 模式、全部全局功能快捷键、开机启动、退出时恢复和日志级别。托盘、设置、提示浮层和主业务协调器运行在同一个 Avalonia STA UI 线程；主业务状态不存放在窗口控件中。

### 7.8 `Watchdog`

Watchdog 不属于 Phase 0/Phase 1 的核心闭环；当前已作为独立进程实现 Phase 2 的 v1 恢复协议：

```text
GhostSlacking.App.exe ── heartbeat + recovery manifest ──► GhostSlacking.Watchdog.exe
       │                                                   │
       └── 正常退出并清理                                  └── 心跳超时则执行恢复
```

Watchdog 只能恢复它理解且验证过的状态，不能盲目对所有同名窗口操作。恢复清单必须包含 HWND、进程 ID、进程启动标识、受支持的快照版本、placement、region、style 和 DWM 恢复字段。

## 8. 状态机

### 8.1 应用级状态

```text
Idle
  │ PickRequested
  ▼
Picking ── Cancel/Error ──► Idle
  │ WindowSelected
  ▼
Preparing ── Snapshot/Apply failure ──► Recovering ──► Idle/Error
  │ success
  ▼
Ghost
  │ PeekPressed + cursor in target bounds
  ▼
Reveal
  │ CursorChanged
  ├──────────────► Reveal (更新所选形状的 region)
  │ PeekReleased / cursor leaves / target invalid
  ▼
Ghost
  │ WindowVisibilityRequested
  ▼
Visible ── WindowVisibilityRequested ──► Ghost
  │ RestoreRequested / Exit / TargetClosed
  ▼
Restoring ── success ──► Idle
       └── partial failure ──► RecoveryError
```

### 8.2 状态不变量

| 状态          | 目标窗口要求        | 允许的修改                    | 退出条件          |
| ----------- | ------------- | ------------------------ | ------------- |
| `Idle`      | 无活动目标或已恢复     | 无                        | Pick          |
| `Picking`   | 不修改目标         | Picker 高亮层可见             | 选择/取消         |
| `Preparing` | 快照已开始         | 只允许一次性应用                 | 成功/失败         |
| `Ghost`     | HWND 有效，默认不可见 | 可更新检测信息                  | Peek/Restore  |
| `Reveal`    | HWND 有效       | 更新所选形状的 region 和可选视觉层    | Release/离开/失效 |
| `Visible`   | HWND 有效，完整显示  | 保留恢复资料，不应用 Reveal region | 显隐切换/Restore  |
| `Restoring` | 允许目标已关闭       | 恢复原始状态                   | 完成/失败         |

禁止并发执行 `ApplyGhost`、`ApplyReveal` 和 `Restore`。所有窗口修改在同一协调上下文串行执行，避免光标更新与恢复交叉覆盖。

## 9. Win32/PInvoke 技术方案

### 9.1 User32 API

当前实现使用：

| API/消息                                      | 用途                                  |
| ------------------------------------------- | ----------------------------------- |
| `WindowFromPoint`                           | 根据屏幕点获取窗口                           |
| `GetAncestor`                               | 获取顶层窗口                              |
| `GetWindowRect`                             | 获取屏幕坐标矩形                            |
| `GetClientRect`                             | 辅助检查客户区尺寸                           |
| `IsWindow` / `IsWindowVisible`              | 生命周期和可见性检查                          |
| `IsIconic`                                  | 判断最小化                               |
| `GetWindowThreadProcessId`                  | 绑定进程身份                              |
| `GetCursorPos`                              | 获取鼠标屏幕坐标                            |
| `SetWindowRgn`                              | 应用/清除窗口区域                           |
| `GetWindowRgn`                              | 读取原始区域信息                            |
| `GetWindowPlacement` / `SetWindowPlacement` | 保存/恢复普通、最大化和最小化状态                   |
| `Windows.UI.Composition` / Win2D            | 绘制鼠标穿透的实时背景模糊和羽化遮罩                  |
| `CreateDispatcherQueueController`           | 为 Avalonia UI 线程建立 Composition 调度队列 |
| `RegisterHotKey`                            | 注册全局组合快捷键                           |
| `UnregisterHotKey`                          | 注销快捷键                               |
| `GetWindowLongPtr` / `SetWindowLongPtr`     | 读取/恢复必要 style                       |
| `ShowWindow`                                | 必要时隐藏/恢复显示状态                        |
| `GetLastError`                              | 获取失败原因                              |

消息循环通过 Avalonia 主线程承载 `WM_HOTKEY` 和定时器回调。P/Invoke 声明应集中在 `Win32NativeMethods`，返回值统一封装为可诊断的结果类型。

### 9.2 GDI region API

V0.1 主要使用：

- `CreateEllipticRgn` 创建圆形 region；
- `CreateRectRgn`/`CombineRgn` 做边界求交（需要时）；
- `GetRgnBox` 获取区域包围盒；
- `GetRegionData` 或等价方式复制原始 region 数据；
- `DeleteObject` 释放创建失败或尚未移交的 GDI 对象。

region 句柄封装为 `SafeHandle` 更安全。每次更新都必须有明确的所有权分支：创建失败、`SetWindowRgn` 失败、`SetWindowRgn` 成功分别如何释放。

### 9.3 关于 `SetWindowRgn` 的边界

`SetWindowRgn` 直接裁剪窗口可见区域，不需要截图或目标进程注入。但它有明确限制：

- 边缘是硬切，不支持羽化；
- 不同应用、自绘窗口和复杂合成内容可能表现不同；
- region 频繁更新可能造成重绘压力；
- 目标窗口的原始 region 必须可可靠保存和恢复；
- 它不等于安全隐私机制，不能承诺阻止所有捕获路径。

当前羽化不替换上述裁剪机制：应用创建一个非激活、无任务栏项且与目标窗口同 bounds 的 Composition overlay。原始 Reveal 保持完全清晰，目标内容 region 向外扩展到羽化宽度的 70%；一个 alpha 遮罩驱动一个 `CompositionBackdropBrush`，用用户选择的轻/中/强单一模糊半径从清晰边界平滑渐入，并在外侧 30% 淡出。遮罩按形状、尺寸、羽化宽度和模糊量缓存，光标移动只更新小型 visual 的 offset 和“外扩轮廓减去清晰核心”的系统窗口 region，因此窗口边缘裁剪不会重建 Composition 资源，清晰核心的点击和滚轮仍直接到达目标窗口。应用不读取或持久化窗口像素；overlay 或图形设备失败时，协调器立即把目标缩回原始硬边 Reveal。

对目标窗口应用 Reveal region 后会立即读取校验。Chrome/Electron 在重绘消息交错时可能瞬时返回无 region，因此单次更新内部最多重施三次；若一整轮仍无法确认，协调器先安全隐藏目标并在下一 tick 重试，而不是立即向用户报错。连续三轮均失败才视为持续故障。

## 10. Window Picker 细节

Picker 需要避免把 GhostSlacking 自己的提示窗选为目标。推荐以屏幕点为中心按以下顺序处理：

Picker 活跃期间同时安装鼠标与键盘低级钩子；Esc 的按下、自动重复和抬起均由应用拦截，取消流程只执行一次并恢复进入 Picker 前的领域状态。

```text
WindowFromPoint
  ↓
排除 GhostSlacking HWND / owned helper window
  ↓
GetAncestor(GA_ROOT)
  ↓
验证可见、可恢复、非桌面/任务栏
  ↓
读取 rect、PID、进程名
  ↓
用户点击确认
```

高亮可以先做为边框提示，且必须是临时 UI。不要让高亮窗覆盖目标的输入区域，避免干扰最终点击。

## 11. WindowTracker 细节

Tracker 维护一个 `TrackedWindow`，包含最后一次有效 rect、PID、最小化状态和更新时间。轮询到几何变化时：

1. 更新 profile 的当前 rect；
2. 若处于 `Reveal`，将光标位置重新转换为窗口本地坐标；
3. 仅当中心点或直径变化时重建 region；
4. 若窗口最小化或不可见，暂停 Reveal 更新；
5. 若 HWND 无效或 PID 不匹配，进入恢复清理。

不要依据窗口标题作为身份。窗口标题会变，HWND 也可能被系统复用，因此恢复前必须再次验证 HWND、PID 和进程启动身份；当前恢复路径已经执行这三项校验。

## 12. TriggerEngine 细节

默认建议沿用产品设计：

```text
Ctrl + Alt + P   进入 Picker（可配置）
Ctrl + Alt + G   切换窗口隐藏与完整显示；无目标时进入 Picker（可配置）
Peek Key         Hold-to-Peek 或按下切换（可配置）
Ctrl + Alt + R   Restore 当前窗口（可配置）
Ctrl + Shift + Alt + R  Emergency Restore All（可配置）
Ctrl + Alt + S   打开设置（可配置）
Ctrl + Alt + Q   退出（可配置）
```

当前默认键位为 `P/G/R/S/Q` 的 `Ctrl+Alt` 组合，Peek 默认使用 `Alt` 且默认按下切换；所有快捷键均可在设置中捕获、清除或恢复默认，并对重复或注册失败提供反馈。

Hold-to-Peek 的判定条件为：

```text
ghostWindow != null
AND peekKeyDown
AND cursor 在当前目标屏幕矩形内
AND targetWindow 有效
```

未满足条件时保持 Ghost。按键抬起事件必须优先于下一次光标更新执行，目标是快速清除 Reveal。

## 13. Visibility/RegionEngine 细节

### 13.1 Ghost 策略

Ghost 的当前实现固定采用 `SW_HIDE`，并在 Reveal 前后配合 region 和 placement 校正：

1. 使用 `ShowWindow(SW_HIDE)` 隐藏目标；
2. Reveal 时先应用所选形状的 region，再显示目标并再次校验 region；
3. 对窗口帧、DWM 属性和 TopMost 状态做临时控制，Restore 时还原快照。

产品要求是“默认不可见”。`SW_HIDE` 对当前实现的 Ghost 路径提供确定的隐藏语义，但不同窗口仍需验证视觉效果、点击、焦点、重绘、恢复和窗口移动。

实现上不使用空 region 作为 Ghost 的主要机制，因为部分窗口会在空 region 和重绘之间出现不稳定行为；Reveal region 失败时先安全隐藏并按重试策略处理，连续失败才报告错误。

### 13.2 Reveal 更新

```text
每个 tick:
  if cursor 未变化且 rect 未变化: return
  if 不在 targetRect: ApplyGhost()
  else:
      local = ScreenToTargetLocal(cursor, targetRect)
      rgn = RegionEngine.CreateReveal(local, diameter, shape, cornerRadius)
      ApplyReveal(rgn)
```

更新需要避免无意义的 `SetWindowRgn` 调用。当前对 Reveal region 做立即读取校验；遇到 Chrome/Electron 等窗口的瞬时重绘竞态时，单轮最多重施三次，失败后安全隐藏并在下一 tick 重试，连续三轮失败才通知用户。

### 13.3 交互假设

region 裁剪有机会让清晰核心内的目标窗口继续接收鼠标输入；羽化 overlay 只覆盖外环并保持鼠标穿透。焦点、拖拽、鼠标离开事件和不同窗口的重绘行为仍需用真实应用验证，不把所有应用视为等价。

## 14. Recovery/Watchdog

### 14.1 主进程恢复

应用退出前按以下顺序执行（正常退出遵循 `RestoreOnExit`；Emergency Restore 和关闭请求强制恢复）：

```text
停止输入与 tracker
  ↓
禁止新状态转换
  ↓
按设置执行 RestoreAll()
  ↓
注销热键、释放 region/GDI 资源
  ↓
写入退出日志
  ↓
退出 Avalonia 消息循环
```

`Dispatcher.UIThread.UnhandledException`、`AppDomain.UnhandledException`、进程退出事件只能作为尽力而为的最后防线，不能代替 Watchdog。

### 14.2 Watchdog 恢复协议

Phase 2 的 Watchdog v1 使用仅限当前用户的本地命名管道和逐行 JSON 消息。主进程每次启动生成随机会话 ID，并按以下顺序发送：

```text
Hello(protocolVersion, sessionId, main PID/start identity)
  ↓ HelloAcknowledged
RecoveryManifest(schemaVersion, manifestId, targets[])
  ↕ Heartbeat
ShutdownCompleted
```

每个 envelope 都携带协议版本、会话 ID、消息类型和发送时间。Watchdog 拒绝版本不兼容、会话不匹配、握手前消息、清单 schema 不兼容和正常关闭后的追加消息。恢复清单只包含恢复窗口原状所需的元数据：HWND、PID、进程启动标识、快照版本、region 数据、style/ex-style、可见/最小化状态和相关 DWM 属性，不包含窗口内容。

心跳超时后，Watchdog：

1. 读取最后一份清单；
2. 验证目标进程仍存在且 PID/启动身份匹配；
3. 逐个执行支持的恢复操作；
4. 记录成功、失败和跳过原因；
5. 清理清单，避免重复处理旧目标。

同一 `(sessionId, manifestId)` 只执行一次；超时取出清单后立即从会话状态移除，正常关闭也清空最后清单。如果验证失败，宁可逐项跳过并记录明确原因，不根据窗口标题或旧 HWND 猜测目标。当前 v1 清单由独立 Watchdog 进程保存在内存中，不写入磁盘；Watchdog 日志写入本地日志目录。

## 15. DPI 与多显示器

### 15.1 进程 DPI 设置

应用 manifest 已声明 `Per Monitor DPI Aware V2`，避免不同显示器缩放比例下出现虚拟化坐标。Avalonia DIP 和 Win32 屏幕物理像素的职责要分开。

### 15.2 坐标约定

GhostSlacking 内部统一使用屏幕物理像素表达目标 rect、光标位置和 Reveal 直径。转换路径固定为：

```text
GetCursorPos()                 屏幕坐标
GetWindowRect()                屏幕坐标
cursor - windowRect.TopLeft    目标窗口本地坐标
CreateEllipticRgn()             region 本地坐标
```

严禁在核心计算中混用 Avalonia DIP、Win32 物理像素或未经标注的缩放值。

### 15.3 测试组合

至少验证：

- 单屏 100%、125%、150%；
- 左侧/上方存在负坐标显示器；
- 两台显示器不同缩放比例；
- 窗口跨显示器移动；
- 光标位于窗口边缘和显示器边缘；
- DPI 变化后重新计算 region。

## 16. 权限与安全边界

GhostSlacking 默认普通用户权限运行，不默认要求管理员权限。控制更高完整性级别的窗口可能因 UIPI 或窗口策略失败，失败时显示可理解的提示：目标窗口以更高权限运行，需用户明确选择以相同权限重启。

安全边界：

- 不读取目标窗口内容，不注入目标进程；
- 不保存截图或聊天内容；
- 配置和日志只写入用户本地应用数据目录；
- Watchdog 恢复清单只保存恢复所需的窗口元数据；
- 不将功能宣传为防录屏或防监控。

## 17. 性能设计

目标值：

| 项目            | V0.1 目标          |
| ------------- | ----------------:|
| 空闲 CPU        | 接近 0%            |
| Ghost 状态 CPU  | 通常低于 1%          |
| Reveal 状态 CPU | 通常低于 3%          |
| 内存            | 小于 100 MB        |
| 启动时间          | 通常小于 1 秒         |
| 光标响应          | Reveal 跟随显示器，最高 120 Hz；其他状态 60 Hz |

性能原则：

- 不截图、不做 OCR、不引入浏览器运行时；
- 光标和 rect 未变化时不重建 region；
- 首次 Reveal 使用完整兼容路径，连续移动使用单次 region 增量更新；
- Composition 遮罩与屏幕位置解耦，连续移动只更新 visual offset 和环形命中 region；
- P/Invoke 失败不进入高频重试死循环；
- 高频成功路径不逐帧写日志；Debug 模式每 120 个有效移动帧汇总有效 FPS、平均/P95/最大 tick 耗时和超预算帧数；
- 显示模式查询按显示器缓存 2 秒，`WM_DISPLAYCHANGE` 立即失效缓存；刷新率不可用时安全回退到 60 Hz；
- 用限频计数器和手工矩阵测量 `SetWindowRgn` 频率、tick 耗时和失败率。

## 18. 错误处理

错误分为三类：

1. 用户可修复：快捷键冲突、目标权限不足、选择了不支持窗口；
2. 可恢复运行错误：目标窗口关闭、region 应用失败、窗口状态发生变化；
3. 严重错误：恢复失败、IPC/Watchdog 异常、内部状态不一致。

每次原生调用失败都记录 API、参数摘要、错误码、目标 HWND/PID 和当前状态。不要在日志中记录窗口内容、键盘输入内容或不必要的个人数据。

应用层收到错误后的原则：

```text
修改前失败  → 保持原状并提示
修改中失败  → 立即尝试 Restore
恢复部分失败 → 继续其他项，显示 RecoveryError
连续失败    → 停止自动重试，等待用户操作
```

## 19. 配置与日志

### 19.1 配置

配置文件建议使用 `%LOCALAPPDATA%\GhostSlacking\settings.json`，只保存：

- 快捷键定义；
- Peek Key；
- 最大 Peek 帧率（Auto/60/90/120，Auto 最高 120 FPS）；
- Reveal 直径、快捷键调整步长、软边宽度和形状；
- 开机启动、退出恢复和日志级别；
- 以后版本的 schema version。

V0.1 不持久化窗口内容，也不默认持久化目标 HWND。若未来支持应用 profile，应以进程名/应用标识为主，并重新验证窗口身份。

### 19.2 日志

日志级别：`Error`、`Warning`、`Info`、`Debug`。生产默认 `Info`，设置保存后立即更新主程序过滤级别；Watchdog 固定记录 `Info` 及以上的恢复审计。高频状态转换和采样性能数据使用 `Debug`，成功路径不逐帧写入。

主程序和 Watchdog 使用同一滚动实现、不同文件组。每个文件最大 2 MB，每组包含当前文件和最多 4 个备份，并清理超过 30 天的备份；滚动失败且当前文件已满时丢弃新记录，避免无限增长，同时绝不让日志错误影响恢复路径。

设置页可分别导出日志 ZIP 和已保存配置 JSON。日志 ZIP 只收集 GhostSlacking 管理的日志，并附带版本、操作系统、运行时、架构和日志级别摘要；不自动包含配置、用户名、机器名、窗口标题或其他环境数据。导出只由用户主动触发，不包含上传行为。

关键事件：`PickStarted`、`WindowSelected`、`SnapshotSaved`、`GhostApplied`、`RevealApplied`、`RestoreStarted`、`RestoreCompleted`、`NativeCallFailed`、`TargetClosed`、`WatchdogRecovery`。

## 20. 测试边界

### 20.1 必测窗口

- 记事本或其他标准 Win32 窗口；
- 文件资源管理器；
- Chrome/Edge；
- VS Code 或其他 Electron 窗口；
- 微信、Discord、Telegram、Slack 中至少一种；
- 一个需要管理员权限的窗口；
- 一个最小化、关闭和频繁调整大小的窗口。

### 20.2 明确记录但不承诺的窗口

- 全屏游戏、独占 DirectX/OpenGL 窗口；
- 受 DRM/受保护内容窗口；
- 具有特殊 overlay 或始终置顶策略的窗口；
- UWP/系统安全桌面窗口；
- 运行在更高权限或不同用户会话的窗口。

### 20.3 单元测试与手工测试分工

纯状态机、坐标计算、边界求交、配置校验、恢复计划生成可做自动化测试。真实 HWND 生命周期、DPI、焦点、region 重绘和权限必须通过 Windows 集成/手工测试验证。

## 21. Composition 羽化与后续演进

`SetWindowRgn` 继续承担可恢复的核心裁剪，Windows Composition 只负责原始 Reveal 外侧的非抓屏实时毛玻璃。当前路径为：

```text
V0.1  SetWindowRgn                  硬边 Reveal 和原生交互
  +
V0.1  Windows Composition overlay   外扩实时模糊、透明羽化、硬边回退
  ↓
后续  可替换视觉宿主                 不改变 region、交互和恢复契约
```

演进时应保持不变的接口：

- `VisibilityEngine` 的状态和命令；
- `RegionEngine`/`RevealGeometry` 的几何输入输出；
- `RecoveryManager` 的恢复契约；
- `TriggerEngine` 的用户交互事件。

`IRevealVisualHost` 隔离可选 GPU 视觉层，`IVisibilityBackend` 继续负责窗口 region/style/尺寸和恢复。Composition 不是恢复链路的必要依赖，任何初始化或设备失败都必须回退到原始硬边 Reveal；后续若评估 DWM Thumbnail 或 live clone，也只能替换视觉宿主，不能绕过快照与恢复契约。

## 22. 架构验收条件

当前实现已满足的架构条件：

- 目标窗口的 region 应用、跟随和恢复已沉淀到 Core/Platform/App 实现；
- 核心状态机可以在不启动 Avalonia 的情况下测试；
- 所有原生资源有明确的拥有者和释放路径；
- 目标窗口身份至少绑定 HWND + PID，恢复流程可拒绝不匹配对象；
- DPI、多显示器和权限边界已写入测试矩阵，但真实环境验收仍未完成；
- GPU 羽化与恢复链路边界明确，未引入截图或目标进程注入架构。

自动化构建和测试通过不代表真实窗口验收完成；Windows 版本、DPI、权限、重绘、Composition 设备和长时间运行结果必须继续记录在实施状态文档中。
