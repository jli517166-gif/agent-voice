// Show the first configuration only after composition and critical local images are ready.
import "./art-direction.js";
import "./workflow.js";
import "./workspace.js";
const win = document.querySelector("#window");
let deadline;
const decoded = [...win.querySelectorAll("img")].map((img) =>
  img.decode?.().catch(() => {}),
);
Promise.race([
  Promise.all([...decoded, document.fonts.ready]),
  new Promise((r) => (deadline = setTimeout(r, 300))),
]).then(() => {
  clearTimeout(deadline);
  requestAnimationFrame(() => {
    document.body.classList.remove("ui-booting");
    win.setAttribute("aria-busy", "false");
    window.dispatchEvent(new Event("ui-ready"));
    warmNext();
  });
});
const nextAssets = [
  ["headphone", "music"],
  ["computer", "setting"],
  ["setting", "mic", "headphone"],
  ["folder", "copy"],
  ["tools", "puzzle"],
  ["mic", "music"],
  ["headphone", "play"],
  ["chat", "link"],
  ["airi-11-icon.png"],
  ["headphone", "music"],
];
const cache = new Map();
let idle = 0;
const defer = window.requestIdleCallback || ((fn) => setTimeout(fn, 200));
const cancel = window.cancelIdleCallback || clearTimeout;
function warmNext() {
  cancel(idle);
  idle = defer(
    () => {
      if (document.hidden || win.classList.contains("workspace")) return;
      const step = +win.dataset.referenceStep;
      for (const name of nextAssets[Math.min(step + 1, 9)] || []) {
        if (cache.has(name)) continue;
        const img = new Image();
        img.decoding = "async";
        img.fetchPriority = "low";
        img.src = "assets/" + (name.includes(".") ? name : name + ".webp");
        img.decode?.().catch(() => {});
        cache.set(name, img);
        if (cache.size > 4) cache.delete(cache.keys().next().value);
      }
    },
    { timeout: 1000 },
  );
}
window.addEventListener("design-route", () => {
  if (!document.body.classList.contains("ui-booting")) warmNext();
});
window.addEventListener("pagehide", () => {
  cancel(idle);
  clearTimeout(deadline);
});
