# 交给 AI 的音色训练操作约定

> 当前版本：本体不提供默认音色或音色包导入导出。文中涉及音色包迁移的步骤，须先在“模块 → 导入模块”安装独立音色包模块，再在模块窗口操作；这些步骤不属于首次配置。训练结果可直接在主程序选择，训练和朗读不依赖该模块。

将本文件、《音色训练从零开始.md》和《高级CosyVoice训练手册.md》一起交给执行 AI。文件中的路径示例不是本机真实路径，必须先检查，再给出适合该电脑的命令。

## 输入与职责

用户应提供：Agent 程序目录、素材目录、目标引擎／型号、训练机器（Windows+WSL 或 Linux）、允许存放训练数据和结果的位置、希望模仿的声音目标。缺失信息先做可行的只读检查，不能假定显卡或已有环境。

当前正式引擎 ID 为 cosyvoice 与 gpt-sovits；此训练流程仅为 **CosyVoice3 LLM 微调**。一段参考录音 + 台词是零样本推理条件，不是训练好的新权重。GPT-SoVITS 的声音不能靠导入 CosyVoice 权重获得。未经适配的引擎不会因放入目录自动可用。

目标为 GPT 时改用《[GPT-SoVITS 训练指南](GPT-SoVITS训练指南.md)》，先区分 `v2ProPlus` 与 `v5turbo`。使用 GPT 的 `.list` 标注和官方特征提取／训练页面；不要执行下面的 CosyVoice CSV、Kaldi 和训练包命令。明确记录输出是完整权重还是 LoRA；本程序没有自动合并 LoRA 的功能。GPT 导出后缀为 `.gpt-sovits.ttsmodel`，只支持轻量／模型完整包，禁止冒充 CosyVoice 的高级增量格式。后面的盘点、保留独立试听集、实际执行证据和报告真实性要求仍适用。

## 必须按阶段留证据

1. **盘点**：记录 GPU、驱动、可用显存、Python/Torch/CUDA、源码提交、基础模型完整文件哈希；检查源码确有训练脚本。程序目录内运行 `runtime/core/python.exe -I -X utf8 tools/prepare_training.py inspect-repo --input <CosyVoice源码目录>`。返回缺失项时另建完整训练仓库，不覆盖推理环境。
2. **数据**：按基础手册生成 CSV；执行 validate，修复报错后 prepare。检查报告的 ok、splits、warnings，人工核对台词。禁止同一录音跨集合；最终 test 集不能参与训练或挑最佳权重。检查工具不判断台词语义是否正确。
3. **试跑**：固定源码、配置和随机种子；在新输出目录训练少量步骤。逐项记录命令、退出码、日志、显存峰值和实际 checkpoint。退出码 0 仍需确认产生非空、可加载的预期文件。OOM 不得解释为训练成功。
4. **正式训练**：用户目标和硬件允许后再增加步数，记录训练／验证损失，保留最佳候选。上游 run.sh 只作 recipe 参考，逐条校对模块与路径，不盲目运行全部阶段。
5. **推理验收**：新目录组装完整模型，验证官方推理与 Agent 实际推理。使用未参加训练的文本和同样参考条件与原版对照；记录速度、漏字重复、音色、情绪和长文漂移。不要把 ASR 或说话人相似度单指标说成用户已认可。
6. **导出**：选择 trained（相对基础变化权重）或 trained-full（完整训练模型），输出 `名称.cosyvoice.ttsmodel`。轻量和 full 包不能声称包含新训练成果。至少一次在独立目录导入、生成、播放，再确认交付。

所有训练写入新目录；原认可模型和参考录音不得原地改写。清理仅限本次创建的临时目录，保留训练配置、报告和选定权重。不要将账号、token、私人录音或私有模型提交到公共仓库。

## AI 最终报告格式

```json
{
  "engine": "cosyvoice",
  "modelType": "cosyvoice3",
  "stage": "data-prepared | training-failed | trained | inference-verified | delivered",
  "sourceCommit": "真实提交",
  "baseModelFingerprint": "真实SHA256记录",
  "dataReport": "实际报告路径",
  "trainCommand": "实际执行命令",
  "exitCode": 0,
  "selectedCheckpoint": "实际文件路径，未生成则为null",
  "unseenTextTests": [],
  "package": "实际音色包路径，未导出则为null",
  "limitations": []
}
```

阶段按证据填写：仅生成清单时只能填 data-prepared。没有实际更新并验证权重时不得填 trained。没有真人试听认可时明确写“等待用户试听”。工具的 `--report-only` 只用于界面展示错误时保持进程正常退出；AI 自动化验收应检查 JSON.ok，不能仅凭进程退出码判断。

## 小显存微调工具

仓库提供 `tools/train_cosy_lora.py`，基于兼容 CosyVoice3 官方前向损失，只训练注意力 q/v 投影的 LoRA，冻结声学 flow 与声码器。需要预先准备带 text_token、speech_token、instruct_token、sha256、split 字段的 JSONL，并严格按音频去重划分 train/dev/test。默认 seed=20261017、rank=8、学习率 1e-4、梯度累积 4、240 步。输出目录必须全新；仅当验证损失改善时才保存合并 llm.pt，原始基础模型不改写。输出报告记录权重变化、基础模型指纹和验证轨迹。

这是已在 Fairy 本机实验中使用的专门工具，不等于一键训练所有版本；没有覆盖官方全量训练的能力。自动转写必须标注未人工逐句校对，含专有名词的错误不能当作准确台词。机械合成声的验收应保留参考的节奏与合成质感，不套用“越像真人越好”的目标。训练输出必须补齐其余原始模型文件，再用未见文本试听，用户确认后才切换发行默认。
