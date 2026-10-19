// Decorative scene construction. No configuration state or business requests.
const svg = (body, cls = "") =>
  `<svg class="scene-drawing ${cls}" viewBox="0 0 600 400" preserveAspectRatio="none" aria-hidden="true">${body}</svg>`;
const scenes = [
  `<div class="acoustic-orbit orbit-back"></div><div class="acoustic-orbit orbit-front"></div><div class="listening-plinth"></div>${svg('<path d="M-20 310C125 180 320 378 630 202"/><path d="M-20 324C125 194 320 392 630 216"/>', "opening-ribbon")}`,
  `<div class="diagnostic-pane"><i></i><i></i><i></i><i></i></div>${svg('<path d="M180 235V300H88V350M280 240V350M385 230V295H510V350"/><circle cx="88" cy="350" r="4"/><circle cx="280" cy="350" r="4"/><circle cx="510" cy="350" r="4"/>', "diagnostic-circuit")}<div class="screen-sheen"></div>`,
  `<div class="mechanism-ring outer-ring"></div><div class="mechanism-ring inner-ring"></div><div class="engine-prism"></div>${svg('<path d="M75 290C155 290 170 100 300 100S430 290 540 290"/>', "engine-arc")}`,
  `<div class="archive-sheet sheet-back"><i></i><i></i><i></i></div><div class="archive-sheet sheet-front"><i></i><i></i><i></i></div><div class="archive-tray"></div>${svg('<path d="M84 32C30 150 470 180 425 312"/><path d="m410 299 15 13 19-15"/>', "archive-route")}`,
  `<div class="assembly-track"><i></i><i></i></div><div class="assembly-cell cell-one"></div><div class="assembly-cell cell-two">${svg('<rect x="225" y="112" width="150" height="176" rx="18"/><rect x="258" y="151" width="84" height="98" rx="9"/><path d="M242 82v30m38-30v30m40-30v30m38-30v30M242 288v30m38-30v30m40-30v30m38-30v30M200 145h25m-25 45h25m-25 45h25m150-90h25m-25 45h25m-25 45h25"/>')}</div><div class="assembly-cell cell-three"></div>`,
  `<div class="recording-capsule"></div><div class="voice-halo"></div>${svg('<path d="M42 200C100 80 128 320 190 200S280 80 340 200S430 320 488 200S550 80 600 200"/><path d="M42 218C100 98 128 338 190 218S280 98 340 218S430 338 488 218S550 98 600 218"/>', "voice-contour")}`,
  `<div class="sound-ripple ripple-one"></div><div class="sound-ripple ripple-two"></div><div class="sound-ripple ripple-three"></div><div class="listening-plinth"></div><div class="plinth-edge"></div>`,
  `${svg('<path class="codex-route" d="M150 0C150 240 218 210 260 210"/><path class="deepseek-route" d="M450 0C450 240 382 210 340 210"/>', "client-convergence")}<div class="convergence-core"><span class="core-bars"><i></i><i></i><i></i><i></i><i></i></span><span>Agent Voice</span></div>`,
  "",
  `<div class="completion-frame"></div><div class="listening-plinth"></div>${svg('<path d="M-20 280C150 150 340 390 630 240"/><path d="M-20 294C150 164 340 404 630 254"/>', "completion-ribbon")}`,
];

export function createMaterialRoom(step) {
  const room = document.createElement("div");
  room.className = "scene-room";
  room.dataset.scene = step;
  room.setAttribute("aria-hidden", "true");
  if (step === 2) return room;
  if (step === 7) {
    room.innerHTML = `<div class="scene-bloom"></div>${scenes[step]}`;
    return room;
  }
  room.innerHTML = `<div class="scene-bloom"></div>${scenes[step]}`;
  return room;
}
