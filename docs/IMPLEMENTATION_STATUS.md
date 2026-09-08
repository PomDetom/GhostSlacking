# Implementation status

## 已完成：Phase 1 Core Demo 基线

- 三层项目边界：`Core`、`Platform`、`App`。
- Per Monitor V2 DPI manifest 和 WinForms DPI 初始化。
- 顶层窗口拾取：`WindowFromPoint`、`GetAncestor`、可见性/系统窗口/自身进程过滤。
- 原始窗口快照：HWND、PID、必需的进程启动标识、rect、完整 `WINDOWPLACEMENT`、可见/最小化/最大化状态、原始 region 数据和 style 快照；启动身份或 placement 不可读取时在修改前失败。
- `Ghost → Reveal → Ghost → Restore` 状态协调器。
- 基于屏幕物理像素的圆形、矩形和圆角矩形区域几何计算，光标或窗口几何无变化时跳过 native 更新。
- `SetWindowRgn` 句柄所有权和失败释放路径。
- Ghost 使用 `SW_HIDE`，Reveal 时在显示前后均应用所选 region，兼容会在显示过程中重置窗口帧的窗口。
- Reveal 后读取 `GetWindowRgn`/`GetRgnBox` 校验裁剪结果；目标窗口临时使用 `WS_EX_TOOLWINDOW` 排除 Alt+Tab，恢复时还原原始扩展样式。
- Chrome 在重绘期间偶发短暂清除 region 时，后台会静默重施并校验三次；单轮竞态安全隐藏后在下一 tick 恢复，不再弹出误报，只有连续三轮仍失败才通知用户。
- 修正 GDI 椭圆 region 右/下边界的开区间换算，避免校验误报导致 Reveal 被保持隐藏。
- Windows 11 Reveal 期间临时关闭 `DWMWA_SYSTEMBACKDROP_TYPE`，恢复时还原原始 DWM backdrop 设置。
- Ghost/Reveal 保留目标窗口的标题栏、缩放边框和系统按钮 style，避免 Chrome/Electron 因非客户区 style 变化重排；仍临时控制 Alt+Tab 扩展样式及 DWM 装饰，Restore 时还原原值。
- Ghost/Reveal 期间临时置顶目标窗口，避免 Alt+Tab 后目标位于其他窗口下方；Restore 时恢复原始 TopMost 状态。
- Ghost、Reveal 和 Restore 使用完整 placement 做状态感知校正；普通/Snap 恢复坐标与尺寸，最大化/最小化恢复对应状态。Restore 经过 33ms 周期的三次连续匹配才释放恢复资料，最多纠正十次 Chrome/Electron 延迟漂移。
- Reveal 默认使用 144px 圆角矩形、16px 羽化和轻度模糊；保留原始清晰核心，并把目标内容 region 外扩到羽化宽度的 70%。非激活的 Windows Composition overlay 使用用户选择的单一模糊等级，通过连续透明遮罩渐入并淡出；宿主命中区域仅保留羽化外环，清晰核心的点击和滚轮直接落到目标窗口。渲染或设备失败时立即缩回硬边 region。
- 全局热键、低级键盘 Peek 状态和拾取用低级鼠标钩子。
- 设置页支持捕获自定义 Peek 按键，避免 Alt/Shift 对浏览器或其他前台应用产生快捷键副作用。
- Peek 支持“按住显示”和“按下切换”两种模式，默认按下切换；切换模式按下沿翻转状态，按键重复不会重复切换。
- 支持配置窗口显隐快捷键；当前目标可在隐藏和完整显示之间切换，最终恢复仍由 Restore 快捷键负责。
- 所有全局功能均可在设置页配置快捷键：选择窗口、窗口显隐、增大/减小 Reveal 直径、恢复当前窗口、恢复所有窗口、打开设置和退出应用；直径调整使用默认 16px 的可配置共用步长，设置页支持捕获 Ctrl/Alt/Shift 组合键，并为所有输入提供单项恢复默认按钮。
- 托盘菜单、设置页、JSON 配置、滚动日志、单实例互斥。
- 无主窗口状态反馈优先使用 Windows App SDK 系统通知；通知静音、使用系统默认短时展示、30 秒后过期，并以固定 Tag/Group 替换上一条运行提示。管理员模式或运行环境不支持 App SDK 通知时回退到现有托盘图标气泡；用户或组策略明确禁用通知时不绕过系统设置。设置保存结果通过 FluentAvalonia `InfoBar` 在设置页内反馈。
- 中文/English 界面切换，中文为默认语言，语言选择持久化到配置文件。
- Core 自动化测试：坐标换算、区域边界、状态更新去重、身份不匹配保护、配置归一化。
- Picker 同时使用鼠标和键盘低级钩子；Esc 按下、自动重复和抬起均被拦截且只取消一次，并恢复进入 Picker 前的 Ghost、Reveal 或完整显示状态。

## 已完成：Phase 2 Reliability 首个增量

- 新增独立 `GhostSlacking.Watchdog` 可执行项目并加入解决方案；App 构建输出会携带 Watchdog 运行文件。
- 使用仅限当前用户的本地命名管道与 Watchdog v1 逐行 JSON 协议。
- 支持随机会话 ID、版本化 `Hello/HelloAcknowledged` 握手、心跳、恢复清单和 `ShutdownCompleted` 正常关闭。
- 恢复清单包含独立 manifest ID、snapshot schema 2、HWND、PID、进程启动身份、完整 placement、region/style/显示状态和 DWM 恢复字段；缺少 placement 的旧恢复项会被拒绝。
- 主进程在恢复配置注册、移除或成功恢复时同步刷新最后清单；正常清理成功后通知 Watchdog 清除清单。
- Watchdog 心跳超时后只对 HWND、PID 和进程启动身份全部匹配的目标逐项恢复，并记录成功、失败或跳过原因。
- 同一会话清单只能恢复一次；超时取出或正常关闭后清除内存中的旧清单，避免重复处理。
- 新增协议序列化、协议版本、会话校验、心跳超时、正常关闭、PID/HWND 复用、进程启动身份不匹配和幂等恢复测试。
- Debug 基线：解决方案构建 0 警告/0 错误；Core 测试 91/91、App 通知测试 9/9 通过；本地命名管道握手、空清单、正常关闭和断连超时冒烟测试退出码均为 0。

## 尚未完成：后续 Phase

- Phase 2 其余可靠性工作：WinForms/AppDomain/注销关机恢复路径的完整协调、权限错误用户反馈、DPI/显示器热插拔验证和长时间资源观测。
- Phase 1/2 真实窗口验收：普通 Win32、资源管理器、浏览器、Electron/自绘窗口上的 Ghost/Reveal/Restore、区域内点击/滚轮、焦点与重绘；100%/125%/150% DPI、负坐标与混合缩放双屏、移动/缩放/最小化/关闭、普通/管理员权限目标，以及主进程异常终止后的 Watchdog 真实恢复。当前自动化环境未替代这些交互式检查。
- 安装包、诊断导出和更完整的用户反馈（Phase 3）；当前已实现开机启动注册的基础开关。
- Composition 羽化的 Chrome/Electron、混合 DPI、远程桌面、透明效果关闭和图形设备丢失手工兼容性验收。
