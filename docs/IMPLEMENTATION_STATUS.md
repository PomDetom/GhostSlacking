# Implementation status

> 更新基准：2026-09-11，依据当前工作区代码、解决方案项目清单和 Debug/Release 构建测试结果整理。代码已实现不等于真实窗口兼容性验收已完成；后者仍以 Windows 手工矩阵为准。

## 当前总览

| 阶段 | 当前状态 | 说明 |
|---|---|---|
| Phase 0 Technical Spike | 实现基线已完成，手工验收待补 | `SetWindowRgn`、坐标、拾取、交互和恢复路径已沉淀到当前实现；没有单独保留 Spike 工程。 |
| Phase 1 Ghost Core | 已完成实现 | 单窗口闭环、托盘宿主、设置、快捷键和自动化测试已在当前解决方案中。 |
| Phase 2 Reliability | Watchdog v1 已完成，其余进行中 | 异常恢复协议和独立进程已落地；注销/关机、权限诊断、DPI 热插拔和长时间观测仍待验证或补齐。 |
| Phase 3 Productization | 主要代码已完成，发布验收待补 | Avalonia 设置、配置、反馈、单实例、开机启动、GitHub Releases 更新、MSI 和 GitHub Actions 已存在；签名和干净系统验收仍未完成。 |
| Phase 4 Advanced Rendering | Composition 原型与自适应刷新率热路径优化已实现，兼容性验收待补 | 非抓屏 Composition 外扩羽化/模糊、位置无关遮罩缓存、最高 120 Hz 的显示器刷新率自适应和硬边回退已接入；真实设备/窗口性能仍需实测。 |

## 当前验证结果

- `dotnet build GhostSlacking.sln --configuration Debug`：0 个警告，0 个错误。
- `dotnet test GhostSlacking.sln --configuration Debug/Release`：250 个测试通过（Core 121，App 129），两个配置均为 0 个失败、0 个跳过；发布元数据 PowerShell 测试另行通过。
- 解决方案当前包含 5 个生产项目：`Core`、`Platform`、`App`、`Watchdog`、`Updater`；以及 2 个测试项目：`Core.Tests`、`App.Tests`。

## 已完成：Phase 1 Core Demo 与 App 宿主基线

- 五个生产项目边界：`Core`、`Platform`、`App`、独立的 `Watchdog` 和 `Updater`；Core 保持平台无关，Win32 细节位于 Platform。
- Per Monitor V2 DPI manifest 和 Avalonia Win32 平台初始化。
- 顶层窗口拾取：`WindowFromPoint`、`GetAncestor`、可见性/系统窗口/自身进程过滤。
- 原始窗口快照：HWND、PID、必需的进程启动标识、rect、完整 `WINDOWPLACEMENT`、可见/最小化/最大化状态、原始 region 数据和 style 快照；启动身份或 placement 不可读取时在修改前失败。
- `Ghost → Reveal → Ghost → Visible/Restore` 状态协调器，支持临时完整显示和最终恢复两条路径。
- 基于屏幕物理像素的圆形、矩形和圆角矩形区域几何计算，光标或窗口几何无变化时跳过 native 更新。
- `SetWindowRgn` 句柄所有权和失败释放路径。
- Ghost 使用 `SW_HIDE`；首次或重新显示 Reveal 时在显示前后均应用所选 region，连续移动时只应用和校验一次增量 region，兼容会在显示过程中重置窗口帧的窗口且避免重复同步重绘。
- Reveal 后读取 `GetWindowRgn`/`GetRgnBox` 校验裁剪结果；目标窗口临时使用 `WS_EX_TOOLWINDOW` 排除 Alt+Tab，恢复时还原原始扩展样式。
- Chrome 在重绘期间偶发短暂清除 region 时，后台会静默重施并校验三次；单轮竞态安全隐藏后在下一 tick 恢复，不再弹出误报，只有连续三轮仍失败才通知用户。
- 修正 GDI 椭圆 region 右/下边界的开区间换算，避免校验误报导致 Reveal 被保持隐藏。
- Windows 11 Reveal 期间临时关闭 `DWMWA_SYSTEMBACKDROP_TYPE`，恢复时还原原始 DWM backdrop 设置。
- Ghost/Reveal 保留目标窗口的标题栏、缩放边框和系统按钮 style，避免 Chrome/Electron 因非客户区 style 变化重排；仍临时控制 Alt+Tab 扩展样式及 DWM 装饰，Restore 时还原原值。
- Ghost/Reveal 期间临时置顶目标窗口，避免 Alt+Tab 后目标位于其他窗口下方；Restore 时恢复原始 TopMost 状态。
- Ghost、Reveal 和 Restore 使用完整 placement 做状态感知校正；普通/Snap 恢复坐标与尺寸，最大化/最小化恢复对应状态。Restore 经过约 16.7ms 周期的三次连续匹配才释放恢复资料，最多纠正十次 Chrome/Electron 延迟漂移。
- Reveal 默认使用 144px 圆角矩形、16px 羽化和轻度模糊；保留原始清晰核心，并把目标内容 region 外扩到羽化宽度的 70%。非激活的 Windows Composition overlay 由当前目标窗口拥有并固定覆盖目标 bounds，确保目标因点击激活后羽化仍位于其上方；位置无关遮罩按视觉参数缓存，光标移动仅更新 visual offset 和羽化外环 region，清晰核心的点击和滚轮直接落到目标窗口。渲染或设备失败时立即缩回硬边 region。
- Peek 使用 Input 优先级轮询：非 Reveal 保持 60 Hz，Reveal 按鼠标所在显示器的当前模式动态调度，并受 Auto/60/90/120 FPS 设置和全局 120 FPS 上限约束。显示查询按显示器缓存 2 秒并由 `WM_DISPLAYCHANGE` 失效，失败时回退 60 Hz；Debug 日志每 120 个有效移动帧输出目标/有效 FPS、平均/P95/最大耗时和超出动态帧预算的帧数，不逐帧写入成功日志。
- 全局热键、低级键盘 Peek 状态和拾取用低级鼠标钩子。
- 设置页支持捕获自定义 Peek 按键，并提供 Auto/60/90/120 最大 Peek 帧率选择，避免 Alt/Shift 对浏览器或其他前台应用产生快捷键副作用。
- Peek 支持“按住显示”和“按下切换”两种模式，默认按下切换；切换模式按下沿翻转状态，按键重复不会重复切换。
- 支持配置窗口显隐快捷键；当前目标可在隐藏和完整显示之间切换，最终恢复仍由 Restore 快捷键负责。
- 所有全局功能均可在设置页配置快捷键：选择窗口、窗口显隐、增大/减小 Reveal 直径、恢复当前窗口、恢复所有窗口、打开设置和退出应用；直径调整使用默认 16px 的可配置共用步长，设置页支持捕获 Ctrl/Alt/Shift 组合键，并为所有输入提供单项恢复默认按钮。
- Avalonia 托盘菜单、FluentAvalonia 设置页、JSON 配置、受限滚动日志、日志/配置导出、单实例互斥。
- 无主窗口状态反馈使用 Avalonia 非激活提示浮层；连续提示替换上一条并在 2.5 秒后隐藏。设置保存结果通过 FluentAvalonia `InfoBar` 在设置页内反馈。
- 中文/English 界面切换，中文为默认语言，语言选择持久化到配置文件。
- Core 自动化测试：坐标换算、区域边界、状态更新去重、身份不匹配保护、配置归一化。
- Picker 同时使用鼠标和键盘低级钩子；Esc 按下、自动重复和抬起均被拦截且只取消一次，并恢复进入 Picker 前的 Ghost、Reveal 或完整显示状态。

## 已完成：Phase 2 Reliability 的 Watchdog v1 增量

- 新增独立 `GhostSlacking.Watchdog` 可执行项目并加入解决方案；App 构建输出会携带 Watchdog 运行文件。
- 使用仅限当前用户的本地命名管道与 Watchdog v1 逐行 JSON 协议。
- 支持随机会话 ID、版本化 `Hello/HelloAcknowledged` 握手、心跳、恢复清单和 `ShutdownCompleted` 正常关闭。
- 恢复清单包含独立 manifest ID、snapshot schema 2、HWND、PID、进程启动身份、完整 placement、region/style/显示状态和 DWM 恢复字段；缺少 placement 的旧恢复项会被拒绝。
- 主进程在恢复配置注册、移除或成功恢复时同步刷新最后清单；正常清理成功后通知 Watchdog 清除清单。
- Watchdog 心跳超时后只对 HWND、PID 和进程启动身份全部匹配的目标逐项恢复，并记录成功、失败或跳过原因。
- 同一会话清单只能恢复一次；超时取出或正常关闭后清除内存中的旧清单，避免重复处理。
- 新增协议序列化、协议版本、会话校验、心跳超时、正常关闭、PID/HWND 复用、进程启动身份不匹配和幂等恢复测试。
- Debug 基线：解决方案构建 0 警告/0 错误；Core 与 App 自动化测试全部通过；本地命名管道握手、空清单、正常关闭和断连超时冒烟测试退出码均为 0。

## 已实现：Phase 3 Productization 基线

- Avalonia + FluentAvalonia 设置页、中文/English 文案、跟随系统/浅色/深色主题、主题实时预览与取消回滚、设置保存反馈和单项恢复默认按钮。
- 左侧“关于”页、GitHub 项目/Release 入口、默认稳定版检查和可选测试版通道、可点击且支持悬停暂停的更新通知、可重复激活的独立更新窗口、中英双语及跨版本 Release 说明、跳过版本后关闭窗口、上次成功检查时间，以及受校验的一键 MSI 自动升级。
- `Updater` 在应用安全退出后等待主进程和 Watchdog，显示 MSI 被动进度并负责重新启动应用；UAC 取消或安装失败时恢复可用应用，并通过一次性结果文件在下次启动反馈结果。
- JSON 配置归一化、损坏配置回退、每类 2 MB × 5 文件且清理 30 天前备份的滚动日志、日志诊断 ZIP、已保存配置 JSON 导出、单实例互斥、HKCU 开机启动开关和退出时恢复选项。
- WiX MSI 安装器、开始菜单/桌面快捷方式选项、首次安装和升级完成页的启动选择、升级协议与安全关闭检查；`build-release.ps1` 可执行测试、稳定版/beta/RC 构建和 MSI 校验，GitHub Actions 已配置 Conventional Commits、功能 PR 元数据校验、双语 Release 汇总及分支约束标签发布流程。

## 已实现：Phase 4 Advanced Rendering 原型

- `RevealEdgeOverlay` 使用由当前目标窗口拥有、非激活且鼠标穿透的 Windows Composition/Win2D overlay，在清晰核心外侧提供可配置羽化和单一模糊等级；目标切换或销毁后重建宿主，避免激活导致的 Z 序遮挡和陈旧 HWND。
- 目标内容 region 外扩到羽化宽度的 70%，overlay 只命中外环，清晰核心仍由目标窗口接收点击和滚轮。
- Overlay 宿主与目标窗口同 bounds；遮罩 surface 与屏幕位置解耦并按视觉参数缓存，边缘/角落移动不再重建 Win2D 和 Composition 资源。
- Composition 初始化、设备或渲染失败时回退到硬边 region；不读取、不保存目标窗口像素。

## 尚未完成：验收与后续工作

- Phase 2 其余可靠性工作：注销/关机通知下的尽力恢复、权限级别识别与更具体的用户反馈、DPI/显示器热插拔验证和长时间资源观测。当前 `AppDomain.UnhandledException` 只保留诊断输出；异常终止后的窗口恢复由 Watchdog 负责。
- Phase 1/2 真实窗口验收：普通 Win32、资源管理器、浏览器、Electron/自绘窗口上的 Ghost/Reveal/Restore、区域内点击/滚轮、焦点与重绘；100%/125%/150% DPI、负坐标与混合缩放双屏、移动/缩放/最小化/关闭、普通/管理员权限目标，以及主进程异常终止后的 Watchdog 真实恢复。自动化测试未替代这些交互式检查。
- Phase 3 剩余代码签名/发布签名策略、干净 Windows 环境中的安装/升级/卸载回归和完整用户文档验收；诊断与配置导出已完成自动化覆盖，仍需发布环境手工验收。
- Composition 羽化在 Chrome/Electron、混合 DPI、远程桌面、透明效果关闭和图形设备丢失场景下的手工兼容性与性能验收。
