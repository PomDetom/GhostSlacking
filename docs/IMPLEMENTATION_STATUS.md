# Implementation status

## 已完成：Phase 1 Core Demo 基线

- 三层项目边界：`Core`、`Platform`、`App`。
- Per Monitor V2 DPI manifest 和 WinForms DPI 初始化。
- 顶层窗口拾取：`WindowFromPoint`、`GetAncestor`、可见性/系统窗口/自身进程过滤。
- 原始窗口快照：HWND、PID、必需的进程启动标识、rect、可见/最小化状态、原始 region 数据和 style 快照；启动身份不可读取时在修改前失败。
- `Ghost → Reveal → Ghost → Restore` 状态协调器。
- 基于屏幕物理像素的圆形、矩形和圆角矩形区域几何计算，光标或窗口几何无变化时跳过 native 更新。
- `SetWindowRgn` 句柄所有权和失败释放路径。
- Ghost 使用 `SW_HIDE`，Reveal 时在显示前后均应用所选 region，兼容会在显示过程中重置窗口帧的窗口。
- Reveal 后读取 `GetWindowRgn`/`GetRgnBox` 校验裁剪结果；目标窗口临时使用 `WS_EX_TOOLWINDOW` 排除 Alt+Tab，恢复时还原原始扩展样式。
- 修正 GDI 椭圆 region 右/下边界的开区间换算，避免校验误报导致 Reveal 被保持隐藏。
- Windows 11 Reveal 期间临时关闭 `DWMWA_SYSTEMBACKDROP_TYPE`，恢复时还原原始 DWM backdrop 设置。
- Reveal 期间移除目标窗口的标题栏、边框和系统按钮，并关闭 DWM 非客户区渲染；Restore 时恢复原始窗口样式。
- Ghost/Reveal 期间临时置顶目标窗口，避免 Alt+Tab 后目标位于其他窗口下方；Restore 时恢复原始 TopMost 状态。
- 全局热键、低级键盘 Peek 状态和拾取用低级鼠标钩子。
- 设置页支持捕获自定义 Peek 按键，避免 Alt/Shift 对浏览器或其他前台应用产生快捷键副作用。
- Peek 支持“按住显示”和“按下切换”两种模式；切换模式按下沿翻转状态，按键重复不会重复切换。
- 支持配置窗口显隐快捷键；当前目标可在隐藏和完整显示之间切换，最终恢复仍由 Restore 快捷键负责。
- 所有全局功能均可在设置页配置快捷键：选择窗口、窗口显隐、恢复当前窗口、恢复所有窗口、打开设置和退出应用；设置页支持捕获 Ctrl/Alt/Shift 组合键。
- 托盘菜单、设置页、JSON 配置、滚动日志、单实例互斥。
- 中文/English 界面切换，中文为默认语言，语言选择持久化到配置文件。
- Core 自动化测试：坐标换算、区域边界、状态更新去重、身份不匹配保护、配置归一化。
- Picker 取消会恢复进入 Picker 前的 Ghost、Reveal 或完整显示状态，避免领域状态与真实窗口可见性漂移。

## 已完成：Phase 2 Reliability 首个增量

- 新增独立 `GhostSlacking.Watchdog` 可执行项目并加入解决方案；App 构建输出会携带 Watchdog 运行文件。
- 使用仅限当前用户的本地命名管道与 Watchdog v1 逐行 JSON 协议。
- 支持随机会话 ID、版本化 `Hello/HelloAcknowledged` 握手、心跳、恢复清单和 `ShutdownCompleted` 正常关闭。
- 恢复清单包含独立 manifest ID、snapshot schema、HWND、PID、进程启动身份、region/style/显示状态和 DWM 恢复字段。
- 主进程在恢复配置注册、移除或成功恢复时同步刷新最后清单；正常清理成功后通知 Watchdog 清除清单。
- Watchdog 心跳超时后只对 HWND、PID 和进程启动身份全部匹配的目标逐项恢复，并记录成功、失败或跳过原因。
- 同一会话清单只能恢复一次；超时取出或正常关闭后清除内存中的旧清单，避免重复处理。
- 新增协议序列化、协议版本、会话校验、心跳超时、正常关闭、PID/HWND 复用、进程启动身份不匹配和幂等恢复测试。
- Debug 基线：解决方案构建 0 警告/0 错误；Core 测试 38/38 通过；本地命名管道握手、空清单、正常关闭和断连超时冒烟测试退出码均为 0。

## 尚未完成：后续 Phase

- Phase 2 其余可靠性工作：WinForms/AppDomain/注销关机恢复路径的完整协调、权限错误用户反馈、DPI/显示器热插拔验证和长时间资源观测。
- Phase 1/2 真实窗口验收：普通 Win32、资源管理器、浏览器、Electron/自绘窗口上的 Ghost/Reveal/Restore、区域内点击/滚轮、焦点与重绘；100%/125%/150% DPI、负坐标与混合缩放双屏、移动/缩放/最小化/关闭、普通/管理员权限目标，以及主进程异常终止后的 Watchdog 真实恢复。当前自动化环境未替代这些交互式检查。
- 安装包、诊断导出和更完整的用户反馈（Phase 3）；当前已实现开机启动注册的基础开关。
- Layered Window、软边和 DirectComposition（Phase 4，非 V0.1 必需）。
