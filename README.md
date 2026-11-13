# GhostSlacking

GhostSlacking 是适用于 Windows 10/11 的窗口隐藏与局部查看工具。隐藏指定窗口后，可在鼠标附近临时显示一块区域进行查看、点击或滚动。

```text
选择窗口 → 隐藏窗口 → 局部查看（Peek）→ 恢复窗口
```

## 主要功能

- 全局快捷键与系统托盘操作。
- 圆形、矩形和圆角矩形 Reveal，支持羽化、模糊与 Hold/Toggle Peek。
- 可调整快捷键、外观、HUD 通知、语言和主题。
- 签名更新校验，以及异常退出后的独立 Watchdog 恢复。
- 默认关闭的[实验性后台交互](docs/product/EXPERIMENTAL_BACKGROUND_INTERACTION.md)，当前兼容性有限。
- 不截取窗口内容，不向目标进程注入代码。

## 系统要求与安装

Windows 10/11 x64，需安装 [.NET 8 Runtime x64](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0)。从 [GitHub Releases](https://github.com/PomDetom/GhostSlacking/releases) 下载 `GhostSlacking-<版本>-win-x64-setup.exe`。默认安装到当前用户，无需管理员权限。旧 MSI 用户须先安全退出并手动卸载旧版，设置与日志保留。

## 快速开始

启动后程序常驻系统托盘：

1. 按 `Ctrl+Alt+P` 选择窗口，`Esc` 取消。
2. 按 `Ctrl+Alt+G` 隐藏或显示窗口。
3. 按 `Alt` 切换鼠标附近的 Reveal 区域。
4. 按 `Ctrl+Alt+R` 恢复当前窗口；紧急恢复全部使用 `Ctrl+Shift+Alt+R`。
5. 按 `Ctrl+Alt+S` 打开设置。

建议通过托盘正常退出，以恢复受控窗口。完整操作、设置和软件更新见[使用指南](docs/product/USAGE.md)，兼容性与限制见[质量边界](docs/quality/KNOWN_LIMITATIONS.md)。
