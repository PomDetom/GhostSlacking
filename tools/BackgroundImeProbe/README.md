# 中文输入研究原型

这是独立程序，不自动接入正式交互。先恢复全部窗口并退出 GhostSlacking。原型不隐藏 B、不改样式、不读文档或剪贴板，也不切换 A 的布局。

## 系统输入法优先

启动 `BackgroundImeProbe.exe`，默认使用系统 TSF。B 选择正常打开的 Notepad++ 空白文档或标准 Edit/RichEdit 文本框，A 使用浏览器焦点诊断页。光标停在 B 的文字编辑区域，保持 A 为前台，按 Ctrl+Alt+F9 绑定实际文本 HWND，再按 Ctrl+Alt+F10 开始。松开启动组合键后输入拼音。

原型提供本进程 ITextStoreACP 文本库、组合事件、UI-less 候选、ITfThreadMgrEx 和 ITfKeystrokeMgr；尝试在本研究进程选择已安装的微软拼音。候选显示在非激活面板，通过输入法数字/方向/空格/Enter 选择。Esc 或 Ctrl+Alt+F10 结束并取消未提交组合，Ctrl+Alt+F12 退出。

只向身份核对通过的 Edit/RichEdit/Scintilla 提交文本。Chromium 等缺少已验证插入接口时拒绝，不改用整字段覆盖或剪贴板替换。HRESULT 成功、按键被处理或出现候选，都不能代替 B 文本正确、A 全程未失焦、结束后 Shift 切换正常这三个条件。

## 原生不成立后研究独立层

运行 `BackgroundImeProbe.exe --independent`，使用相同快捷键。自编小词表提供拼音前缀、最多九个候选、数字/空格/Enter 确认、退格修改、Esc 取消并结束。单独 Shift 切换会话中英文；Shift 组合可输入大写。可试 nihao、zhongwen、ceshi、shurufa、fuzhi、zhantie。

小词表只证明组合、候选和提交链，不是完整输入法：没有分词、上下文排序、学习、模糊音或云词库。没有打包第三方引擎或词库；生产化可研究 librime，但引擎、词库和第三方库许可须分别审查。

## 诊断

Ctrl+Alt+F8 导出 EXE 同目录 probe-metadata.txt，仅包含引擎模式、HRESULT、焦点/受保护布局变化、组合事件、处理/未处理事件、配置状态和提交次数，不保存输入内容。面板文本仅留在内存，结束时清空。

分别对比：结束后立即激活 B 切换输入法；退出原型后再激活 B 切换。再测按住 Shift/Ctrl 结束、目标关闭和 A 短暂失焦。超时可能已经部分投递，停止后检查 B，不盲目重发。

编译：`powershell -NoProfile -File tools/BackgroundImeProbe/build.ps1`。系统 TSF、第三方输入法及候选体验的验收状态统一见[实施状态](../../docs/IMPLEMENTATION_STATUS.md#自动化与隔离证据)，源码和可编译不代表实际可用。
