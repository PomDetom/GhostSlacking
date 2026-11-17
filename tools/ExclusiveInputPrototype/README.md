# 独占输入验证工具

x64 实验交互：Reveal 期间只操作目标 B，保留前台 A 的真实焦点；取消 Reveal 后恢复 A 的正常输入。不提供多用户并行操作，不依赖 MouseMux，不调用 Scintilla 的复制、选区等专用编辑命令。

正常构建和安装包已携带生产桥接，用户通过实验设置启用即可。本目录只维护独立验证工具，不承担生产 DLL 的源码或编译实现。

## 构建与启动

需要 PowerShell、.NET 8 SDK 和 Visual Studio C++ Build Tools：

```powershell
pwsh -NoProfile -File tools/ExclusiveInputPrototype/build.ps1
```

输出位于 `artifacts/tools/ExclusiveInputPrototype/packaged-input/`。验证副本为其中的 `app/GhostSlacking.App.exe`，`GhostSlacking.InputBridge.dll` 随它放置，Watchdog 沿用现有恢复路径。编译原件由 `scripts/build-input-bridge.ps1` 与正常 App 构建共享。

1. 在旧副本托盘恢复全部窗口并退出；启动脚本发现旧 App 时只拒绝启动，不结束它。
2. 运行 `pwsh -NoProfile -File tools/ExclusiveInputPrototype/start.ps1`。无需启动参数，设置格式不变。
3. 按[实验操作](../../docs/product/EXPERIMENTAL_BACKGROUND_INTERACTION.md)启用后台交互、Toggle Peek 和持续交互。验证副本沿用用户设置，保存设置仍有正常的持久化效果。
4. A 打开[焦点诊断页](../background-focus-probe.html)，B 先用同目录 `BridgeFixture.exe --target` 或空白测试编辑器。选择并隐藏 B，回到 A 开始记录，再 Peek。
5. 测试点击、英文、回车、退格、拖选与编辑快捷键；每项检查实际结果。取消 Peek 后确认 A 正常输入、没有卡键；用普通恢复确认 B 正常。不要用敏感内容或发送真实聊天。

首次绑定、首次点击、取消和重新进入均计入焦点区间。必须 A 全程无 blur/hidden、系统前台无异常变化且 B 结果正确，才算该操作通过。发生保护退出不能记成功。更多验收条件见[手工矩阵](../../docs/quality/WINDOWS_MANUAL_MATRIX.md#实验性后台交互)。

## 机制与边界

生产实现位于 Platform，正常 App 构建自动携带 DLL；机制由[Reveal 与输入](../../docs/architecture/REVEAL_INPUT.md#原生桥接与菜单)维护，组件分发由[安装与更新](../../docs/architecture/INSTALLATION_UPDATES.md#输入组件分发与加载)维护。此目录保留自有窗口和隔离 Chrome 验证工具。

## 隔离自测

```powershell
pwsh -NoProfile -File tools/ExclusiveInputPrototype/build.ps1 -NativeOnly
Push-Location artifacts/tools/ExclusiveInputPrototype/packaged-input
./BridgeFixture.exe --self-test
Get-Content bridge-self-test.log
Pop-Location
```

自测创建独立后台 Windows desktop 和自己的测试进程，不切换用户 desktop，不操作用户目标；覆盖首次点击不阻塞、消息循环快捷键、单次字符翻译、拖选状态、弹窗归属拒绝、跨出主窗口命中、菜单显示及嵌套置顶回调不越过独立宿主浮层、菜单激活拦截与取消、执行中取消、不重放旧输入、身份拒绝、停止、重新进入与租约过期。

原版 Chromium 的独立窗口探测（先完成上方 native 构建）：

```powershell
dotnet build tools/ExclusiveInputPrototype/ChromiumProbe/ChromiumProbe.csproj -c Release -o artifacts/tools/ExclusiveInputPrototype/packaged-input/chromium-probe
Copy-Item artifacts/tools/ExclusiveInputPrototype/packaged-input/GhostSlacking.InputBridge.dll artifacts/tools/ExclusiveInputPrototype/packaged-input/chromium-probe/
./artifacts/tools/ExclusiveInputPrototype/packaged-input/chromium-probe/ChromiumProbe.exe --browser 'C:\Program Files\Google\Chrome\Application\chrome.exe'
```

探测只启动新的独立 profile、后台 desktop 和空白测试页面，严格核对新建浏览器 PID。CDP 仅布置和读取该页面，UIA 只定位本次新建菜单的测试项；点击与悬停先经过 Core 硬件归属路由，断言右键释放后的无按钮移动仍可投递，再与按键一起使用桥接队列，不调用 UIA 操作模式；该探测未安装真实低级输入 Hook。复制事件和 Ctrl+V 默认动作在页面中取消，避免读写用户剪贴板。覆盖首次点击、英文、Ctrl+A、复制事件、粘贴按键事件、正文拖选、菜单复制、悬停展开、主窗口外子菜单命令、Esc 和按住菜单项取消；根窗口使用实际 Ghost/非激活 Reveal 样式，另建带消息循环的宿主进程浮层，使用真实 Platform 裁剪/恢复宿主，检查准备前隐藏、完整快照先登记、显示后 region 不超出 Reveal 且 Z 序始终低于浮层。输出为 `chromium-probe/chromium-probe.log`。正常关闭自建窗口，不结束用户浏览器。后台 desktop 的系统前台可能为零；该探测默认权限相同，不能证明管理员宿主与普通目标组合、可见 Composition 成像、真实 A 全程焦点或默认剪贴板粘贴通过。
