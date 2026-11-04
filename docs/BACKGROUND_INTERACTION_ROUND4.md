# 第四轮交付与诊断报告

本轮只编译，没有执行测试套件、启动主程序或研究原型，也没有进行真实交互/视觉验收。下表区分代码路径和实测结果，不从构建成功推断应用通过。

## 已知原因与未确认部分

- 2026-10-30 15:57:37 的日志记录：向 `Chrome_WidgetWin_1` 根窗口投递鼠标消息，约 3ms 后系统前台事件指向该目标，随后 `ForegroundChanged` 收起。16:06:35 再次出现同样顺序；该 PID 在检查时对应 Codex/ChatGPT。这证明旧通用路径发生了真实切换，不能当作“首次加载”忽略，也没有证据证明具体 Chromium 内部函数就是唯一原因。
- 旧通用键盘路径统一拒绝 Ctrl 组合，Scintilla 之外的复制粘贴没有专用实现。第四轮按控件能力分发，不再统一拒绝或靠整字段覆盖插入。
- 旧 Core 尾部路径只要还有一个按键记录，就继续捕获新按键，且没有绝对截止；这是可确认的实现缺口。但现有旧日志没有尾部数量、卸载结果及输入法状态，不能确认它就是本次“恢复 B 后 Shift 失效”的根因。还需区分目标自身输入上下文状态与残留钩子/修饰键。

## 实现与边界

- 物理事件归属由独立 `BackgroundInputOwnership` 管理：前台已收到的按下、会话消费的按下和有限的系统组合尾部分开处理。目标消息账本继续只释放实际投递的按下。
- Hook 在独立消息线程运行，只分流/入队；实际控件操作由应用线程按代次处理。移动在相邻按钮/滚轮边界之间合并，按钮和抬起不合并。队列有界，停止后废弃旧代次。
- 停止后只保护已有按下和修饰键尾部；新的一对 Shift 不再因一个旧普通字符尾部被无限消费。独立线程使用单调时钟执行两秒绝对截止；期限不因新尾部延长，卸载失败时回调透传并继续尝试卸载。不会向前台注入“修复性”按键。
- Scintilla 保留标量定位、选区和编辑命令。标准 `Edit` 用 `EM_CHARFROMPOS/EM_SETSEL` 拖选，Ctrl+C/V 使用目标原生命令；标量坐标接口限制在 65535 字符以内，超出时拒绝定位而不是截断索引。其双击选词没有额外保证。
- Chromium/Electron、Firefox、WPF/部分 WinUI 以及 RichEdit 使用按 B 根窗口限定的 UI Automation 正文选区查询，禁用已知根窗口按钮回退。客户区坐标转换回 UI Automation 所需的物理屏幕坐标；不读取正文定位。
- UI Automation 复制只在明确 Ctrl+C 时读取选区，输出 Unicode 纯文本到系统剪贴板。标准控件复制/粘贴交由目标处理。UI Automation 没有已验证的光标插入接口时，Ctrl+V 保持会话并显示“粘贴受限”；不会调用 `ValuePattern.SetValue` 重写整个字段。
- UI Automation 有界等待 200ms，标量控件命令每次最多 50ms、一次访问总预算 75ms。提供者超时后的原生调用无法保证已撤销，记为结果未验证并收起；不能盲目重发。UIA 的 Select 也可能由提供者自行激活窗口，发生时按真实失焦退出，不自动抢回 A。
- 适配器增加逐操作能力和确认文本提交接口；正式程序的输入引擎仍为英文。单独 Shift、Ctrl+Space/Win+Space 请求组合输入时消费并提示“拼音受限”，不会误切 A；结束会话后由系统正常处理这些按键。Shift 组合仍用于大写和选择。
- 图标改为细线瓶身、瓶口、液面和两个气泡，前景色绑定导航项并使用既有主题/选中画刷。配置 schema 10、完整快照、身份校验和 Watchdog 协议不变。

## 应用与操作矩阵

| 目标 | 定位/拖选路径 | 复制 | 粘贴 | 本轮实际结果 |
| --- | --- | --- | --- | --- |
| Chrome / Edge | UIA TextPattern，提供者须支持选区 | UIA 选区纯文本 | 无已验证插入接口时不支持 | 全部待测 |
| Firefox | UIA TextPattern，受版本/提供者影响 | UIA 选区纯文本 | 无已验证插入接口时不支持 | 全部待测 |
| Codex / ChatGPT（Electron） | UIA TextPattern，不向根窗口发送按钮 | UIA 选区纯文本 | 无已验证插入接口时不支持 | 全部待测；第三轮根窗口点击已出现真实失焦 |
| Notepad++ / Scintilla | 标量定位/双击/拖选 | 控件命令 | 控件命令 | 第四轮待测 |
| 标准 Win32 Edit | 标量定位/正反拖选，长度受限 | WM_COPY | WM_PASTE | 待测 |
| RichEdit | UIA 定位/选区 | WM_COPY | WM_PASTE | 待测 |
| 聊天应用（记录名称/版本，不发送消息） | 根据实际文本提供者分发 | 按提供者能力 | 按提供者能力 | 待测，不声明所有聊天应用兼容 |
| 未识别应用 | 保留原通用路径，仍可能主动激活 | 没有适配时受限 | 没有适配时受限 | 待测；能力缺口须单列 |

“部分支持”必须注明通过的操作；“不支持”包含操作无效、错误接收输入或 A 曾失焦；“通过”要求 B 结果正确且 A 全程未失焦。不能用最终前台回到 A 代替完整证据。

IMM 查询可用时记录 `imeOpen`，不可查询记为 -1；该代理值不能单独代表所有 TSF 输入法的真实模式。`prepare/reset/restored` 诊断没有调用修改目标输入法上下文的 API。

## 用户复测顺序

1. 恢复全部窗口，退出旧版，启动 `artifacts/background-interaction-round4/GhostSlacking.App.exe`。保持原实验开关和持续模式设置，使用独立 Peek 键。
2. A 打开焦点诊断页；B 先测空白 Notepad++ / Edit，再测浏览器、Codex 与聊天应用。从冷启动、重新选窗开始，检查首次操作；不通过提前激活编辑区预热。
3. 分别检查正向、反向、跨行拖选，Ctrl+C，光标处 Ctrl+V、选区替换，以及 Unicode 文本/多行文本。受限操作应提示并保留会话，不能将文本复制到 A 的输入框。
4. 按住 Shift/Ctrl/普通键或鼠标，分别用 Peek、交互快捷键、移出目标、恢复、目标关闭等方式结束；验证释放后无卡键。两秒以后新 Shift、Ctrl+Space、Win+Space 必须由系统正常处理。
5. 结束后先正常激活 B 的同一个文本框验证切换；再退出 GhostSlacking，重新激活 B 做相同操作；补查 A 与第三个应用。记录时间及 Windows/应用版本。只有 B 仍异常且 Hook 明确全部卸载时，才能进一步偏向目标上下文问题；不能直接宣布输入法损坏。
6. 中文先执行 [系统 TSF 原型说明](../tools/BackgroundImeProbe/README.md)，原生路线在本机不成立时再运行 `--independent`。两者都是隔离研究，不自动接入主程序。
7. 检查实验图标的浅深主题、正常/选中状态及中英文；正常进出持续模式不重复弹结束通知，真实失败不循环重试。

## 中文可行性与依赖

系统原型实际实现了自有 `ITextStoreACP`、上下文、组合事件、UI-less 候选、微软拼音枚举/选择和 TSF 按键调用；这不代表后台 TSF 已可用。Windows 文档将输入上下文与有焦点窗口关联：[IMM 输入上下文](https://learn.microsoft.com/en-us/windows/win32/intl/input-context)、[TSF 文档焦点](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfthreadmgr-setfocus)。研究进程只同步自身线程的按键表，不使用 AttachThreadInput：[SetKeyboardState 的线程范围](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setkeyboardstate)。完整原生候选、第三方 TIP、正常/异常退出和 B 恢复后的切换均待实测。

同时提供一个独立层预备原型，不表示原生路线已经判定失败。它用自编的有限词表验证非激活组合、候选和确认提交，不是生产输入法。后续引擎候选 librime 的主许可证为 [BSD 3-Clause](https://github.com/rime/librime/blob/master/LICENSE)，常见 luna-pinyin 词库的许可证为 [LGPL v3](https://github.com/rime/rime-luna-pinyin/blob/master/LICENSE)；引擎、词库和依赖须按固定版本分别审查。本轮没有引入这些第三方文件或依赖。

## 编译记录

App、Core/App 测试项目仅编译 Release；独立 C++ 原型使用 x64 Visual C++、Windows SDK、`/W4 /WX` 和静态 CRT 构建。测试源码增加硬件归属、截止不延长、Shift 恢复、组合尾部和 UIA POINT ABI 回归检查，但没有执行。构建结果不替代上面的人工矩阵。
