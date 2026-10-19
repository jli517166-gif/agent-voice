const fs = require("fs");
const path = require("path");
const { chromium } = require(
  "C:/Users/25908/AppData/Local/OpenAI/Codex/runtimes/cua_node/78fb63cda1a9e2b2/bin/node_modules/playwright-core",
);
const out = path.join(__dirname, "assets", "onboarding");
fs.mkdirSync(out, { recursive: true });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

(async () => {
  const browser = await chromium.launch({
    executablePath:
      "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
    headless: true,
    args: ["--disable-gpu"],
  });
  const page = await browser.newPage({
    viewport: { width: 1120, height: 780 },
    deviceScaleFactor: 1,
  });
  await page.goto("http://127.0.0.1:51272/#setup-0", {
    waitUntil: "networkidle",
  });
  await page.waitForFunction(() => !document.body.classList.contains("ui-booting"));
  const spec = [];
  for (let step = 0; step < 10; step++) {
    await page.evaluate((n) => window.reviewBridge.showSetup(n, false), step);
    await sleep(2600);
    const data = await page.evaluate(() => {
      const stage = document.querySelector("#screens .screen");
      const area = document.querySelector(".screen-area");
      const base = area.getBoundingClientRect();
      const box = (node) => {
        if (!node) return null;
        const r = node.getBoundingClientRect();
        return {
          x: Math.round(r.left - base.left),
          y: Math.round(r.top - base.top),
          w: Math.round(r.width),
          h: Math.round(r.height),
        };
      };
      const text = (node) => (node?.textContent || "").replace(/\s+/g, " ").trim();
      const buttons = [...stage.querySelectorAll("button,input,select")]
        .filter((n) => n.offsetParent !== null)
        .map((n) => ({
          text: text(n),
          tag: n.tagName,
          action: n.dataset.action || "",
          value: n.dataset.value || "",
          box: box(n),
        }))
        .filter((n) => n.box && n.box.w > 0 && n.box.h > 0);
      return {
        step,
        title: { text: text(stage.querySelector("h1")), box: box(stage.querySelector("h1")) },
        lead: { text: text(stage.querySelector(".lead")), box: box(stage.querySelector(".lead")) },
        eyebrow: { text: text(stage.querySelector(".eyebrow")), box: box(stage.querySelector(".eyebrow")) },
        buttons,
        stage: { w: Math.round(base.width), h: Math.round(base.height) },
      };
    });
    spec.push(data);
    await page.evaluate(() => {
      document.querySelectorAll(".screen-area .top,.screen-area .footer,.review-tag,.review-bar").forEach((n) => (n.style.display = "none"));
      const intro = document.querySelector("#screens .screen .intro-content > div:first-child");
      if (intro) intro.style.visibility = "hidden";
      document.querySelectorAll("#screens .screen h1,#screens .screen .lead,#screens .screen .eyebrow,#screens .screen .small-result,#screens .screen .after-options,#screens .screen .scene-help,#screens .screen .intro-foot,#screens .screen .spec-tags,#screens .screen .client-tags,#screens .screen .dependency-pins,#screens .screen .package-names,#screens .screen .scene-controls").forEach((n) => (n.style.visibility = "hidden"));
      document.querySelectorAll("#screens .screen button").forEach((n) => {
        n.style.background = "transparent";
        n.style.border = "0";
        n.style.boxShadow = "none";
        n.style.color = "transparent";
        n.querySelectorAll("*").forEach((c) => {
          if (c.tagName !== "IMG") c.style.color = "transparent";
        });
      });
    });
    await page.locator(".screen-area").screenshot({ path: path.join(out, `step-${step}.png`) });
    await page.reload({ waitUntil: "networkidle" });
    await page.waitForFunction(() => !document.body.classList.contains("ui-booting"));
  }
  fs.writeFileSync(path.join(out, "layout.json"), JSON.stringify(spec, null, 2));
  console.log(JSON.stringify({ steps: spec.length, stage: spec[0].stage, dir: out }));
  await browser.close();
})();
