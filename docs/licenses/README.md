# 第三方素材许可

本目录仅列出已补齐文本的部分素材，**不是完整依赖许可清单**。

- `AIRI-MIT.txt`：AIRI 官方项目 MIT 许可及版权声明；图标素材来源为 `https://raw.githubusercontent.com/moeru-ai/airi/main/apps/stage-web/public/favicon.svg`。许可文本取自 `https://raw.githubusercontent.com/moeru-ai/airi/main/LICENSE`，获取日期 2026-10-22。AIRI 名称和图标用于兼容功能说明，不表示官方背书。
- `3dicons-CC0.txt`：3dicons 项目的 CC0 文本，取自 `https://raw.githubusercontent.com/realvjy/3dicons/develop/LICENSE`，获取日期 2026-10-22。具体已使用的透明素材来源和 SHA-256 见 `assets/setup/art/cutouts/sources.json`。
- `WebView2.txt`：构建时从实际使用的 Microsoft WebView2 SDK 复制，输出至构建目录的 `docs/licenses`。

TTS 源码、权重、Python、CUDA、FFmpeg 及各子依赖需按各自模块包和锁定版本继续整理，不能用本目录覆盖所有第三方资源的许可。

## TTS 源码许可

- `CosyVoice-Apache-2.0.txt`：CosyVoice 官方源码许可。
- `GPT-SoVITS-MIT.txt`：GPT-SoVITS 官方源码许可。
- 获取来源、日期与 SHA-256 见 `TTS-sources.json`；权重与附属依赖不得推定沿用源码许可。适用范围见 `../使用须知与TTS许可.md`。

- `Matcha-TTS-MIT.txt`：Matcha-TTS 根项目 MIT，锁定修订 `bd4d90d93214b37f7a159cf205ae85762c2c10aa`，版权 Shivam Mehta；SHA-256 `874d84104bdc7b301f369b2f2e66b31f07826a67495af909655f3699c857620d`。源码：https://github.com/shivammehta25/Matcha-TTS 。引擎打包会补对应文本，但不据此推定其他依赖已清权。
