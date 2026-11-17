# 运行结构与职责

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

## 机制入口

- [窗口生命周期与恢复](WINDOW_RECOVERY.md)：状态、快照、身份、DPI 和 Watchdog。
- [Reveal 与后台输入](REVEAL_INPUT.md)：region、Composition、调度和输入归属。
- [应用服务](APP_SERVICES.md)：设置草稿、通知、日志与导出。
- [安装与更新](INSTALLATION_UPDATES.md)：签名、独立 Updater 和原生安装事务。

当前限制及未验证范围见[质量边界](../quality/KNOWN_LIMITATIONS.md)，不能从实现或隔离测试推导真实应用兼容性。
