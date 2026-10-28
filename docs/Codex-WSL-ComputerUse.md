# Codex WSL Computer Use 兼容性记录

核查日期：2026-10-27。

本机 Windows MSIX 包版本只读查询结果为 `OpenAI.Codex 26.1021.3705.0`。这仅用于记录当前复现环境，不能与插件或运行时版本混为一谈。

当前会话中 `mcp__node_repl__js` 在 JavaScript 执行前返回：

```text
Mcp error: -32602: js: codex/sandbox-state-meta:
sandboxCwd is not a local file URI: file:///mnt/d/myproject
```

按技能要求重新尝试并重置 JavaScript 内核后，错误仍相同。没有修改 Codex 配置或二进制，也没有关闭安全校验。

## 查到的对应报告

- [openai/codex #52468](https://github.com/openai/codex/issues/52468)：2026-10-09 的报告，同时涉及 Computer Use 和 Browser Use；node_repl 入口在执行 JavaScript 前拒绝 WSL 文件 URI。
- [openai/codex #29413](https://github.com/openai/codex/issues/29413)：Windows 原生 node_repl 接收到 `/mnt/c/...` 形式的 sandboxCwd。评论中存在 Windows 原生代理成功、WSL 代理失败的对照报告。
- [openai/codex #33560](https://github.com/openai/codex/issues/33560)：即使代码物理位置在 Windows 盘，只要代理工作环境为 WSL，仍可能传入 Linux 路径并失败。

本次用 GitHub API 核对，三个 issue 状态均为 open。社区报告与本机表现高度一致，但不能据此声称已经确认内部实现根因，也没有确认可解决本机问题的官方发布版本。

官方 [WSL 文档](https://developers.openai.com/codex/windows/wsl) 说明，WSL 模式下代理运行在 Linux 环境；[Computer Use 文档](https://developers.openai.com/codex/use-cases/use-your-computer-with-codex) 说明该功能使用桌面端环境。这些文档未给出上述错误的已发布修复版本。

## 对本项目的影响

- Windows 原生 PowerShell、MSVC、.NET、WinUI 程序构建与运行仍然成功，不能把该错误概括为 WSL 无法调用 Windows 程序。
- 本机受阻的是 Codex 正式 node_repl/Computer Use 入口的元数据校验。
- 可以考虑在 Windows 原生代理模式下打开 `E:\haoyue\myproject\home\ClariBar`，作为后续工具兼容性的对照验证；此会话尚未执行该对照，不保证它一定解决问题。
- 本轮用程序自身测试入口检查六页导航、主题、实时数据与退出；用 WinUI RenderTargetBitmap 渲染自己的 XAML 树供布局核对。它不是系统桌面截图，也不等同于已完成人工鼠标交互覆盖。
