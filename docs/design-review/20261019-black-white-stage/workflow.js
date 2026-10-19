import { dealFromCenter, settle } from "./center-deal.js";
import { prepareText } from "./page-compositions.js";
// Finite, cancellable state demonstrations; never installation or connection evidence.
const B = window.reviewBridge,
  q = (s) => document.querySelector(s);
const safe = (s) =>
  String(s).replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
const specs = {
  loadVoice: {
    name: "加载声音",
    title: "切换到新的声音。",
    image: "mic.webp",
    path: true,
    steps: [
      "取消旧任务并卸载旧声音",
      "核对引擎、模型和参考条件",
      "加载新选择并执行短句检查",
    ],
    failure: "新声音未通过加载检查，当前配置没有切换。",
    fix: "核对引擎版本、模型路径与参考台词。明确修复后再重试，不用旧引擎的输出冒充新引擎成功。",
  },
  training: {
    name: "打开训练界面",
    title: "准备你的声音工坊。",
    image: "mic.webp",
    path: true,
    steps: [
      "定位所选引擎的训练入口",
      "检查专用环境与硬件条件",
      "打开兼容的已有训练界面",
    ],
    failure: "当前引擎目录中没有兼容的训练入口。",
    fix: "按照对应引擎的训练手册安装支持训练的版本，推理环境本身不保证含有训练界面。",
  },
  computer: {
    name: "电脑检查",
    title: "检查这台电脑。",
    image: "computer.webp",
    steps: ["检查系统与显卡", "检查驱动与目录权限", "整理兼容性结果"],
    failure: "显卡驱动未满足当前引擎要求。",
    fix: "安装兼容驱动并重启电脑，再重试检查。软件环境包不能代替显卡驱动。",
  },
  engine: {
    name: "准备引擎",
    title: "准备引擎文件。",
    image: "folder.webp",
    path: true,
    steps: ["确认来源与引擎版本", "检查空间并准备资源", "校验清单与模型哈希"],
    failure: "资源校验失败，当前文件不完整。",
    fix: "重新下载对应引擎包，或使用完整离线包。未通过校验的文件不设为可用。",
  },
  environment: {
    name: "补齐环境",
    title: "补齐运行环境。",
    image: "tools.webp",
    path: true,
    steps: [
      "扫描当前依赖与显卡档位",
      "在软件内准备兼容环境",
      "执行导入检查与短句自测",
    ],
    failure: "当前环境包缺少兼容的 GPU 依赖。",
    fix: "选择匹配引擎、Windows 64 位及显卡档位的环境包；严格离线时只报告缺项，不暗中联网。",
  },
  voice: {
    name: "导入音色包",
    title: "让声音归位。",
    image: "mic.webp",
    path: true,
    steps: ["识别包内引擎与版本", "检查哈希及重复资源", "匹配引擎并试运行"],
    failure: "音色包对应的引擎版本尚未准备。",
    fix: "先准备与包内清单匹配的引擎；不要只改文件名或强行套用另一个引擎的配置。",
  },
  preview: {
    name: "声音试听",
    title: "听到，再确认。",
    image: "headphone.webp",
    steps: ["匹配当前模型与参考条件", "生成测试短句", "等待播放完成回执"],
    failure: "引擎没有返回可播放的音频。",
    fix: "先检查模型和参考台词，再以默认语速重试。失败文本保留，不反复无限重试。",
  },
  connection: {
    name: "接入客户端",
    title: "让对话接上声音。",
    image: "chat.webp",
    path: true,
    steps: [
      "识别安装目录与客户端",
      "配置连接器并发起随机挑战",
      "核对正文、来源与播放回执",
    ],
    failure: "停在“发起真实验证会话”，尚未取得有效回复。",
    fix: "确认客户端已登录。若命令行不能使用桌面凭据，在已登录聊天框手动发送验证短句。",
  },
  airi: {
    name: "AIRI 桌宠联动",
    title: "接回你熟悉的 AIRI。",
    image: "airi-11-icon.png",
    path: true,
    steps: [
      "识别原 AIRI 并备份桥接文件",
      "等待桥接握手并发送测试声音",
      "核对桌宠播放完成回执",
    ],
    failure: "桥接握手未完成，未收到桌宠播放回执。",
    fix: "打开原先已登录的 AIRI 并允许本机连接。AIRI 更新可能覆盖桥接，必要时重新接入；不建立另一套 AIRI。",
  },
  save: {
    name: "保存方案",
    title: "留住这一份听感。",
    image: "copy.webp",
    steps: ["整理当前声音与参数", "创建同名方案文件夹", "校验引用与保存结果"],
    failure: "无法写入方案文件夹。",
    fix: "选择可写目录，或修复软件目录写入权限后重试。声音模型本身不重复复制。",
  },
  export: {
    name: "导出音色包",
    title: "把声音带走。",
    image: "folder.webp",
    steps: [
      "确认引擎与所选包类型",
      "收集需要的模型与参考文件",
      "写入清单、引擎后缀和校验信息",
    ],
    failure: "缺少所选完整包必须包含的基础权重。",
    fix: "补齐兼容基础权重，或明确改选轻量包。不能把缺少权重的包标记为完整。",
  },
  cleanup: {
    name: "清理音频",
    title: "给下一段声音留空间。",
    image: "tools.webp",
    steps: [
      "计算可清理的历史音频",
      "排除播放中与队列引用文件",
      "清理选中历史并保留结果记录",
    ],
    failure: "部分历史文件仍被其他程序占用。",
    fix: "保留占用中的文件，关闭相关程序后再重试；音色包与参考录音不在清理范围。",
  },
};
let active = null,
  focusBefore = null,
  timers = [];
let taskAnimations = [],
  motionRevision = 0;
const outcome = document.createElement("select");
outcome.className = "prototype-outcome";
outcome.setAttribute("aria-label", "预览执行结果");
outcome.innerHTML =
  '<option value="success">反馈演示：通过</option><option value="failure">反馈演示：失败</option><option value="manual">反馈演示：需手动</option>';
q(".review-bar").append(outcome);
function schedule(fn, ms) {
  const id = setTimeout(fn, ms);
  timers.push(id);
}
function cancelTimers() {
  timers.forEach(clearTimeout);
  timers = [];
}
function taskMode(enabled) {
  q("#window").classList.toggle("task-active", enabled);
  q("#window")
    .querySelectorAll(":scope > .top,:scope > .screen-area,:scope > .footer")
    .forEach((n) => (n.inert = enabled));
  if (enabled) window.dispatchEvent(new Event("design-suspend"));
}
function close(restore = true) {
  cancelTimers();
  stopMotion();
  q(".task-scene")?.remove();
  taskMode(false);
  q("#window").style.removeProperty("--task-reading-top");
  q("#window")
    .querySelectorAll(".celebration-piece")
    .forEach((n) => n.remove());
  active = null;
  if (restore) B.refresh();
  if (focusBefore?.isConnected) focusBefore.focus();
}
function run(kind, options = {}) {
  close(false);
  B.closeModal();
  focusBefore = document.activeElement;
  active = {
    kind,
    spec: specs[kind],
    options,
    phase: "ready",
    index: 0,
    path:
      options.path ||
      (kind === "engine" && B.getState().source === "在线准备"
        ? "tts-engines/" +
          (B.getState().engine === "CosyVoice" ? "cosyvoice" : "gpt-sovits") +
          "/"
        : kind === "environment"
          ? "tts-engines/" +
            (B.getState().engine === "CosyVoice" ? "cosyvoice" : "gpt-sovits") +
            "/runtime/"
          : ""),
    outcome: options.outcome || outcome.value,
  };
  // Keep these two preparation flows at their originating heading, then keep it
  // fixed through progress/retry instead of recentering when the body changes.
  const sourceHeading = q(".scene-heading"),
    host = q("#window");
  if (["engine", "environment"].includes(kind) && sourceHeading) {
    const top =
      sourceHeading.getBoundingClientRect().top -
      host.getBoundingClientRect().top;
    host.style.setProperty(
      "--task-reading-top",
      Math.max(54, Math.min(170, top)) + "px",
    );
  }
  taskMode(true);
  draw();
}
function pathMarkup() {
  if (!active.spec.path) return "";
  return `<label class="ui-sr" for="taskPath">资源或安装位置</label><div class="task-path"><input id="taskPath" value="${safe(active.path)}" placeholder="选择目录或输入路径（演示）"></div><button class="ui-button subtle" data-task="choose">选择本地${["connection", "airi"].includes(active.kind) ? "目录" : "文件"}</button><input id="taskFile" hidden type="file" ${["connection", "airi"].includes(active.kind) ? "webkitdirectory" : ""}>`;
}
function ready() {
  return `<p>${active.kind === "cleanup" ? "只清理生成的历史音频，保留模型、参考录音和正在使用的文件。" : "先确认资源或安装位置，再开始当前步骤。"}</p>${pathMarkup()}${active.options.detail ? `<p>${safe(active.options.detail)}</p>` : ""}<p class="task-demo-note">这是设计状态演示，不读取文件内容、不安装环境、不发送真实验证。</p>`;
}
function progress() {
  return `<p>正在${active.spec.steps[active.index]}…</p>${active.path ? `<div class="task-path">${safe(active.path)}</div>` : ""}<ol class="task-steps">${active.spec.steps.map((s, i) => `<li class="${i < active.index ? "done" : i === active.index ? "current" : ""}"><b></b>${s}</li>`).join("")}</ol><p class="task-demo-note">流程与时间均为演示，结果不能证明本机接入成功。</p>`;
}
function failed() {
  return `<div class="task-error" role="alert">${active.spec.failure}</div><p>${active.spec.fix}</p>${["connection", "airi"].includes(active.kind) ? '<button class="ui-button subtle" data-task="manual">查看手动验证步骤</button>' : ""}<p class="task-demo-note">失败状态示例，未对本机进行真实诊断。</p>`;
}
function manual() {
  return `<p>在已经登录的${safe(active.options.name || "客户端")}聊天框发送下面的验证短句，再等待正文和播放回执。</p><code class="task-manual-code">VOICE-REVIEW-1024 · 这是一条验证示例。</code><button class="ui-button subtle" data-task="copy">复制验证示例</button><p class="task-demo-note" id="manualCopyResult">当前短句仅用于审核布局，不是有效随机挑战。</p>`;
}
function success() {
  return `<p>当前步骤的完成反馈示例。${["connection", "airi"].includes(active.kind) ? "实际实现仍需核对随机正文、来源与播放回执。" : "实际实现仍需校验真实资源和执行结果。"}</p><ul class="receipt-list">${active.spec.steps.map((s) => `<li>${s} · 演示通过</li>`).join("")}</ul>${active.path ? `<div class="task-path">${safe(active.path)}</div>` : ""}<p class="task-demo-note">未执行真实操作，不是本机验收结果。</p>`;
}
function stopMotion() {
  motionRevision++;
  settle(taskAnimations, true);
  taskAnimations = [];
}
function taskVisual(kind) {
  if (kind === "engine")
    return `<div class="resource-deck" aria-hidden="true"><i class="resource-sheet sheet-one"></i><i class="resource-sheet sheet-two"></i><i class="resource-sheet sheet-three"></i><span class="resource-label">ENGINE</span></div>`;
  if (kind === "environment")
    return `<div class="runtime-circuit" aria-hidden="true"><div class="runtime-wire"></div>${["Python", "GPU", "依赖"].map((label, i) => `<span class="runtime-module" data-module="${i}"><b>${["Py", "GPU", "+"][i]}</b><small>${label}</small></span>`).join("")}</div>`;
  return "";
}
function animateTask(scene, changed) {
  if (B.isReduced()) {
    scene.dataset.dealing = "false";
    return;
  }
  if (changed) {
    const entries = [...scene.querySelectorAll(".title-piece")].map(
      (node, i) => ({ node, role: "title", delay: i * 100, duration: 650 }),
    );
    scene
      .querySelectorAll(".task-body > :not(input[hidden]):not(.ui-sr)")
      .forEach((node, i) =>
        entries.push({
          node,
          role: "task-content",
          delay: 300 + i * 60,
          duration: 540,
        }),
      );
    scene
      .querySelectorAll(
        active.phase === "running"
          ? ".task-footer .inline-row > *"
          : ".task-footer > button,.task-footer .inline-row > *",
      )
      .forEach((node, i) =>
        entries.push({
          node,
          role: "task-control",
          delay: 590 + i * 70,
          duration: 520,
        }),
      );
    entries.push({
      node: scene.querySelector(".task-image"),
      role: "task-illustration",
      delay: 730,
      duration: 650,
      angle: -4,
    });
    scene.dataset.dealing = "true";
    taskAnimations = dealFromCenter(q("#window"), entries);
    const revision = ++motionRevision;
    Promise.allSettled(taskAnimations.map((a) => a.finished)).then(() => {
      if (revision === motionRevision && scene.isConnected)
        scene.dataset.dealing = "false";
    });
  } else if (active.phase === "running") {
    const node = scene.querySelector(".task-steps .current");
    if (node)
      taskAnimations.push(
        node.animate(
          [
            { opacity: 0.25, transform: "translateX(-8px)" },
            { opacity: 1, transform: "translateX(0)" },
          ],
          { duration: 330, easing: "ease-out" },
        ),
      );
  }
}
function draw() {
  if (!active) return;
  const a = active,
    phase = a.phase,
    title =
      phase === "success"
        ? a.spec.name + "已完成 · 演示"
        : phase === "failure"
          ? a.spec.name + "未通过。"
          : phase === "manual"
            ? a.spec.name + "需要你继续。"
            : a.spec.title;
  let scene = q(".task-scene");
  const changed = !scene || scene.dataset.phase !== phase;
  if (changed) stopMotion();
  if (!scene) {
    scene = document.createElement("section");
    scene.className = "task-scene";
    scene.setAttribute("role", "dialog");
    scene.setAttribute("aria-modal", "true");
    scene.setAttribute("aria-labelledby", "taskTitle");
    scene.innerHTML = `<div class="task-main"><div class="task-copy"><div class="task-kicker"></div><h2 id="taskTitle"></h2><div class="task-body" aria-live="polite"></div></div><div class="task-image" data-kind="${a.kind}"><img src="assets/${a.spec.image}" alt="" decoding="async">${taskVisual(a.kind)}<span class="task-complete-mark" aria-hidden="true">✓</span><div class="task-progress-track" aria-hidden="true"><i></i></div></div></div><footer class="task-footer"></footer>`;
    q("#window").append(scene);
    scene.onclick = onTask;
  }
  scene.dataset.phase = phase;
  scene.dataset.kind = a.kind;
  scene.dataset.index = String(a.index);
  scene.setAttribute("aria-busy", String(phase === "running"));
  scene.querySelector(".task-kicker").textContent =
    (a.options.name || a.spec.name) + " · 设计反馈";
  const heading = scene.querySelector("#taskTitle");
  if (heading.getAttribute("aria-label") !== title) {
    heading.replaceChildren();
    delete heading.dataset.motionReady;
    if (
      ["engine", "environment"].includes(a.kind) &&
      ["ready", "running"].includes(phase)
    ) {
      const action = document.createElement("span"),
        subject = document.createElement("span");
      action.textContent = a.kind === "engine" ? "准备" : "补齐";
      subject.textContent = a.kind === "engine" ? "引擎文件。" : "运行环境。";
      heading.append(action, subject);
    } else heading.textContent = title;
    prepareText(scene, "#taskTitle");
  }
  const body = scene.querySelector(".task-body");
  if (phase === "running" && !changed) {
    // Progress advances in place: don't discard cards while their entrance is running.
    body.querySelector("p").textContent = "正在" + a.spec.steps[a.index] + "…";
    body.querySelectorAll(".task-steps li").forEach((node, i) => {
      node.className = i < a.index ? "done" : i === a.index ? "current" : "";
    });
  } else
    body.innerHTML = {
      ready,
      running: progress,
      failure: failed,
      manual,
      success,
    }[phase]();
  scene.querySelector(".task-image").dataset.running = String(
    phase === "running",
  );
  if (changed || phase !== "running")
    scene.querySelector(".task-footer").innerHTML =
      `<button class="ui-button subtle" data-task="cancel">${phase === "running" ? "取消执行" : "返回"}</button><div class="inline-row">${phase === "ready" ? `<button class="ui-button" data-task="example">使用示例${a.spec.path ? "位置" : ""}</button><button class="ui-button main" data-task="start">开始${a.spec.name} · 演示</button>` : phase === "running" ? '<span class="compact-hint">执行中，可随时取消</span>' : phase === "failure" ? '<button class="ui-button main" data-task="retry">预览重试通过</button>' : phase === "manual" ? '<button class="ui-button main" data-task="manualComplete">预览回执通过</button>' : '<button class="ui-button main" data-task="complete">完成并返回</button>'}</div>`;
  const input = q("#taskPath");
  if (input)
    input.oninput = () => {
      active.path = input.value;
    };
  const file = q("#taskFile");
  if (file)
    file.onchange = () => {
      const f = file.files[0];
      if (f) {
        active.path = f.webkitRelativePath
          ? f.webkitRelativePath.split("/")[0] + "/"
          : f.name;
        draw();
      }
    };
  if (phase !== "running")
    scene
      .querySelector("input:not([hidden]),button")
      ?.focus({ preventScroll: true });
  animateTask(scene, changed);
}
function start(force) {
  cancelTimers();
  stopMotion();
  active.outcome = force || active.outcome;
  active.phase = "running";
  active.index = 0;
  draw();
  schedule(() => {
    if (!active) return;
    active.index = 1;
    draw();
  }, 1100);
  schedule(() => {
    if (!active) return;
    active.index = 2;
    draw();
  }, 2200);
  schedule(() => {
    if (!active) return;
    active.phase =
      active.outcome === "failure"
        ? "failure"
        : active.outcome === "manual" &&
            ["connection", "airi"].includes(active.kind)
          ? "manual"
          : "success";
    draw();
    if (active.phase === "failure") active.options.onFailure?.();
    if (active.phase === "success") {
      B.cue(9);
      if (["connection", "airi"].includes(active.kind)) celebrate();
    }
  }, 3600);
}
function celebrate() {
  if (B.isReduced()) return;
  const win = q("#window");
  for (let i = 0; i < 48; i++) {
    const p = document.createElement("i");
    p.className = "celebration-piece";
    p.style.left = ((i * 37) % 100) + "%";
    p.style.background = i % 3 ? "#f3f5f8" : "#b7a1ff";
    win.append(p);
    const a = p.animate(
      [
        { opacity: 0, transform: "translateY(-10px) rotate(0)" },
        { opacity: 1, offset: 0.1 },
        {
          opacity: 0,
          transform: `translate(${(i % 2 ? 1 : -1) * (20 + (i % 7) * 10)}px,${win.clientHeight}px) rotate(${i * 23}deg)`,
        },
      ],
      {
        duration: 1000 + (i % 7) * 75,
        delay: (i % 4) * 55,
        easing: "cubic-bezier(.15,.6,.3,1)",
      },
    );
    a.onfinish = () => p.remove();
  }
}
function onTask(e) {
  const b = e.target.closest("[data-task]");
  if (!b || !active) return;
  switch (b.dataset.task) {
    case "cancel":
      close();
      break;
    case "example":
      active.path =
        active.path ||
        (["connection", "airi"].includes(active.kind)
          ? "用户选择的安装目录/"
          : "用户选择的资源包");
      draw();
      break;
    case "choose":
      q("#taskFile").click();
      break;
    case "start":
      if (active.spec.path && !active.path.trim()) {
        active.path = "示例资源位置";
      }
      start();
      break;
    case "retry":
      start("success");
      break;
    case "manual":
      active.phase = "manual";
      draw();
      break;
    case "manualComplete":
      start("success");
      break;
    case "copy":
      navigator.clipboard
        .writeText("VOICE-REVIEW-1024 · 这是一条验证示例。")
        .then(() => {
          if (q("#manualCopyResult"))
            q("#manualCopyResult").textContent = "已复制示例短句。";
        })
        .catch(() => {
          if (q("#manualCopyResult"))
            q("#manualCopyResult").textContent =
              "无法访问剪贴板，请手动复制上方短句。";
        });
      break;
    case "complete": {
      const cb = active.options.onComplete;
      close(false);
      if (cb) cb();
      else B.refresh();
      break;
    }
  }
}
function reference() {
  B.openModal(
    "参考声音",
    `<div class="ui-form"><label>参考音频（可多选）<input type="file" id="referenceFiles" accept="audio/*" multiple></label><label>参考录音逐字台词<textarea id="referenceWords" placeholder="填写与选定推理参考一致的台词"></textarea></label><p class="form-hint">可以整理多段素材；推理时按引擎支持方式选择实际参考，不把全部录音盲目拼在一起。需要台词的克隆方式必须与参考音频一致。</p><div id="referenceNames" class="form-hint"></div><p class="form-error" id="referenceError" role="alert"></p><div class="form-actions"><button class="ui-button main" id="referenceUse">应用参考状态 · 演示</button></div></div>`,
    () => {
      q("#referenceFiles").onchange = (e) =>
        (q("#referenceNames").textContent = [...e.target.files]
          .map((f) => f.name)
          .join("、"));
      q("#referenceUse").onclick = () => {
        if (
          !q("#referenceFiles").files.length ||
          !q("#referenceWords").value.trim()
        ) {
          q("#referenceError").textContent =
            "请选择参考音频，并填写对应的逐字台词。";
          return;
        }
        B.setVoice("自定义参考声音", B.getState().engine);
        B.closeModal();
        B.refresh();
      };
    },
  );
}
function paths() {
  B.openModal(
    "指定已有引擎",
    `<form class="ui-form" id="pathForm"><label>引擎目录<input id="enginePath" placeholder="例如：软件目录/tts-engines/cosyvoice" required></label><label>独立 Python 运行环境<input id="pythonPath" placeholder="引擎专用 python.exe" required></label><p class="form-hint">路径需要进一步执行兼容性与短句检查；文件存在不表示环境可用。</p><button class="ui-button main" type="submit">预览路径验证</button></form>`,
    () =>
      (q("#pathForm").onsubmit = (e) => {
        e.preventDefault();
        const path = q("#enginePath").value;
        B.closeModal();
        run("engine", { path });
      }),
  );
}
document.addEventListener(
  "click",
  (e) => {
    if (B.getState().mode !== "setup" || active) return;
    const b = e.target.closest("[data-action],#next");
    if (!b) return;
    const a = b.dataset.action,
      step = B.getState().step;
    let action = null;
    if (b.id === "next" && [3, 4].includes(step))
      action = () =>
        run(step === 3 ? "engine" : "environment", {
          onComplete: () => B.showSetup(step + 1),
        });
    if (a === "computer") action = () => run("computer");
    if (a === "preview") action = () => run("preview");
    if (a === "connectionFlow")
      action = () =>
        run("connection", {
          name: [...B.getState().clients].join("、") || "Codex",
        });
    if (a === "airiFlow") action = () => run("airi", { name: "AIRI" });
    if (a === "importVoice")
      action = () => window.workspaceDesign.importVoice();
    if (a === "reference") action = reference;
    if (a === "paths") action = paths;
    if (a === "source" && b.dataset.value === "指定已有目录")
      action = () => {
        B.setSource("指定已有目录");
        paths();
      };
    if (action) {
      e.preventDefault();
      e.stopImmediatePropagation();
      action();
    }
  },
  true,
);
document.addEventListener(
  "keydown",
  (e) => {
    if (!active) return;
    if (e.key === "Escape") {
      e.preventDefault();
      e.stopImmediatePropagation();
      if (q(".task-scene")?.dataset.dealing === "true") {
        settle(taskAnimations);
        q(".task-scene").dataset.dealing = "false";
      } else close();
      return;
    }
    if (e.key === "Tab") {
      const f = [
        ...q(".task-scene").querySelectorAll(
          "button:not(:disabled),input:not([hidden]),a,select,textarea",
        ),
      ];
      if (!f.length) return;
      const first = f[0],
        last = f.at(-1);
      if (!q(".task-scene").contains(document.activeElement)) {
        e.preventDefault();
        first.focus();
        return;
      }
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first.focus();
      }
    }
  },
  true,
);
window.addEventListener("design-dispose", () => close(false));
window.addEventListener("pagehide", () => close(false));
window.reviewTasks = { run, close, isActive: () => !!active };
