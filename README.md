<div align="center">
  <img src="src/GhostSlacking.App/Assets/GhostSlacking.svg" width="96" height="96" alt="GhostSlacking 幽灵图标">
  <h1>GhostSlacking</h1>
  <p><strong>一键隐藏窗口，随时局部查看。</strong></p>
  <p>
    <a href="https://github.com/PomDetom/GhostSlacking/releases"><img src="https://img.shields.io/badge/Windows-10%20%2F%2011%20x64-0078D4?style=flat-square" alt="Windows 10 / 11 x64"></a>
    <a href="https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0"><img src="https://img.shields.io/badge/.NET-8-512BD4?style=flat-square" alt=".NET 8"></a>
    <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-1CB2A5?style=flat-square" alt="MIT License"></a>
  </p>
  <p>
    <a href="https://github.com/PomDetom/GhostSlacking/releases"><strong>下载安装</strong></a> ·
    <a href="docs/product/USAGE.md">使用指南</a> ·
    <a href="https://github.com/PomDetom/GhostSlacking/issues">反馈问题</a>
  </p>
</div>

---

GhostSlacking 是适用于 Windows 10/11 的窗口隐藏与局部查看工具。选中一个窗口后，你可以将它隐藏，再通过快捷键在鼠标附近临时显示一小块内容，按需查看、点击或滚动。

应用常驻系统托盘，一次管理一个目标窗口。查看结束后，可以收起局部区域继续隐藏，也可以随时恢复完整窗口。

## 主要功能

- **快捷隐藏与显示**：通过全局快捷键或托盘菜单选择目标窗口，随时切换隐藏与显示。
- **跟随鼠标的局部查看**：在窗口原有范围内打开局部可见区域（Reveal），查看内容并进行点击、滚动。支持圆形、矩形和圆角矩形，以及按住显示或按下切换两种 Peek 方式。
- **恢复完整窗口**：不再需要隐藏时，一键结束窗口控制，将窗口恢复原有状态。

## 下载与安装

需要 **Windows 10/11 x64** 和 **[.NET 8 Runtime x64](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0)**。安装包不包含运行时，请先确认已安装。

从 [GitHub Releases](https://github.com/PomDetom/GhostSlacking/releases) 下载 `GhostSlacking-<版本>-win-x64-setup.exe`，按向导完成安装。默认仅安装到当前用户，无需管理员权限。

启动后，应用会常驻系统托盘；点击幽灵图标或使用快捷键即可开始。详细安装说明与旧版本迁移方法见[使用指南](docs/product/USAGE.md)。

## 快速开始

1. **选择窗口**：按 `Ctrl+Alt+P`，单击要控制的窗口；按 `Esc` 可取消选择。
2. **隐藏窗口**：按 `Ctrl+Alt+G` 隐藏所选窗口，再次按下可显示。
3. **局部查看**：将鼠标移到窗口原有范围内，按 `Alt` 打开局部查看区域，再次按下收起。
4. **恢复窗口**：按 `Ctrl+Alt+R` 结束控制，恢复窗口原有状态。

更多操作与快捷键说明见[使用指南](docs/product/USAGE.md)。

## 文档

- [使用指南](docs/product/USAGE.md)：安装、日常操作、快捷键与设置说明。
- [实验性后台交互](docs/product/EXPERIMENTAL_BACKGROUND_INTERACTION.md)：默认关闭的后台交互功能，包含开启方式与能力边界。
- [兼容性与限制](docs/quality/KNOWN_LIMITATIONS.md)：已知问题、实际应用反馈与未验证范围。
- [开发文档](docs/README.md)：项目架构、开发约定与发布流程的文档入口。

## 开源许可

本项目采用 [MIT License](LICENSE)。
