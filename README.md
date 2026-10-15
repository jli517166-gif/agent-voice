# Agent 语音

面向 Windows 的本地语音服务与桌面控制程序。它以本地 TTS 为主要输出方式，可选 RVC 二次变声，并能接收 Codex、DeepSeek Harness 与 AIRI 的朗读请求。

> [!IMPORTANT]
> 本仓库**不包含 CosyVoice 源码、CosyVoice 预训练模型、Python 运行环境、任何音色包、参考录音或 RVC 模型**。这些文件体积大，并可能受各自许可证或个人声音资产限制。首次使用前请按照《[安装手册](安装手册.md)》自行下载 CosyVoice，并放到规定目录。

## 主要功能

- 本地 TTS 直接输出，默认使用 CosyVoice 3。
- 可选 RVC `.pth` 与 `.index`，支持音调、声线粗细和相关高级参数。
- 导入、导出轻量、参考完整、高级训练和高级完整四种 `.cosyvoice.ttsmodel`。
- 本地 TTS 引擎统一放在 `tts-engines`，按引擎 ID、模型类型、源码、Python 依赖和模型 SHA-256 指纹自动匹配。
- 保存、应用和删除本地 TTS 或 RVC＋本地 TTS 声音配置。
- 长回复按完整句号、问号、感叹号或自然换行分段，生成首句后立即播放，后续句子保持原顺序排队。
- 正式支持经过真机端到端验证的 Codex 与 DeepSeek Harness；使用同一个入口选择目录，程序自动识别类型、安装连接器、检查 CLI、启动真实验证会话并确认播放结果。任一验证阶段失败时会停止并说明原因，修复后选择同一目录即可重试。
- 为 Codex 与 DeepSeek Harness 提供聊天栏快捷控制：Codex 安装“语音控制”技能入口（聊天栏技能选择器或 `$agent-voice-control`）；DeepSeek 在聊天输入栏安装“语音”按钮并保留 `/agent-voice` 命令。两者通过同一套本地 MCP 工具启停服务、停止朗读、查看状态、开关自动朗读以及列出和切换声音方案。
- DeepSeek Harness 自动验证使用安装包自带的 `dsh headless`，并从桌面配置同步当前模型提供方和已配置的本机受信任 CA；无需手动复制验证短句。
- AIRI 桌宠播放和嘴型联动；默认关闭，用户手动启用并选择原安装目录后，程序自动准备桥接、打开已登录实例并验证实际播放。AIRI 未启用时仍可使用本机扬声器。

其他 Harness 不作为未经验证的内置功能发布。开发者可以按《[Harness 二次开发指南](Harness二次开发指南.md)》新增回复适配器，并按《[MCP 控制插件开发说明](MCP控制插件开发说明.md)》复用或扩展 MCP 控制服务。
- 自定义程序图标、桌面快捷方式、托盘图标、页面配色、整窗背景和等待动画；等待动画支持 PNG、JPG、BMP 与 GIF。
- 自动清理历史音频、日志与缓存。

## 系统要求

- Windows 10/11 64 位。
- NVIDIA 显卡和可用的 CUDA 驱动。
- .NET Framework 4.x（用于编译和运行 WinForms 界面）。
- Git、Git LFS、Miniconda 或兼容的 Python 环境管理工具。
- 足够磁盘空间：CosyVoice 源码、环境和模型通常需要十几 GB。

## 快速开始

```powershell
git clone https://github.com/jli517166-gif/agent-voice.git
cd agent-voice
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

构建结果位于 `dist`。继续按照《[安装手册](安装手册.md)》安装 CosyVoice 源码、Python 环境和预训练模型，然后运行：

```powershell
.\dist\agent语音.exe
```

首次启动不会自带声音资产。请先按照安装手册把 CosyVoice 放进 `tts-engines`，再导入轻量或完整音色包；也可以在“CosyVoice 训练相关参考”中选择参考音频并填写逐字一致的台词。

## 默认目录

```text
dist/
├─ agent语音.exe
├─ service/                         # 本地语音服务
├─ integrations/                    # Harness 连接器及注入 AIRI 的桥接源码
├─ tools/                           # 配置、音色包与接入工具
├─ tts-engines/                     # 所有本地 TTS 引擎的专用根目录
│  └─ cosyvoice/
│     ├─ source/                    # 用户自行下载的 CosyVoice 源码
│     ├─ runtime/tools/python.exe   # 用户自行创建的 CosyVoice Python 环境
│     └─ models/
│        └─ Fun-CosyVoice3-0.5B/    # 用户自行下载的预训练模型
├─ engines/
│  └─ rvc/                          # 可选 RVC 引擎，不随仓库提供
├─ voice-model-packages/             # 用户自己的 .cosyvoice.ttsmodel
├─ models/                           # 用户自己的 RVC .pth/.index
├─ voices/                           # 用户自己的参考资源
└─ data/                             # 首次运行后生成的设置、缓存与输出
```

默认情况下，主服务、CosyVoice 和 RVC 共用 `tts-engines/cosyvoice/runtime/tools/python.exe`，避免重复安装大型 Python、PyTorch 与 CUDA 依赖。RVC 引擎代码放在 `engines/rvc`；`.pth` 与 `.index` 可以放在 `models`、`voices` 或用户选择的外部目录。

AIRI 不复制进程序目录。连接后，桥接直接安装到用户选择的单个 AIRI 目录，并在其 `resources` 下保留 `app.asar.agent-voice-backup` 单文件备份；`agent-voice-integration.json` 记录桥接版本和语音服务位置。AIRI 更新可能覆盖桥接，失效后重新执行连接验证即可自动检查和修复。

## 使用要点

- 首页“应用当前设置的声音配置”会更新当前运行设置，不会训练模型。
- “声音方案”保存参数和资产路径快照，不会重复复制几 GB 的模型。
- “轻量包”只保存参考音频、逐字台词、声音参数以及引擎源码、运行依赖和基础模型指纹；目标电脑必须已经安装完全一致的环境与模型。
- “参考完整包”会额外携带当前基础模型。“高级训练包”只携带训练后变化的权重，需要目标电脑已有完全相同的基础模型；“高级完整包”携带完整训练模型。四种包都不包含 CosyVoice 源码、Python 或 CUDA。
- 高配置用户可按《[高级 CosyVoice 训练手册](高级CosyVoice训练手册.md)》训练专属权重、验收结果并选择合适的包类型。
- “完整便携包”额外保存完整基础模型，适合离线换机；相同模型指纹下，两种包输出一致。
- “音色包”不会写入 RVC、外观、Harness、AIRI、连接密钥或 Python 运行环境。
- 高级声音与说话风格默认关闭。关闭后仍保留本地 TTS 模型、参考条件、语速，以及 RVC 音调与声线粗细。
- `local` 表示当前 Windows 电脑的默认扬声器或耳机；选择 AIRI 输出才会把播放和嘴型交给桌宠。

## 开发与检查

界面使用系统自带 C# 编译器构建，服务端使用 Python。无需 CosyVoice 模型即可运行大部分单元测试：

```powershell
python -m unittest discover -s tests -p "test_*.py"
node .\tests\test_connector.mjs
node .\tests\test_mouth.mjs
```

真实语音生成测试需要完成《[安装手册](安装手册.md)》中的 CosyVoice 安装，并准备合法使用的音色模型或参考录音。

## 隐私与发布范围

程序默认把设置、日志、缓存和生成音频写入本地 `data` 目录。本仓库通过 `.gitignore` 排除运行环境、模型权重、音色包、参考录音、生成数据、验收截图和本机专用脚本；提交前仍应检查即将上传的文件列表。

CosyVoice 与预训练模型由其原作者发布，本项目不重新分发这些文件。使用、训练和发布声音模型前，请确认你拥有相应录音、模型与角色声音的使用权。
