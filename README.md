# GhostSlacking

Windows 10/11 x64 上的轻量窗口 Ghost/Peek 工具。当前实现包含 V0.1 Phase 1 闭环和 Phase 2 的首个 Watchdog 恢复增量：

`Pick Window → Ghost → Peek 局部 Reveal → Restore`

Reveal 形状可在设置中选择：圆形、矩形或圆角矩形（默认圆形）。Peek 按键可在设置中自定义，建议使用 F8/F9 等不会影响浏览器的按键；Peek 模式支持按住显示或按下切换。

## 构建与测试

```powershell
dotnet build GhostSlacking.sln --configuration Debug
dotnet test GhostSlacking.sln --configuration Debug
dotnet run --project src/GhostSlacking.App --configuration Debug
```

默认快捷键（全部可在设置中修改）：

- `Ctrl+Alt+G`：没有目标时开始拾取；有目标时切换当前窗口的隐藏/显示状态
- `Ctrl+Alt+P`：选择窗口
- 自定义 Peek 按键：按设置的模式显示或切换光标附近的 Reveal 区域
- `Ctrl+Alt+R`：恢复当前窗口
- `Ctrl+Shift+Alt+R`：Emergency Restore All
- `Ctrl+Alt+Q`：退出并按设置恢复

设置页会分别显示并捕获选择窗口、窗口显隐、恢复当前窗口、恢复所有窗口、打开设置和退出应用的快捷键；`Ctrl+Alt+R` 仍会最终恢复窗口并解除目标关联。

实现使用 `SetWindowRgn` 硬边裁剪，不截图、不注入目标进程，也不提供防录屏保证。特殊窗口、不同权限窗口和高频重绘窗口仍需按文档中的手工矩阵验证。

独立 `GhostSlacking.Watchdog` 进程通过当前用户本地命名管道接收版本化心跳和恢复清单。主进程意外停止心跳时，Watchdog 只恢复 HWND、PID 与进程启动身份均匹配的登记目标；正常退出会清除最后清单。
