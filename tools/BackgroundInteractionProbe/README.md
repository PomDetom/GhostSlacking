# 后台交互独立探测工具

本工具用于后台交互的分层验证，不是已验证的 ChatGPT 编辑插件。UIA/原生/MSAA 的试验在独立、可终止的进程中运行，修改仅发生在工具自己创建的窗口；CDP 仅连接新建的隔离 headless Chromium。不会改变真实应用的启动参数或连接真实聊天，不发送消息。当前证据边界见[质量边界](../../docs/quality/KNOWN_LIMITATIONS.md#实验交互实际兼容性)。

从仓库根目录构建与执行：

```powershell
dotnet build tools/BackgroundInteractionProbe/BackgroundInteractionProbe.csproj -c Release --output artifacts/tools/BackgroundInteractionProbe
./tools/BackgroundInteractionProbe/run-probe.ps1 -Mode SelfTest
./tools/BackgroundInteractionProbe/run-probe.ps1 -Mode ResearchSelfTest
./tools/BackgroundInteractionProbe/run-probe.ps1 -Mode CdpSelfTest
./tools/BackgroundInteractionProbe/run-probe.ps1 -Mode TimeoutCheck -TimeoutSeconds 2
./tools/BackgroundInteractionProbe/run-probe.ps1 -Mode Inspect -WindowHandle 123456
```

- `SelfTest`：实际 UIA Invoke/Toggle/SelectionItem/ExpandCollapse、抬起一次执行、拖动与合并移动不误触、空白编辑框中文多行写入、已有草稿/空白字符/富子控件/只读保护、输入目标清理。用合成剪贴板依赖，不修改系统剪贴板。
- `ResearchSelfTest`：标准 Edit 的光标插入、选区替换、多行与撤销；MSAA 的单次默认动作。仅针对工具自己的控件，不能据此给 Chromium 补发 WM_PASTE 或认定真实 App 支持。
- 两种窗口试验都创建不激活的测试窗口，保护运行开始时现有前台 A。WinEventHook 记录整个试验期间的前台变化，守卫在实际调用前后复查；用户切换前台会导致失败。它不能证明浏览器 A 的 DOM 焦点或任意网站检测行为。
- `CdpSelfTest`：默认使用 Edge，可传 `-BrowserPath`。新建独立 profile、仅绑定 loopback 动态端口，在固定的空白页中验证鼠标点击、中文多行 Input.insertText、编辑器 input 事件、选区替换和撤销。不调用 Page.bringToFront；只比较前后台采样，没有可见 Reveal 或浏览器 A 的完整事件证据。
- `Inspect`：对显式 HWND 做只读根/首个编辑框能力检查，不读 Name、Value、Text 或 URL，不执行选择/写入/点击。它不是对整个页面的完整诊断。
- 包装脚本默认 20 秒硬截止，终止的只有它启动的 helper 进程树。`TimeoutCheck` 故意挂起 helper 以验证硬截止。修改操作一旦发出，终止 helper 不能撤销目标中可能发生的结果；不自动重试。

结果写入输出目录中的 `probe-*.jsonl`。隔离 Chromium 的 profile 留在对应输出目录用于检查；不要将它当作真实用户 profile 或打入试用包。浏览器测试只说明隔离宿主协议可用；真实目标是否开放连接、是否能定位其页面、可见窗口与 A 的焦点行为都待后续验证。
