// Design-only workspace. All state stays in memory; no service endpoints are called.
const B = window.reviewBridge;
const q = (s) => document.querySelector(s);
const esc = (s) =>
  String(s).replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
const paths = {
  play: "m8 4 12 8-12 8Z",
  stop: "M6 6h12v12H6Z",
  replay: "M4 11a8 8 0 1 1 1 6M4 4v7h7",
  engine: "M8 5v14M12 2v20M16 7v10M4 9v6M20 8v8",
  package: "M5 7h14v14H5ZM8 3h11v4M9 11h6M9 15h6",
  harness: "M8 7H3v10h5M16 7h5v10h-5M7 12h10m-4-3 3 3-3 3",
  environment: "M4 6h16M4 12h16M4 18h16M8 3v6M16 9v6M10 15v6",
  computer:
    "M4 4h6a3 3 0 0 1 3 3v14a4 4 0 0 0-4-2H4ZM13 7a3 3 0 0 1 3-3h5v15h-4a4 4 0 0 0-4 2",
};
const ico = (id) =>
  `<svg class="ico" viewBox="0 0 24 24" aria-hidden="true" style="stroke-width:1.5"><path d="${paths[id] || paths.engine}"/></svg>`;
const button = (text, action, cls = "", extra = "") =>
  `<button class="ui-button ${cls}" data-work="${action}" ${extra}>${text}</button>`;
const state = {
  page: "speech",
  service: false,
  text: "工作好久啦，起来动一动、喝口水吧～",
  active: "fairy",
  speed: 1,
  volume: 80,
  advanced: false,
  settings: "engine",
  training: "cosy",
  airi: false,
  auto: true,
  stage: "idle",
  queue: [],
  phrases: [
    { name: "休息一下", text: "工作好久啦，起来动一动、喝口水吧～" },
    { name: "任务完成", text: "任务已经完成啦，来看看结果吧。" },
  ],
  voices: [
    { id: "fairy", name: "Fairy", engine: "CosyVoice", type: "轻量音色包" },
    {
      id: "miyabi",
      name: "星见雅",
      engine: "GPT-SoVITS · v5 Turbo",
      type: "轻量音色包",
    },
    {
      id: "custom",
      name: "自定义声音",
      engine: "GPT-SoVITS · ProPlus",
      type: "模型完整包",
    },
  ],
  schemes: [
    {
      id: "daily",
      name: "Fairy · 日常",
      voice: "fairy",
      speed: 1,
      volume: 80,
      advanced: false,
      expression: "",
      snapshot: {
        id: "fairy",
        name: "Fairy",
        engine: "CosyVoice",
        type: "轻量音色包",
      },
    },
  ],
  connections: { Codex: "pending", "DeepSeek Harness": "off", AIRI: "off" },
};
let playbackTimer = 0,
  toastTimer = 0,
  serial = 0,
  backgroundUrl = "";
const current = () =>
  state.voices.find((v) => v.id === state.active) || state.voices[0];
const meter = (active) =>
  `<span class="mini-spectrum ${active ? "active" : ""}" aria-hidden="true">${Array.from({ length: 5 }, (_, i) => `<i style="--i:${i}"></i>`).join("")}</span>`;
function toast(text) {
  q(".toast")?.remove();
  clearTimeout(toastTimer);
  const node = document.createElement("div");
  node.className = "toast";
  node.setAttribute("role", "status");
  node.textContent = text;
  q("#window").append(node);
  toastTimer = setTimeout(() => node.remove(), 2700);
}
function dialog(title, html, setup) {
  B.openModal(title, html, setup);
}
function notifyModal(text) {
  let node = q(".modal-status");
  if (!node) {
    node = document.createElement("p");
    node.className = "modal-status";
    node.setAttribute("role", "status");
    q("#modalContent").append(node);
  }
  node.textContent = text;
}
function heading(title, lead, actions = "") {
  return `<div class="work-heading"><div><h1>${title}</h1><p>${lead}</p></div><div class="heading-actions">${actions}</div></div>`;
}
function service() {
  return `<div class="service-pill ${state.service ? "on" : ""}"><b></b><span>${state.service ? "服务已开启 · 演示" : "服务未开启"}</span><button data-work="service">${state.service ? "关闭服务" : "开启服务"}</button></div>`;
}
function speech() {
  const v = current();
  return (
    heading(
      "文字，有了声音。",
      "手动朗读与自动朗读，在这里统一控制。",
      service(),
    ) +
    `<div class="speech-layout"><div><div class="current-voice"><div class="voice-mark">${ico("engine")}</div><div><strong>${esc(v.name)}</strong><small>${esc(v.engine)} · ${state.speed.toFixed(2)} 倍语速</small></div>${button("切换声音", "pickVoice")}</div><div class="composer"><textarea id="studioText" aria-label="朗读文本" spellcheck="false">${esc(state.text)}</textarea><div class="composer-meta"><div class="destination"><span></span>${state.airi && state.connections.AIRI === "verified" ? "AIRI 桌宠 · 演示" : "本机播放"}</div><span id="studioCount">${state.text.length} 字</span></div></div><div class="phrase-block"><div class="section-label">常用句<button data-work="phrases">管理</button></div><div class="phrase-chips">${state.phrases.map((p, i) => `<button data-work="phrase" data-id="${i}">${esc(p.name)}</button>`).join("") || '<span class="compact-hint">还没有常用句，可以从当前文本保存。</span>'}</div></div></div><aside class="queue-pane" aria-label="朗读队列"><div class="section-label">朗读队列<button data-work="clearQueue">清空</button></div><div id="queueList">${queueMarkup()}</div><div class="connection-mini"><strong>自动朗读 ${state.auto ? "已启用" : "已关闭"} · 演示</strong><br>${state.service ? "以完整句末分段，顺序播放。" : "待服务开启后接收任务。"}<br><button data-work="demoQueue">体验对话进入队列</button><br><button data-work="connections">管理客户端接入</button></div></aside></div>`
  );
}
function queueMarkup() {
  return state.queue.length
    ? state.queue
        .map(
          (j, i) =>
            `<div class="queue-item"><span>${esc(j.text)}</span><small>${i === 0 ? (state.stage === "paused" ? "已暂停" : state.stage === "generating" ? "生成中" : "播放中") : esc(j.source)}</small></div>`,
        )
        .join("")
    : `<div class="queue-empty"><div class="queue-lines" aria-hidden="true">${[5, 10, 17, 9, 13, 6].map((h) => `<i style="--h:${h}px"></i>`).join("")}</div>暂时没有朗读任务。<br>来自客户端的完整句子<br>会按顺序出现在这里。</div>`;
}
function deck() {
  return `<div class="play-deck"><div class="play-status" id="studioStatus" role="status">${playText()}</div>${button(ico("stop") + "停止", "stop", "stop-btn")}${button(ico("replay"), "replay", "replay-btn", 'aria-label="重新播放当前文本"')}${button(playButtonText(), "play", "main", 'id="studioPlay"')}</div>`;
}
function playText() {
  const names = {
    idle: "准备就绪",
    generating: "正在生成 · 演示",
    playing: "正在播放 · 演示",
    paused: "已暂停 · 演示",
    done: "播放完成 · 演示",
    stopped: "已停止",
  };
  return `${meter(state.stage === "playing")}<span class="play-state-text">${names[state.stage]}</span><small>仅演示交互，不生成真实语音</small>`;
}
function playButtonText() {
  return state.stage === "playing"
    ? "暂停播放"
    : state.stage === "paused"
      ? "继续播放"
      : state.stage === "generating"
        ? "生成中…"
        : ico("play") + "生成并播放";
}
function voices() {
  return (
    heading(
      "音色包",
      "声音文件放在这里，参数组合放在“声音方案”。",
      button("导入音色包", "importVoice", "main") +
        button("导出音色包", "exportVoice"),
    ) +
    `<div class="voice-list">${state.voices.map((v) => `<article class="voice-row"><div class="voice-symbol">${ico("engine")}</div><div><h2>${esc(v.name)}${v.id === state.active ? '<span class="tag">当前声音</span>' : ""}</h2><p>${esc(v.engine)} · ${esc(v.type)}</p></div><div class="row-actions">${button(v.id === state.active ? "调整设置" : "应用声音", v.id === state.active ? "voiceSettings" : "applyVoice", v.id === state.active ? "" : "main", `data-id="${v.id}"`)}${button("详情", "voiceDetails", "subtle", `data-id="${v.id}"`)}</div></article>`).join("")}</div><p class="compact-hint">这里的三个声音仅为布局示例，不读取或修改你的真实音色包。</p><div class="detail-footer">${button("训练自己的音色", "training", "subtle")}${button("打开音色包文件夹", "packageFolder", "subtle")}</div>`
  );
}
function schemes() {
  return (
    heading(
      "声音方案",
      "保存当前声音和参数，随时切回熟悉的听感。",
      button("保存当前设置", "saveScheme", "main") +
        button("打开方案文件夹", "schemeFolder"),
    ) +
    `<div class="scheme-list">${state.schemes.map((s) => `<article class="scheme-row"><div><h2>${esc(s.name)}</h2><p>${esc(state.voices.find((v) => v.id === s.voice)?.name || "音色已移除")} · ${s.speed.toFixed(2)} 倍语速 · 音量 ${s.volume}%<br>声音细调${s.advanced ? "开启" : "关闭"}</p></div><div class="inline-row">${button("应用", "applyScheme", "main", `data-id="${s.id}"`)}${button("删除", "deleteScheme", "danger", `data-id="${s.id}"`)}</div></article>`).join("") || '<div class="empty-library">还没有保存的方案。先选择音色，再保存当前设置。</div>'}</div><p class="compact-hint">方案记录设置；音色包保存声音文件。两者分开管理，避免重复导出模型。</p>`
  );
}
function connections() {
  return (
    heading(
      "连接",
      "先选目录，再验证真实回复与播放回执。",
      button("接入验证说明", "connectionHelp"),
    ) +
    ["Codex", "DeepSeek Harness", "AIRI"]
      .map((name, i) => {
        const s = state.connections[name],
          isAiri = i === 2;
        return `<article class="client-row"><div class="client-brand">${isAiri ? '<img src="assets/airi-11-icon.png" alt="AIRI">' : i === 0 ? "Cx" : "DS"}</div><div class="client-info"><h2>${name}</h2><p>${isAiri ? "使用你已经登录的原 AIRI，不另建副本。" : i === 0 ? "MCP 语言控制与生成过程自动朗读。" : "聊天栏控制入口与生成过程自动朗读。"}</p><span class="client-state ${s === "verified" ? "verified" : s === "failed" ? "failed" : ""}">${s === "verified" ? "验证通过状态 · 仅演示" : s === "off" ? "未启用" : s === "failed" ? "验证未通过 · 演示" : "已选择，尚未验证"}</span></div><div class="row-actions">${isAiri ? `<button class="small-switch" data-work="toggleAiri" role="switch" aria-label="AIRI 联动" aria-checked="${state.airi}"></button>` : ""}${button(s === "verified" ? "重新验证" : "选择目录并验证", "connect", "", `data-id="${name}" ${isAiri && !state.airi ? "disabled" : ""}`)}</div></article>`;
      })
      .join("") +
    `<div class="setting-line"><div>自动朗读<p>任务执行过程中的正文与最终回复都可朗读；完整句子按顺序播放。</p></div><button class="small-switch" data-work="auto" role="switch" aria-label="自动朗读" aria-checked="${state.auto}"></button></div><div class="detail-footer">${button("开发其他客户端连接器", "extension", "subtle")}<span class="compact-hint">当前实测支持范围：Codex、DeepSeek Harness</span></div>`
  );
}
const settingTabs = {
  engine: "引擎与环境",
  sound: "声音",
  appearance: "外观",
  storage: "存储",
};
const line = (name, desc, control) =>
  `<div class="setting-line"><div>${name}<p>${desc}</p></div><div class="inline-row">${control}</div></div>`;
function settings() {
  let body = "";
  if (state.settings === "engine")
    body =
      line(
        "本地 TTS 引擎",
        "CosyVoice、GPT-SoVITS v5 Turbo / ProPlus 分开匹配环境。",
        button("管理引擎", "engineManager"),
      ) +
      line(
        "检查与补齐环境",
        "联网准备或导入离线环境包，文件保存在软件目录内。",
        button("检查环境", "environment"),
      ) +
      line(
        "首次配置",
        "重新进入独立向导；已有设置可保留。",
        button("重新配置", "setup"),
      );
  if (state.settings === "sound")
    body =
      line(
        "语速",
        "属于当前声音设置，可存为声音方案。",
        `<output id="speedValue">${state.speed.toFixed(2)}×</output><input type="range" id="studioSpeed" aria-label="语速" min="0.5" max="2" step="0.05" value="${state.speed}">`,
      ) +
      line(
        "音量",
        "只改变播放音量，不更换音色。",
        `<output id="volumeValue">${state.volume}%</output><input type="range" id="studioVolume" aria-label="音量" min="0" max="100" value="${state.volume}">`,
      ) +
      line(
        "声音细调",
        "默认关闭。开启后再调整表达、音色处理与说话风格。",
        `<button class="small-switch" role="switch" data-work="advanced" aria-label="声音细调" aria-checked="${state.advanced}"></button>`,
      ) +
      (state.advanced
        ? line(
            "表达与风格",
            "仅作用于支持此能力的 TTS；不把风格文字当作朗读正文。",
            button("调整表达", "expression"),
          )
        : "") +
      line(
        "保存当前听感",
        "保存音色、语速、音量和声音细调开关。",
        button("保存声音方案", "saveScheme", "main"),
      );
  if (state.settings === "appearance")
    body =
      line(
        "强调色",
        "配置向导与日常工作台分别保持清楚层级。",
        `<button class="appearance-swatch selected" style="--swatch:#b7a1ff" data-work="color" data-id="#b7a1ff" aria-label="电紫"></button><button class="appearance-swatch" style="--swatch:#9fd3e5" data-work="color" data-id="#9fd3e5" aria-label="冰蓝"></button><button class="appearance-swatch" style="--swatch:#e7acc7" data-work="color" data-id="#e7acc7" aria-label="浅粉"></button>`,
      ) +
      line(
        "背景图片",
        "背景只在程序整体显示，避免每个卡片重复出现。",
        `<div class="background-preview" id="backgroundPreview"></div>${button("选择图片", "background")}`,
      ) +
      line(
        "应用图标",
        "桌面、窗口和托盘图标一起更换，自动处理圆角。",
        button("选择图标", "appIcon"),
      ) +
      line(
        "等待动画",
        "图片或 GIF；可恢复默认音块动画。",
        button("自定义动画", "loadingArt"),
      ) +
      line(
        "界面音效",
        "默认关闭，单独试听后再开启。",
        button(B.getState().sound ? "关闭音效" : "开启音效", "sound"),
      );
  if (state.settings === "storage")
    body =
      line(
        "音色包",
        "voice-model-packages，导入与训练产物使用同一位置。",
        button("查看位置", "packageFolder"),
      ) +
      line(
        "声音方案",
        "每个名称一个文件夹，保存参数并引用相应音色。",
        button("查看位置", "schemeFolder"),
      ) +
      line(
        "生成音频",
        "按保留期限清理；正在播放或排队的音频不删除。",
        button("清理音频", "cleanup"),
      ) +
      line(
        "诊断信息",
        "导出失败步骤与日志，分享前移除路径和连接密钥。",
        button("诊断说明", "diagnostics"),
      );
  return (
    heading("设置", "需要时再展开，常用操作留在前台。") +
    `<div class="settings-tabs" role="tablist" aria-label="设置分类">${Object.entries(
      settingTabs,
    )
      .map(
        ([id, name]) =>
          `<button role="tab" data-work="settingTab" data-id="${id}" aria-selected="${state.settings === id}">${name}</button>`,
      )
      .join("")}</div>${body}`
  );
}
const trainData = {
  cosy: {
    name: "CosyVoice",
    heading: "从一段干净的参考录音开始。",
    lead: "先完成参考克隆，再决定是否需要训练权重。",
    steps: [
      [
        "准备声音",
        "录音清楚、无背景音乐，逐字台词与声音一致。保留人工合成机械音的特点，不强行自然化。",
      ],
      [
        "选择训练路线",
        "参考克隆使用录音与台词；硬件条件较好时按高级指南准备训练数据、基础模型与检查点。",
      ],
      [
        "本地试听并验证",
        "确认引擎版本、模型结构、台词和参考音频一致；用短句及长句分别验证。",
      ],
      [
        "导出训练结果",
        "按实际内容选择轻量、模型完整、训练增量或训练完整包；文件名附带 .cosyvoice.ttsmodel。",
      ],
    ],
  },
  turbo: {
    name: "GPT-SoVITS · v5 Turbo",
    heading: "先对齐参考，再检查长句表现。",
    lead: "沿用当前适配版本，参考素材和声学权重分开核对。",
    steps: [
      [
        "整理训练素材",
        "裁剪干净录音并校对逐字文本；使用对应版本的分词、文本特征和语音特征流程。",
      ],
      [
        "训练并保留基础模型信息",
        "按 GPT-SoVITS 指南记录 GPT / SoVITS 权重、采样率和版本；不要把其他架构的权重混入。",
      ],
      [
        "验证推理",
        "参考音频与对应台词一起选择；检查音色、漏字、重复、机械音特点和长句速度。",
      ],
      [
        "导出已验证音色包",
        "轻量包复用匹配基础引擎；完整包附带相应模型权重。使用 .gpt-sovits.ttsmodel，清单记录 v5 Turbo。",
      ],
    ],
  },
  pro: {
    name: "GPT-SoVITS · ProPlus",
    heading: "版本匹配，才是稳定的起点。",
    lead: "ProPlus 与 v5 不互换声学权重，按对应训练指南操作。",
    steps: [
      [
        "确认训练版本",
        "使用对应 ProPlus 分支与基础模型，按指南检查显存和数据格式。",
      ],
      [
        "准备并训练",
        "完成音频切片、文本校对、特征提取与对应权重训练，保留可回退检查点。",
      ],
      [
        "应用兼容权重并试听",
        "接入已适配的固定目录结构；先验证生成和播放，再比较参考音色与语气。",
      ],
      [
        "导出包并标注版本",
        "后缀为 .gpt-sovits.ttsmodel，内部清单必须标明 ProPlus；导入时再次核对版本。",
      ],
    ],
  },
};
function training() {
  const t = trainData[state.training];
  return (
    heading("训练指南", "先选引擎，再按当前阶段继续。") +
    `<div class="training-tabs" role="tablist" aria-label="训练引擎">${Object.entries(
      trainData,
    )
      .map(
        ([id, d]) =>
          `<button role="tab" data-work="trainingTab" data-id="${id}" aria-selected="${state.training === id}">${d.name}</button>`,
      )
      .join(
        "",
      )}</div><div class="training-intro"><div><h2>${t.heading}</h2><p>${t.lead}</p></div><img src="assets/mic.webp" alt=""></div><div class="training-steps">${t.steps.map(([h, p]) => `<section class="training-step"><div><h3>${h}</h3><p>${p}</p></div></section>`).join("")}</div><div class="detail-footer">${button("查看详细操作手册", "manual", "main")}${button("打开训练界面", "trainingTool")}</div><p class="compact-hint">本页是指南信息架构审核。正式训练能力、硬件条件与兼容边界以项目对应手册为准。</p>`
  );
}
function render(page = state.page) {
  const configured = B.getState();
  if (configured.voice !== "尚未选择" && configured.voice !== current().name) {
    let match = state.voices.find(
      (v) => v.name === configured.voice && v.engine === configured.engine,
    );
    if (!match) {
      match = {
        id: "configured-" + ++serial,
        name: configured.voice,
        engine: configured.engine,
        type: "参考声音配置 · 演示",
      };
      state.voices.push(match);
    }
    state.active = match.id;
  }
  state.page = [
    "speech",
    "voices",
    "schemes",
    "connections",
    "settings",
    "training",
  ].includes(page)
    ? page
    : "speech";
  const nav = [
    ["speech", "朗读", "play"],
    ["voices", "音色包", "engine"],
    ["schemes", "声音方案", "package"],
    ["connections", "连接", "harness"],
    ["settings", "设置", "environment"],
    ["training", "训练指南", "computer"],
  ];
  q("#screens").innerHTML =
    `<section class="screen" aria-label="${nav.find((n) => n[0] === state.page)[1]}工作台"><div class="work-shell"><nav class="work-nav" aria-label="日常功能">${nav.map(([id, name, icon], i) => `<button data-work="navigate" data-id="${id}" ${state.page === id ? 'aria-current="page"' : ""} class="${i === 5 ? "nav-bottom" : ""}">${ico(icon)}${name}</button>`).join("")}<div class="nav-foot"><i></i>本地声音工作室<br>设计审核 · 不连接服务</div></nav><div class="work-main"><div class="work-scroll">${{ speech, voices, schemes, connections, settings, training }[state.page]()}</div>${state.page === "speech" ? deck() : ""}</div></div></section>`;
  q("#screens .screen").onclick = onAction;
  const text = q("#studioText");
  if (text)
    text.oninput = () => {
      state.text = text.value;
      q("#studioCount").textContent = state.text.length + " 字";
    };
  const speed = q("#studioSpeed");
  if (speed)
    speed.oninput = () => {
      state.speed = +speed.value;
      q("#speedValue").textContent = state.speed.toFixed(2) + "×";
    };
  const vol = q("#studioVolume");
  if (vol)
    vol.oninput = () => {
      state.volume = +vol.value;
      q("#volumeValue").textContent = state.volume + "%";
    };
  if (backgroundUrl && q("#backgroundPreview"))
    q("#backgroundPreview").style.backgroundImage = `url("${backgroundUrl}")`;
  if (!B.isReduced())
    q(".work-main").animate(
      [
        { opacity: 0, transform: "translateX(9px)" },
        { opacity: 1, transform: "translateX(0)" },
      ],
      { duration: 260, easing: "cubic-bezier(.16,1,.3,1)" },
    );
}
function paint() {
  if (q("#studioStatus")) q("#studioStatus").innerHTML = playText();
  if (q("#studioPlay")) {
    q("#studioPlay").innerHTML = playButtonText();
    q("#studioPlay").disabled = state.stage === "generating";
  }
  if (q("#queueList")) q("#queueList").innerHTML = queueMarkup();
}
function stop() {
  clearTimeout(playbackTimer);
  state.queue = [];
  state.stage = "stopped";
  paint();
}
function pump() {
  if (!state.queue.length) {
    state.stage = "done";
    paint();
    return;
  }
  state.stage = "generating";
  paint();
  playbackTimer = setTimeout(() => {
    state.stage = "playing";
    paint();
    B.cue(6);
    playbackTimer = setTimeout(() => {
      state.queue.shift();
      pump();
    }, 2500);
  }, 900);
}
function play() {
  if (state.stage === "playing") {
    clearTimeout(playbackTimer);
    state.stage = "paused";
    paint();
    return;
  }
  if (state.stage === "paused") {
    state.stage = "playing";
    paint();
    playbackTimer = setTimeout(() => {
      state.queue.shift();
      pump();
    }, 1800);
    return;
  }
  if (state.stage === "generating") return;
  if (!state.text.trim()) {
    toast("先输入需要朗读的文字。");
    q("#studioText")?.focus();
    return;
  }
  state.queue.push({ text: state.text, source: "手动朗读" });
  pump();
}
function applyVoice(id) {
  const v = state.voices.find((v) => v.id === id);
  stop();
  window.reviewTasks.run("loadVoice", {
    path: v.path || "voice-model-packages/" + v.name + "/",
    name: v.engine,
    onComplete: () => {
      state.active = id;
      B.setVoice(v.name, v.engine);
      B.refresh();
      toast("已应用 " + v.name + " · 仅演示");
    },
  });
}
function pickVoice() {
  dialog(
    "切换声音",
    `<div class="voice-list">${state.voices.map((v) => `<div class="voice-row"><div class="voice-symbol">${ico("engine")}</div><div><h2>${esc(v.name)}</h2><p>${esc(v.engine)}</p></div>${button("应用", "modalApplyVoice", "main", `data-id="${v.id}"`)}</div>`).join("")}</div><p class="compact-hint">更换引擎与参考条件会一起匹配；不复用上个引擎的残留参数。</p>`,
    () =>
      (q("#modalContent").onclick = (e) => {
        const b = e.target.closest('[data-work="modalApplyVoice"]');
        if (b) {
          B.closeModal();
          applyVoice(b.dataset.id);
        }
      }),
  );
}
function importVoice() {
  const origin = B.getState();
  dialog(
    "导入音色包",
    `<form class="ui-form" id="importForm"><label>选择音色包<input id="voiceFile" type="file" accept=".ttsmodel"></label><p class="form-hint">支持 .cosyvoice.ttsmodel 与 .gpt-sovits.ttsmodel。正式导入同时验证内部清单、引擎版本和文件哈希。</p><p class="form-error" id="importError" role="alert"></p><div class="form-actions">${button("取消", "modalCancel")}<button class="ui-button main" type="submit">预览导入过程</button></div></form>`,
    () => {
      q('[data-work="modalCancel"]').onclick = B.closeModal;
      q("#importForm").onsubmit = (e) => {
        e.preventDefault();
        const file = q("#voiceFile").files[0];
        if (!file) {
          q("#importError").textContent = "请先选择一个音色包。";
          return;
        }
        const engine = file.name.endsWith(".cosyvoice.ttsmodel")
          ? "CosyVoice"
          : file.name.endsWith(".gpt-sovits.ttsmodel")
            ? "GPT-SoVITS · v5 Turbo"
            : null;
        if (!engine) {
          q("#importError").textContent =
            "后缀不匹配，请选择带引擎名称的 .ttsmodel 文件。";
          return;
        }
        const name = file.name.replace(
          /\.(cosyvoice|gpt-sovits)\.ttsmodel$/,
          "",
        );
        B.closeModal();
        window.reviewTasks.run("voice", {
          path: file.name,
          onComplete: () => {
            if (
              !state.voices.some((v) => v.name === name && v.engine === engine)
            )
              state.voices.push({
                id: "import-" + ++serial,
                name,
                engine,
                type: "导入示例包",
              });
            if (origin.mode === "setup") {
              B.setVoice(name, engine);
              B.showSetup(origin.step);
            } else B.showDesk("voices");
            toast("导入流程演示完成，未复制真实文件。");
          },
        });
      };
    },
  );
}
function exportVoice() {
  const v = current(),
    cosy = v.engine === "CosyVoice";
  dialog(
    "导出音色包",
    `<form class="ui-form" id="exportForm"><label>音色名称<input id="exportName" value="${esc(v.name)}" required></label><label>包类型<select id="exportType"><option value="轻量包">轻量包 · 复用匹配的基础引擎</option><option value="模型完整包">模型完整包 · 携带对应权重</option>${cosy ? '<option value="训练增量包">训练增量包 · 高级训练结果</option><option value="训练完整包">训练完整包 · 基础模型与训练权重</option>' : ""}</select></label><p class="form-hint">输出名会附带 ${cosy ? ".cosyvoice.ttsmodel" : ".gpt-sovits.ttsmodel"}。包类型影响内容与体积，不主动改变音色。不导出其他引擎或个人外观。</p><div class="form-actions"><button class="ui-button main" type="submit">预览导出过程</button></div></form>`,
    () =>
      (q("#exportForm").onsubmit = (e) => {
        e.preventDefault();
        const suffix = cosy ? ".cosyvoice.ttsmodel" : ".gpt-sovits.ttsmodel",
          path = q("#exportName").value.trim() + suffix,
          type = q("#exportType").value;
        B.closeModal();
        window.reviewTasks.run("export", {
          path,
          detail: type,
          onComplete: () => {
            B.showDesk("voices");
            toast("导出状态演示完成，没有生成实际模型文件。");
          },
        });
      }),
  );
}
function saveScheme() {
  dialog(
    "保存声音方案",
    `<form class="ui-form" id="schemeForm"><label>方案名称<input id="schemeName" placeholder="例如：Fairy · 日常" required maxlength="60"></label><p class="form-hint">${esc(current().name)} · ${state.speed.toFixed(2)} 倍语速 · 音量 ${state.volume}% · 声音细调${state.advanced ? "开启" : "关闭"}。<br>每个方案一个同名文件夹，仅引用当前声音。</p><p class="form-error" id="schemeError" role="alert"></p><div class="form-actions"><button class="ui-button main" type="submit">保存方案 · 演示</button></div></form>`,
    () =>
      (q("#schemeForm").onsubmit = (e) => {
        e.preventDefault();
        const name = q("#schemeName").value.trim();
        if (!name || /[<>:"/\\|?*]/.test(name)) {
          q("#schemeError").textContent =
            "名称不能为空，也不能包含文件夹不支持的字符。";
          return;
        }
        if (state.schemes.some((s) => s.name === name)) {
          q("#schemeError").textContent = "已有同名方案，请换一个名称。";
          return;
        }
        B.closeModal();
        window.reviewTasks.run("save", {
          path: "data/voice-profiles/" + name + "/",
          onComplete: () => {
            state.schemes.push({
              id: "scheme-" + ++serial,
              name,
              voice: state.active,
              speed: state.speed,
              volume: state.volume,
              advanced: state.advanced,
              expression: state.expression || "",
              snapshot: {
                ...current(),
                references: [...(current().references || [])],
              },
            });
            B.showDesk("schemes");
            toast("方案已保存在本次演示中。");
          },
        });
      }),
  );
}
function phrases() {
  dialog(
    "管理常用句",
    `<form class="ui-form" id="phraseForm"><label>名称<input id="phraseName" required placeholder="例如：休息一下"></label><label>内容<textarea id="phraseText" required>${esc(state.text)}</textarea></label><button class="ui-button main" type="submit">添加常用句</button></form><div id="phraseRows">${state.phrases.map((p, i) => `<div class="scheme-row"><div><h2>${esc(p.name)}</h2><p>${esc(p.text)}</p></div>${button("删除", "removePhrase", "danger", `data-id="${i}"`)}</div>`).join("")}</div>`,
    () => {
      q("#phraseForm").onsubmit = (e) => {
        e.preventDefault();
        const name = q("#phraseName").value.trim(),
          text = q("#phraseText").value.trim();
        if (!name || !text) return;
        state.phrases.push({ name, text });
        phrases();
        render();
      };
      q("#phraseRows").onclick = (e) => {
        const b = e.target.closest('[data-work="removePhrase"]');
        if (b) {
          state.phrases.splice(+b.dataset.id, 1);
          phrases();
          render();
        }
      };
    },
  );
}
function connect(name) {
  window.reviewTasks.run(name === "AIRI" ? "airi" : "connection", {
    name,
    onComplete: () => {
      state.connections[name] = "verified";
      B.showDesk("connections");
      toast(name + " 验证通过状态 · 仅演示");
    },
    onFailure: () => {
      state.connections[name] = "failed";
    },
  });
}
function appearanceFile(kind) {
  const accept = kind === "loadingArt" ? "image/*,.gif" : "image/*";
  dialog(
    kind === "background"
      ? "选择背景"
      : kind === "appIcon"
        ? "自定义应用图标"
        : "自定义等待动画",
    `<div class="ui-form"><label>选择图片${kind === "loadingArt" ? "或 GIF" : ""}<input type="file" id="appearanceFile" accept="${accept}"></label><p class="form-hint">仅在本次演示中预览，不会写入程序配置。图标圆角和三处应用将在正式实现中统一处理。</p><div id="appearancePreview"></div></div>`,
    () =>
      (q("#appearanceFile").onchange = (e) => {
        const file = e.target.files[0];
        if (!file) return;
        if (!file.type.startsWith("image/")) {
          notifyModal("请选择有效的图片或 GIF。");
          return;
        }
        if (backgroundUrl) URL.revokeObjectURL(backgroundUrl);
        backgroundUrl = URL.createObjectURL(file);
        const img = document.createElement("img");
        img.src = backgroundUrl;
        img.alt = "自定义素材预览";
        img.style.cssText =
          "max-width:100%;max-height:190px;border-radius:18px;display:block";
        q("#appearancePreview").replaceChildren(img);
        if (kind === "background" && q("#backgroundPreview"))
          q("#backgroundPreview").style.backgroundImage =
            `url("${backgroundUrl}")`;
        notifyModal("已载入本次演示；未上传文件。");
      }),
  );
}
function onAction(e) {
  const b = e.target.closest("[data-work]");
  if (!b) return;
  const a = b.dataset.work,
    id = b.dataset.id;
  switch (a) {
    case "navigate":
      B.showDesk(id);
      break;
    case "service":
      state.service = !state.service;
      if (!state.service) stop();
      render();
      toast(state.service ? "服务开启状态 · 仅演示" : "服务已关闭 · 仅演示");
      break;
    case "play":
      play();
      break;
    case "stop":
      stop();
      toast("当前播放与队列已停止 · 演示");
      break;
    case "replay":
      stop();
      play();
      break;
    case "pickVoice":
      pickVoice();
      break;
    case "applyVoice":
      applyVoice(id);
      break;
    case "phrase":
      state.text = state.phrases[+id].text;
      render();
      q("#studioText").focus();
      break;
    case "phrases":
      phrases();
      break;
    case "clearQueue":
      stop();
      toast("朗读队列已清空 · 演示");
      break;
    case "demoQueue":
      if (!state.auto) {
        toast("先开启自动朗读。");
        break;
      }
      state.service = true;
      state.queue.push(
        { text: "我先检查配置和依赖。", source: "Codex" },
        { text: "检查完成，接下来整理结果。", source: "DeepSeek" },
      );
      if (!["playing", "generating", "paused"].includes(state.stage)) pump();
      render();
      break;
    case "connections":
      B.showDesk("connections");
      break;
    case "importVoice":
      importVoice();
      break;
    case "exportVoice":
      exportVoice();
      break;
    case "saveScheme":
      saveScheme();
      break;
    case "applyScheme": {
      const s = state.schemes.find((s) => s.id === id);
      if (s.snapshot) {
        const i = state.voices.findIndex((v) => v.id === s.voice);
        if (i >= 0) state.voices[i] = { ...s.snapshot };
        else state.voices.push({ ...s.snapshot });
      }
      Object.assign(state, {
        active: s.voice,
        speed: s.speed,
        volume: s.volume,
        advanced: s.advanced,
        expression: s.expression || "",
      });
      B.setVoice(current().name, current().engine);
      render();
      toast("已应用 " + s.name + " · 演示");
      break;
    }
    case "deleteScheme": {
      const s = state.schemes.find((s) => s.id === id);
      dialog(
        "删除声音方案",
        `<p>删除“${esc(s.name)}”的参数方案，音色包仍保留。</p><div class="inline-row">${button("取消", "cancelDelete")}${button("删除方案", "confirmDelete", "danger")}</div>`,
        () => {
          q('[data-work="cancelDelete"]').onclick = B.closeModal;
          q('[data-work="confirmDelete"]').onclick = () => {
            state.schemes = state.schemes.filter((s) => s.id !== id);
            B.closeModal();
            render();
            toast("方案已从演示中删除。");
          };
        },
      );
      break;
    }
    case "connect":
      connect(id);
      break;
    case "toggleAiri":
      state.airi = !state.airi;
      state.connections.AIRI = state.airi ? "pending" : "off";
      B.setAiri(state.airi);
      render();
      break;
    case "auto":
      state.auto = !state.auto;
      render();
      break;
    case "settingTab":
      state.settings = id;
      render();
      break;
    case "trainingTab":
      state.training = id;
      render();
      break;
    case "advanced":
      state.advanced = !state.advanced;
      render();
      break;
    case "voiceSettings":
      B.showDesk("settings");
      state.settings = "sound";
      render();
      break;
    case "voiceDetails":
      voiceDetails(id);
      break;
    case "training":
      B.showDesk("training");
      break;
    case "setup":
      B.showSetup(0);
      break;
    case "environment":
      window.reviewTasks.run("environment");
      break;
    case "engineManager":
      dialog(
        "管理本地引擎",
        `<div class="scheme-row"><div><h2>CosyVoice</h2><p>tts-engines/cosyvoice · 默认引擎</p></div>${button("准备引擎", "prepareEngine", "main")}</div><div class="scheme-row"><div><h2>GPT-SoVITS</h2><p>v5 Turbo / ProPlus · 独立运行资源</p></div>${button("准备引擎", "prepareEngine", "")}</div><p class="compact-hint">可联网准备、导入引擎包或指定已有目录。自建引擎按适配开发说明接入。</p>`,
        () =>
          (q("#modalContent").onclick = (e) => {
            if (e.target.closest('[data-work="prepareEngine"]')) {
              B.closeModal();
              window.reviewTasks.run("engine");
            }
          }),
      );
      break;
    case "color":
      document.documentElement.style.setProperty("--ui-accent", id);
      q(".appearance-swatch.selected")?.classList.remove("selected");
      b.classList.add("selected");
      toast("强调色预览已更新。");
      break;
    case "background":
    case "appIcon":
    case "loadingArt":
      appearanceFile(a);
      break;
    case "sound":
      B.toggleSound();
      render();
      break;
    default:
      info(a);
  }
}
const infoText = {
  packageFolder: [
    "音色包存放位置",
    "软件目录 / voice-model-packages/。导入包和训练结果统一存放，匹配同一份模型，避免重复。审核稿不会打开本机文件管理器。",
  ],
  schemeFolder: [
    "声音方案存放位置",
    "软件目录 / data/voice-profiles/方案名称/。每份方案一个目录。这里只显示位置设计，不访问本机文件。",
  ],
  connectionHelp: [
    "真实验证标准",
    "选择安装目录 → 识别客户端 → 安装连接器 → 随机正文挑战 → 生成与播放回执。桌面凭据不可用时，明确让用户在已登录聊天框发送挑战短句。",
  ],
  extension: [
    "扩展其他客户端",
    "只将已实测的 Codex、DeepSeek Harness 标为正式支持；其他客户端使用连接器模板、事件协议和验收说明二次开发。MCP 语言控制与客户端内 UI 分开维护。",
  ],
  expression: [
    "表达与说话风格",
    "界面按当前引擎能力显示选项。关闭声音细调后绕过相应处理；语速和模型参考条件仍独立生效。",
  ],
  diagnostics: [
    "诊断信息",
    "显示失败步骤、引擎版本、配置摘要及必要日志，连接密钥默认遮盖；分享前预览内容。",
  ],
  manual: [
    "详细操作手册",
    "设计实现后将打开对应引擎的本地手册。当前文案依据仓库的音色训练从零开始、GPT-SoVITS 训练指南与高级 CosyVoice 训练手册整理；支持将操作约定交给 AI 阅读。",
  ],
  trainingTool: [
    "打开训练界面",
    "先检查当前引擎是否提供兼容训练界面，再打开已有实例。若缺少依赖，则显示检查结果和手动修复入口。设计稿不会启动训练程序。",
  ],
};
function voiceDetails(id) {
  const v = state.voices.find((v) => v.id === id);
  dialog(
    "模型与参考声音",
    `<form class="ui-form" id="voiceSettingsForm"><label>本地 TTS<select id="voiceEngine">${["CosyVoice", "GPT-SoVITS · v5 Turbo", "GPT-SoVITS · ProPlus"].map((n) => `<option ${v.engine === n ? "selected" : ""}>${n}</option>`).join("")}</select></label><label>训练音色模型包位置<input id="voiceModelPath" value="${esc(v.path || "voice-model-packages/" + v.name + "/")}" required></label><label>参考音频（可多选）<input id="voiceReference" type="file" accept="audio/*" multiple></label><label>推理参考逐字台词<textarea id="voicePrompt" placeholder="与实际推理参考音频一致">${esc(v.prompt || "")}</textarea></label><p class="form-hint">引擎变更后重新匹配模型与参考条件。只有新选择加载通过后才更新当前声音；失败保留旧配置，并明确报错。</p><button class="ui-button main" type="submit">应用当前声音配置 · 演示</button></form>`,
    () =>
      (q("#voiceSettingsForm").onsubmit = (e) => {
        e.preventDefault();
        const engine = q("#voiceEngine").value,
          path = q("#voiceModelPath").value,
          prompt = q("#voicePrompt").value,
          refs = [...q("#voiceReference").files].map((f) => f.name);
        B.closeModal();
        stop();
        window.reviewTasks.run("loadVoice", {
          name: engine,
          path,
          onComplete: () => {
            v.engine = engine;
            v.path = path;
            v.prompt = prompt;
            v.references = refs;
            state.active = v.id;
            B.setVoice(v.name, v.engine);
            B.showDesk("voices");
            toast("新声音加载通过状态 · 仅演示");
          },
        });
      }),
  );
}
function expression() {
  dialog(
    "表达与说话风格",
    `<form class="ui-form" id="expressionForm"><label>CosyVoice 表达指令<textarea id="expressionText" placeholder="例如：用轻快、温柔的语气说话。">${esc(state.expression || "")}</textarea></label><p class="form-hint">只对支持的引擎生效。关闭声音细调后绕过此指令，台词与表达指令分开处理，不把指令当作正文朗读。</p><button class="ui-button main" type="submit">应用表达设置 · 演示</button></form>`,
    () =>
      (q("#expressionForm").onsubmit = (e) => {
        e.preventDefault();
        state.expression = q("#expressionText").value;
        B.closeModal();
        toast("表达指令已保存在本次演示中。");
      }),
  );
}
let manualRequest = 0;
async function manual(kind) {
  const request = ++manualRequest;
  const file =
    kind === "ai"
      ? "ai-training.md"
      : kind === "advanced"
        ? "cosyvoice-advanced.md"
        : state.training === "cosy"
          ? "voice-training.md"
          : "gpt-sovits.md";
  dialog(
    "训练操作手册",
    `<div class="inline-row"><a class="ui-button" href="manuals/${file}" target="_blank" rel="noreferrer">打开 Markdown 原文</a>${state.training === "cosy" ? button("高级权重训练", "manualAdvanced") : ""}${button("交给 AI 阅读", "manualAi")}</div><div id="manualBody" class="manual-body" role="status">正在载入本地手册…</div>`,
    () =>
      (q("#modalContent").onclick = (e) => {
        const b = e.target.closest("[data-work]");
        if (b?.dataset.work === "manualAdvanced") manual("advanced");
        if (b?.dataset.work === "manualAi") manual("ai");
      }),
  );
  try {
    const r = await fetch("manuals/" + file);
    if (!r.ok) throw Error("manual unavailable");
    const text = await r.text();
    if (request !== manualRequest || !q("#manualBody")) return;
    const body = document.createElement("pre");
    body.className = "manual-text";
    body.textContent = text;
    q("#manualBody").replaceChildren(body);
  } catch {
    if (q("#manualBody"))
      q("#manualBody").textContent =
        "本地手册未找到，请恢复 manuals 目录后重试。";
  }
}
function info(key) {
  if (key === "cleanup") {
    window.reviewTasks.run("cleanup");
    return;
  }
  if (key === "expression") {
    expression();
    return;
  }
  if (key === "manual") {
    manual();
    return;
  }
  if (key === "trainingTool") {
    window.reviewTasks.run("training");
    return;
  }
  const t = infoText[key];
  if (t) dialog(t[0], `<p>${t[1]}</p>`);
}
document.addEventListener(
  "click",
  (e) => {
    if (e.target.closest("#globalStop") && B.getState().mode === "desk") {
      e.preventDefault();
      e.stopImmediatePropagation();
      stop();
      toast("播放和队列已停止 · 演示");
    }
  },
  true,
);
window.addEventListener("pagehide", () => {
  clearTimeout(playbackTimer);
  clearTimeout(toastTimer);
  if (backgroundUrl) URL.revokeObjectURL(backgroundUrl);
});
window.workspaceDesign = { render, importVoice, toast };
window.addEventListener("hashchange", () => {
  B.closeModal();
  const setup = location.hash.match(/^#setup-(\d+)$/),
    desk = location.hash.match(
      /^#desk-(speech|voices|schemes|connections|settings|training)$/,
    );
  if (setup) B.showSetup(+setup[1]);
  else if (desk) B.showDesk(desk[1]);
});
if (B.getState().mode === "desk") B.showDesk(B.getState().deskPage);
