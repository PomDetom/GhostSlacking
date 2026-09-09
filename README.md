# GhostSlacking

Windows 10/11 x64 上的轻量窗口 Ghost/Peek 工具。当前实现包含 V0.1 Phase 1 闭环和 Phase 2 的首个 Watchdog 恢复增量：

`Pick Window → Ghost → Peek 局部 Reveal → Restore`

Reveal 默认使用 144px 圆角矩形、16px 羽化范围和轻度模糊，直径快捷键步长为 16px；形状、羽化和模糊等级均可在设置中调整。原有 Reveal 中心保持完全清晰且可直接点击、滚动，外侧由系统合成器使用所选的单一模糊强度生成平滑渐入、淡出的透明毛玻璃。Peek 默认采用按下切换，也可改为按住显示；Avalonia + FluentAvalonia 设置页的每项输入均提供恢复默认按钮。

## 构建与测试

```powershell
dotnet build GhostSlacking.sln --configuration Debug
dotnet test GhostSlacking.sln --configuration Debug
dotnet run --project src/GhostSlacking.App --configuration Debug
```

## 发布打包

生成可双击安装的 Windows x64 MSI：

```powershell
.\installer\build-installer.ps1
```

输出文件为 `artifacts\installer\GhostSlacking-0.1.0-win-x64.msi`。安装包不包含 .NET；目标机器需要预先安装普通 [.NET 8 Runtime x64](https://aka.ms/dotnet/8.0/runtime-win-x64.exe)，不需要 Desktop Runtime。简体中文安装向导允许修改默认的 `%ProgramFiles%\GhostSlacking` 安装位置，并可独立选择开始菜单和桌面快捷方式；开始菜单快捷方式默认创建，桌面快捷方式默认不创建。安装完成后会显示成功页面。安装时会显示标准 UAC 提示，应用启动后仍以普通用户权限运行。

不带安全升级协议标记的旧版（包括 `0.1.0`）不能直接覆盖升级。安装器会在关闭程序或修改文件前提示用户：先退出程序，并在 Windows 设置的“已安装的应用”中卸载所有 GhostSlacking 条目，再重新运行新版 MSI；普通卸载不会删除 `%LocalAppData%\GhostSlacking` 中的设置与日志。

从带有升级协议标记的新版本开始，后续升级可直接运行更高版本（或同版本重新构建）的 MSI，无需先卸载。安装器会沿用原安装位置和快捷方式选择；如程序正在运行，会先请求安全退出并恢复受控窗口，最多等待 15 秒。升级事务成功后，只有升级前处于运行状态且使用交互式向导时才自动启动新版；全新安装和静默升级均不会自动启动。若程序或 Watchdog 未能安全退出，文件替换会失败并回滚保留旧版本。更改安装位置仍需先卸载再重新安装。

指定版本可运行 `.\installer\build-installer.ps1 -Version 0.2.0`；版本必须使用三段数字且升级时递增。构建完成后脚本会检查 MSI 的 UpgradeCode、升级协议标记、旧版拦截条件、功能迁移、关闭动作、事务时序和非提权重启动作。

默认快捷键（全部可在设置中修改）：

- `Ctrl+Alt+G`：没有目标时开始拾取；有目标时切换当前窗口的隐藏/显示状态
- `Ctrl+Alt+P`：选择窗口
- 自定义 Peek 按键：按设置的模式显示或切换光标附近的 Reveal 区域
- 默认未绑定：按可配置步长增大或减小 Reveal 直径
- `Ctrl+Alt+R`：恢复当前窗口
- `Ctrl+Shift+Alt+R`：Emergency Restore All
- `Ctrl+Alt+Q`：退出并按设置恢复

设置页会分别显示并捕获选择窗口、窗口显隐、Reveal 直径增减、恢复当前窗口、恢复所有窗口、打开设置和退出应用的快捷键；`Ctrl+Alt+R` 仍会最终恢复窗口并解除目标关联。

实现使用 `SetWindowRgn` 扩展目标的局部可见范围，并通过鼠标穿透的 Windows Composition overlay 在清晰 Reveal 外侧以单一模糊半径实时扩散和淡出；不截图、不注入目标进程，也不提供防录屏保证。窗口恢复保存完整 `WINDOWPLACEMENT`，并在延迟稳定校验完成前保留恢复资料。合成效果不可用时会退回原有硬边 Reveal。特殊窗口、不同权限窗口和高频重绘窗口仍需按文档中的手工矩阵验证。

独立 `GhostSlacking.Watchdog` 进程通过当前用户本地命名管道接收版本化心跳和恢复清单。主进程意外停止心跳时，Watchdog 只恢复 HWND、PID 与进程启动身份均匹配的登记目标；正常退出会清除最后清单。

选择窗口、选择成功和运行错误通过 Avalonia 非激活提示浮层反馈，无需打开设置窗口；连续提示会替换上一条并在 2.5 秒后隐藏。设置保存结果仍通过 FluentAvalonia `InfoBar` 显示在设置页内。
