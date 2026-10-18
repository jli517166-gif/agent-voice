# 维护与回归

## GitHub 同步与回退

用户要求每轮修改完成后同步 GitHub。开始改动前确认远端与本地基线；重要界面或架构改版先为现有基线建立标签。每个可独立恢复的改动使用单独提交，验证通过后推送，记录测试和剩余限制。失败的测试不得通过删断言或隐瞒结果来换取提交。

公共主分支回改优先使用 `git revert <提交>` 并重新验证、推送，不强推或重写已发布历史。Git 仅保存源码、测试、说明和可公开的设计资源；用户声音、引擎环境、密钥与打包产物继续排除。版本记录不替代本机资产备份。

## 模块边界

- `ui/`：WinForms 页面、交互与等待动画；环境修复页只调工具，不包含 pip 安装业务。
- `ui/WelcomeForm.cs`、`WelcomeState.cs`、`SetupPage.cs`：静音开场、首次入口和可继续的配置中心；欢迎偏好独立存储，不触发服务、下载或设备验证。
- `service/engine-catalog.json`、`engine_registry.py`：正式引擎能力与选择；新增引擎必须写适配器和契约测试。
- `engine_cosy.py`、`engine_gpt.py`：推理适配器；工作进程独立，切换必须取消旧任务并核对实际模型身份。
- `jobs.py`：串行队列、分段生成、渐进播放、取消和 AIRI 投递；`pipeline.py`：实际推理、校验、重试与流式合并。
- `audio_limits.py`：统一音频与任务等待上限；长文正文最多 4000 字，异常长句分段限制 220 字，正常完整句子优先。
- `environment_repair.py`、`environment-catalog.json`：软件内环境检查、固定版本下载与修复，禁止改写外部 Python 或覆盖训练权重。
- `voice_profiles.py`、`tts_bundle.py`：声音方案与带引擎标记的音色包；轻量包指纹不匹配必须拒绝，不能静默换模型。
- `assets/bundled-voice.json`：便携发行时由打包工具生成的公共默认音色清单；Python 与桌面端仅在无用户设置时读取。清单不能指定密钥、外观、Harness 或 AIRI，也不能引用程序外的声音文件。
- `tools/package_portable.py`：从新构建、指定引擎环境与单个公共音色生成独立暂存；发布清单、私有资产检查、ZIP CRC 与 SHA-256 必须通过。不得用复制整个日常安装目录替代此流程。
- `integrations/`：Codex MCP、DeepSeek 连接器和 AIRI 桥接；界面成功提示必须依赖真实回执。
- `tools/train_cosy_lora.py`：可复现的小显存 CosyVoice3 注意力 LoRA 微调；合并后使用普通 llm.pt，无需修改推理器。

## 修改前后

1. 记录当前配置和实际加载模型，运行环境自检。保留设置、二进制和代码备份，勿删除用户模型。
2. 优先在独立验收目录使用独立端口测试；真实压力测试不得和训练同时争抢显存。
3. 执行下面回归，再构建界面。模拟推理测试只验证协议、并发和错误处理，不代表真实音质。
4. 真机生成每种受影响引擎的短句与长文，验证取消、恢复、跨引擎切换、声音身份和实际播放。记录全部失败，不只选成功案例。
5. 需要用户确认音色时，先给原录音与候选试听；确认后再设置发行默认、制作便携包。

```powershell
python -m pip install -r tools/core-requirements.txt
python -X utf8 -m unittest discover -s tests -p 'test_*.py'
node --test tests/test_connector.mjs tests/test_deepseek_control_client.mjs
powershell -ExecutionPolicy Bypass -File build.ps1
.\dist\agent语音.exe --selftest .\acceptance\ui
```

## 更新环境清单

版本来自已验证环境和上游主仓库。更新 `environment-catalog.json` 时，需要在干净目录测试新装，并在现有环境测试缺项修复与幂等重试。验证 Python／pip／FFmpeg 的 SHA-256，固定源码 commit，核对模型结构和依赖冲突。健康环境不做强制升级。只验证现有环境时，不得宣称所有干净安装已经通过。

训练记录应包含素材 SHA、自动转写状态、去重方法、互不重叠的训练／验证／测试集合、基础模型指纹、随机种子、超参数、验证损失、权重实际变化和试听。合并成功不代表声音通过；不要自动把机械音“改善”为真人音。

## 发布边界

Git 仓库不上传 Python、TTS 源码副本、模型、用户录音、配置密钥、缓存和试听音频。真实测试证据存本机 `acceptance/`，可公开的统计与限制写 `docs/`。CI 运行无 GPU 的回归和 Windows 界面编译；GPU 推理、声卡、Harness 和 AIRI 真机验收仍需本机完成。

完整便携发行的可复现命令、默认声音、移动目录测试与清理边界见《[便携发布流程](docs/便携发布流程.md)》。源码仓库与发行 ZIP 的资源范围不同，不得因源码仓库没有模型而误删本机模型。

自动回归也应在英文 Windows 上运行。业务日志不能因控制台编码或日志管道断开使生成失败；临时目录需先规范化，兼容 Windows 8.3 短路径与实际长路径。新增此类问题的针对性回归，避免只在开发电脑上通过。
