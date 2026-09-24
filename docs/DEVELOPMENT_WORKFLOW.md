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
5. 标签工作流生成 MSI、SHA256、`release.json` 和双语 GitHub Release。稳定版标记为 latest。

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

GitHub 和应用使用完整 SemVer，例如 `0.2.0-beta.1`。Windows Installer 的 `ProductVersion` 只使用对应的三段基础版本 `0.2.0`；MSI 文件名和 `release.json` 保留完整版本，以支持 beta 之间以及 beta 到稳定版的连续升级。

发布标签、MSI、SHA256 和 `release.json` 一经发布不可覆盖。修复发布问题必须使用新的 beta、RC 或稳定版本号。

## 应用更新通道

应用默认使用 Stable 通道，只检查稳定版。用户在设置中主动开启 Test 通道后，才会检查 beta/RC，并且仍然遵循版本比较、资产校验和安全升级流程。

## 仓库设置

GitHub 仓库应保护 `dev` 和 `master`：要求 PR、`Release tests`、对话解决，禁止 force-push 和删除分支。仓库合并设置只保留 Squash merge 和 Merge commit；发布 PR 使用 Merge commit，普通功能 PR 使用 Squash merge。
