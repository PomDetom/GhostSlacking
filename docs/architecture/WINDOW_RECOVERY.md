# 窗口生命周期与恢复

## 修改入口与快照

`GhostCoordinator` 经 `VisibilityEngine` 和 `Win32VisibilityBackend` 控制 region、位置、样式与可见性；`RecoveryManager` 持有恢复资料。后台交互的临时非激活样式由独立会话经 Platform 操作，同样以已登记快照及身份检查为前提。UI 不直接修改目标窗口。

第一次修改前必须完整捕获并登记 `WindowSnapshot`：HWND、PID、进程启动身份、屏幕矩形、完整 `WINDOWPLACEMENT`、显示/最小化状态、原始 region 数据、style/ex-style 以及可读取的 DWM 属性。必需身份或 placement 捕获失败时，目标保持原状。快照 schema 为 2，region 复制为托管数据，不把临时 GDI 句柄当恢复资料。

主进程和 Watchdog 恢复前均核对 HWND、PID 与进程启动身份。窗口标题不用于身份判断；目标关闭或句柄被复用后不得向新窗口重放恢复。恢复资料不包含窗口内容，Watchdog 清单只存内存，不持久化旧 HWND。

## 状态转换

状态定义以 [状态定义](../../src/GhostSlacking.Core/Coordination/GhostState.cs) 和 [协调器](../../src/GhostSlacking.Core/Coordination/GhostCoordinator.cs) 为准：

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

主程序每 tick 观察目标身份和位置；失效后清理活动状态。Platform 另外读取窗口 DPI、显示器缩放、屏幕/工作区及 placement，Core 比较显示上下文。真实变化先暂停旧矩形纠正，Ghost/Reveal 目标仅隐藏，Visible 目标保持显示；几何连续稳定 250ms 后接受系统调整的位置与尺寸，再按当前 Peek 意图重新建立 region/overlay。过渡最长两秒；查询失败、状态不符或超时进入 RecoveryError，保留恢复资料并提示手动恢复，不自动反复显示。普通漂移最多连续纠正十次，成功观测匹配后清零。

几何更新再次验证 HWND/PID/进程启动身份，只替换恢复快照的屏幕矩形与 placement 位置字段，保留最初捕获的显示状态、flags、可见性、样式、region、DWM 属性及捕获身份；不把 Ghost 状态重捕获为原始状态。首次稳定后，原两秒预算内后到的尺寸调整重新等待 250ms 静止，不立即纠正回刚接受的矩形，也不重置总超时。RecoveryManager 发布 ProfilesChanged，使主恢复路径与 Watchdog 使用相同的新几何；现有 schema 不变。过渡期间尚未稳定时保留上一份完整恢复资料。缩放过渡会结束实验输入并清理按下账本，实验模式须重新触发 Peek。

临时工具窗口样式会改变 placement 坐标约定；Platform 将运行期读取的位置换算回原始样式约定，保留无位置的 (-1,-1) 哨兵。保持最大化/最小化 placement 时则反向转换到当前样式约定，避免顶部/左侧任务栏导致坐标偏移。依据 Windows 的 [WINDOWPLACEMENT 坐标约定](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-windowplacement)。实际单屏缩放和混合 DPI 验收边界见[质量边界](../quality/KNOWN_LIMITATIONS.md)。

Restore 恢复 region、style/ex-style、DWM 属性、TopMost、placement 和原始可见性。异步恢复要求连续三次观测匹配，最多纠正十次以容纳浏览器延迟漂移；失败保留恢复资料。同步 Restore All 逐项返回成功、失败或跳过结果。

最大化恢复由 Windows 按当前工作区及 DPI 决定外框，不能强制重放旧缩放的像素矩形。Platform 在身份验证后检查原生最大化状态、目标显示器及工作区覆盖，允许的外扩只取窗口/显示器 DPI 对应的系统缩放边框；原始工具窗口也允许其显示器全区域。合法的新几何先登记并同步清单，再由异步/同步恢复使用同一基准完成三次稳定观测、清除恢复资料和菜单状态。普通窗口仍要求精确矩形，错误显示器、未最大化或异常外框不能仅因可见而算成功。Watchdog 的原生恢复共用此最大化验证，schema 不变。日志区分 RestoreApplied（已执行）与 RestoreVerified（稳定验证完成）。

## Watchdog 与退出

Watchdog 使用仅限当前用户的命名管道和逐行 JSON 协议。每次启动随机 session ID；握手校验协议、主 PID/启动身份，之后发送版本化恢复清单和心跳，正常恢复完成后发送 `ShutdownCompleted`。清单含 manifest ID、schema 2 快照及全部恢复字段；缺少 placement 的旧项拒绝。

主程序在恢复资料注册/移除/恢复成功时刷新清单。Watchdog 心跳超时取出最后有效清单，逐项校验身份后恢复并记录结果；取出、正常关闭后清空旧清单，同一会话清单只处理一次。版本/会话不匹配、握手前消息和关闭后追加消息拒绝。

可控退出取消更新/Picker/交互，按 `RestoreOnExit` 或强制恢复标志执行 Restore All，再释放 Hook、热键、视觉层和 UI。安装关闭请求强制恢复；成功后才通知 Watchdog 正常关闭，否则保留清单供其超时处理。UI 线程异常记录并提示，`AppDomain.UnhandledException` 只保留诊断，不保证异常中执行恢复。注销/关机消息提前恢复尚未实现；异常恢复依赖独立 Watchdog。
