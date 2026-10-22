# 版权、第三方组件与分发范围

Agent Voice 自有代码采用根目录 MIT License。项目所有者于 2026-10-22 确认该选择。贡献者保留各自依法享有的权利；本声明不代表第三方代码的著作权已转让给本项目。

第三方组件、素材、模型、录音和训练数据按其各自条款处理，不因位于同一仓库或归档就统一改为 MIT。根 MIT 不授予他人的声音、肖像、商标、录音或角色材料使用权。

| 组件／素材 | 已确认条款 | 声明位置与边界 |
|---|---|---|
| CosyVoice 源码 | Apache-2.0 | `docs/licenses/CosyVoice-Apache-2.0.txt`；不覆盖全部外部权重或参考录音 |
| Matcha-TTS 源码 | MIT | `docs/licenses/Matcha-TTS-MIT.txt`；按锁定提交保留主项目声明，HiFi-GAN 子项目另保留其声明 |
| GPT-SoVITS 源码 | MIT | `docs/licenses/GPT-SoVITS-MIT.txt`；附属模型、词典、运行环境分别核查 |
| AIRI 图标及引用的上游代码 | MIT | `docs/licenses/AIRI-MIT.txt`；用于兼容说明，不代表上游背书 |
| 已登记的 3dicons 素材 | CC0-1.0 | `docs/licenses/3dicons-CC0.txt` 和 `assets/setup/art/cutouts/sources.json`；不覆盖未登记图片 |
| WebView2 | Microsoft 随 SDK 附带条款 | 构建输出的 `docs/licenses/WebView2.txt`；运行时分发条件另核查 |
| FFmpeg、Python、Torch、CUDA 及其依赖 | 各实际版本的独立条款 | 本文件不作整体授权。FFmpeg 对应源码、GPL／LGPL 组件及专有库需按发行检查记录提供材料 |

`assets/README.md` 记录 AI 主图的生成信息与九个原创合成提示音的复现方式。其他图像、字体及公开截图仍应逐项确认来源。

软件本体不附带默认音色、用户参考录音或训练角色权重。可选音色包模块仅提供实现，不证明导入导出内容已获授权。基础模型和训练材料也不能据主引擎源码许可推定清权。

发行审查状态以对应版本的组件清单、实际包的来源与许可报告为准；`docs/legal-components.json` 是审查规则和待办登记，不是全部资源已经允许再分发的证明。公开分发前运行 `tools/release_compliance.py`，完整工程审查通过仍不替代实际授权或法律适用判断。
