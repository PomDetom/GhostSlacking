# GhostSlacking

GhostSlacking 是一款适用于 Windows 10/11 的轻量级窗口隐藏与局部查看工具。它可以隐藏指定窗口，并在鼠标附近临时显示一块可交互区域，方便查看、点击或滚动内容，而不必完整恢复窗口。

```text
选择窗口 → 隐藏窗口 → 局部查看（Peek）→ 恢复窗口
```

## 主要功能

- 通过全局快捷键或系统托盘选择、隐藏和恢复窗口。
- 在鼠标附近显示圆形、矩形或圆角矩形 Reveal 区域。
- Reveal 区域支持羽化、模糊、点击和滚动操作。
- 支持按住显示和按键切换两种 Peek 模式。
- 支持自定义快捷键、Reveal 外观、界面语言和主题。
- 启动时检查稳定版更新，并在独立窗口中显示更新内容。
- 内置 Watchdog，在程序异常终止后尝试安全恢复受控窗口。
- 不截取窗口内容，也不向目标进程注入代码。

## 系统要求

- Windows 10 或 Windows 11，x64。
- [.NET 8 Runtime](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0)。
- 控制以管理员身份运行的窗口时，GhostSlacking 通常也需要以相同权限运行。

## 安装

从 [GitHub Releases](https://github.com/PomDetom/GhostSlacking/releases) 下载最新的 `GhostSlacking-<版本>-win-x64.msi`，然后按照安装向导完成安装。

GhostSlacking 启动后常驻系统托盘，不显示普通主窗口。

## 快速开始

1. 按 `Ctrl+Alt+P`，或单击托盘图标进入窗口选择状态。
2. 单击需要控制的窗口；按 `Esc` 可取消选择。
3. 按 `Ctrl+Alt+G` 隐藏或显示当前窗口。
4. 按 `Alt` 在鼠标附近显示或隐藏 Reveal 区域。
5. 按 `Ctrl+Alt+R` 恢复当前窗口。
6. 按 `Ctrl+Alt+S` 打开设置并调整快捷键或显示效果。

### 默认快捷键

| 功能 | 默认快捷键 |
| --- | --- |
| 选择窗口 | `Ctrl+Alt+P` |
| 隐藏/显示窗口 | `Ctrl+Alt+G` |
| Peek / Reveal | `Alt` |
| 恢复当前窗口 | `Ctrl+Alt+R` |
| 紧急恢复全部窗口 | `Ctrl+Shift+Alt+R` |
| 打开设置 | `Ctrl+Alt+S` |
| 退出应用 | `Ctrl+Alt+Q` |

所有快捷键均可在设置中修改。建议通过托盘菜单或退出快捷键正常关闭程序，以便恢复受控窗口。

## 软件更新

应用会在启动后检查 GitHub Releases。发现新版本时，可点击通知、托盘入口或“关于”页查看中英双语更新说明。点击“下载并安装”后，应用会自动完成下载、校验、安装和重新启动；安装期间仍需确认 Windows UAC 提示。

## 从源码运行

需要 Windows 10/11 x64、Git 和 [.NET 8 SDK](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0)。

```powershell
git clone https://github.com/PomDetom/GhostSlacking.git
cd GhostSlacking
dotnet restore GhostSlacking.sln
dotnet build GhostSlacking.sln --configuration Debug
dotnet run --project src/GhostSlacking.App --configuration Debug
```

运行测试：

```powershell
dotnet test GhostSlacking.sln --configuration Debug
```

## 更多信息

- [技术架构](docs/ARCHITECTURE.md)
- [实施计划](docs/IMPLEMENTATION_PLAN.md)
- [实施状态](docs/IMPLEMENTATION_STATUS.md)

配置和日志位于 `%LOCALAPPDATA%\GhostSlacking`。遇到异常时，请先按 `Ctrl+Shift+Alt+R` 尝试恢复全部窗口。
