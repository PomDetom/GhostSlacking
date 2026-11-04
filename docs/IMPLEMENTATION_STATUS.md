# Implementation status

> 本页记录当前阶段完成度和未验证事项。项目边界以[技术架构](ARCHITECTURE.md)为准，自动化检查以 [AGENTS.md](../AGENTS.md) 和 CI 为准，真实窗口验收以[实施计划的手工矩阵](IMPLEMENTATION_PLAN.md#9-测试矩阵)为准。代码已实现不等于兼容性验收已完成。

## 当前总览

第七轮新增按控件命中的 UIA Invoke/Toggle/ExpandCollapse/SelectionItem，完整单击抬起执行一次；编辑框定位与正文选区独立，显式 Ctrl+V 才尝试严格空白框 ValuePattern 纯文本赋值。已有草稿、空白字符、富内容或身份无法确认时拒绝，不整框重写。实验烧瓶重绘并对实际 FAPathIcon 绑定导航图标承载器颜色，完整 20 DIP 画布避免路径被额外放大。默认开关、schema 10、通知和恢复协议不变。

本轮实际执行 Release 自动测试、元数据检查、图标状态/渲染验证与独立 UIA/原生 Edit/MSAA/CDP 隔离试验。CDP 仅验证隔离 headless Chromium；真实 ChatGPT、浏览器 A 完整焦点事件、另一 App 和混合 DPI 仍未验收。独立工具不自动连接或改变真实目标。结果边界与步骤见[第七轮报告](BACKGROUND_INTERACTION_ROUND7.md)。

新增默认关闭的本机窗口后台交互原型：独立快捷键、初始按键释放等待、临时 `WS_EX_NOACTIVATE`、后台键盘/字符及鼠标按钮/滚轮消息、持续非激活 HUD、前台变化事件日志及会话清理。手动交互停止或真实失败后收起 Reveal，须手动重新开启，避免回落到普通点击路径。设置 schema 升至 10，旧配置默认关闭，持续选项默认关闭。快照和 Watchdog 协议不变，原有扩展样式字段保留恢复能力。

中文组合输入、死键和部分系统组合键不支持；真实应用及 Windows 10/11 手工验收待用户执行，不能将自动化通过等同于网站检测兼容性。操作步骤与矩阵见[实验原型报告](EXPERIMENTAL_BACKGROUND_INTERACTION.md)。

第六轮将通知改为按内容自适应，现有宽度保存值改作“最大宽度”。短通知可小于 100 DIP，运行时、持续 HUD 及设置预览共享测量缓存和位置计算；高度、圆角、通知生命周期和焦点保障保持原样。输出 `artifacts/background-interaction-round6/`，仅编译主程序及相关测试源码，未执行用例或真实窗口自动化；前五轮和中文研究原型保留，schema 仍为 10。详情及用户验收见[第六轮报告](BACKGROUND_INTERACTION_ROUND6.md)。

第五轮后用户反馈 ChatGPT 不再失败，但无法粘贴；15:09:10 日志为 `PasteUnavailable`，当前 UIA 路径没有开放粘贴，不是投递超时。第六轮保留该能力限制；单纯不再失败不足以证明所有文本操作及 A 全程焦点已通过验收。

第五轮增加首次 Peek 的前台等待及自动继续、独立启动拒绝原因；未满足前台条件时不安装 Hook，B 保持隐藏。ChatGPT/UIA 路径改为缓存命中、有限祖先查找和异步回执，区分 1.5 秒只读超时与 500ms 修改后结果不确定；停止后丢弃旧会话结果，不重放选区或复制。实验图标删除显式前景色绑定/赋值，继承导航模板；通知恢复 9 DIP 间距，图文实际宽度整体居中。

第五轮输出 `artifacts/background-interaction-round5/`，主程序及 Core/App 回归用例源码仅编译，未运行测试套件或真实窗口自动化。前四轮及第四轮中文原型保留；配置 schema 10、完整快照和 Watchdog 协议不变。Notepad++ 的启动拒绝细项需新日志确认，ChatGPT 点击/拖选/复制的实际效果、主题颜色和最小通知尺寸仍待用户实测，详见[第五轮报告](BACKGROUND_INTERACTION_ROUND5.md)。

第四轮实现独立硬件归属账本与两秒尾部截止、独立 Hook 消息线程、代次输入队列/移动合并、标准 Edit 和 UIA 正文适配、按能力复制/粘贴、细线主题烧瓶及更多退出诊断。Chromium/Electron 已知根窗口按钮不再作为回退；UIA 无已验证插入接口时粘贴明确受限，不能宣称浏览器完整编辑兼容。旧代码无限扩大吞键尾部是确认缺口，但本机“恢复 B 后 Shift 失效”的实际根因仍待复测分辨。

输出 `artifacts/background-interaction-round4/`；中文研究程序位于其 `ime-research/` 子目录，提供系统 TSF 和独立小词表两种隔离原型。原生是否能保持 A 焦点完成中文组合尚未验证；独立小词表不能替代生产输入法。第四轮 App、Core/App 测试项目只编译，C++ 研究原型按 `/W4 /WX` 构建；未执行测试或真实交互。应用矩阵和诊断步骤见[第四轮报告](BACKGROUND_INTERACTION_ROUND4.md)。配置仍为 schema 10，完整快照和 Watchdog 协议未改变，前三轮目录保留。

第二轮增加 Scintilla 专用定位/选区/编辑命令，绕开鼠标按下时的主动 `SetFocus`；基本键盘使用固定美式英文映射，中文布局不再导致普通英文退出。未支持键或已知控件操作仅拒绝并提示，保持会话；真实失焦、超时和投递失败仍收起。键盘账本记录原生按下回执，字符/编辑命令退出时不额外投递抬起。控件类、适配器、目标焦点和拒绝原因进入诊断日志，不记录输入字符。

第二轮输出 `artifacts/background-interaction-round2/`，App Release 与 Core 测试项目编译均零警告/错误。按用户要求未运行测试套件，Notepad++ 首次点击及基本键盘待用户复测，不能据此声明所有应用兼容。

第三轮新增独立“实验性功能”菜单（单色矢量烧瓶），迁入原有后台开关/快捷键，增加持久化的持续交互开关及 Toggle 保存/运行时守卫。持续模式在目标隐藏时完成输入拦截及准备，再显示并交付输入；自然移出/返回自动结束/重建，真实失焦或投递失败清除显示意图且不自动重试。准备期间新输入被消费且不重放，原属前台的按键抬起仍保留，尾部清理和代次守卫保护后续会话。恢复开始后不追加隐藏。通知固定配置尺寸，移除固定文字宽度，使用 18 + 6 + 剩余空间及 13–11 DIP 字体，统一简短状态文案。

第三轮输出 `artifacts/background-interaction-round3/`，App、Core/App 测试项目均仅编译 Release，零警告/错误；前两轮目录保留。按用户要求只编译，未运行测试套件，也未启动应用做 UI/焦点自动化；持续模式时序、最小通知尺寸的视觉效果和 Windows 10/11 应用兼容性均待用户验收。

2026-10-30 初版本地 Release 套件 311 项通过（Core 153、App 158，无跳过），Release metadata 检查通过；随后用户实测 Notepad++ 单击会激活目标。第一轮按用户要求只编译，输出 `artifacts/background-interaction-round1/`；其用户反馈为首次点击失焦、再次点击可用，键盘操作触发输入拒绝。第二轮状态见上文；初版测试结果不代表后续修复已通过测试。使用 SDK 10.0.400 构建 .NET 8 目标，不替代 CI 的最低 SDK 8 验证。

| 阶段 | 当前状态 | 说明 |
|---|---|---|
| Phase 0 Technical Spike | 实现基线已完成，手工验收待补 | `SetWindowRgn`、坐标、拾取、交互和恢复路径已沉淀到当前实现；没有单独保留 Spike 工程。 |
| Phase 1 Ghost Core | 已完成实现 | 单窗口闭环、托盘宿主、设置、快捷键和自动化测试已在当前解决方案中。 |
| Phase 2 Reliability | Watchdog v1 已完成，其余进行中 | 异常恢复协议和独立进程已落地；注销/关机、权限诊断、DPI 热插拔和长时间观测仍待验证或补齐。 |
| Phase 3 Productization | 主要代码已完成，发布验收待补 | Avalonia 设置、配置、反馈、单实例、开机启动、GitHub Releases 更新、NSIS EXE、发布签名和 GitHub Actions 已存在；Authenticode 和干净系统验收仍未完成。 |
| Phase 4 Advanced Rendering | Composition 原型与自适应刷新率热路径优化已实现，兼容性验收待补 | 非抓屏 Composition 外扩羽化/模糊、位置无关遮罩缓存、最高 120 Hz 的显示器刷新率自适应和硬边回退已接入；真实设备/窗口性能仍需实测。 |

## 已完成：Phase 1 Core Demo 与 App 宿主基线

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
- 全局功能使用 `RegisterHotKey`；Peek 按键状态由定时轮询和 `PeekStateTracker` 处理；低级键盘/鼠标钩子用于 Picker 的 Esc 和点击拦截。
- 设置页支持捕获自定义 Peek 按键，并提供 Auto/60/90/120 最大 Peek 帧率选择，避免 Alt/Shift 对浏览器或其他前台应用产生快捷键副作用。
- Peek 支持“按住显示”和“按下切换”两种模式，默认按下切换；切换模式按下沿翻转状态，按键重复不会重复切换。
- 支持配置窗口显隐快捷键；当前目标可在隐藏和完整显示之间切换，最终恢复仍由 Restore 快捷键负责。
- 所有全局功能均可在设置页配置快捷键：选择窗口、窗口显隐、增大/减小 Reveal 直径、恢复当前窗口、恢复所有窗口、打开设置和退出应用；直径调整使用默认 16px 的可配置共用步长，设置页支持捕获 Ctrl/Alt/Shift 组合键，并为所有输入提供单项恢复默认按钮。
- Avalonia 托盘菜单、FluentAvalonia 设置页、JSON 配置、受限滚动日志、日志/配置导出、单实例互斥。
- 无主窗口状态反馈使用 Avalonia 非激活提示浮层；连续提示替换上一条并在 2.5 秒后隐藏。设置修改与保存结果通过页内浮动横条反馈，无修改时隐藏；“取消”恢复最近一次保存的全部设置并保持窗口打开。
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

## 已实现：Phase 3 Productization 基线

- 极简 HUD 已替换卡片／胶囊／聊天三种运行时样式；默认 132 × 44 DIP、10 DIP 圆角、底部居中，简短中英文提示，图标固定、文字使用剩余区域，复用设置主题，单窗口替换且支持淡入淡出和轻微位移。常规设置提供圆角、宽度、高度、水平／垂直位置五项及草稿预览；旧配置迁移到 schema 8，旧字段保留兼容读写。
- HUD 的本机 Windows 11 自动化窗口检查覆盖普通提示鼠标穿透、两类窗口无激活样式及显示前后前台 HWND 保持不变、悬停暂停／恢复、点击一次性消费、设置保存失败／成功及取消恢复；布局测试覆盖负坐标、位置极值、100%／125%／150%／200% 缩放和窄工作区。真实 Windows 10、混合 DPI 多屏、任务栏移动及显示器插拔仍待手工验收。
- 本机 Windows 11、125% 缩放下，以隔离宿主运行真实 Avalonia 窗口，检查 HUD 浅色／深色、成功／错误／更新图标和中文布局，以及常规设置五项参数和修改草稿；原生窗口截图与设置页截图保存在 `artifacts/hud/screenshots/`。宿主使用内存保存回调，不改写实际用户配置；此视觉检查不代表安装版更新链路或多屏完整验收。
- Avalonia + FluentAvalonia 设置页、中文/English 文案、跟随系统/浅色/深色主题、主题实时预览与取消回滚、设置保存反馈和单项恢复默认按钮。
- 设置页采用 56/184 DIP 侧栏、精简的中英文菜单文案、20 DIP 图标、四角 12 DIP 圆角内容面板和约 3.3 DIP 的右/下间距；浮动横条在底部居中显示，采用单行短提示和固定 60 DIP 高度，宽度随文字与按钮自适应，详细错误放入悬停提示；支持修改取消、保存失败重试、成功反馈自动收起及系统动画开关。当前 Windows 会话以隔离预览宿主和内存保存回调打开真实 Avalonia 设置窗口，检查浅色/深色、侧栏展开/收起、中英文、取消恢复、保存失败/成功和反馈收起；滚动到末尾时最后一项完整位于横条上方。截图和尺寸记录位于本地 `artifacts/settings-preview/`。此检查不代表实际配置持久化、Windows 10/11 或混合 DPI 的完整兼容性验收。
- 左侧“关于”页、GitHub 项目/Release 入口、默认稳定版检查和可选测试版通道、可点击且支持悬停暂停的更新通知、可重复激活的独立更新窗口、中英双语及跨版本 Release 说明、跳过版本后关闭窗口、上次成功检查时间，以及受校验的签名 EXE 自动升级。
- 当 Release 历史仅有旧 MSI 包时，更新检查会报告“暂无兼容的签名更新”，与网络/API 错误区分。
- `Updater` 在应用安全退出后等待主进程和 Watchdog，显示 NSIS 被动进度并负责重新启动应用；安装取消或失败时尝试重新启动应用，成功安装须确认实际启动版本，并通过一次性结果文件在下次启动反馈结果。
- JSON 配置归一化、损坏配置回退、每类 2 MB × 5 文件且清理 30 天前备份的滚动日志、日志诊断 ZIP、已保存配置 JSON 导出、单实例互斥、HKCU 开机启动开关和退出时恢复选项。
- 当前用户 NSIS EXE 安装器、开始菜单/桌面快捷方式选项、手动安装完成页的启动选择、升级协议与安全关闭检查；`build-release.ps1` 可执行测试、稳定版/beta/RC 构建、清单签名和 EXE 校验，GitHub Actions 已配置 Conventional Commits、功能 PR 元数据校验、双语 Release 汇总及分支约束标签发布流程。

## 已实现：Phase 4 Advanced Rendering 原型

- `RevealEdgeOverlay` 使用由当前目标窗口拥有、非激活且鼠标穿透的 Windows Composition/Win2D overlay，在清晰核心外侧提供可配置羽化和单一模糊等级；目标切换或销毁后重建宿主，避免激活导致的 Z 序遮挡和陈旧 HWND。
- 目标内容 region 外扩到羽化宽度的 70%，overlay 只命中外环，清晰核心仍由目标窗口接收点击和滚轮。
- Overlay 宿主与目标窗口同 bounds；遮罩 surface 与屏幕位置解耦并按视觉参数缓存，边缘/角落移动不再重建 Win2D 和 Composition 资源。
- Composition 初始化、设备或渲染失败时回退到硬边 region；不读取、不保存目标窗口像素。

## 尚未完成：验收与后续工作

- Phase 2 其余可靠性工作：注销/关机通知下的尽力恢复、权限级别识别与更具体的用户反馈、DPI/显示器热插拔验证和长时间资源观测。当前 `AppDomain.UnhandledException` 只保留诊断输出；异常终止后的窗口恢复由 Watchdog 负责。
- Phase 1/2 真实窗口验收：普通 Win32、资源管理器、浏览器、Electron/自绘窗口上的 Ghost/Reveal/Restore、区域内点击/滚轮、焦点与重绘；100%/125%/150% DPI、负坐标与混合缩放双屏、移动/缩放/最小化/关闭、普通/管理员权限目标，以及主进程异常终止后的 Watchdog 真实恢复。自动化测试未替代这些交互式检查。
- Phase 3 剩余Windows Authenticode 签名、干净 Windows 环境中的安装/升级/卸载回归和完整用户文档验收；诊断与配置导出已完成自动化覆盖，仍需发布环境手工验收。
- Composition 羽化在 Chrome/Electron、混合 DPI、远程桌面、透明效果关闭和图形设备丢失场景下的手工兼容性与性能验收。

## 安装链路替换（2026-10-10）

- 已实现：当前用户 EXE、系统 .NET 8 x64 轻量检测、旧 MSI 身份检测及拒绝自动迁移、卸载保留用户数据。
- 已实现：固定公钥发布签名、App/Updater 双重验证、只读包体锁、Ready/Go 授权、安全关闭管道及新版本启动确认。
- 已实现：原生辅助程序不依赖 .NET 8；目录互斥、暂存解包、事务回退、中断恢复、禁止降级和启动后清理备份。
- 本地验证：Release .NET 测试、原生事务测试、NSIS beta EXE 编译及 EXE/哈希/签名一致性。真实 App 在空闲状态下约 4.5 秒完成启动会话及 beta 版本确认，随后 App/Watchdog 安全关闭通过；这不代表 Ghost/Peek 状态恢复或完整更新性能已验收。当前机器存在旧 MSI，身份检测正确阻止安装；没有自动卸载旧安装。
- 发布密钥：固定公钥已嵌入应用，对应私钥保存在仓库外，GitHub Actions 的 `GHOSTSLACKING_RELEASE_SIGNING_KEY` 已配置。私钥仍需维护者自行备份。
- 待验证：干净 Windows 10/11、缺少/x86-only .NET、真实 Ghost/Peek 窗口恢复、跨权限副本、完整安装更新十次性能测试和 GitHub beta 发布。自动化通过不代表这些场景已验收。
