## 发布说明 / Release notes

这是维护者发起的 `dev` → `master` 发布 PR。功能 PR 的发布说明会由发布工作流自动汇总。

## 发布版本 / Release version

- 稳定版标签：`vMAJOR.MINOR.PATCH`
- 本次发布目标版本：
- 是否包含未完成或已知限制：

## 发布范围 / Included changes

- 本次发布包含的功能 PR 或主题：
- 明确排除的 PR 或主题：

## 发布前验证 / Release validation

## 验证 / Validation

- `pwsh -NoProfile -File tests/Run-ReleaseTests.ps1`
- Windows 手工验证（按 [验收矩阵](../../docs/quality/WINDOWS_MANUAL_MATRIX.md)，未测环境说明原因）：

## 风险 / Risks

- Win32、恢复行为或兼容性风险：
- 安装、升级、卸载验证：


工程约定与知识归属见[开发导航](../../docs/README.md)。验证结果保存在本 PR，只有长期能力或限制变化才更新权威知识。
