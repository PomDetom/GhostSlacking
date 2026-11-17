# Reveal 与后台输入

## 拾取、几何与 Reveal

- Picker 使用 `WindowFromPoint`/`GetAncestor` 选择顶层窗口，过滤桌面、任务栏、自身及不支持目标；Esc 按下、重复及抬起均拦截且只取消一次。
- 领域几何统一使用物理屏幕像素，进程使用 Per Monitor V2 DPI manifest；UI 的 DIP 只在显示层转换。普通窗口、最大化和最小化按完整 placement 区分。
- Ghost 使用 `SW_HIDE`，临时控制工具窗口、置顶和 DWM 装饰；保留标题栏与缩放边框样式，避免浏览器因非客户区变化重排。Restore 还原原值。
- Reveal 支持圆形、矩形、圆角矩形。首次显示在隐藏时先应用 region，显示后再应用/校验；连续移动使用增量更新。光标与几何不变时跳过重复 native 更新。
- `SetWindowRgn` 成功后由系统拥有 HRGN；失败时由调用方删除。曲线 region 的右/下开区间须补偿，原始 region 恢复失败须明确返回错误。
- 为兼容重绘竞态，单次 region 更新最多重施/校验三次；该轮仍失败先安全隐藏，下个 tick 重试，连续三轮失败才通知。这不证明所有自绘窗口均兼容。

### Composition 与调度

`IRevealVisualHost` 隔离可选 `RevealEdgeOverlay`；`IVisibilityBackend` 保留可独立运行的硬边路径。默认 Reveal 为 144px 圆角矩形、16px 羽化、轻度模糊。Composition/Win2D 不抓屏：清晰核心保留，目标内容 region 外扩至羽化宽度的 70%，单一 backdrop 模糊配合渐变遮罩绘制外环。

Overlay 非激活、鼠标穿透、无任务栏项，并由当前目标拥有，防止点击目标后被其遮挡。宿主覆盖目标 bounds，目标切换/销毁后重建；遮罩按视觉参数缓存，与光标位置无关。移动只更新 visual offset 和外环 region，核心点击/滚轮仍到目标。设备、初始化或渲染失败时隐藏 overlay，将内容缩回硬边 region，不依赖视觉层执行恢复。

非 Reveal 轮询约 60 Hz，Reveal 按鼠标所在显示器当前模式及 Auto/60/90/120 上限调度，最高 120 Hz。显示查询按显示器缓存两秒，`WM_DISPLAYCHANGE` 使缓存失效，失败回退 60 Hz。Debug 每 120 个有效移动帧汇总有效 FPS、平均/P95/最大 tick 耗时和超预算帧数，不逐帧写成功日志。

## 实验性后台输入

当前实现是默认关闭的兼容性原型，使用方法见[实验说明](../product/EXPERIMENTAL_BACKGROUND_INTERACTION.md)。能力和实际验收必须分开：接口调用成功不证明目标完成操作，更不证明前台网页全程未失焦。

### 会话与输入归属

`BackgroundInteractionSession` 负责身份、受保护前台和输入归属；Platform 使用独立 Hook 消息线程、代次输入队列及 MTA UIA 工作线程。主业务仍在 Avalonia UI 线程；不注入目标进程，不使用 `AttachThreadInput` 借用前台输入上下文。

仅为已登记目标临时设置 `WS_EX_NOACTIVATE`，结束时恢复快照中该位的原值，保留其他位。持续模式只允许 Toggle Peek：先等待有效独立前台和已按输入释放，再准备 Hook/目标，最后显示 Reveal。准备期间的新输入消费但不排队重放，准备前属于 A 的按键抬起仍交给 A。

正常移出/返回可结束/重建持续会话；真实失焦、投递失败、目标失效或未管理弹窗清除显示意图，须重新手动 Peek。设置、选窗、恢复和退出取消等待/准备/交互，恢复开始后不再追加隐藏。

只为实际投递的原生按下发送抬起；字符和编辑命令不伪造抬起。硬件归属与投递账本独立，释放尾部最多两秒，不因后续输入无限延长。停止清除接收者、修饰键、选区上下文及旧代次回执；未支持操作与真实失败分别处理，结果不确定时不重放。

### 当前能力路径

| 路径 | 当前实现及边界 |
| --- | --- |
| 英文键盘 | 已加载 US 布局只读翻译，缺少时固定映射；不加载/切换布局。普通字符只发 `WM_CHAR`，按翻译实际长度读取。Shift 单击、Ctrl/Win+Space 和 IME 组合请求直接返回 `ImeComposition`，不借用 A 的文本上下文。 |
| Scintilla | 定位、单/双击、Shift/跨行拖选及编辑快捷键走控件标量命令；剪贴板由目标按用户快捷键处理。编辑区外、右键和未适配组合明确拒绝。 |
| Edit/RichEdit | Edit 使用原生定位/选区/复制粘贴，RichEdit 按当前 UIA/原生命令能力处理；不由根窗口消息推断所有编辑器可用。 |
| UIA | 每次按坐标命中并验证 B 祖先关系；正文选区和编辑框分离。Invoke/Toggle/ExpandCollapse/SelectionItem 仅在未拖动的完整单击抬起时调用一次；合并移动保留最大偏移。 |
| 空白框粘贴 | 显式 Ctrl+V 才读 Unicode 纯文本，剪贴板分配块上限 1 MiB；要求当前 Edit、可写 ValuePattern、值精确为空且无未确认富内容/子项。读后复查身份和空白，SetValue 后核对；已有草稿、空格、换行或旧接收者拒绝。不是光标插入，不改剪贴板，不写内容日志。 |

已知 Chromium/Electron 语义窗口不回退根窗口按钮；其他通用消息接收者仍可能主动激活，发生时收起。UIA 只读发现含排队预算 1.5 秒，修改后 500ms 结果不确定即停止。逻辑超时不能取消已发 COM 调用，不能证明操作未发生，也不能据此换路线重试。

原生 Edit/MSAA/隔离 CDP 探测与中文 TSF/独立小词表工具均独立运行，不自动接入真实目标。其构建、边界和命令由各工具 README 维护；当前研究边界见[质量边界](../quality/KNOWN_LIMITATIONS.md)。
