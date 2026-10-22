# GPT-SoVITS 训练与接入指南

> 当前版本：本体不提供默认音色或音色包导入导出。文中涉及音色包迁移的步骤，须先在“模块 → 导入模块”安装独立音色包模块，再在模块窗口操作；这些步骤不属于首次配置。训练结果可直接在主程序选择，训练和朗读不依赖该模块。

适用本程序的 ProPlus（上游版本名 `v2ProPlus`）与 v5 Turbo（`v5turbo`）。先在“基础训练”选择对应引擎。本程序的素材检查和 Kaldi 清单生成仅用于 CosyVoice；不把参考克隆称为已训练权重。

## 先选目标

| 目标 | 操作与当前支持范围 |
| --- | --- |
| 快速获得参考音色 | 两版均可直接用 3～10 秒参考录音和准确台词，无需训练 |
| ProPlus 专属权重 | 在官方 WebUI 选择 v2ProPlus，完成数据处理及 SoVITS、GPT 微调；先在上游推理页验收完整权重 |
| v5 Turbo 微调 | 按所安装上游版本的实际训练说明操作；可能产生 LoRA。本程序当前没有自动合并／加载 LoRA 的入口，必须先在官方工具完成兼容的完整权重整理，否则留在官方环境推理 |

两版不是可以随意互换的声学模型，不应把 ProPlus 权重改名后声称变为 v5。CosyVoice 的高级训练增量包也不能装入 GPT。

## 1. 准备独立环境和素材

按 [GPT-SoVITS 官方说明](https://github.com/RVC-Boss/GPT-SoVITS/blob/main/docs/cn/README.md)准备训练仓库、Python、PyTorch、CUDA、预训练模型和训练依赖。推理环境能生成语音不代表训练依赖齐全。固定源码提交、依赖版本、基础权重和显卡信息，不覆盖程序现有模型。

在该独立环境、仓库目录运行 `python webui.py`（Windows 官方整合包可使用其 `go-webui.bat`）。不要使用主服务的 `runtime/core/python.exe` 训练；它没有 Torch。

录音需单人、清晰、无伴奏／强混响／重叠人声。使用官方工具切成自然短句并校对 ASR 标注，保留独立试听集。标注示例：

```text
D:/tts_data/wavs/001.wav|speaker1|ZH|工作好久啦，起来动一动、喝口水吧。
D:/tts_data/wavs/002.wav|speaker1|EN|Take a break and drink some water.
```

文件通常保存为 `.list`，使用 UTF-8；路径必须存在，台词逐字一致。不要把路径和台词简单按逗号拆分，不要拿 CosyVoice 的 CSV／Kaldi 文件直接喂给 GPT。

## 2. 在上游训练和验收

选择对应版本、实验名、素材和标注文件，按上游界面顺序执行文本处理、SSL 特征与语义 token 提取，再进行对应的 SoVITS／GPT 训练。具体页签名与配置随上游版本改变，以本地 `webui.py` 和对应训练配置为准。先用小批次验证能处理全部标注，再扩大数据与轮数；显存不足时先降 batch size，不覆盖已认可版本。

记录训练输出的 GPT `.ckpt`、SoVITS `.pth` 或 LoRA，以及版本和基础权重。在官方推理页加载对应训练结果，对中文、中英混合、数字、长段落、多个未见句子测试错读、吞字、静音、相似度和速度。不能只凭训练完成提示判定声音通过。

## 3. 整理本程序可用的完整模型目录

本程序当前使用目录内固定的推理文件布局；界面不提供任意 GPT／SoVITS 权重文件二选一。将独立结果整理在音色包库的 `训练输出` 下，保留原始 checkpoint 和训练记录另存。

完整目录需要：`s1v3.ckpt`、`chinese-hubert-base`、`chinese-roberta-wwm-ext-large`，以及对应版本：

- ProPlus：`v2Pro/s2Gv2ProPlus.pth` 和 `sv/pretrained_eres2netv2w24s4ep4.ckpt`。
- v5 Turbo：`gsv-v5-pretrained/s2Gv5turbo.pth` 和 `gsv-v5-pretrained/vocoder.pth`。

对于已在上游验证架构和完整权重格式兼容的 ProPlus 训练结果，可复制语义权重到新目录的 `s1v3.ckpt`、声学权重到 `v2Pro/s2Gv2ProPlus.pth`。这是约定文件名，不会转换模型架构；错误版本、半成品 checkpoint 或 LoRA 无法仅靠改名工作。无法确认时继续使用原基础模型加参考克隆，保留训练结果等待适配。

## 4. 接回程序

首页选择准确版本 → 选择上述完整模型目录 → 设置 3～10 秒参考和台词 → “使用当前声音”。程序会取消旧任务、卸载旧声音并加载新选择；只有“声音已就绪”才算加载通过，失败时查看自检与日志，不使用旧引擎掩盖失败。

用未见短句、中英混合和段落实际生成、试听。随后保存声音方案，或导出 `名称-轻量包.gpt-sovits.ttsmodel`／`名称-模型完整包.gpt-sovits.ttsmodel`；包内保存版本。轻量包目标电脑需安装完全一致的模型和环境；完整包也不携带 Python、源码或 CUDA。

训练不会补齐英文发音资源。在 GPT 独立 Python 环境运行：

```powershell
& '.\tts-engines\gpt-sovits\runtime\Scripts\python.exe' '.\tools\prepare_gpt_resources.py' --source '.\tts-engines\gpt-sovits\source'
```

资源保存在 `tts-engines/gpt-sovits/nltk_data`，随环境一起迁移。离线检查时追加 `--check`；不会把资源安装进某个 Windows 用户账户。运行“环境自检”还会实际读取词典并执行词性标注，避免只导入模块却误报通过。

交给 AI 操作时，要求其报告：版本与硬件 → 素材及划分检查 → 实际执行命令 → checkpoint 类型和位置 → 上游推理结果 → 本程序接入结果。未执行训练、未合并 LoRA、未试听的项目应明确标成未完成。
