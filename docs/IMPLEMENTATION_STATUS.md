# Implementation status

## 已完成：Phase 1 Core Demo 基线

- 三层项目边界：`Core`、`Platform`、`App`。
- Per Monitor V2 DPI manifest 和 WinForms DPI 初始化。
- 顶层窗口拾取：`WindowFromPoint`、`GetAncestor`、可见性/系统窗口/自身进程过滤。
- 原始窗口快照：HWND、PID、进程启动标识、rect、可见/最小化状态、原始 region 数据和 style 快照。
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
- 托盘菜单、设置页、JSON 配置、滚动日志、单实例互斥。
- 中文/English 界面切换，中文为默认语言，语言选择持久化到配置文件。
- Core 自动化测试：坐标换算、区域边界、状态更新去重、身份不匹配保护、配置归一化。

## 尚未完成：后续 Phase

- Watchdog 独立进程和恢复清单 IPC（Phase 2）。
- 真实窗口/DPI/权限/多屏手工验收记录；当前环境未替代用户进行交互式窗口测试。
- 安装包、诊断导出和更完整的用户反馈（Phase 3）；当前已实现开机启动注册的基础开关。
- Layered Window、软边和 DirectComposition（Phase 4，非 V0.1 必需）。
