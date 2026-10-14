# Harness 二次开发指南

## 正式支持边界

当前发行版只把 **Codex** 与 **DeepSeek Harness** 标为正式支持。两者已经在真实客户端上完成目录识别、连接器安装、随机挑战回复、语音生成和实际播放验证。Claude Code、pi、OpenCode 及其他 Harness 不再出现在用户开关、自动识别或成功状态中。

新增 Harness 时不能仅根据目录名判断，也不能把“文件写入成功”或“收到心跳”当成接入成功。新增适配器必须完成本文全部阶段后，才能加入正式支持列表。

## 模块位置

| 作用 | 文件 |
|---|---|
| 支持来源和配置结构 | `service/state.py`、`ui/Models.cs` |
| 安装目录识别与连接器安装 | `service/harness_setup.py` |
| 最终回复事件归一化 | `service/harness.py` |
| 本地接收接口与验证状态 | `service/server.py`、`service/verification.py` |
| Harness CLI 真实调用 | `tools/verify_harness.py` |
| 桌面接入流程 | `ui/ConnectionPages.cs`、`ui/SettingsPages.cs` |
| 连接器公共发送逻辑 | `integrations/connector.mjs`、`integrations/send_event.py` |
| MCP 控制服务 | `integrations/agent_voice_mcp.py` |

## 最终回复事件契约

连接器只在主 Agent 的最终回复完成后向本机发送：

```http
POST http://127.0.0.1:<port>/api/harness/<source>
Authorization: Bearer <data/settings.json 中的 token>
Content-Type: application/json
```

事件必须能稳定得到以下三个值：

```json
{"text":"只包含最终助手正文","session":"Harness 会话 ID","turn":"本轮唯一 ID"}
```

在 `service/harness.py::normalize` 中把原始事件转换为：

```json
{"text":"最终正文","source":"新增来源 ID","eventId":"来源:会话:轮次"}
```

必须排除思考过程、工具调用、代码执行输出、子 Agent、流式中间片段、取消和错误轮次。`eventId` 必须可重复计算，服务依靠它去重。

## 新增适配器步骤

1. 在 `service/state.py` 和 `ui/Models.cs` 增加来源 ID，默认关闭。
2. 在 `service/harness_setup.py` 增加强特征识别。至少使用可执行文件、稳定配置文件或正式插件目录中的两项；弱目录名不能单独通过。
3. 安装连接器前备份所有将修改的文件，并保证重复执行不会产生重复 Hook 或插件。
4. 在 `service/harness.py` 增加最终事件归一化。
5. 在 `tools/verify_harness.py` 增加该 Harness 的无交互真实回复命令。客户端没有可靠 CLI 时，需要实现明确的手动挑战步骤。
6. 在桌面 UI 中增加名称、故障提示和重载说明。
7. 增加目录识别、重复安装、配置保留、正文过滤、去重和验证失败回退测试。
8. 在真实客户端完成下面的验收后，才把它列为正式支持。

## 强制验收

一次完整验证必须依次证明：

1. 选择的是目标 Harness 的真实目录。
2. 连接器安装到了实际生效的配置范围。
3. Harness CLI 或客户端处于已登录、可回复状态。
4. 随机挑战由目标 Harness 真实返回。
5. 服务从该来源收到完整最终正文。
6. 对应语音完成生成并实际播放。

任一步失败都必须保持或恢复为禁用，显示失败阶段，并允许用户修复后重新选择同一目录。绿色状态和礼炮只能在第六步完成后出现。

## MCP 扩展

自动朗读连接器与 MCP 是两个独立模块：连接器把最终回复送入语音队列；MCP 让模型主动控制服务。新 Harness 支持 stdio MCP 时，可以直接启动：

```powershell
<Agent Voice Python> integrations\agent_voice_mcp.py --root <Agent Voice 根目录> --host <来源 ID>
```

如需让新的 `--host` 生效，需要同时扩展 `agent_voice_mcp.py` 的来源白名单和配置来源。MCP 安装格式、工具合同和测试方法见《MCP控制插件开发说明.md》。

## 发布规则

- 未完成真机验证的适配器只能放在独立实验分支或第三方适配包中。
- README 必须区分“正式支持”“社区适配”和“仅设计”。
- 适配器不得打包用户账号、Token、会话记录或绝对私有路径。
- Harness 升级后应重新执行随机挑战和播放验证；旧成功状态不能永久沿用。
