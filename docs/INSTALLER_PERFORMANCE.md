# 安装器验证与性能记录

本记录区分已执行的本机测试、渲染验证与尚未完成的环境验收。3 秒是目标，不是已达成的承诺。

## 环境与计时口径

- 日期：2026-10-08；本机 Windows 11 Pro，OS build 26200，约 16 GiB RAM，.NET SDK 10.0.400。Windows 10 未测试；CI 使用 .NET 8 验证最低 SDK。
- 旧产品基线：`0.1.4-beta.6+44ede9ce141b8455865d2865ca9f24e890816c5d`。保存原 Windows Installer 缓存 MSI、安装文件及设置备份，逐次恢复旧 MSI 和相同 Feature 选择；未保存受控窗口内容。
- 每种压缩测量 5 次，均为 `cache=uncontrolled`，开启 verbose diagnostics 以提取阶段信息。未重启或清空 OS 文件缓存；测量期间有构建和其他本机活动。这些结果不能替代隔离环境中的冷缓存与热缓存及常规日志模式验收。
- `safe_close_ms` 是安装入口验证、发送关闭请求并等待主进程/Watchdog 的时间，发生在 UAC 之前。
- `apply_ms` 包含 Burn Apply 和 UAC 等待；`package_ms` 是 MSI 开始执行到 ApplyComplete 的区间，包含事务结束及 Burn 收尾。
- `post_uac_ms` 从 Burn 的成功 ElevateComplete 回调到 ApplyComplete，近似 UAC 后耗时。它没有覆盖 UAC 确认到提升通道建立、最后图标渲染及进程退出，因此不能用于宣称严格的“确认 UAC 到安装程序结束 ≤3 秒”。
- 原生 MSI 阶段通过新产品 server thread 的相邻 Doing action 时间计算，避免嵌套旧 MSI 的 ActionStart 提前截断旧产品移除区间。`file_execute_ms` 为 InstallExecute，包括实际文件写入及其他 deferred actions；`old_product_remove_ms` 包含旧 MSI 整个卸载事务；`commit_ms` 为新产品 InstallFinalize 区间。它们是阶段区间，不是孤立磁盘操作耗时。

## 压缩对照

以下为透明界面接入前、相同旧基线和相同应用 payload 的对照。压缩影响内部 MSI；Burn 还会压缩 EXE 外层容器，因此未压缩 MSI 的 EXE 不会按 MSI 原始大小等比例增长。

| 压缩 | EXE bytes | MSI bytes | safe close 中位/最大 ms | MSI 区间中位/最大 ms | UAC 后中位/最大 ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| high | 72,113,126 | 16,985,958 | 634.5 / 715.2 | 3,128.4 / 3,527.2 | 3,678.6 / 4,071.9 |
| MSZIP | 75,519,178 | 20,598,630 | 572.9 / 660.1 | 3,923.9 / 5,288.7 | 4,673.4 / 6,589.8 |
| none | 72,724,230 | 64,806,758 | 669.3 / 824.6 | 4,251.4 / 7,885.3 | 5,785.4 / 9,941.9 |

high 的五次 UAC 后耗时为 3678.6、3615.5、4071.9、3948.0、3505.9 ms；MSZIP 为 6589.8、4778.6、4648.2、3684.4、4673.4 ms；none 为 5785.4、5854.7、5044.0、9941.9、5118.1 ms。数据和详细日志在本机 `artifacts/performance/{high,mszip,none}/`。

决定：保留 high。MSZIP/none 没有达到中位耗时改善至少 20% 的门槛。所有 EXE 均小于 256 MiB。恢复点策略、回滚和安全关闭期限保持不变。

原生 MSI 阶段中位/最大值如下，单位 ms。旧产品移除包含其缓存 MSI 的运行时检查；阶段中位值不能直接相加为一次安装总耗时。

| 压缩 | 新产品 .NET 检查 | InstallExecute | 旧产品移除 | 提交 |
| --- | ---: | ---: | ---: | ---: |
| high | 204 / 228 | 737 / 874 | 997 / 1137 | 73 / 76 |
| MSZIP | 290 / 331 | 735 / 988 | 1198 / 1527 | 88 / 120 |
| none | 231 / 376 | 756 / 963 | 1237 / 3867 | 86 / 177 |

透明界面接入后的重复测量和剩余耗时在完成实际安装验证后补充。当前记录尚未证明 3 秒目标达成。

## 停顿调查

本机 Windows Installer 事件曾记录 16:24:11.634–16:25:32.667 的约 81 秒事务，应用退出日志为 16:25:30.153；另一次事务 16:07:27.262–16:07:30.603 约 3.34 秒。没有前一次的 MSI verbose log，无法确定等待发生在哪个 action。

对照测试没有复现约 81 秒停顿。不能将它归因于关闭超时、.NET 检测或压缩，也不能声称已消除该瓶颈。旧缓存 MSI 的运行时检查仍会执行；`NOT Installed` 优化只有新版成为被升级版本后才减少旧产品卸载检查。

## 自动化与界面

- 完整 Release suite：125 Core + 179 App = 304 个测试通过。发布元数据检查通过。
- 包装：验证 MSI 表和内嵌 MSI hash；验证 Bundle 四段版本、单一 vital MSI、隐藏 MSI、`MSIFASTINSTALL=0`、无禁用回滚属性、自包含 coreclr/WPF。发布入口输出 EXE、checksum、manifest，MSI 为内部构建产物。
- 实际 WPF 渲染：0%、1%、50%、99%、100%，分别使用 96/120/144 DPI。测试直接检查像素：透明 padding、白色眼睛、填充递增、99% 仍有灰色、100% 全部填充；另检查选项收起后图标屏幕位置、重复开始/完成防护、Enter/Space 激活和 Esc 取消事件，以及安装前目录拒绝。
- 本机 PNG：`artifacts/transparent-preview/ready.png`、`expanded.png` 和 `progress-*-dpi-*.png`。CI 生成相同渲染样例，上传 `installer-previews` artifact 供 PR 查看。渲染 DPI 不等于改变 Windows 显示缩放后的实际交互测试。
- 方案依据：[WPF 官方非矩形窗口](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/)及[Win32 分层窗口透明像素命中机制](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#layered-windows)。图标外围透明，不设置整个窗口的 WS_EX_TRANSPARENT，不要求界面提权。

## 复现方式

正常更新仅在 `%LOCALAPPDATA%\GhostSlacking\logs\` 写简要阶段耗时：`ghostslacking.log`、`updater.log`、`installer.log`。诊断更新可设置 `GHOSTSLACKING_UPDATE_DIAGNOSTICS=1`；手动 EXE 使用 `/log <绝对路径> --diagnostics`，得到 Burn log 与相邻的 MSI verbose log。

```powershell
./installer/Measure-Upgrade.ps1 `
    -BaselineMsi <已备份的旧安装器绝对路径> `
    -SetupExe <待测EXE绝对路径> `
    -Samples 5 -Cache uncontrolled `
    -OutputDirectory artifacts/performance/transparent
```

测量脚本会暂时卸载测试 Bundle 并恢复旧 MSI，逐次启动应用和 Watchdog 后测升级；结束时恢复旧 MSI 和 Feature 选择并启动应用。它是测试夹具，不是产品升级方式。先保存原安装器和设置；有原 Bundle 时还应重新运行原 EXE，恢复 Bundle 注册。仅在操作者实际控制缓存条件时标记 warm/cold，脚本不主动清空缓存。

独立 BA 的只读渲染模式：`artifacts/setup-publish/win-x64/GhostSlacking.Setup.exe --preview <PNG绝对路径> <0至100进度> <96/120/144 DPI> [ready|expanded]`。它不启动 Burn，也不安装产品。

## 尚待验收

- Windows 10；干净机器、缺少 .NET 的真实安装；Windows 10/11 的 100%/125%/150%/200% 系统缩放、透明像素穿透及任务栏恢复。
- 同一快照/相同旧状态的严格冷缓存和热缓存各 5 次；覆盖最终进程退出的 UAC 后外部计时。
- 故障注入后的 MSI 失败/回滚、文件占用、主进程/Watchdog 超时、窗口恢复失败、UAC 取消及普通权限边界。相关状态和结果反馈有自动化覆盖，不能代替这些实际场景。
- 透明 RC 窗口已在本机启动。界面自动化将它识别为 Visual Studio Installer，随后拒绝窗口身份验证，无法读取或点击；已请求操作者手动完成或关闭该窗口。该实例仍停在 DetectComplete，未开始安装。实际手动流程和最终透明 EXE 的五次重测尚未完成，避免与仍打开的安装入口同时改变安装状态。
- 旧 MSI → EXE 已通过本机迁移及前述 15 次压缩对照：原目录和快捷方式 Feature 沿用，退出码 0；单一可见 Bundle 卸载项，内部 MSI 隐藏。透明窗口下的 EXE → EXE 更新、同版本修复和手动选项验收仍待完成。
