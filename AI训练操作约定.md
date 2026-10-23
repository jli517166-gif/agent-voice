# 训练自动化执行规范

本规范用于 AI 或脚本辅助训练，描述可复现的技术过程，不代替实际执行和人工试听。

## 输入

明确目标引擎与版本、素材目录、授权范围、基础模型、训练机器、源码提交和输出位置。先核对 GPU、驱动、Python、Torch、CUDA 及训练脚本是否齐全。

## 阶段

1. 检查素材、台词、重复录音和训练／验证／测试划分。
2. 在新目录生成清单，不改写原录音和基础模型。
3. 固定配置和随机种子，运行少量步骤并确认非空 checkpoint。
4. 记录命令、退出码、损失与显存；再决定正式训练规模。
5. 组装完整推理目录，在上游和应用中使用未见文本验证。
6. 保存配置、选定权重、依赖版本和验证结果。

CosyVoice 和 GPT 使用各自的数据与权重格式。自动转写应标注尚未人工校对；未实际更新权重不能标为已训练，未实际生成不能标为推理通过。输出应区分完整权重、LoRA 与合并权重。

## 报告

```json
{
  "engine": "cosyvoice",
  "stage": "data-prepared",
  "sourceCommit": "<source revision>",
  "baseModelFingerprint": "<sha256 record>",
  "command": null,
  "exitCode": null,
  "selectedCheckpoint": null,
  "unseenTextTests": [],
  "limitations": []
}
```

报告只填写真实存在的证据；失败保留错误阶段。自动化不得提交原录音、私有权重、账号或密钥到公共仓库。具体步骤见 [基础训练](音色训练从零开始.md)、[高级训练](高级CosyVoice训练手册.md)与 [GPT 指南](GPT-SoVITS训练指南.md)。
