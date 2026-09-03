# GhostSlacking

Windows 10/11 x64 上的轻量窗口 Ghost/Peek 工具。当前实现按 `docs/ARCHITECTURE.md` 和 `docs/IMPLEMENTATION_PLAN.md` 的 V0.1 Phase 1 闭环搭建：

`Pick Window → Ghost → 按住 Alt 局部 Reveal → Restore`

Reveal 形状可在设置中选择：圆形、矩形或圆角矩形（默认圆形）。

## 构建与测试

```powershell
dotnet build GhostSlacking.sln --configuration Debug
dotnet test GhostSlacking.sln --configuration Debug
dotnet run --project src/GhostSlacking.App --configuration Debug
```

默认快捷键：

- `Ctrl+Alt+G`：没有目标时开始拾取；有目标时恢复当前目标
- `Alt`：按住时显示光标附近的 Reveal 区域
- `Ctrl+Alt+R`：恢复当前窗口
- `Ctrl+Shift+Alt+R`：Emergency Restore All
- `Ctrl+Alt+Q`：退出并按设置恢复

实现使用 `SetWindowRgn` 硬边裁剪，不截图、不注入目标进程，也不提供防录屏保证。特殊窗口、不同权限窗口和高频重绘窗口仍需按文档中的手工矩阵验证。
