# MCP 控制插件开发说明

## 功能边界

当前 MCP 控制插件只为 **Codex** 和 **DeepSeek Harness** 提供自动安装。它控制桌面语音服务，不负责捕获 Harness 的回复。自动朗读仍依赖各自经过验证的回复连接器。

本地 MCP 服务器位于 `integrations/agent_voice_mcp.py`。它使用 stdio JSON-RPC，标准输出只写协议消息，诊断写标准错误。本地连接密钥从 Agent Voice 的 `data/settings.json` 读取，不写入 Codex 或 DeepSeek 配置。

## DeepSeek 路径迁移和失败恢复

程序目录由当前 EXE 决定，没有固定安装盘符。历史验收报告里的 `E:\App\Agent 测试` 等路径只记录当时环境，不是当前安装要求。

“安装到 DeepSeek”先检查主服务 Python、MCP 脚本，再备份 desktop/headless 的生效配置、受本程序管理的连接器文件与本地安装记录。安装后回读 `cordis.patch.yml`，核对 `command`、脚本参数、`--root` 和 `cwd` 与当前安装相符且文件存在。已安装且版本匹配的 MCP 客户端直接复用；失败会恢复生效配置和连接器，明确报告备份位置。包管理器下载的依赖缓存保留，不代表插件已激活。

主程序启动时检查已经安装的 DeepSeek MCP：旧目录已不存在时自动修复；指向另一套仍存在的程序时只提示冲突，由用户点击“安装到 DeepSeek”明确切换，不自动接管。配置语法损坏不自动覆盖。也可在“连接”或“Harness 设置”点击“检查 MCP”。启动错误设置为 `failOnStartupError: true`，不再静默跳过；修复后需重载插件或重启 Harness。

状态必须分别解释：

- **配置正确，尚未确认客户端读取工具**：仅通过文件与路径校验，不能认定工具已加载。
- **客户端已读取 8 个工具**：实际 `dsh-mcp-client` 完成 `initialize` 和 `tools/list`。回执限定当前程序目录及仍存活的进程；普通协议探针不算 Harness 回执。它仍不证明模型注册或实际播放成功。
- **功能验证**：在重载后的 Harness 会话调用 `mcp__agent_voice__voice_service_status`、`read_text`，确认任务完成并听到声音。聊天栏“语音”按钮有独立控制通道，按钮可用不能代替 MCP 工具验证。

回执位于 `data/mcp-clients/`，只记录客户端名、目录、进程身份和协议读取阶段，不记录连接密钥或朗读内容。正常退出删除本进程回执，异常退出的旧记录因进程身份不符而失效。自动检查不会自动朗读、改变音色或替用户重启 Harness。MCP 仅安装到 desktop；headless 保留原自动朗读连接器，需要工具的开发者应按适配器契约另行配置。

## 已提供工具

| MCP 工具 | 作用 |
|---|---|
| `read_text` | 用当前声音朗读指定正文，必要时启动服务 |
| `voice_service_status` | 查看服务、队列、当前声音和当前 Harness 自动朗读开关 |
| `start_voice_service` | 启动后台语音服务 |
| `stop_voice_service` | 关闭后台语音服务 |
| `stop_speech` | 停止当前朗读并清空等待队列 |
| `list_voice_profiles` | 列出声音方案 ID、名称、模式和当前状态 |
| `apply_voice_profile` | 按 ID 切换声音方案 |
| `set_auto_reading` | 开关当前 Harness 的进度说明和最终回复自动朗读 |

切换声音时只接受程序已经保存的声音方案 ID，避免模型自行提交任意本机文件路径。

## Codex 安装

桌面程序的“安装到 Codex”会调用本机 Codex CLI：

```powershell
codex mcp add agent_voice -- <python.exe> <agent_voice_mcp.py> --root <程序目录> --host codex
```

安装器还会注册程序内 `.agents/plugins` 的本地市场并安装 `agent-voice-control` 插件。重载插件或新建会话后，可在聊天栏技能选择器中选择 **语音控制**，或输入 `$agent-voice-control`。`composerIcon` 是原生入口图标元数据，并非自定义常驻按钮 API；当前不承诺安装后自动出现和 DeepSeek 相同的常驻控制菜单。该技能只负责把自然语言控制意图映射到 `agent_voice` MCP；已有会话不保证重新加载入口或工具。

安装后运行 `codex mcp get agent_voice` 或 `codex mcp list` 检查底层配置。运行 `codex plugin list` 可以检查聊天栏插件。

安装器只对 `agent_voice` 这一台本地 MCP 写入 `default_tools_approval_mode = "approve"`，使用户明确要求的启动、停止和切换操作可以在非交互 Harness 会话中执行。它不会修改其他 MCP 的批准策略。服务端工具只接受已保存的声音方案 ID，且不提供删除文件或执行任意命令的接口。

## DeepSeek Harness 安装

桌面程序的“安装到 DeepSeek”会：

1. 使用接入验证时记录的 DeepSeek 安装目录。
2. 读取 `dsh --version`。
3. 给 desktop profile 安装同版本的 `@deepseek-ai/dsh-mcp-client`。
4. 备份 `~/.dsh/profiles/desktop/cordis.patch.yml`。
5. 写入名为 `agent_voice` 的 stdio MCP 项。

写入后的核心配置为：

```yaml
- insert:
  - id: agent-voice-mcp
    name: '@deepseek-ai/dsh-mcp-client'
    config:
      serverName: agent_voice
      transport: stdio
      command: '<python.exe>'
      args: ['<agent_voice_mcp.py>', '--root', '<程序目录>', '--host', 'deepseek']
      cwd: '<程序目录>'
      failOnStartupError: false
```

重启 DeepSeek Harness 后才能加载。安装成功后，浏览器半插件会在 `conversation.input.left` 槽位显示 **语音** 按钮；菜单通过 Harness 原生 `remote.commands.execute` 执行已注册的 `/agent-voice ui <JSON>` 控制命令，再调用本机 MCP。结果直接显示在菜单中，不修改聊天草稿、不发送给模型。声音方案可在菜单内下拉选择并直接应用。安装器同时刷新实际 profile 内的 client.js 并校验内容。入口仅显示图标和文字，无边框、透明背景；展开菜单继承 Harness 的明暗主题、文字和配色，并使用实色底层避免透字。安装失败不会删除备份，也不代表自动朗读连接器失效。

播放动画另走只读状态通道：后端通过 `connection.fetch.register` 注册 `POST /api/agent-voice-playback`，前端用 `connection.rpc.call('/api', 'agent-voice-playback', {}, signal)` 请求。使用 Harness 原生连接认证，不在浏览器保存本机密钥，不通过聊天命令轮询，也不为每次轮询启动 Python。后端只返回运行状态、播放阶段和队列数量；正文、声音路径、本机密钥不会转发给按钮。

`/health.playback.state` 区分 `idle`、`queued`、`generating` 和 `playing`。本机音频设备实际启动后才进入播放动画，单纯 `busy: true` 或仅生成文件均不会让波形运动。停止会立即使原任务的播放状态失效，避免旧请求把动画重新点亮。旧服务缺少此字段时显示“状态未确认”。播放时每 650 毫秒检查，生成或排队时每 1100 毫秒检查，空闲或出错时每 3 秒检查；隐藏页面暂停，卸载释放定时器，系统减少动态效果设置会关闭运动。此动画表达播放阶段，不分析声音振幅。

DeepSeek 回复连接器同时向原生人工命令注册表注册 `/agent-voice`。它供支持原生人工命令分发的版本使用。部分桌面版会把手动提交的斜杠文本交给模型，因此日常控制请使用“语音”按钮；按钮直接调用命令服务，不向聊天输入框提交文本：

```text
/agent-voice status
/agent-voice start
/agent-voice stop
/agent-voice quiet
/agent-voice profiles
/agent-voice profile <方案 ID>
/agent-voice auto on
/agent-voice auto off
```

`/agent-voice help` 会在聊天区显示简短控制面板。命令执行仍通过 `agent_voice_mcp.py`，因此和模型主动调用 MCP 使用同一套参数校验及错误返回。

## 手动协议测试

向服务器标准输入逐行发送：

```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"manual","version":"1"}}}
{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"voice_service_status","arguments":{}}}
```

每一行应得到对应 JSON-RPC 响应，且标准输出中不能混入日志。

新声音方案保存 TTS 参数、固定参考录音和模型校验信息。MCP 与桌面端均应用同一套引擎、模型、参考录音、逐字台词、语速和档位：GPT-SoVITS 使用 `gptVariant`（`proplus` / `v5turbo`）；F5 已淘汰。不再提供第二次变声或混合输出模式。工具名与调用方式保持不变。

## 二次开发规则

- 新工具应使用小而明确的参数结构，并设置 `additionalProperties: false`。
- 不返回本地连接密钥、私有参考录音路径或账号信息。
- 修改配置优先调用本地 HTTP API，避免 MCP 进程和桌面程序同时覆盖设置文件。
- 破坏性操作必须由明确的工具名表达；不要把删除文件或重置配置藏在普通状态工具里。
- 新增工具后同时更新 `TOOLS`、`AgentVoice.call`、本说明和协议测试。
- 为其他 Harness 安装时，只新增该 Harness 的配置安装器；不要复制一份 MCP 业务逻辑。

## Codex 采用语言命令

Codex 保留原生技能与 MCP，不提供自定义按钮、悬浮窗或面板。用户可说“停止朗读”“开启语音服务”“列出声音方案”“切换到某个声音”“关闭自动朗读”或“朗读这段文字”。`read_text` 只提交用户指定正文，不把网址、工具输出或其他回复替代为正文。

1.4.0 已移除 `open_voice_player`、MCP Apps HTML 资源、面板元数据、窗口跟随及其设置／托盘入口。旧文件在更新时移至本机 `data/retired-codex-ui`，不再加载。

本次检查官方插件扩展文档及本机 CLI 导出的公开 App Server 协议（包含实验字段），未发现内置朗读播放源或本机 TTS 提供方注册入口。可见实时语音启动、音色列表及追加可朗读文本接口，但其音色为固定枚举，不等同于替换客户端内置朗读。没有修改 Codex 客户端或声称已接管该按钮。

参考：[插件扩展](https://developers.openai.com/plugins/build/extensions)、[语音功能](https://learn.chatgpt.com/docs/features/voice)。

## TTS 集成同步（2026-10-16）

正式选择为 CosyVoice、GPT-SoVITS v5 Turbo 和 ProPlus。声音方案同时保存 `engine`、`gptVariant`、`gptModel` 与参考条件。MCP 状态和方案列表返回引擎、版本；服务仍只有一个，原有停止、队列与自动朗读操作共用。配置版本为 4，完整使用范围见《[TTS 使用手册](TTS使用手册.md)》。

稳定性、自检、恢复和声音方案固定参考规则见《[稳定性与故障排查](稳定性与故障排查.md)》；扩展引擎见《[TTS 适配开发说明](TTS适配开发说明.md)》。
