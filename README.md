# GhostSlacking

GhostSlacking 是一款适用于 Windows 10/11 的轻量级窗口隐藏与局部查看工具。它可以隐藏指定窗口，并在鼠标附近临时显示一块可交互区域，方便在不完整恢复窗口的情况下查看、点击或滚动内容。

核心使用流程：

```text
选择窗口 → 隐藏窗口 → 局部查看（Peek）→ 恢复窗口
```

## 功能特性

- 通过全局快捷键或托盘菜单选择目标窗口。
- 隐藏目标窗口，并在需要时切换完整显示状态。
- 在鼠标附近显示圆形、矩形或圆角矩形 Reveal 区域。
- Reveal 区域支持羽化和模糊效果，区域内可正常点击、滚动和操作。
- Peek 支持按住显示或按下切换两种模式。
- 可配置 Reveal 尺寸、调整步长、形状、羽化宽度和模糊等级。
- 可自定义窗口选择、显隐、恢复、设置、退出等全局快捷键。
- 支持简体中文和英文界面。
- 支持跟随系统、浅色和深色主题。
- 支持开机启动、退出时恢复窗口和日志级别设置。
- 支持从设置页导出日志诊断包和当前已保存的配置。
- 内置 Watchdog，在主程序异常终止后尝试安全恢复受控窗口。
- 单实例运行，不会同时启动多个托盘进程。

## 系统要求

- Windows 10 或 Windows 11，x64。
- [.NET 8 Runtime x64](https://aka.ms/dotnet/8.0/runtime-win-x64.exe)。无需安装 .NET Desktop Runtime。
- 控制以管理员身份运行的窗口时，GhostSlacking 通常也需要以相同权限运行。

## 安装

从项目的 GitHub Releases 页面下载 `GhostSlacking-<版本>-win-x64.msi`，双击并按照安装向导完成安装。

安装器支持选择安装位置，以及是否创建开始菜单和桌面快捷方式。普通卸载不会删除用户设置和日志。

## 使用方法

GhostSlacking 启动后常驻系统托盘，不显示普通主窗口。单击托盘图标可开始选择窗口，右键托盘图标可使用完整菜单。

### 基本流程

1. 启动 GhostSlacking。
2. 按 `Ctrl+Alt+P`，或单击托盘图标进入窗口选择状态。
3. 单击需要控制的普通顶层窗口。按 `Esc` 可以取消选择。
4. 按 `Ctrl+Alt+G` 隐藏或完整显示当前目标窗口。
5. 按 `Alt` 在鼠标附近显示或隐藏 Reveal 区域。默认模式为按下切换，可在设置中改为按住显示。
6. 按 `Ctrl+Alt+R` 恢复当前窗口并解除目标关联。

### 默认快捷键

| 功能            | 默认快捷键              |
| ------------- | ------------------ |
| 选择窗口          | `Ctrl+Alt+P`       |
| 隐藏/显示当前窗口     | `Ctrl+Alt+G`       |
| Peek / Reveal | `Alt`              |
| 恢复当前窗口        | `Ctrl+Alt+R`       |
| 紧急恢复全部窗口      | `Ctrl+Shift+Alt+R` |
| 打开设置          | `Ctrl+Alt+S`       |
| 退出应用          | `Ctrl+Alt+Q`       |
| 增大/减小 Reveal  | 默认未绑定              |

快捷键和 Peek 按键均可在设置页面中调整。若快捷键与其他应用冲突，GhostSlacking 会显示提示并保留可恢复的设置状态。

### 设置与退出

按 `Ctrl+Alt+S` 或从托盘菜单打开设置。界面主题会在选中后立即预览，取消或关闭窗口会恢复已保存主题；其他修改及主题持久化仍需点击“保存”后生效。保存成功状态会短暂显示后自动隐藏。

建议通过托盘菜单或 `Ctrl+Alt+Q` 正常退出。默认情况下，退出应用会恢复所有由 GhostSlacking 修改过的窗口。

## 配置、日志与数据

应用数据保存在当前用户的本地应用数据目录：

```text
%LOCALAPPDATA%\GhostSlacking\
├── settings.json
└── logs\
    ├── ghostslacking.log
    ├── ghostslacking.1.log ... ghostslacking.4.log
    ├── watchdog.log
    └── watchdog.1.log ... watchdog.4.log
```

- `settings.json`：用户设置。
- `ghostslacking.log`：主程序日志。
- `watchdog.log`：异常恢复进程日志。

两类日志均按 2 MB 单文件滚动，每类最多保留 5 个文件，并自动清理超过 30 天的备份；日志总占用上限约为 20 MB。默认日志级别为 `Info`，在设置页修改后会立即作用于主程序日志。Watchdog 始终保留 `Info` 及以上的恢复审计信息。

设置页的“数据与诊断”区域提供两个导出入口：

- “导出日志”生成 ZIP 诊断包，包含当前及滚动日志，以及不含用户名、机器名和配置内容的版本/运行环境摘要。
- “导出配置”生成 JSON 文件，只包含当前已保存并生效的设置，不包含尚未保存的界面修改。

所有导出都由用户主动选择保存位置；GhostSlacking 不会自动上传或发送这些文件。

可在资源管理器地址栏输入 `%LOCALAPPDATA%\GhostSlacking` 直接打开该目录。

## 从源码构建和启动

### 开发环境

- Windows 10/11 x64。
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。
- Git。

在 PowerShell 中执行：

```powershell
git clone <你的仓库地址>
cd GhostSlacking

dotnet restore GhostSlacking.sln
dotnet build GhostSlacking.sln --configuration Debug
dotnet run --project src/GhostSlacking.App --configuration Debug
```

程序启动后会出现在系统托盘中。调试期间请通过托盘菜单退出，避免已有实例占用单实例互斥锁或全局快捷键。

### 运行测试

```powershell
dotnet test GhostSlacking.sln --configuration Debug
```

发布前建议同时验证 Release 配置：

```powershell
dotnet test GhostSlacking.sln --configuration Release
```

## 构建安装包

双击仓库根目录的 `package.cmd`，或运行交互式发布脚本：

```powershell
.\build-release.ps1
```

脚本可以选择 Patch、Minor、Major 或指定版本，并依次完成依赖还原、Release 测试、应用发布、MSI 构建和安装包校验。输出位于：

```text
artifacts\releases\<版本>\
```

自动化环境可使用非交互参数：

```powershell
# 指定版本并执行完整构建
.\build-release.ps1 -Version 0.1.0

# 自动递增补丁版本
.\build-release.ps1 -Increment Patch

# 仅在临时排查时跳过测试
.\build-release.ps1 -Version 0.1.0 -SkipTests
```

如只需直接生成 MSI：

```powershell
.\installer\build-installer.ps1 -Version 0.1.0
```

MSI 将生成到 `artifacts\installer\GhostSlacking-0.1.0-win-x64.msi`。WiX 依赖由项目在还原和构建时自动获取。

## 发布到 GitHub

仓库包含以下 GitHub Actions 工作流：

- 推送到 `master` 或创建 Pull Request 时运行 Release 测试。
- 推送 `vMAJOR.MINOR.PATCH` 格式的标签时构建 MSI、生成 SHA256 与发布清单，并创建 GitHub Release。

示例：

```powershell
git tag v0.1.0
git push origin v0.1.0
```

## 项目结构

```text
src/
├── GhostSlacking.Core/       领域模型、状态协调、设置与恢复逻辑
├── GhostSlacking.Platform/   Windows/Win32 平台适配
├── GhostSlacking.App/        托盘主程序、设置与通知界面
└── GhostSlacking.Watchdog/   主程序异常后的窗口恢复进程
tests/
├── GhostSlacking.Core.Tests/
└── GhostSlacking.App.Tests/
installer/                    WiX MSI 安装器
docs/                         架构、实施计划和兼容性说明
```

更多实现细节请参阅 [技术架构](docs/ARCHITECTURE.md) 和 [实施状态](docs/IMPLEMENTATION_STATUS.md)。

## 工作原理与限制

GhostSlacking 使用 Windows 窗口区域和合成效果实现局部显示，不截取窗口内容，也不向目标进程注入代码。它不是防录屏或隐私防护工具。

部分特殊窗口、使用独立渲染表面的应用、高权限窗口或高频重绘窗口可能存在兼容性限制。恢复窗口时会校验窗口句柄、进程和进程启动身份，避免将旧状态错误应用到已复用的窗口句柄。

遇到异常时，请先使用 `Ctrl+Shift+Alt+R` 尝试恢复所有窗口，再查看 `%LOCALAPPDATA%\GhostSlacking\logs` 中的日志。
