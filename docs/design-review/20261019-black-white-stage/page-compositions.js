// Scene composition only. Existing controls, delegated actions and task states stay intact.
// AIRI is deliberately excluded: its approved DOM, geometry and animation are unchanged.
const names = [
  "opening",
  "diagnostic",
  "engines",
  "resources",
  "assembly",
  "voice",
  "listening",
  "conversation",
  null,
  "ready",
];
export function composePage(screen, step, host) {
  const name = names[step];
  if (!name) return;
  const grid = screen.querySelector(".step-grid,.intro-content");
  document.querySelector("#window").dataset.composition = name;
  if (grid.classList.contains("scene-grid")) return;
  const copy = [...grid.children].find((n) => n !== host);
  const heading = document.createElement("header");
  heading.className = "scene-heading";
  const controls = document.createElement("div");
  controls.className = "scene-controls";
  [...copy.children].forEach((n) =>
    (n.matches(".eyebrow,h1,.lead") ? heading : controls).append(n),
  );
  grid.replaceChildren(heading, host, controls);
  grid.classList.add("scene-grid");
  const titles = {
    1: "先了解<br>这台电脑。",
    4: "准备好<br>运行环境。",
    5: "选择<br>你的声音。",
    7: "连接你的对话。",
  };
  if (titles[step]) heading.querySelector("h1").innerHTML = titles[step];
  if (step === 2 || step === 7) {
    controls.querySelectorAll(".option").forEach((button, i) => {
      const pane = document.createElement("span");
      pane.className = "choice-image-pane";
      pane.setAttribute("aria-hidden", "true");
      const object = (
        step === 2 ? ["setting", "mic", "headphone"] : ["chat", "link"]
      )[i];
      pane.innerHTML = `<span class="choice-glass"></span><img src="assets/${object}.webp" alt="" draggable="false">`;
      button.prepend(pane);
    });
  }
  if (step === 7) {
    controls
      .querySelector(".option-list")
      .classList.add("conversation-choices");
    const help = controls.querySelector(".after-options");
    help.classList.add("scene-help");
    grid.append(help);
  }
  if (step === 9) {
    const label = document.createElement("p");
    label.className = "receipt-heading";
    label.textContent = "本次选择";
    controls.prepend(label);
    controls.querySelectorAll(".summary-line").forEach((line, i) => {
      const field = document.createElement("span");
      field.className = "summary-label";
      field.textContent = ["TTS 引擎", "音色", "自动朗读"][i];
      line.prepend(field);
    });
  }
}

// All surfaces use the same porcelain and lilac light; only their silhouette changes.
export function compositionField(step, rect, dx = 0, dy = 0) {
  const { x, y, width: w, height: h } = rect;
  const a = x + dx,
    b = y + dy;
  if ([0, 1, 2, 6, 7].includes(step)) {
    const cx = a + w / 2,
      cy = b + h / 2,
      rx = w / 2,
      ry = h / 2;
    return `path("M ${cx - rx} ${cy} A ${rx} ${ry} 0 1 0 ${cx + rx} ${cy} A ${rx} ${ry} 0 1 0 ${cx - rx} ${cy} Z")`;
  }
  if (step === 3)
    return `polygon(${a + w * 0.12}px ${b}px,${a + w}px ${b}px,${a + w * 0.88}px ${b + h}px,${a}px ${b + h}px)`;
  const r = Math.min(step === 5 ? w / 2 : 32, h / 2);
  return `path("M ${a + r} ${b} H ${a + w - r} Q ${a + w} ${b} ${a + w} ${b + r} V ${b + h - r} Q ${a + w} ${b + h} ${a + w - r} ${b + h} H ${a + r} Q ${a} ${b + h} ${a} ${b + h - r} V ${b + r} Q ${a} ${b} ${a + r} ${b} Z")`;
}
