// Agent Voice design review, independent from the installed program.
const direction = [
  { main: "headphone", small: "music", name: "声音入场" },
  { main: "computer", small: "setting", name: "电脑与运行条件" },
  { main: "setting", small: "mic", name: "引擎驱动声音" },
  { main: "folder", small: "copy", name: "资源进入本机" },
  { main: "tools", small: "puzzle", name: "环境装配" },
  { main: "mic", small: "music", name: "选择属于你的声音" },
  { main: "headphone", small: "play", name: "试听与播放" },
  { main: "chat", small: "link", name: "对话交接" },
  { main: "airi-11-icon.png", small: null, name: "AIRI 原版图标" },
  { main: "puzzle", small: "music", name: "配置组合" },
];
let current = null;
const image = (name, cls) =>
  `<img class="${cls}" src="assets/${name.includes(".") ? name : name + ".webp"}" alt="" draggable="false">`;
const ticks = () =>
  Array.from(
    { length: 19 },
    (_, i) =>
      `<i style="--i:${i};--h:${8 + Math.sin(((i + 1) / 20) * Math.PI) * Math.abs(Math.sin(i * 0.72)) * 42}px"></i>`,
  ).join("");
function visualMarkup(step) {
  const p = direction[step];
  let extra = "";
  if (step === 0) extra = '<div class="intro-staff">' + ticks() + "</div>";
  if (step === 1)
    extra =
      '<div class="diagnostic-scan"></div><div class="spec-tags"><span>硬件</span><span>驱动</span><span>存储</span></div>';
  if (step === 2)
    extra =
      '<div class="engine-route"><span>文字</span><i></i><span>声音</span></div>';
  if (step === 3)
    extra = '<div class="resource-strips"><i></i><i></i><i></i></div>';
  if (step === 4)
    extra =
      '<div class="dependency-pins"><span>Python</span><span>GPU</span><span>依赖库</span></div>';
  if (step === 5) extra = '<div class="voice-signature">' + ticks() + "</div>";
  if (step === 6) extra = '<div class="listening-wave">' + ticks() + "</div>";
  if (step === 7)
    extra =
      '<div class="client-tags"><span data-client="codex">Codex</span><span data-client="deepseek">DeepSeek</span></div><div class="conversation-path"><i></i></div>';
  if (step === 8)
    extra =
      '<div class="airi-sound">' +
      ticks() +
      '</div><div class="airi-sound echo">' +
      ticks() +
      '</div><div class="airi-link"><svg viewBox="0 0 260 80"><path d="M0 65C70 80 125-27 260 47"/></svg></div>';
  if (step === 9)
    extra =
      '<div class="package-names"><span>软件</span><span>引擎</span><span>音色</span></div>';
  return (
    (step === 8
      ? '<div class="airi-glass">' + image(p.main, "hero-object") + "</div>"
      : image(p.main, "hero-object")) +
    (p.small ? image(p.small, "support-object") : "") +
    extra
  );
}
function teardown() {
  current?.();
  current = null;
  const win = document.querySelector("#window");
  win.classList.remove("reference-setup");
  delete win.dataset.referenceStep;
}
function mount() {
  teardown();
  const screen = [...document.querySelectorAll("#screens .screen")].findLast(
    (n) => n.getAttribute("aria-hidden") !== "true",
  );
  if (
    !screen ||
    document.querySelector("#window").classList.contains("workspace")
  )
    return;
  const step = +(screen.dataset.step || 0);
  screen.dataset.direction = step;
  const win = document.querySelector("#window");
  win.classList.add("reference-setup");
  win.dataset.referenceStep = step;
  if (step === 8)
    screen.querySelector("h1").innerHTML =
      '让桌宠<span class="reference-accent">开口。</span>';
  let host = screen.querySelector(".boot-art,.step-symbol");
  if (!host) {
    const grid = screen.querySelector(".step-grid.no-art");
    if (grid) {
      grid.querySelector(".sound-bars")?.remove();
      const copy = document.createElement("div");
      while (grid.firstChild) copy.append(grid.firstChild);
      grid.replaceChildren(copy);
      grid.classList.remove("no-art");
      host = document.createElement("div");
      host.className = "step-symbol";
      grid.append(host);
    }
  }
  if (!host) return;
  host.replaceChildren();
  host.classList.add("art-host");
  host.removeAttribute("aria-hidden");
  const floor = document.createElement("div");
  floor.className = "art-field";
  floor.setAttribute("aria-hidden", "true");
  win.prepend(floor);
  const ribbons = document.createElement("div");
  ribbons.className = "reference-ribbons";
  ribbons.setAttribute("aria-hidden", "true");
  ribbons.innerHTML =
    '<div class="floor-light"></div><svg viewBox="0 0 1280 800" preserveAspectRatio="none"><defs><linearGradient id="referenceRibbon"><stop stop-color="#fff" stop-opacity="0"/><stop offset=".55" stop-color="#fff" stop-opacity=".3"/><stop offset=".8" stop-color="#fff" stop-opacity=".67"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></linearGradient></defs><path d="M80 770C420 550 850 615 1280 420L1280 448C865 670 430 600 80 770" fill="url(#referenceRibbon)"/><path d="M270 765C560 625 930 490 1280 515L1280 524C930 505 570 650 270 765" fill="url(#referenceRibbon)"/><path d="M320 635C650 620 945 540 1280 448" stroke="url(#referenceRibbon)" stroke-width="2"/><path d="M260 774C660 710 935 615 1230 556" stroke="url(#referenceRibbon)" stroke-width="1"/></svg>';
  win.append(ribbons);
  const art = document.createElement("figure");
  art.className = "art-stage";
  art.dataset.scene = step;
  art.setAttribute("aria-label", direction[step].name + "（设计示意）");
  art.innerHTML = visualMarkup(step);
  win.append(art);
  function fitField() {
    const s = win.getBoundingClientRect(),
      r = host.getBoundingClientRect(),
      w = s.width,
      h = s.height,
      shift = screen.getBoundingClientRect().left - s.left;
    const mobile = innerWidth <= 780;
    art.style.inset = "auto";
    art.style.left =
      r.left - s.left - shift + r.width * (mobile ? 0.12 : -0.12) + "px";
    art.style.top = r.top - s.top + r.height * (mobile ? 0 : -0.09) + "px";
    art.style.width = r.width * (mobile ? 0.76 : 1.24) + "px";
    art.style.height = r.height * (mobile ? 1 : 1.18) + "px";
    if (mobile) {
      art.style.clipPath = `inset(0px -${r.width * 0.12}px 0px -${r.width * 0.12}px round 27px)`;
      return;
    }
    const a = art.getBoundingClientRect();
    const shape = (x, y) =>
      `path("M ${x + w * 0.59} ${y} H ${x + w} V ${y + h} H ${x + w * 0.33} C ${x + w * 0.46} ${y + h * 0.79}, ${x + w * 0.43} ${y + h * 0.41}, ${x + w * 0.59} ${y} Z")`;
    floor.style.clipPath = shape(0, 0);
    art.style.clipPath = shape(s.left - a.left, s.top - a.top);
  }
  const sizing = new ResizeObserver(fitField);
  sizing.observe(win);
  fitField();
  screen.getAnimations().forEach((a) =>
    a.finished
      .then(() => {
        if (art.isConnected) fitField();
      })
      .catch(() => {}),
  );
  if (step === 7) {
    const on = [...screen.querySelectorAll('[role="checkbox"]')].map(
      (n) => n.getAttribute("aria-checked") === "true",
    );
    art.dataset.codex = on[0] ? "on" : "off";
    art.dataset.deepseek = on[1] ? "on" : "off";
  }
  if (step === 8) {
    art.dataset.airi =
      screen.querySelector('[role="switch"]')?.getAttribute("aria-checked") ===
      "true"
        ? "on"
        : "off";
  }
  let timer = 0,
    animations = [];
  function freeze() {
    clearTimeout(timer);
    animations.forEach((a) => {
      try {
        a.finish();
      } catch {}
    });
    art.dataset.playing = "false";
  }
  function play() {
    freeze();
    art.classList.remove("art-reveal");
    floor.classList.remove("field-reveal");
    void art.getBoundingClientRect();
    const reduced =
      document.body.classList.contains("reduced") ||
      matchMedia("(prefers-reduced-motion:reduce)").matches;
    art.classList.toggle("still", reduced);
    art.classList.add("art-reveal");
    floor.classList.add("field-reveal");
    animations = [
      ...art.getAnimations({ subtree: true }),
      ...floor.getAnimations(),
    ];
    if (reduced) {
      freeze();
      return;
    }
    const slow = new URLSearchParams(location.search).get("slow") === "1";
    if (slow) animations.forEach((a) => (a.playbackRate = 0.5));
    art.dataset.playing = "true";
    timer = setTimeout(freeze, slow ? 3200 : 1600);
  }
  const onAction = (e) => {
    const a = e.target.closest("[data-action]")?.dataset.action;
    if ((step === 1 && a === "computer") || (step === 6 && a === "preview"))
      play();
  };
  screen.addEventListener("click", onAction);
  play();
  const cleanup = () => {
    freeze();
    sizing.disconnect();
    animations.forEach((a) => a.cancel());
    screen.removeEventListener("click", onAction);
    floor.remove();
    ribbons.remove();
    art.remove();
  };
  cleanup.freeze = freeze;
  current = cleanup;
}
window.addEventListener("design-route", mount);
window.addEventListener("design-dispose", teardown);
window.addEventListener("pagehide", teardown);
document.addEventListener("keydown", (e) => {
  if (e.key === "Escape") current?.freeze?.();
});
document.addEventListener("visibilitychange", () => {
  if (document.hidden) current?.freeze?.();
  else mount();
});
function modal(title, content) {
  document.querySelector("#modalTitle").textContent = title;
  document.querySelector("#modalContent").innerHTML = content;
  document.querySelector("#modal").hidden = false;
  document.querySelector("#closeModal").focus();
}
document.querySelector("#references").onclick = () =>
  modal(
    "素材与设计说明",
    `<p>按用户重新指定的 AIRI 草图继续：黑白曲面分区、玻璃质感、柔光声浪、大字号宋体及胶囊按钮。交互流程保留。</p><p>AIRI 使用本机原版图标；其他页面继续使用 <a href="https://3dicons.co/collection/b68cf8-v1" target="_blank" rel="noreferrer">3dicons V1</a> 的 CC0 素材，各页主体动作仍独立。</p><p>全部检测、接入和生成结果为设计演示，不能作为本机验收。</p><p><a href="设计与素材说明.md" target="_blank">设计、来源和检查记录</a></p>`,
  );
document.querySelector("#airiConcept").onclick = () =>
  modal(
    "本轮视觉基准",
    '<p>按你指定的这张草图继续完善实际页面，保留构图、材质、宋体大字和胶囊按钮。</p><img src="assets/AIRI-构图草图.png" alt="用户选定的 AIRI 设计基准" style="width:100%;height:auto;display:block"><p><a href="assets/AIRI-构图草图.png" target="_blank">打开大图</a></p>',
  );
const brand = document.querySelector(".top .brand .mini");
brand.setAttribute("viewBox", "0 0 40 40");
brand.innerHTML =
  '<path d="M4 18v4M10 12v16M16 6v28M22 10v20M28 15v10M34 18v4"/>';
const reviewBar = document.querySelector(".review-bar");
reviewBar.append(
  document.querySelector("#soundToggle"),
  document.querySelector("#motionToggle"),
);
document.querySelector(".footer-left").append(document.querySelector("#defer"));
for (const [selector, path] of [
  ["#next .ico", "m9 4 8 8-8 8"],
  ["#back .ico", "m15 4-8 8 8 8"],
]) {
  const svg = document.querySelector(selector);
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.innerHTML = `<path d="${path}"/>`;
}
mount();
