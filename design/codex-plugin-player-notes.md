# Codex 插件朗读试验验收

语音控制插件 1.3.0 增加 MCP Apps HTML 资源与会话面板入口。面板提供正文、朗读、停止朗读和状态刷新，其他设置继续通过原生技能及 MCP。旧浮窗默认关闭，本机已有浮窗也已关闭。声音模型与参数未修改。

## 已验证

- stdio 初始化、工具发现、资源发现、读取 HTML、拒绝其他资源地址。
- 面板打开或接收新正文不播放；点击朗读准确提交正文；停止调用同一 MCP 工具。
- 只接受父窗口协议消息，错误不冒充成功，宿主未提供工具桥接时禁用按钮并提示改用技能。
- 6 项 Python MCP 测试通过；前端桥接及 DeepSeek 菜单回归通过。
- 原生界面 17 个页面、120 次切换及浮窗兼容测试通过。
- 本机真实 stdio MCP 测试：服务自动启动，“工作好久啦，起来动一动、喝口水吧～”准确生成并由本机播放，音频时长 3.52 秒；第二次朗读被停止并取消；声音配置哈希一致。测试结束恢复原先未启动的服务状态。

## 尚未验证及明确边界

- 当前 Codex 会话仍使用旧的 7 项 MCP 工具；安装新版后本会话没有自动加载新增工具。需重载插件或新建会话，然后通过“语音控制”发送“打开朗读面板”，验证实际显示及按钮执行。当前 `visibleHostUiVerified` 为 false，不能将协议和模拟宿主测试当作界面成功。
- 内置“朗读”按钮尚未接管。查阅的官方插件界面及会话面板扩展文档没有提供替换内置朗读播放源的接口。不能将新面板称为内置朗读。
- 若宿主只返回文字，保留原生技能与 MCP 控制。插件安装成功不等于原生常驻入口或面板渲染成功。
- 本机证据：`acceptance/codex-plugin-player-live.json`、`acceptance/codex-plugin-player-ui/ui-results.json`、`acceptance/codex-plugin-player-floating/floating-results.json`，均不进入发布包。

官方参考：[MCP 界面](https://developers.openai.com/plugins/build/chatgpt-ui)、[会话面板扩展](https://developers.openai.com/plugins/build/extensions)。
