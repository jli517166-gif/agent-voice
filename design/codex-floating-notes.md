# Codex 悬浮控制设计与验证

采用 12ui 改进方案 A，保留青蓝主操作、明暗两套配色、波形字标和分组状态区；将它落实到现有 Windows 原生界面。设计套件与原始图保留在本机忽略目录 `acceptance/codex-floating-design`。

## 原生实现的适配

- 删除用户明确要求去掉的“收起”按钮。点击语音入口切换面板，点击面板空白处或失去焦点时关闭。
- 生成图中的重复停止朗读、重复自动朗读仅保留一个入口。不存在独立功能的音量、设置图标不作为虚假按钮加入。
- 应用声音仍是明确的应用操作，不改成生成图中的开关，避免无意切换声音。
- 采用原生桌面所需的紧凑布局。保留设计的层级、颜色、边界和素材；背景与字标由提取的真实素材提供。
- 背景、字标均只在创建面板时读取一次；主题变化时才更新缓存背景。移动窗口不读取图片、不重新生成图层。PNG 仅进行了无损 IDAT 重压缩，未更改像素。
- 样图对照及 LayerDoc closeout 位于 `acceptance/codex-floating-design-closeout-final`。该原生截图计划未进行 DOM 匹配，不将套件完成当作像素精度评分；界面已另外人工检查文字、对齐和明暗状态。

## 跟随与交互验证

- Windows 窗口位置、前后台、最小化及显示事件触发更新，UI 消息合并处理；保留 1 秒安全检查。通知不可用时退回 40 毫秒轮询。
- 仅位置变化才移动窗口，连续 200 次静止刷新没有重复定位。
- 在本机自己的测试窗口上执行 20 次真实位置事件，跟随中位数约 16 毫秒、P95 约 31 毫秒。此结果是测试窗口的事件延迟，不承诺其他硬件上完全相同的时间。
- 覆盖入口重复点击、菜单重复打开、最小化和前后台隐藏、100/150/200% 布局及双屏坐标。
- 声音服务、模型和处理参数保持原有配置。

## 12ui 设计调用记录


Every purchase this kit made, from `improve.json` and the run journal. Paste it into the report as it is; an id that is here and not in the report is spend nobody accounted for.

| purchase | stage | invocation | price ceiling |
| --- | --- | --- | --- |
| `crt-cb8be1733fac0132270cdab04e07dc025b821dad` | draft | `improve` pid 3448, started 2026-10-15T09:45:32.238Z | $0.12 (stage ceiling) |
| `50d5cbfb-fcdb-4621-aae6-f15f21085049` | convert | `improve` pid 24884, started 2026-10-15T09:47:31.384Z | $0.55 (stage ceiling, shared by 2 purchases) |
| `83302095-6519-47c4-8bdc-572c3f85a271` | convert | `improve` pid 24884, started 2026-10-15T09:47:31.384Z | $0.55 (stage ceiling, shared by 2 purchases) |

3 purchases across 2 runs. Ceilings are per stage, not per purchase; the distinct ceilings above total $0.67. Actual prices settle server-side and are not in the kit; read them with `12ui spend`, whose runs are keyed by the ids above.

