# Agent Voice

让 Harness 的回复通过本地 TTS 发声。

Agent Voice 是 Windows 本地语音服务与桌面控制台，连接 Codex、DeepSeek Harness，并可与 AIRI 联动。文字由所选 TTS 引擎直接生成语音，模型、参考录音、播放设置和界面外观由使用者自行配置。

## 功能

- 控制台管理服务、手动朗读、停止播放、常用句和声音方案。
- 支持 CosyVoice，以及 GPT-SoVITS 的 v5 Turbo、ProPlus 适配。
- 自动朗读按完整句子排队，支持停止、取消及失败恢复。
- MCP 提供服务启停、朗读控制、状态查询和声音方案切换。
- AIRI 可接收语音并同步嘴型；不开启联动也可使用本机播放。
- 首次配置按需准备引擎与环境，各步骤可跳过，支持联网准备和离线资源安装。
- 使用须知、开源许可和适用范围集中在“设置 → 须知与许可”，按需阅读，不拦截配置与朗读。
- 软件更新支持联网检查、本地更新包、逐文件校验与失败回退。
- 支持自定义配色、背景、图标与等待动画。

## 安装

**直接下载程序：** [Windows x64 维护预览版](https://github.com/jli517166-gif/agent-voice/releases/tag/v0.2.0-preview.1)。无需自行编译；先阅读 [下载与首次使用](下载与首次使用.md)，准备配套引擎和环境。若仓库访问受限，附件仅对有访问权限的账户可见。

需要 Windows 10／11 64 位、.NET Framework 4.8，以及适配的 NVIDIA 显卡和驱动。具体显存、磁盘和依赖要求取决于所选引擎。

**源码仓库及软件本体不包含 CosyVoice、GPT-SoVITS、Python 环境、预训练权重或参考录音。** 引擎和环境需要另行准备。普通使用者下载 Release 中的软件 ZIP；以下命令面向需要从源码构建的开发者。

开发主线为 `main`：

```powershell
git clone https://github.com/jli517166-gif/agent-voice.git
cd agent-voice
git switch main
powershell -File .\build.ps1
```

构建结果位于 `dist`。运行 `dist/agent语音.exe`，按配置页面准备资源。手动安装请先阅读 [安装手册](安装手册.md)：CosyVoice 默认位于 `tts-engines/cosyvoice/`，GPT-SoVITS 默认位于 `tts-engines/gpt-sovits/`，都以当前 EXE 所在目录为根。

## 使用

1. 准备所选引擎的源码、环境及完整基础模型。
2. 在控制台选择引擎和模型，设置有权使用的参考录音与逐字台词。
3. 启动服务，先生成短句，再检查长文本和停止操作。
4. 在连接页面配置 Harness；需要聊天端控制时安装 MCP，并重载客户端。
5. 需要 AIRI 时，选择已安装并登录的 AIRI 目录，单独启用和验证联动。

## 文档

| 主题 | 文档 |
|---|---|
| 安装与目录 | [安装手册](安装手册.md)、[资源安装指南](模块安装与配置指南.md) |
| 环境与运行 | [环境补齐说明](环境补齐说明.md)、[TTS 使用手册](TTS使用手册.md) |
| 连接 | [接入说明](接入说明.md)、[MCP 开发说明](MCP控制插件开发说明.md) |
| 开发 | [贡献指南](CONTRIBUTING.md)、[架构与开发](docs/架构与开发.md) |
| 训练 | [基础训练指南](音色训练从零开始.md)、[高级训练指南](高级CosyVoice训练手册.md)、[GPT-SoVITS 指南](GPT-SoVITS训练指南.md) |
| 故障与隐私 | [故障排查](稳定性与故障排查.md)、[本地数据与隐私](docs/本地数据与隐私.md) |

本项目代码许可见 [LICENSE](LICENSE)，第三方组件与素材见 [第三方声明](THIRD_PARTY_NOTICES.md)。录音、训练材料、基础模型与依赖分别遵循其适用条款。源码仓库不收集使用者的录音、密钥或聊天记录；提交问题前请先脱敏。
