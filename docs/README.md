# 项目知识导航

面向用户的入口是[产品介绍](../README.md)。开发与 Agent 从[根规则](../AGENTS.md)进入，修改某目录前读取最近的局部 `AGENTS.md`。

| 要解决的问题 | 权威来源 | 维护时机 |
| --- | --- | --- |
| 如何使用、安装和设置 | [使用指南](product/USAGE.md)、[实验操作](product/EXPERIMENTAL_BACKGROUND_INTERACTION.md) | 用户操作变化时 |
| 进程与项目边界 | [架构入口](architecture/OVERVIEW.md) | 依赖或职责变化时 |
| 快照、身份、DPI、状态、Watchdog | [窗口恢复](architecture/WINDOW_RECOVERY.md) | 恢复契约或生命周期变化时 |
| region、Composition、帧率、后台输入 | [Reveal 与输入](architecture/REVEAL_INPUT.md) | 渲染或输入路径变化时 |
| 设置、HUD、日志、导出 | [应用服务](architecture/APP_SERVICES.md) | 应用服务变化时 |
| 签名与安装更新协议 | [安装更新](architecture/INSTALLATION_UPDATES.md) | 更新或安装事务变化时 |
| 环境、代码约定、PR 与保护规则 | [开发指南](development/DEVELOPMENT.md) | 工程工作流变化时 |
| 版本、打包、密钥和标签 | [发布指南](development/RELEASE.md) | 发布链路变化时 |
| 缺陷、未实现能力、未验证范围 | [质量边界](quality/KNOWN_LIMITATIONS.md) | 新证据改变边界时 |
| Windows 验收怎么做 | [手工矩阵](quality/WINDOWS_MANUAL_MATRIX.md) | 场景或预期变化时 |

源码、测试、配置及 CI 是可执行事实的依据。文档描述当前机制和重要原因，目标必须注明差距和验收条件。局部规则只承载局部约束；技能只承载重复流程。不要复制命令目录、保存会话进度或新增逐轮验证档案。重要决策写入其所属主题并解释原因，过时实现由 Git 保留。

仓库技能：[变更验证](../.agents/skills/ghostslacking-validation/SKILL.md)、[发布准备](../.agents/skills/ghostslacking-release-preparation/SKILL.md)。技能不替代目录规则或 CI，也不自动授权外部操作。
