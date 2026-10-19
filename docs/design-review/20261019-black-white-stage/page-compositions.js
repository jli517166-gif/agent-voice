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
  const actions = [
    "开始配置",
    "选择引擎",
    "准备引擎文件",
    "准备引擎",
    "补齐环境",
    "进入试听",
    "连接客户端",
    "接入并验证",
    "继续",
    "进入工作台",
  ];
  document.querySelector("#next span").textContent = actions[step];
  if (step === 8)
    document.querySelector("#next span").textContent =
      screen.querySelector('[role="switch"]')?.getAttribute("aria-checked") ===
      "true"
        ? "连接并验证 AIRI"
        : "继续，暂不联动";
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
  const reading = document.createElement("div");
  reading.className = "scene-copy";
  reading.append(heading, controls);
  grid.replaceChildren(reading, host);
  grid.classList.add("scene-grid");
  const titles = {
    0: '声音，<br><span class="focus-word">现在开始。</span>',
    1: '检查<span class="focus-word">这台电脑。</span>',
    2: '选择<span class="focus-word">语音引擎。</span>',
    3: '准备<span class="focus-word">引擎文件。</span>',
    4: '补齐<span class="focus-word">运行环境。</span>',
    5: '选择<span class="focus-word">你的音色。</span>',
    6: '播放<span class="focus-word">一段试听。</span>',
    7: '连接<span class="focus-word">对话客户端。</span>',
    9: '回顾<span class="focus-word">本次配置。</span>',
  };
  if (titles[step]) heading.querySelector("h1").innerHTML = titles[step];
  const leads = {
    1: "先检查系统、显卡和存储，确认这台电脑能运行所选引擎。",
    2: "选一种生成声音的方式，之后可以随时更换。",
    3: "下载引擎文件，或使用你已经准备好的引擎包。",
    4: "检查缺少的依赖，选择在线补齐或导入离线环境包。",
    5: "导入训练好的音色包，或用自己的参考录音配置声音。",
    6: "播放一段测试内容，确认声音和输出是否符合预期。",
    7: "选择要自动朗读的客户端，再完成连接与播放验证。",
    9: "确认本次选择。跳过的项目，可以进入软件后继续配置。",
  };
  if (leads[step]) heading.querySelector(".lead").textContent = leads[step];
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
    controls.append(help);
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
