# MCP 控制接口

MCP 服务通过标准输入输出与宿主通信，再调用本机 Agent Voice 服务。它提供控制能力，不替代自动朗读连接器，也不保证宿主支持自定义面板。

## 工具

| 工具 | 作用 |
|---|---|
| `voice_service_status` | 读取状态 |
| `start_voice_service` | 启动服务 |
| `stop_voice_service` | 停止服务 |
| `read_text` | 提交指定正文 |
| `stop_speech` | 停止朗读并清理等待队列 |
| `list_voice_profiles` | 列出已保存声音方案 |
| `apply_voice_profile` | 应用方案 ID |
| `set_auto_reading` | 修改自动朗读开关 |

工具清单以 `integrations/agent_voice_mcp.py` 为准。提交朗读成功只表示进入队列，完成和实际播放需继续观察任务状态。服务端不提供任意文件删除或通用命令执行工具。

## 安装参数

服务器由主服务 Python 启动，使用隔离模式和明确根目录：

```text
<pythonService> -I -X utf8 <应用根目录>/integrations/agent_voice_mcp.py --root <应用根目录> --host <codex或deepseek>
```

路径由运行中的应用生成，不固定盘符。安装器应备份宿主配置、回读结果并在失败时回滚；迁移时检查 command、args、root 和 cwd，不覆盖无关服务器配置。

## 检查流程

1. 核对服务器文件、Python 和根目录。
2. 核对宿主中 `agent_voice` 的启用状态及工具权限。
3. 执行 initialize、tools/list 和只读状态调用。
4. 等待当前宿主进程读取工具的回执；区分本地探针与宿主客户端。

该检查不发送聊天消息、不调用模型生成、不启动 TTS 或播放。客户端重载之前可显示等待，不能显示完整接入成功。自动朗读需独立测试其连接器、事件来源及播放结果。

## 客户端与界面

Codex 使用技能选择与语言命令。DeepSeek 可通过原生扩展槽提供控制菜单；播放动画应根据真实播放阶段，后台只读状态轮询不写入聊天草稿或发送模型消息。隐藏页面应暂停轮询，卸载应释放计时器。

只把已保存声音方案的 ID 传入切换工具，不允许宿主模型自行指定任意本机文件路径。凭据只留在本机服务配置，不暴露到网页或公开日志。
