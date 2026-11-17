# Reveal 独占输入原型

显式启用的 x64 研究路径：Reveal 期间只操作目标 B，保留前台 A 的真实焦点；取消 Reveal 后恢复 A 的正常输入。不提供多用户并行操作，不依赖 MouseMux，不调用 Scintilla 的复制、选区等专用编辑命令。

本轮选型阶段已完成，维护者确认最新试用版的反馈场景无异常；该桥接已成为后续实验交互的技术方向。[路线状态](../../docs/architecture/REVEAL_INPUT.md#技术路线状态)维护结论与产品化边界，本页维护当前显式试用入口和机制；仍需专用构建，不自动启用到默认运行路径。

## 构建与启动

需要 PowerShell、.NET 8 SDK 和 Visual Studio C++ Build Tools：

```powershell
pwsh -NoProfile -File tools/ExclusiveInputPrototype/build.ps1
```

输出位于 `artifacts/tools/ExclusiveInputPrototype/menu-hover/`。原型 App 为其中的 `app/GhostSlacking.App.exe`，`GhostSlacking.InputPrototype.MenuHover.dll` 随它放置，Watchdog 沿用现有恢复路径。新目录和 DLL 名称避免覆盖仍在运行的旧原型。

1. 在旧副本托盘恢复全部窗口并退出；启动脚本发现旧 App 时只拒绝启动，不结束它。
2. 运行 `pwsh -NoProfile -File tools/ExclusiveInputPrototype/start.ps1`。也可直接给原型 App 加 `--exclusive-input-prototype`。无该参数仍走原有实现，设置格式不变。
3. 按[实验操作](../../docs/product/EXPERIMENTAL_BACKGROUND_INTERACTION.md)启用后台交互、Toggle Peek 和持续交互。当前原型沿用用户设置，保存设置仍有正常的持久化效果。
4. A 打开[焦点诊断页](../background-focus-probe.html)，B 先用同目录 `BridgeFixture.exe --target` 或空白测试编辑器。选择并隐藏 B，回到 A 开始记录，再 Peek。
5. 测试点击、英文、回车、退格、拖选与编辑快捷键；每项检查实际结果。取消 Peek 后确认 A 正常输入、没有卡键；用普通恢复确认 B 正常。不要用敏感内容或发送真实聊天。

首次绑定、首次点击、取消和重新进入均计入焦点区间。必须 A 全程无 blur/hidden、系统前台无异常变化且 B 结果正确，才算该操作通过。发生保护退出不能记成功。更多验收条件见[手工矩阵](../../docs/quality/WINDOWS_MANUAL_MATRIX.md#实验性后台交互)。

## 当前机制与边界

- 通过目标 UI 线程的 `WH_CALLWNDPROC` 加载桥接 DLL。只处理已验证身份的根窗口、同线程子控件及下述菜单，不默认提权；拒绝 x86、GS 自身和 A/B 同进程。
- 原型仅替换目标已加载的非 Windows 模块中部分 User32 普通及延迟导入表条目：局部焦点/激活、键状态、光标、鼠标捕获、消息位置、字符翻译和指定菜单的创建/显示。只在所选线程的有效会话中生效；不修改执行代码，不钩 A，不连接渲染器或私有接口。
- 输入写入容量 128 的有界队列，`WH_GETMESSAGE` 在 B 的正常消息循环中还原消息，让应用先处理快捷键，再按其 `TranslateMessage` 调用生成字符。桥接返回只表示入队，不等待每次输入处理。目标尝试 Win32 激活/真实设焦点时由该线程 CBT 守卫拒绝。
- 菜单支持限于与 B 同 PID、同 UI 线程、所有者链回到 B 的启用、无标题栏且不穿透的 `Chrome_WidgetWin_1` 弹出窗口（Chrome/Electron Views 菜单及其子菜单）。指定窗口的首次显示先被延后；Platform 捕获完整快照并加入同一 Watchdog 恢复清单，设置与鼠标 Reveal 一致的裁剪，再通知目标线程非激活显示在浮层下。未准备好的菜单不接收鼠标操作，取消或失败不重放显示与输入。带标题栏对话框、其他线程、其他所有者和原生 `#32768` 系统菜单仍不纳入。
- 菜单和主窗口共享随鼠标移动的 Reveal，不完整展示菜单，也不冻结区域；菜单伸出 B 时扩展可交互表面和浮层宿主范围。按当前 Z 序及窗口矩形命中，独立于上一帧裁剪。宿主先将自己的浮层非激活置顶，不联动所有者 Z 序，再提交菜单就绪；目标仅显示自身菜单，保留 Z 序，嵌套显示回调也不能将菜单置顶。桥接协议不传递浮层 HWND，避免普通权限目标引用管理员宿主浮层作为 `hWndInsertAfter` 时返回拒绝访问。所有菜单窗口修改前均已登记快照，隐藏/销毁后还原或移除辅助恢复资料，保持 schema 不变。
- 菜单准备或显示失败仍保护退出，不重试不确定操作；诊断 operation 区分区域读取、显示和显示后区域重设，不记录菜单文本或输入内容。
- 独占原型在硬件归属路由显式启用无按钮移动转发，拖动结束或右键松开后仍持续发送位置、悬停与光标更新；准备、停止和默认后台路径不启用此路由。系统移动仍透传以驱动 Reveal 跟随，不产生第二个系统光标。刚进入 Reveal 即初始化光标；菜单完成裁剪和显示后，在激活守卫内通过正常消息循环立即刷新当前点的悬停和光标，不要求额外点击或移动。菜单在按下时关闭或销毁，仅消费该菜单匹配的抬起并清理目标线程账本，不把菜单关闭当成根目标身份改变，也不向复用的 HWND 投递。
- 沿用现有 Hook：键盘和鼠标按钮/滚轮在交互时独占，系统光标移动仍用于 Reveal 跟随，不提供第二个独立系统光标；移出 B 及有效菜单范围后结束本次交互。菜单的真实激活/设焦点仍由同一 CBT 守卫拦截。
- 取消立即丢弃尚未执行的队列项，并在激活保护内先取消菜单；鼠标释放交由 native 清理，不额外入队一次可能触发菜单项的普通抬起。已在执行的消息返回消息循环后释放实际执行过的按下、还原导入表并解除 Hook。为避免遗留回调指向已卸载代码，目标中的桥接模块会驻留到目标正常退出；它在会话外透传，不影响原调用。原型失败或不再需要时可正常关闭并重新打开该测试目标。
- GS 在 UI 交互轮询中续租，租约两秒。GS 退出、UI 停止续租、租约过期或 A 真失焦后目标侧虚拟接口透传；过期租约不能被迟到心跳复活，须重新建立会话。超时使会话失效，不重发不确定操作。超时/崩溃时导入条目和已加载模块可能仍驻留，但不再虚拟化；Watchdog 仍负责窗口样式/可见性恢复。
- 尚未覆盖动态解析的 API、新加载模块、其他 UI 线程、Raw Input、框架私有焦点、系统菜单/文件弹窗和中文组合输入。Win/Alt 系统组合与 IME 请求拒绝。Ctrl 编辑操作经过应用普通消息循环，剪贴板由目标按用户操作处理，桥接自身不读写剪贴板；默认复制/粘贴结果和真实 Chromium/Electron 兼容性待逐项验证。
- 本路径验证的是局部输入虚拟化能否有用，不承诺任意程序全程无失焦。独占输入不会消除应用内部焦点依赖。

## 隔离自测

```powershell
pwsh -NoProfile -File tools/ExclusiveInputPrototype/build.ps1 -NativeOnly
Push-Location artifacts/tools/ExclusiveInputPrototype/menu-hover
./BridgeFixture.exe --self-test
Get-Content bridge-self-test.log
Pop-Location
```

自测创建独立后台 Windows desktop 和自己的测试进程，不切换用户 desktop，不操作用户目标；覆盖首次点击不阻塞、消息循环快捷键、单次字符翻译、拖选状态、弹窗归属拒绝、跨出主窗口命中、菜单显示及嵌套置顶回调不越过独立宿主浮层、菜单激活拦截与取消、执行中取消、不重放旧输入、身份拒绝、停止、重新进入与租约过期。

原版 Chromium 的独立窗口探测（先完成上方 native 构建）：

```powershell
dotnet build tools/ExclusiveInputPrototype/ChromiumProbe/ChromiumProbe.csproj -c Release -o artifacts/tools/ExclusiveInputPrototype/menu-hover/chromium-probe
Copy-Item artifacts/tools/ExclusiveInputPrototype/menu-hover/GhostSlacking.InputPrototype.MenuHover.dll artifacts/tools/ExclusiveInputPrototype/menu-hover/chromium-probe/
./artifacts/tools/ExclusiveInputPrototype/menu-hover/chromium-probe/ChromiumProbe.exe --browser 'C:\Program Files\Google\Chrome\Application\chrome.exe'
```

探测只启动新的独立 profile、后台 desktop 和空白测试页面，严格核对新建浏览器 PID。CDP 仅布置和读取该页面，UIA 只定位本次新建菜单的测试项；点击与悬停先经过 Core 硬件归属路由，断言右键释放后的无按钮移动仍可投递，再与按键一起使用桥接队列，不调用 UIA 操作模式；该探测未安装真实低级输入 Hook。复制事件和 Ctrl+V 默认动作在页面中取消，避免读写用户剪贴板。覆盖首次点击、英文、Ctrl+A、复制事件、粘贴按键事件、正文拖选、菜单复制、悬停展开、主窗口外子菜单命令、Esc 和按住菜单项取消；根窗口使用实际 Ghost/非激活 Reveal 样式，另建带消息循环的宿主进程浮层，使用真实 Platform 裁剪/恢复宿主，检查准备前隐藏、完整快照先登记、显示后 region 不超出 Reveal 且 Z 序始终低于浮层。输出为 `chromium-probe/chromium-probe.log`。正常关闭自建窗口，不结束用户浏览器。后台 desktop 的系统前台可能为零；该探测默认权限相同，不能证明管理员宿主与普通目标组合、可见 Composition 成像、真实 A 全程焦点或默认剪贴板粘贴通过。
