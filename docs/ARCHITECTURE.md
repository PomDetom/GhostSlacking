# GhostSlacking 技术架构

本文只记录当前实现、模块边界和恢复约束。完成度及已知问题见[实施状态](IMPLEMENTATION_STATUS.md)，验收步骤见[实施计划](IMPLEMENTATION_PLAN.md)，分支与打包规则见[开发流程](DEVELOPMENT_WORKFLOW.md)。

## 1. 运行结构与职责

GhostSlacking 面向 Windows 10/11 x64，使用 .NET 8、Avalonia/FluentAvalonia 和 Win32。当前只管理一个活动目标窗口；不提供多窗口规则系统、窗口截图、录制或目标进程注入。通用后台交互是后续研究目标，当前实现不代表所有程序均兼容。

```text
托盘 / 快捷键 / 设置 → App → Core 协调器 → Platform → 目标窗口
                       ↕ 当前用户命名管道
                    Watchdog → 身份校验 → Platform 恢复

App → GitHub Releases → Updater → 签名 NSIS EXE → 原生安装辅助程序
```

| 项目 | 当前职责 |
| --- | --- |
| Core | 不依赖 Win32/UI 的状态、几何、配置、快照、恢复和后台输入归属规则 |
| Platform | 窗口拾取/观察、P/Invoke、region、样式、Hook、热键、显示模式、管道及后台输入适配 |
| App | Avalonia 生命周期、托盘、设置、Reveal 视觉宿主、通知及更新编排 |
| Watchdog | 独立进程保存内存恢复清单，主进程异常退出后验证身份并尽力恢复 |
| Updater | 独立验证升级包，等待安全退出，安装、重启并确认实际版本 |

App 引用 Core/Platform，Watchdog 和 Updater 同样引用 Core/Platform。App 构建携带 Watchdog，发布输出另带独立 Updater 子目录。两个测试项目分别验证 Core 与 App/Platform/Updater；独立研究工具不属于生产解决方案。

## 2. 状态与窗口恢复

### 2.1 修改入口与快照

`GhostCoordinator` 经 `VisibilityEngine` 和 `Win32VisibilityBackend` 控制 region、位置、样式与可见性；`RecoveryManager` 持有恢复资料。后台交互的临时非激活样式由独立会话经 Platform 操作，同样以已登记快照及身份检查为前提。UI 不直接修改目标窗口。

第一次修改前必须完整捕获并登记 `WindowSnapshot`：HWND、PID、进程启动身份、屏幕矩形、完整 `WINDOWPLACEMENT`、显示/最小化状态、原始 region 数据、style/ex-style 以及可读取的 DWM 属性。必需身份或 placement 捕获失败时，目标保持原状。快照 schema 为 2，region 复制为托管数据，不把临时 GDI 句柄当恢复资料。

主进程和 Watchdog 恢复前均核对 HWND、PID 与进程启动身份。窗口标题不用于身份判断；目标关闭或句柄被复用后不得向新窗口重放恢复。恢复资料不包含窗口内容，Watchdog 清单只存内存，不持久化旧 HWND。

### 2.2 状态转换

状态定义以 [Domain.cs](../src/GhostSlacking.Core/Domain.cs) 和 [GhostCoordinator.cs](../src/GhostSlacking.Core/GhostCoordinator.cs) 为准：

| 状态 | 行为和退出条件 |
| --- | --- |
| Idle | 无活动目标，等待选择 |
| Picking | 拦截选择点击和 Esc；取消恢复进入拾取前的 Ghost/Reveal/Visible 状态 |
| Preparing | 捕获登记快照后才应用 Ghost；切换目标须先恢复旧目标 |
| Ghost | 隐藏目标，保留恢复资料，等待 Peek/显隐/恢复 |
| Reveal | 只显示鼠标附近区域；关闭 Peek 或移出目标回到 Ghost |
| Visible | 临时完整显示，保留资料以便再次隐藏；区别于最终 Restore |
| Restoring | 恢复并校验稳定性；完成后进入 Idle 或临时 Visible |
| RecoveryError | 恢复失败，保留可用资料并向用户提供错误反馈 |

主程序每 tick 观察目标身份和位置；失效后清理活动状态。当前位置保持逻辑将观测矩形与原快照比较，漂移时纠正。它与运行中 DPI 变化的关系尚未解决，已知闪烁见状态文档，不能据此宣称动态缩放可靠。

Restore 恢复 region、style/ex-style、DWM 属性、TopMost、placement 和原始可见性。异步恢复要求连续三次观测匹配，最多纠正十次以容纳浏览器延迟漂移；失败保留恢复资料。同步 Restore All 逐项返回成功、失败或跳过结果。

## 3. 拾取、几何与 Reveal

- Picker 使用 `WindowFromPoint`/`GetAncestor` 选择顶层窗口，过滤桌面、任务栏、自身及不支持目标；Esc 按下、重复及抬起均拦截且只取消一次。
- 领域几何统一使用物理屏幕像素，进程使用 Per Monitor V2 DPI manifest；UI 的 DIP 只在显示层转换。普通窗口、最大化和最小化按完整 placement 区分。
- Ghost 使用 `SW_HIDE`，临时控制工具窗口、置顶和 DWM 装饰；保留标题栏与缩放边框样式，避免浏览器因非客户区变化重排。Restore 还原原值。
- Reveal 支持圆形、矩形、圆角矩形。首次显示在隐藏时先应用 region，显示后再应用/校验；连续移动使用增量更新。光标与几何不变时跳过重复 native 更新。
- `SetWindowRgn` 成功后由系统拥有 HRGN；失败时由调用方删除。曲线 region 的右/下开区间须补偿，原始 region 恢复失败须明确返回错误。
- 为兼容重绘竞态，单次 region 更新最多重施/校验三次；该轮仍失败先安全隐藏，下个 tick 重试，连续三轮失败才通知。这不证明所有自绘窗口均兼容。

### Composition 与调度

`IRevealVisualHost` 隔离可选 `RevealEdgeOverlay`；`IVisibilityBackend` 保留可独立运行的硬边路径。默认 Reveal 为 144px 圆角矩形、16px 羽化、轻度模糊。Composition/Win2D 不抓屏：清晰核心保留，目标内容 region 外扩至羽化宽度的 70%，单一 backdrop 模糊配合渐变遮罩绘制外环。

Overlay 非激活、鼠标穿透、无任务栏项，并由当前目标拥有，防止点击目标后被其遮挡。宿主覆盖目标 bounds，目标切换/销毁后重建；遮罩按视觉参数缓存，与光标位置无关。移动只更新 visual offset 和外环 region，核心点击/滚轮仍到目标。设备、初始化或渲染失败时隐藏 overlay，将内容缩回硬边 region，不依赖视觉层执行恢复。

非 Reveal 轮询约 60 Hz，Reveal 按鼠标所在显示器当前模式及 Auto/60/90/120 上限调度，最高 120 Hz。显示查询按显示器缓存两秒，`WM_DISPLAYCHANGE` 使缓存失效，失败回退 60 Hz。Debug 每 120 个有效移动帧汇总有效 FPS、平均/P95/最大 tick 耗时和超预算帧数，不逐帧写成功日志。

## 4. 实验性后台输入

当前实现是默认关闭的兼容性原型，使用方法见[实验说明](EXPERIMENTAL_BACKGROUND_INTERACTION.md)。能力和实际验收必须分开：接口调用成功不证明目标完成操作，更不证明前台网页全程未失焦。

### 会话与输入归属

`BackgroundInteractionSession` 负责身份、受保护前台和输入归属；Platform 使用独立 Hook 消息线程、代次输入队列及 MTA UIA 工作线程。主业务仍在 Avalonia UI 线程；不注入目标进程，不使用 `AttachThreadInput` 借用前台输入上下文。

仅为已登记目标临时设置 `WS_EX_NOACTIVATE`，结束时恢复快照中该位的原值，保留其他位。持续模式只允许 Toggle Peek：先等待有效独立前台和已按输入释放，再准备 Hook/目标，最后显示 Reveal。准备期间的新输入消费但不排队重放，准备前属于 A 的按键抬起仍交给 A。

正常移出/返回可结束/重建持续会话；真实失焦、投递失败、目标失效或未管理弹窗清除显示意图，须重新手动 Peek。设置、选窗、恢复和退出取消等待/准备/交互，恢复开始后不再追加隐藏。

只为实际投递的原生按下发送抬起；字符和编辑命令不伪造抬起。硬件归属与投递账本独立，释放尾部最多两秒，不因后续输入无限延长。停止清除接收者、修饰键、选区上下文及旧代次回执；未支持操作与真实失败分别处理，结果不确定时不重放。

### 当前能力路径

| 路径 | 当前实现及边界 |
| --- | --- |
| 英文键盘 | 已加载 US 布局只读翻译，缺少时固定映射；不加载/切换布局。普通字符只发 `WM_CHAR`，按翻译实际长度读取。Shift 单击、Ctrl/Win+Space 和 IME 组合请求直接返回 `ImeComposition`，不借用 A 的文本上下文。 |
| Scintilla | 定位、单/双击、Shift/跨行拖选及编辑快捷键走控件标量命令；剪贴板由目标按用户快捷键处理。编辑区外、右键和未适配组合明确拒绝。 |
| Edit/RichEdit | Edit 使用原生定位/选区/复制粘贴，RichEdit 按当前 UIA/原生命令能力处理；不由根窗口消息推断所有编辑器可用。 |
| UIA | 每次按坐标命中并验证 B 祖先关系；正文选区和编辑框分离。Invoke/Toggle/ExpandCollapse/SelectionItem 仅在未拖动的完整单击抬起时调用一次；合并移动保留最大偏移。 |
| 空白框粘贴 | 显式 Ctrl+V 才读 Unicode 纯文本，剪贴板分配块上限 1 MiB；要求当前 Edit、可写 ValuePattern、值精确为空且无未确认富内容/子项。读后复查身份和空白，SetValue 后核对；已有草稿、空格、换行或旧接收者拒绝。不是光标插入，不改剪贴板，不写内容日志。 |

已知 Chromium/Electron 语义窗口不回退根窗口按钮；其他通用消息接收者仍可能主动激活，发生时收起。UIA 只读发现含排队预算 1.5 秒，修改后 500ms 结果不确定即停止。逻辑超时不能取消已发 COM 调用，不能证明操作未发生，也不能据此换路线重试。

原生 Edit/MSAA/隔离 CDP 探测与中文 TSF/独立小词表工具均独立运行，不自动接入真实目标。其构建、边界和命令由各工具 README 维护；历史证据与研究待办集中在状态文档。

## 5. 恢复进程、退出与安装更新

### Watchdog 与退出

Watchdog 使用仅限当前用户的命名管道和逐行 JSON 协议。每次启动随机 session ID；握手校验协议、主 PID/启动身份，之后发送版本化恢复清单和心跳，正常恢复完成后发送 `ShutdownCompleted`。清单含 manifest ID、schema 2 快照及全部恢复字段；缺少 placement 的旧项拒绝。

主程序在恢复资料注册/移除/恢复成功时刷新清单。Watchdog 心跳超时取出最后有效清单，逐项校验身份后恢复并记录结果；取出、正常关闭后清空旧清单，同一会话清单只处理一次。版本/会话不匹配、握手前消息和关闭后追加消息拒绝。

可控退出取消更新/Picker/交互，按 `RestoreOnExit` 或强制恢复标志执行 Restore All，再释放 Hook、热键、视觉层和 UI。安装关闭请求强制恢复；成功后才通知 Watchdog 正常关闭，否则保留清单供其超时处理。UI 线程异常记录并提示，`AppDomain.UnhandledException` 只保留诊断，不保证异常中执行恢复。注销/关机消息提前恢复尚未实现；异常恢复依赖独立 Watchdog。

### 签名更新与原生安装器

- App 默认检查 Stable，主动开启 Test 后才纳入 beta/RC；完整 SemVer 排序。跳过版本只抑制该版本启动通知/托盘入口，“关于”仍允许安装。检查时间与跳过版本写入 `update-state.json`。
- 旧 MSI-only Release 与无效签名包、网络错误分开处理。标签、清单、EXE 名称/大小、SHA256 相互核对；下载先写 `.partial`。固定公钥 ECDSA P-256/SHA256 验证格式 2 清单；发布签名不同于尚未实现的 Windows Authenticode。
- 仅 HKCU 注册路径匹配、NSIS 协议 2 且未提权的安装副本提供自动安装；其他副本打开 Releases。Updater 再验清单与包体，并在安装期间保持禁止写入/删除的包体锁。
- Ready/Go 两阶段交接授权后才关闭 App。原生安装辅助程序验证管道服务端可执行路径、PID/启动时间，等待 App 恢复目标及 Watchdog 退出后收到 SAFE；身份不符、恢复失败、残留进程或 30 秒超时均停止，不强杀 App/Watchdog。
- Updater 等待安全退出，显示 NSIS 被动进度，安装后重启；失败/取消尝试重启旧应用。成功安装须在 15 秒内经当前用户管道确认新 PID、会话和实际版本，保存一次性结果并回执后才消费结果/清理备份。
- 原生辅助程序独立于 .NET，负责 x64 .NET 8 检测、旧 MSI UpgradeCode 检测、目录互斥、暂存解包、事务回退及中断恢复。安装目录归属通过 `installation.ini` 确认；拒绝不安全目录、降级及自动 MSI 迁移。新版本确认前保留备份，替换阶段禁止取消。
- 安装为当前用户 NSIS EXE；用户数据独立于程序目录。卸载器也携带原生辅助程序，卸载不依赖 .NET，保留设置与日志。构建、密钥和版本规则只维护在开发流程中。

## 6. 设置、通知与诊断

配置位于 `%LOCALAPPDATA%\GhostSlacking`，`AppSettings` schema 为 10。保存前归一化；损坏配置回退。旧通知样式和三个位置字段继续兼容读写但不参与显示，不因 UI 入口消失删除。后台总开关及持续选项默认关闭；关闭总开关保留持续偏好但不生效。

设置使用草稿，支持中英文、跟随系统/浅/深主题、实时预览、单项默认值及全局热键捕获。

外观设置按单项使用锚定到设置行的 `SettingsAdjustmentFlyout`，由 Avalonia 处理弹出位置与边界避让。浮层维护局部草稿，完成时只回写对应字段；取消/Esc/关闭/点击外部及切换页面、滚动设置页不回写。数值滑条与数字双向同步，正式保存仍由设置页负责，不新增配置格式。主人窗口移动、缩放变化、最小化或关闭会取消浮层，动画及位图资源随浮层关闭释放。

Reveal 内置样例复用 Core 几何/羽化算法。步长比较叠加原直径的实线实体与调整一次后的虚线虚化轮廓；羽化比较保留原始硬边实线及柔化外沿。同一帧的两层使用相同中心和比例，示例不改直径配置或真实目标。帧率动画复用 `RevealFrameRatePolicy`。通知运行时与预览共用 `NotificationContent` 的测量/样式，位置缩略图共用 `NotificationLayout`。

浮动保存条只在修改/反馈时显示；取消还原最近保存值并保持窗口打开，保存失败保留草稿，成功反馈后收起。单实例互斥避免多个主进程控制窗口；开机启动使用 HKCU。

HUD 单窗口复用，默认最大宽度 132 DIP、高度 44 DIP、圆角 10 DIP、鼠标屏幕底部居中距工作区 18 DIP。实际宽度随内容收缩，布局/测量缓存由运行时、持续 HUD 和设置预览共享；18 DIP 图标、9 DIP 间距、两侧 10 DIP 留白，13–11 DIP 字体，图文组居中。空间不足只收窄实际渲染尺寸，不改配置。

普通提示 2.5 秒且鼠标穿透；可点击更新提示 4.5 秒、悬停暂停、回调只消费一次。连续消息按优先级替换，可见替换不重启动画；淡入/淡出 150/200ms 和 8 DIP 位移遵循系统动画开关。窗口不激活，原生样式失败则隐藏。新消息选鼠标屏幕，显示配置变化/工作区变化重新定位。

主程序与 Watchdog 共用 `RollingFileLogger`、使用不同文件组：每文件 2 MB，每组最多五文件，清理 30 天前备份；满文件滚动失败丢弃新记录，不影响恢复。主程序等级可配置，Watchdog 固定 Info 及以上。

日志保留身份、API/错误码、状态和操作耗时，不记录窗口文字、按键字符或剪贴板内容。日志 ZIP 只包含应用管理的日志及受限版本/系统/运行时摘要；配置另行主动导出，无自动上传。后台启动、代次、分阶段超时与焦点事件的诊断方法见[验收矩阵](IMPLEMENTATION_PLAN.md#94-实验性后台交互)。
