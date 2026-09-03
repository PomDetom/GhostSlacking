# GhostSlacking 技术架构设计

> 版本：V0.1 设计基线  
> 平台：Windows 10/11  x64  
> 技术路线：C# + .NET + WinForms + Win32 P/Invoke  
> V0.1 渲染路线：`SetWindowRgn` 硬边区域裁剪

## 1. 文档目的

本文将 GhostSlacking 的产品设计转换为可实施的技术边界和运行模型，重点回答：

- 怎样选择并识别目标窗口；
- 怎样让窗口进入 Ghost 状态并在 Peek 时只显示光标附近区域；
- 怎样保证移动、缩放、最小化、关闭和异常退出时可恢复；
- 怎样处理 Win32 坐标、DPI、多显示器和权限差异；
- 哪些能力属于 V0.1，哪些风险留给后续版本。

本文不是对所有 Windows 窗口类型的兼容性承诺。V0.1 以普通顶层桌面窗口为目标，先验证核心体验和恢复能力。

## 2. 目标与非目标

### 2.1 目标

V0.1 应实现以下闭环：

1. 托盘程序启动，不显示常驻主窗口；
2. 用户通过快捷键进入 Window Picker，点击一个普通顶层窗口；
3. 保存必要的原始窗口状态后，目标窗口进入 Ghost 状态；
4. 按住 Peek Key 且光标位于目标窗口原始屏幕区域内时，显示一个随光标移动的圆形 Reveal 区域；
5. Reveal 区域尽量保留目标窗口的正常鼠标点击和滚动行为；
6. 松开 Peek Key 后立即回到 Ghost；
7. 用户可以通过 Restore 快捷键或托盘菜单恢复原窗口；
8. 目标窗口关闭、程序退出或操作失败时，不遗留不可恢复的窗口修改。

### 2.2 非目标

V0.1 不包含：

- 多窗口同时 Ghost；
- 云同步、账户、插件和复杂规则系统；
- OCR、内容识别、截图录制或远程控制；
- GPU Shader、软边渐变、模糊和复杂动画；
- 对 DirectX 独占窗口、受保护内容或所有管理员窗口的兼容承诺；
- “防录屏”“防监控”或安全级别的隐私保证；
- 通过注入、Hook 目标进程或修改目标应用业务逻辑来实现功能。

## 3. 设计原则

### 3.1 单一修改入口

只有 `VisibilityEngine` 可以修改目标窗口的 region、alpha、style 或 visibility。其他模块只产生意图或状态事件，不能直接调用这些 Win32 修改 API。

### 3.2 先保存、后修改

任何第一次修改目标窗口前，`RecoveryManager` 必须完成原始状态快照。快照失败时不进入 Ghost 状态。

### 3.3 可恢复优先于功能完整

任何路径都应尽量导向 `RestoreAll()`。恢复操作应幂等：重复调用不会把窗口置于更坏状态。

### 3.4 V0.1 保持简单

使用一个 UI/消息线程、一个轻量光标轮询定时器和少量 Win32 API。先解决行为正确性，再引入更复杂的输入 Hook 或 GPU 合成。

## 4. 系统上下文

```text
┌──────────────────────┐       Win32 User/GDI/DWM API       ┌─────────────────────┐
│   GhostSlacking.exe  │ ─────────────────────────────────> │ Windows 桌面窗口系统 │
│  WinForms Tray Host  │                                    └─────────┬───────────┘
└─────────┬────────────┘                                              │
          │                                                           │ HWND
          │ 用户输入                                                   ▼
┌─────────▼────────────┐                                    ┌─────────────────────┐
│ 键盘、鼠标、显示器、托盘 │                                    │ 目标应用窗口         │
└──────────────────────┘                                    │ Chrome/微信/记事本等 │
                                                            └─────────────────────┘

        ┌──────────────────────┐
        │ 可选 Watchdog 进程    │ ← 心跳/恢复协议 → GhostSlacking.exe
        └──────────────────────┘
```

外部依赖仅限于 Windows 原生窗口系统、用户输入和本地文件系统。V0.1 不需要网络服务、数据库或目标应用 SDK。

## 5. 运行时结构

建议采用一个主项目和一个后续可选的 Watchdog 项目：

```text
src/
  GhostSlacking.App/          WinForms 托盘、设置、消息循环
  GhostSlacking.Core/         状态机、领域模型、服务接口
  GhostSlacking.Platform/     Win32 P/Invoke 和 Windows 适配器
  GhostSlacking.Watchdog/     Phase 2 后加入的最小恢复进程
tests/
  GhostSlacking.Core.Tests/
  GhostSlacking.Platform.Tests/
  GhostSlacking.ManualTests/
```

V0.1 可以先将 `Core` 和 `Platform` 放在同一解决方案中，但接口边界应先确定，避免 Win32 调用扩散到 UI 和业务代码。

### 5.1 依赖方向

```text
App ───────────────► Core ─────────────► 抽象接口
 │                    ▲
 └──────────────► Platform ─────────────┘

Watchdog ───────────► Platform/Recovery 协议
```

`Core` 不引用 WinForms；`Platform` 实现 `Core` 定义的接口；`App` 负责组合依赖、托盘菜单、消息循环和用户反馈。

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
    IntPtr OriginalRegionData;
    WindowStyleSnapshot Styles;
}

RevealSettings
{
    int DiameterPx;       // 以物理屏幕像素表达
    RevealShape Shape;    // Circle / Rectangle / RoundedRectangle
    PeekTrigger Trigger;  // V0.1 主要为 Hold
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

V0.1 使用 30–60 Hz 的轻量定时检查即可，不先引入全局窗口事件 Hook。每次检查：

- `IsWindow(hwnd)` 是否仍有效；
- `GetWindowRect` 是否变化；
- 是否最小化、隐藏或进入其他显示状态；
- 进程 ID 是否仍与快照一致；
- 光标是否位于目标窗口的最新屏幕区域。

窗口失效时发布 `TargetClosed` 或 `TargetInvalid`，由协调器进入恢复/清理路径。窗口移动或尺寸改变时通知 `VisibilityEngine` 重新计算区域。

### 7.3 `TriggerEngine`

职责：接收快捷键、Peek Key 和光标状态，输出无副作用的意图事件。

建议事件：

```text
PickRequested
GhostToggleRequested
PeekPressed
PeekReleased
RestoreRequested
EmergencyRestoreRequested
CursorChanged
```

切换快捷键使用 `RegisterHotKey`/`WM_HOTKEY`。Hold-to-Peek 需要知道按键按下和抬起状态，V0.1 可采用低级键盘 Hook 或受控的键状态轮询；输入层必须提供去抖和重复事件抑制。TriggerEngine 不直接调用 `SetWindowRgn`。

鼠标位置优先使用 `GetCursorPos` 的定时轮询。只有在性能或交互验证不足时，才考虑 `WH_MOUSE_LL`。

### 7.4 `VisibilityEngine`

职责：将领域状态转换为对目标窗口的最小、可恢复的 Win32 修改。

它是窗口 region、透明度、style、显示/隐藏操作的唯一入口。V0.1 只需要：

```text
Normal  → Ghost       移除可见区域/应用隐藏策略
Ghost   → Reveal      应用圆形 region
Reveal  → Ghost       应用空 region 或隐藏策略
Ghost   → Normal      清除 GhostSlacking 修改并恢复快照
```

V0.1 首选 region 方案。不要把“整个窗口 `alpha=0`”和 region 方案混在一次状态转换中，除非实验明确证明目标窗口需要组合策略；组合修改会增加恢复复杂度。

### 7.5 `RegionEngine`

职责：根据目标窗口最新屏幕矩形和光标屏幕坐标生成窗口本地坐标的圆形区域。

```text
cursorScreen = GetCursorPos()
windowScreen = GetWindowRect(hwnd)
centerLocal  = cursorScreen - windowScreen.TopLeft
radius       = diameter / 2
region       = CreateEllipticRgn(
                 centerLocal.X - radius,
                 centerLocal.Y - radius,
                 centerLocal.X + radius,
                 centerLocal.Y + radius)
SetWindowRgn(hwnd, region, true)
```

需要注意：

- 圆形区域应与窗口矩形求交，避免无意义的大区域；
- 区域坐标必须与 `SetWindowRgn` 所要求的窗口本地坐标一致；
- 成功传给 `SetWindowRgn` 后，region 句柄的所有权由系统接管；失败时才由调用方释放；
- 旧 region 的更新频率应与光标变化绑定，光标未变时不重复创建；
- V0.1 是硬边，直径和形状只做最少设置项。

### 7.6 `RecoveryManager`

职责：保存、校验和恢复 GhostSlacking 修改过的窗口状态。

第一次修改前至少保存：

- HWND 和目标进程 ID；
- 初始屏幕矩形、可见状态和最小化状态；
- 原始 window region（无 region 也要记录）；
- 与本功能有关的 style/ex-style 和 alpha 状态；
- 快照版本、创建时间和恢复原因。

恢复路径包括：手动 Restore、切换目标、目标进程退出、正常退出、应用退出事件、未处理异常和 Watchdog 恢复。恢复操作需逐项记录结果，即使其中一项失败也继续尝试其他项。

### 7.7 `TrayHost` 与设置 UI

`NotifyIcon` 承载常驻入口。托盘菜单至少包含：

```text
Pick Window
Toggle Ghost
Restore Window
Restore All Windows
Settings
Exit
```

设置窗口保持 WinForms 原生控件，V0.1 只暴露 Peek Key、Reveal Diameter、开机启动、退出时恢复和日志级别。主业务状态不应存放在窗体控件中。

### 7.8 `Watchdog`

Watchdog 不属于 Phase 0/Phase 1 的核心闭环。Phase 2 再实现为最小独立进程：

```text
GhostSlacking.exe ── heartbeat + recovery manifest ──► Watchdog.exe
       │                                                   │
       └── 正常退出并清理                                  └── 心跳超时则执行恢复
```

Watchdog 只能恢复它理解且验证过的状态，不能盲目对所有同名窗口操作。恢复清单必须包含 HWND、进程 ID、进程启动标识和受支持的快照版本。

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
  ├──────────────► Reveal (更新 region)
  │ PeekReleased / cursor leaves / target invalid
  ▼
Ghost
  │ RestoreRequested / Exit / TargetClosed
  ▼
Restoring ── success ──► Idle
       └── partial failure ──► RecoveryError
```

### 8.2 状态不变量

| 状态 | 目标窗口要求 | 允许的修改 | 退出条件 |
|---|---|---|---|
| `Idle` | 无活动目标或已恢复 | 无 | Pick |
| `Picking` | 不修改目标 | Picker 高亮层可见 | 选择/取消 |
| `Preparing` | 快照已开始 | 只允许一次性应用 | 成功/失败 |
| `Ghost` | HWND 有效，默认不可见 | 可更新检测信息 | Peek/Restore |
| `Reveal` | HWND 有效 | 更新圆形 region | Release/离开/失效 |
| `Restoring` | 允许目标已关闭 | 恢复原始状态 | 完成/失败 |

禁止并发执行 `ApplyGhost`、`ApplyReveal` 和 `Restore`。所有窗口修改在同一协调上下文串行执行，避免光标更新与恢复交叉覆盖。

## 9. Win32/PInvoke 技术方案

### 9.1 User32 API

V0.1 预计使用：

| API/消息 | 用途 |
|---|---|
| `WindowFromPoint` | 根据屏幕点获取窗口 |
| `GetAncestor` | 获取顶层窗口 |
| `GetWindowRect` | 获取屏幕坐标矩形 |
| `GetClientRect` | 辅助检查客户区尺寸 |
| `IsWindow` / `IsWindowVisible` | 生命周期和可见性检查 |
| `IsIconic` | 判断最小化 |
| `GetWindowThreadProcessId` | 绑定进程身份 |
| `GetCursorPos` | 获取鼠标屏幕坐标 |
| `SetWindowRgn` | 应用/清除窗口区域 |
| `GetWindowRgn` | 读取原始区域信息 |
| `RegisterHotKey` | 注册全局组合快捷键 |
| `UnregisterHotKey` | 注销快捷键 |
| `GetWindowLongPtr` / `SetWindowLongPtr` | 读取/恢复必要 style |
| `ShowWindow` | 必要时隐藏/恢复显示状态 |
| `GetLastError` | 获取失败原因 |

消息循环通过 WinForms 主线程承载 `WM_HOTKEY` 和定时器回调。P/Invoke 声明应集中在 `Win32NativeMethods`，返回值统一封装为可诊断的结果类型。

### 9.2 GDI region API

V0.1 主要使用：

- `CreateEllipticRgn` 创建圆形 region；
- `CreateRectRgn`/`CombineRgn` 做边界求交（需要时）；
- `GetRgnBox` 获取区域包围盒；
- `GetRegionData` 或等价方式复制原始 region 数据；
- `DeleteObject` 释放创建失败或尚未移交的 GDI 对象。

region 句柄封装为 `SafeHandle` 更安全。每次更新都必须有明确的所有权分支：创建失败、`SetWindowRgn` 失败、`SetWindowRgn` 成功分别如何释放。

### 9.3 关于 `SetWindowRgn` 的边界

`SetWindowRgn` 适合 V0.1，因为它直接裁剪窗口可见区域，不需要截图或目标进程注入。但它有明确限制：

- 边缘是硬切，不支持羽化；
- 不同应用、自绘窗口和复杂合成内容可能表现不同；
- region 频繁更新可能造成重绘压力；
- 目标窗口的原始 region 必须可可靠保存和恢复；
- 它不等于安全隐私机制，不能承诺阻止所有捕获路径。

这些限制必须在验收和产品文案中明确，而不是在 V0.1 中通过额外技术掩盖。

## 10. Window Picker 细节

Picker 需要避免把 GhostSlacking 自己的提示窗选为目标。推荐以屏幕点为中心按以下顺序处理：

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

不要依据窗口标题作为身份。窗口标题会变，HWND 也可能被系统复用，因此恢复前必须再次验证 HWND、PID 和进程启动身份（Phase 2 可加入）。

## 12. TriggerEngine 细节

默认建议沿用产品设计：

```text
Ctrl + Alt + G   进入 Picker / 切换 Ghost
Alt              Hold-to-Peek（可配置）
Ctrl + Alt + R   Restore 当前窗口
Ctrl + Shift + Alt + R  Emergency Restore All
Ctrl + Alt + Q   退出
```

具体默认键位可以在 Phase 0 通过实验调整，但必须避免与常用系统快捷键冲突，并对注册失败提供明确反馈。

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

Ghost 的实现必须通过实验决定采用哪一种最小策略：

1. 应用空 region，使窗口没有可见区域；
2. 使用 `ShowWindow(SW_HIDE)` 隐藏，Reveal 时恢复后应用圆形 region；
3. 在确认交互行为后组合使用。

产品要求是“默认不可见”，不是预先假设某一种 API 对全部目标窗口有效。因此 Phase 0 需分别验证：视觉效果、点击穿透、焦点、重绘、恢复和窗口移动。

实现上建议优先让 region 成为唯一的可见性机制，以保持 Reveal 与 Ghost 的同一条路径；若某些窗口对空 region 处理不稳定，再将隐藏/显示策略作为兼容分支，且单独记录快照。

### 13.2 Reveal 更新

```text
每个 tick:
  if cursor 未变化且 rect 未变化: return
  if 不在 targetRect: ApplyGhost()
  else:
      local = ScreenToTargetLocal(cursor, targetRect)
      rgn = RegionEngine.CreateCircle(local, diameter)
      ApplyReveal(rgn)
```

更新需要避免无意义的 `SetWindowRgn` 调用。若窗口自身会频繁重绘，应提供节流上限和诊断计数。

### 13.3 交互假设

region 裁剪有机会让圆形区域内的目标窗口继续接收鼠标输入，但焦点、拖拽、鼠标离开事件和覆盖窗口行为需要用真实应用验证。V0.1 验收应将“区域内点击/滚轮是否可用”作为兼容性结果记录，不把所有应用都视为等价。

## 14. Recovery/Watchdog

### 14.1 主进程恢复

应用退出前按以下顺序执行：

```text
停止输入与 tracker
  ↓
禁止新状态转换
  ↓
RestoreAll()
  ↓
注销热键、释放 region/GDI 资源
  ↓
写入退出日志
  ↓
退出 WinForms 消息循环
```

`Application.ThreadException`、`AppDomain.UnhandledException`、进程退出事件只能作为尽力而为的最后防线，不能代替 Watchdog。

### 14.2 Watchdog 恢复协议

Phase 2 的 Watchdog 使用本地命名管道或受保护的本地 IPC 接收：启动握手、心跳、恢复清单和正常关闭消息。协议应包含版本号、随机会话 ID 和目标进程身份。

心跳超时后，Watchdog：

1. 读取最后一份清单；
2. 验证目标进程仍存在且 PID/启动身份匹配；
3. 逐个执行支持的恢复操作；
4. 记录成功、失败和跳过原因；
5. 清理清单，避免重复处理旧目标。

如果验证失败，宁可跳过并提示用户，不要根据窗口标题或旧 HWND 猜测目标。

## 15. DPI 与多显示器

### 15.1 进程 DPI 设置

进程应声明 `Per Monitor DPI Aware V2`，避免不同显示器缩放比例下出现虚拟化坐标。WinForms 窗体和 Win32 屏幕坐标的职责要分开。

### 15.2 坐标约定

GhostSlacking 内部统一使用屏幕物理像素表达目标 rect、光标位置和 Reveal 直径。转换路径固定为：

```text
GetCursorPos()                 屏幕坐标
GetWindowRect()                屏幕坐标
cursor - windowRect.TopLeft    目标窗口本地坐标
CreateEllipticRgn()             region 本地坐标
```

严禁在核心计算中混用 WinForms 的逻辑像素、WPF DIP 或未经标注的缩放值。

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

| 项目 | V0.1 目标 |
|---|---:|
| 空闲 CPU | 接近 0% |
| Ghost 状态 CPU | 通常低于 1% |
| Reveal 状态 CPU | 通常低于 3% |
| 内存 | 小于 100 MB |
| 启动时间 | 通常小于 1 秒 |
| 光标响应 | 30–60 Hz，有效更新时刷新 |

性能原则：

- 不截图、不做 OCR、不引入浏览器运行时；
- 光标和 rect 未变化时不重建 region；
- P/Invoke 失败不进入高频重试死循环；
- 日志异步或批量写入，不能阻塞消息线程；
- Phase 0 用计数器测量 `SetWindowRgn` 频率、tick 耗时和失败率。

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
- Reveal 直径和形状；
- 开机启动、退出恢复和日志级别；
- 以后版本的 schema version。

V0.1 不持久化窗口内容，也不默认持久化目标 HWND。若未来支持应用 profile，应以进程名/应用标识为主，并重新验证窗口身份。

### 19.2 日志

日志级别：`Error`、`Warning`、`Info`、`Debug`。生产默认 `Info`。采用滚动文件并限制大小，记录状态转换和恢复结果，不记录敏感内容。

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

## 21. 后续 DirectComposition 演进

V0.1 的 `SetWindowRgn` 是验证产品闭环的最短路径，不是最终视觉上限。后续若硬边和频繁 region 更新成为明显瓶颈，可演进为：

```text
V0.1  SetWindowRgn                  硬边、低复杂度、先验证交互
  ↓
V0.5  Layered Window / Alpha Mask   支持透明渐变和软边
  ↓
V1.x  DirectComposition             GPU 合成、平滑动画、多视觉层
```

演进时应保持不变的接口：

- `VisibilityEngine` 的状态和命令；
- `RegionEngine`/`RevealGeometry` 的几何输入输出；
- `RecoveryManager` 的恢复契约；
- `TriggerEngine` 的用户交互事件。

只替换 `IVisibilityBackend`，而不是重写产品状态机。DirectComposition、DWM Thumbnail 或 live clone 只能作为后续渲染后端，不能提前成为 V0.1 的必要依赖。

## 22. 架构验收条件

架构在进入 Phase 1 前应满足：

- 能用一个小型技术 Spike 验证目标窗口的 region 应用、跟随和恢复；
- 核心状态机可以在不启动 WinForms 的情况下测试；
- 所有原生资源有明确的拥有者和释放路径；
- 目标窗口身份至少绑定 HWND + PID，恢复流程可拒绝不匹配对象；
- DPI、多显示器和权限边界已写入测试矩阵；
- V0.1 与 DirectComposition 的边界明确，未引入不必要的 GPU/截图架构。
