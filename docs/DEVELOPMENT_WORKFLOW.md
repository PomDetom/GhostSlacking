# Git 与发布流程

## 分支职责

- `dev`：日常集成分支，允许发布测试版。
- `master`：稳定分支，只接受经过发布 PR 的内容。
- `feature/*`、`fix/*`、`docs/*`、`chore/*`：短期工作分支，完成后提交到 `dev`。

普通功能 PR 必须只解决一个相互关联的功能、问题或维护主题，并使用 Conventional Commits。功能 PR 合入 `dev` 使用 Squash merge。

## 稳定版发布

1. 确认需要发布的功能 PR 已经合入 `dev`。
2. 创建 `dev` → `master` 发布 PR，使用 `.github/PULL_REQUEST_TEMPLATE/release.md`。
3. CI、人工验证和发布范围确认完成后合并发布 PR。该 PR 使用 Merge commit，以保留功能 PR 的可追溯历史。
4. 在新的 `master` 合并提交上创建稳定标签，例如 `v0.2.0`，并只推送该标签。
5. 标签工作流生成 EXE、SHA256、`release.json` 和双语 GitHub Release。稳定版标记为 latest。

## 测试版发布

测试版不会因为每次 `dev` push 自动构建。只有需要给测试者发布时，才在当前 `dev` 提交上创建标签，例如：

```powershell
git switch dev
git pull --ff-only origin dev
git tag v0.2.0-beta.1
git push origin v0.2.0-beta.1
```

后续测试版使用 `v0.2.0-beta.2`，候选版使用 `v0.2.0-rc.1`。测试版发布为 GitHub Pre-release，不会成为 latest，也不会被默认稳定更新通道发现。

## 版本与安装器

GitHub 和应用使用完整 SemVer，例如 `0.2.0-beta.1`。“关于”页读取应用程序集的完整信息版本；发布构建会校验程序集实际写入的版本，防止 beta/RC 后缀丢失。Windows Installer 的 `ProductVersion` 以及 Windows“已安装的应用”版本字段只使用对应的三段基础版本 `0.2.0`；MSI 使用由完整 SemVer 派生的稳定 `ProductCode` 区分同一基础版本下的各次 beta/RC，并将阶段与序号编码到文件版本的第四段，以确保升级替换新程序集。内部 MSI 的 ProductVersion 为三段版本；Bundle 使用相同的四段文件版本编码。EXE 文件名、`release.json` 和安装窗口的任务栏/可访问标题保留完整 SemVer。Windows 的 Bundle 版本字段显示四段编码。

为让 Windows 文件版本保持 alpha → beta → RC → 稳定版的顺序，预发布序号受第四段 16 位范围约束：alpha 和 beta 的序号为 `0`–`16382`，RC 为 `0`–`32766`；稳定版使用 `65535`。超出范围的预发布版本会在打包时被拒绝。

在带有版本标签的提交上运行 `./build-release.ps1`，脚本会自动读取当前提交的标签，并将完整版本写入应用程序集，因此“关于”页会显示 beta/RC 后缀。当前提交没有受支持的精确版本标签时，脚本继续使用交互式版本选择；也可以通过 `-Version` 显式指定版本。

发布标签、EXE、SHA256 和 `release.json` 一经发布不可覆盖。修复发布问题必须使用新的 beta、RC 或稳定版本号。

## 应用更新通道

应用默认使用 Stable 通道，只检查稳定版。用户在设置中主动开启 Test 通道后，才会检查 beta/RC，并且仍然遵循版本比较、资产校验和安全升级流程。

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


EXE 打包由 `installer/build-installer.ps1` 先验证 MSI，再调用 `build-bundle.ps1` 发布独立自包含的 WPF BA 并内嵌 MSI。输出 `artifacts/installer/GhostSlacking-<SemVer>-win-x64-setup.exe`，内部 MSI 保留作验证产物。`build-release.ps1` 与 tag workflow 只发布 EXE、其 SHA256、release.json。旧 MSI 用户手动安装一次 EXE；不发布过渡 MSI。CI Release tests 还构建并验证 MSI/Bundle。性能和 Windows 手动验收见 `docs/INSTALLER_PERFORMANCE.md`。
