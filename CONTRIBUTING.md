# 维护与回归

## GitHub 同步与回退

用户要求每轮修改完成后同步 GitHub。开始改动前确认远端与本地基线；重要界面或架构改版先为现有基线建立标签。每个可独立恢复的改动使用单独提交，验证通过后推送，记录测试和剩余限制。失败的测试不得通过删断言或隐瞒结果来换取提交。

公共主分支回改优先使用 `git revert <提交>` 并重新验证、推送，不强推或重写已发布历史。Git 仅保存源码、测试、说明和可公开的设计资源；用户声音、引擎环境、密钥与打包产物继续排除。版本记录不替代本机资产备份。

## 模块边界

- `ui/`：WinForms 页面、交互与等待动画；环境修复页只调工具，不包含 pip 安装业务。
- `SetupEntry.cs`、`EmbeddedSetupForm.cs`：独立首次配置入口、WebView2 宿主与原生备用。`assets/setup/` 是本地十页前端；`EmbeddedSetupController.cs` 只提供固定消息操作，`SetupWorkflow.cs` / `ConnectionSetup.cs` 执行真实后端。页面不能收到服务密钥或执行任意命令。`WelcomeState.cs` 独立保存进度与偏好。原生 `SetupWizard` 保留为组件缺失时的备用。
- `ui/ModulePackages.cs`：无 Python 的原生模块校验、事务安装、路径重定位和中断恢复。`tools/module_packages.py`：分别制作软件本体、引擎、环境包，声音继续使用独立包。模块清单只允许修改自身路径，不能携带账号设置；导入通过不能替代目标硬件推理验收。
- `ui/SetupAnimation.cs`：旧原生备用配置的说明动画，静音、有限时长；当前路由以外必须停止计时器，减少动画立即停止。不得将说明动画作为真实下载、设备检查或连接成功证据。配置页“下一步”应提交该步选择后再前进；跳过不应用未保存的选择。
- `service/engine-catalog.json`、`engine_registry.py`：正式引擎能力与选择；新增引擎必须写适配器和契约测试。
- `engine_cosy.py`、`engine_gpt.py`：推理适配器；工作进程独立，切换必须取消旧任务并核对实际模型身份。
- `jobs.py`：串行队列、分段生成、渐进播放、取消和 AIRI 投递；`pipeline.py`：实际推理、校验、重试与流式合并。
- `audio_limits.py`：统一音频与任务等待上限；长文正文最多 4000 字，异常长句分段限制 220 字，正常完整句子优先。
- `environment_repair.py`、`environment-catalog.json`：软件内环境检查、固定版本下载与修复，禁止改写外部 Python 或覆盖训练权重。
- `service/voice_profiles.py`、`model_integrity.py`、`file_integrity.py`：本体的声音方案及本地文件／模型校验；不得依赖可选音色包模块。
- `optional-modules/voice-packages/`：音色包窗口和全部归档读写后端，独立编译成 `.avmodule`，根构建不复制。通用功能模块宿主只处理安装、完整性、启动与移除，不内置业务。
- 不得注入默认音色；旧 `bundled-voice.json` 在首次启动被忽略。旧 `package_portable.py stage` 已停用，软件本体、引擎、环境和功能模块独立制作；本体构建与归档必须排除可选实现及用户资产。
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

窗口布局修改还需运行 `--layouttest <输出目录>`（83 个窗口/页面场景）和 `--welcometest <输出目录>`，并实际观察最大化、还原和页面切换。WinForms GUI 可执行文件建议用 `Start-Process -PassThru -Wait` 获取真实退出码，不能将启动成功当成验收通过。`--layout-preview` 仅用于独立界面验收，不自动启动语音服务。当前布局验收范围与未覆盖项见《[最大化布局修复](docs/最大化布局修复-20261020.md)》。

可读性与滚动规则见《[界面尺寸调整](docs/界面尺寸调整-20261020.md)》。不要通过缩小字号来让短窗口回归通过；页面可滚动时，应验证控件在文档范围内并能滚动访问。修改滚动布局后还需执行 `--scrolltest <输出目录>`，测试时保持该验收窗口不被其他窗口覆盖，以采集有效屏幕帧。

## 更新环境清单

版本来自已验证环境和上游主仓库。更新 `environment-catalog.json` 时，需要在干净目录测试新装，并在现有环境测试缺项修复与幂等重试。验证 Python／pip／FFmpeg 的 SHA-256，固定源码 commit，核对模型结构和依赖冲突。健康环境不做强制升级。只验证现有环境时，不得宣称所有干净安装已经通过。

训练记录应包含素材 SHA、自动转写状态、去重方法、互不重叠的训练／验证／测试集合、基础模型指纹、随机种子、超参数、验证损失、权重实际变化和试听。合并成功不代表声音通过；不要自动把机械音“改善”为真人音。

## 发布边界

Git 仓库不上传 Python、TTS 源码副本、模型、用户录音、配置密钥、缓存和试听音频。真实测试证据存本机 `acceptance/`，可公开的统计与限制写 `docs/`。CI 运行无 GPU 的回归和 Windows 界面编译；GPU 推理、声卡、Harness 和 AIRI 真机验收仍需本机完成。

完整便携发行的可复现命令、默认声音、移动目录测试与清理边界见《[便携发布流程](docs/便携发布流程.md)》。源码仓库与发行 ZIP 的资源范围不同，不得因源码仓库没有模型而误删本机模型。

自动回归也应在英文 Windows 上运行。业务日志不能因控制台编码或日志管道断开使生成失败；临时目录需先规范化，兼容 Windows 8.3 短路径与实际长路径。新增此类问题的针对性回归，避免只在开发电脑上通过。

## 内嵌配置回归

构建会校验并缓存微软 WebView2 SDK，不把二进制提交 Git。运行时使用系统 Evergreen 或软件内 `tools/system-prerequisites/webview2-fixed/`；不要向网页加载 CDN 或把演示计时器接成安装成功状态。

```powershell
node --test tests/test_setup_frontend.mjs
# GUI 程序请使用 Start-Process -PassThru -Wait 并检查 ExitCode
.\dist\agent语音.exe --websetup-unit .\acceptance\embedded-controller
.\dist\agent语音.exe --websetup-test .\acceptance\embedded-web
```

控制器测试隔离配置和 HTTP 服务，覆盖 Next/Skip、输入校验、停止冷启动试听和轮询失败清理。WebView 测试覆盖十页、多个窗口大小、真实前端保存引擎、错误服务端口、缺运行时备用入口和缩放帧采样；请保持测试窗口不被其他窗口覆盖。它不运行真实 GPU 合成、系统安装器或登录第三方账号，这些仍需目标电脑验收。Python 测试服务也使用独立空 Codex 会话目录，避免测试时读入开发者的实时聊天。

## 背景、方案列表与配置素材回归

`agent语音.exe --visual-repair-test <独立输出目录>` 使用公开合成背景与虚拟方案，检查默认/明亮背景下导航圆角与侧栏容器颜色一致、方案两行字体高度不超过 DPI 行高。不能只检查按钮 Bounds 就宣称圆角没有绘制异常，也不能只检查 ListBox 外框就宣称 owner-draw 行没有文字重叠。

首次配置图片使用原作者带 alpha 的 PNG，来源和哈希在 `assets/setup/art/cutouts/sources.json`；不要再次用反色混合近似抠图。场景在 `scenes.css` 中表现扫描、文件投递、依赖装配与声浪，只有对应任务进行时才循环，减少动画和缩放暂停仍必须生效。AIRI 原图标与 CSS 曲面、SVG 声浪作为页面元素组合，不可拿配置截图替代 UI。

资源准备与运行依赖分别使用 `resources` 和 `repair --prepare-only --dependencies-only`，文件完整不代表真实推理。保持 Windows 路径归一化、下载器不改外部 Python、源码替换后重查 Matcha，以及 GPT 特征提取配置完整性的回归。

模块回归必须包含独立 Python 子进程（不能看到可选源码）的本地模型／声音方案测试、无 Python 安装功能模块、安装／复用／损坏拒绝／移除、真实包往返和移除后真实 TTS。不要在全局给 Python 加可选目录，再用通过结果证明本体独立。
