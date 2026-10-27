# 贡献指南

欢迎提交缺陷修复、引擎适配、连接器改进和文档更正。

## 开始前

以 `main` 为开发基线，创建独立分支。阅读 [架构说明](docs/架构与开发.md)，确认改动所属模块，避免把引擎安装、推理调度和界面逻辑混在一起。

```powershell
git fetch origin
git switch -c fix/short-description origin/main
```

提交问题时提供系统、显卡与驱动、所选引擎及版本、复现步骤、预期行为、实际结果和脱敏日志。不要提交聊天正文、账号凭据、个人文件路径或原始录音。

## 模块边界

| 目录 | 职责 |
|---|---|
| `ui/` | WinForms 界面、配置流程、原生资源安装与状态显示 |
| `assets/` | 应用公共界面素材与提示音 |
| `service/` | 配置、HTTP API、任务队列、音频处理和引擎适配 |
| `integrations/` | Harness 连接器、MCP 服务和 AIRI 桥接 |
| `tools/` | 环境准备、诊断、训练与构建辅助工具 |
| `optional-modules/` | 独立桌面扩展实现 |
| `tests/` | 协议、容错、兼容性与界面回归 |

新增引擎应更新能力登记并实现适配器；新增 Harness 应提供事件规范、幂等处理和连接验证。界面不得用动画、文件存在或安装命令退出码冒充推理或连接成功。

## 验证

```powershell
python -m pip install -r tools/core-requirements.txt
python -X utf8 -m unittest discover -s tests -p 'test_*.py'
node --test tests/test_connector.mjs tests/test_mouth.mjs
powershell -File build.ps1
.\dist\agent语音.exe --selftest .\acceptance\ui
```

大部分协议测试不需要 GPU；真实生成需要另装引擎和模型。修改推理、队列、播放或引擎切换时，增加对应短句、长文、取消和恢复验证。区分模拟测试、实际生成和实际播放，记录失败与尚未执行的检查。

## 提交

- 一个提交解决一个清楚的问题，说明触发条件、结果和验证方式。
- 提交前检查 `git diff --check`、`git diff --cached` 与新增文件清单。
- 不提交运行环境、模型、录音、生成媒体、用户设置、访问凭据或构建缓存。
- 对外文档只描述产品、协议、安装和开发方法；不要包含私人协作记录、聊天指令或本机验收过程。
- 修改依赖或引用素材时保留来源、版本及对应许可文本。
- 已发布分支优先通过后续修复或 revert 回退，不重写他人的历史。

提交 Pull Request 时说明问题、实现、测试及实际限制。CI 通过不能替代 GPU、声卡、Harness 或 AIRI 的实机验证。
