# MCP 控制插件开发说明

## 功能边界

当前 MCP 控制插件只为 **Codex** 和 **DeepSeek Harness** 提供自动安装。它控制桌面语音服务，不负责捕获 Harness 的回复。自动朗读仍依赖各自经过验证的回复连接器。

本地 MCP 服务器位于 `integrations/agent_voice_mcp.py`。它使用 stdio JSON-RPC，标准输出只写协议消息，诊断写标准错误。本地连接密钥从 Agent Voice 的 `data/settings.json` 读取，不写入 Codex 或 DeepSeek 配置。

## 已提供工具

| MCP 工具 | 作用 |
|---|---|
| `open_voice_player` | 打开插件朗读面板；可选传入正文，不启动播放 |
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

## 二次开发规则

- 新工具应使用小而明确的参数结构，并设置 `additionalProperties: false`。
- 不返回本地连接密钥、私有参考录音路径或账号信息。
- 修改配置优先调用本地 HTTP API，避免 MCP 进程和桌面程序同时覆盖设置文件。
- 破坏性操作必须由明确的工具名表达；不要把删除文件或重置配置藏在普通状态工具里。
- 新增工具后同时更新 `TOOLS`、`AgentVoice.call`、本说明和协议测试。
- 为其他 Harness 安装时，只新增该 Harness 的配置安装器；不要复制一份 MCP 业务逻辑。

## 插件内嵌朗读面板（兼容性试验）

1. 安装或更新语音控制插件后，重载插件或新建会话。
2. 选择原生“语音控制”技能，发送“打开朗读面板”，可同时指定正文。
3. 支持 MCP Apps 的客户端会读取 `ui://agent-voice/player.html`，显示正文、朗读、停止朗读及刷新状态。面板通过宿主的 `tools/call` 调用同一 MCP，不发送聊天消息、不直连 HTTP、不包含连接密钥。
4. “朗读”提交当前正文；“停止朗读”停止播放并清空等待队列。打开面板及更新正文不会自动播放。服务、声音方案和自动朗读等其他功能通过技能调用。
5. 若只有文字返回，或面板提示宿主不提供 MCP 调用能力，应继续使用技能及 MCP。安装成功、资源能读取、初始化声明支持 UI，均不能单独作为“按钮已显示并工作”的证据。

工具通过 `_meta.ui.resourceUri` 关联 HTML，通过 `openai/ui.entrypoints: [{type: "thread"}]` 声明支持的会话面板入口。资源 MIME 为 `text/html;profile=mcp-app`。实现同时提供 `resources/list` 与 `resources/read`；不支持 UI 的宿主仍能调用全部文字工具。

本机 `data/logs/mcp-ui-client-codex.json` 只记录客户端公开名称、版本、扩展能力名称及“尚未验证渲染”，不保存正文、密钥或初始化完整参数。未声明 UI 能力不等于绝对不支持；最终以面板实际显示和按钮执行结果为准。

**客户端自带“朗读”尚未接管。** 本次核查的官方接口未提供替换该按钮播放源或注入本机 TTS 提供方的方法；插件面板不冒充该功能。参考 [MCP 界面](https://developers.openai.com/plugins/build/chatgpt-ui) 和 [会话面板扩展](https://developers.openai.com/plugins/build/extensions)。

旧 Codex 悬浮窗默认关闭，仅保留为可选兼容功能，不作为插件面板的替代验证。已明确关闭的本机实例不会重新打开。
