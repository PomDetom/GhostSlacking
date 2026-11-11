# 开发指南

## 环境与常用命令

从仓库根目录使用 Windows、.NET SDK 8 或更新版本。CI 使用 .NET 8 验证最低支持版本。生产项目将编译警告视为错误。

```powershell
dotnet restore GhostSlacking.sln
dotnet build GhostSlacking.sln --configuration Debug
dotnet run --project src/GhostSlacking.App --configuration Debug
dotnet test GhostSlacking.sln --configuration Release
pwsh -NoProfile -File tests/ReleaseMetadata.Tests.ps1
pwsh -NoProfile -File tests/Installer.Tests.ps1
```

输出被运行实例占用时，可给 build/test 传 `-p:BaseOutputPath=bin/Validation/` 使用隔离输出，不结束用户正在使用的实例。生成文件只放 `bin/`、`obj/` 或 `artifacts/`。

## 代码组织

保留 Core、Platform、App、Watchdog 和 Updater 五个生产项目及两个测试项目。按职责而非文件类型分目录：Core 分协调、窗口、Reveal、输入、设置、恢复、更新、诊断和公共基础；Platform 分窗口、输入、后台交互、安装和恢复；App 分宿主、设置、Reveal、通知、更新、诊断和表现资源。

目录移动不强制改变命名空间。新增代码延续现有项目命名空间；项目内 partial 文件以 `类型.职责.cs` 命名，状态与生命周期有集中入口。独立职责拆为独立类型文件，紧密关联的小型记录或枚举可同文件，不以目录层数或文件数作为目标。程序集与进程边界见[总体架构](../architecture/OVERVIEW.md)。

遵循四空格、文件范围命名空间、可空引用与隐式 using；类型和公共成员 PascalCase，私有字段 `_camelCase`，参数及局部变量 camelCase。广泛格式调整使用 `dotnet format GhostSlacking.sln`，不要夹带无关格式差异。

## 验证与知识维护

按[测试规则](../../tests/AGENTS.md)选择检查；完整 Release 套件必须通过。涉及真实窗口、焦点、恢复、显示或 UI 的变更还需对应[手工矩阵](../quality/WINDOWS_MANUAL_MATRIX.md)。结果记录在 PR，缺少设备时注明条件不足，不能将自动化当作真实兼容性结论。

通过[知识导航](../README.md)找到事实的唯一归属，随行为改动更新。实现、目标、提案和未知范围必须区分；历史实现由 Git 保留，实施过程、待执行清单、逐轮报告、普通测试计数不进入长期知识。

## 分支职责

- `dev`：日常集成分支，允许发布测试版。
- `master`：稳定分支，只接受经过发布 PR 的内容。
- `feature/*`、`fix/*`、`docs/*`、`chore/*`：短期工作分支，完成后提交到 `dev`。

普通功能 PR 必须只解决一个相互关联的功能、问题或维护主题，并使用 Conventional Commits。功能 PR 合入 `dev` 使用 Squash merge。

## 仓库设置

`dev` 和 `master` 都使用相同的分支保护规则：

| 设置 | 要求 |
| --- | --- |
| 合入方式 | 必须通过 Pull Request；单维护者配置要求 0 个额外批准。 |
| 状态检查 | 必须通过 GitHub Actions 的 `Release tests`，并要求分支在合入前与目标分支保持最新。 |
| 对话 | 必须解决 PR 对话。 |
| 分支历史 | 禁止 force-push 和删除分支；不要求线性历史，以便保留发布 PR 的 Merge commit。 |
| 管理员 | 保留管理员应急绕过，仅用于恢复；日常变更仍走 PR。 |

仓库级合并方式只启用 Squash merge 和 Merge commit，关闭 Rebase merge。普通功能 PR 合入 `dev` 使用 Squash merge；`dev` → `master` 发布 PR 使用 Merge commit。修改 GitHub 设置后，回读两个分支的保护规则和仓库合并方式，确认与本节一致。

版本、打包和标签流程见[发布指南](RELEASE.md)。
