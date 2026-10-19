// Opt-in, DOM-readable diagnostics for the design prototype. No service calls.
if (new URLSearchParams(location.search).get("diagnostics") === "1") {
  const win = document.querySelector("#window");
  const panel = document.createElement("details");
  panel.id = "renderDiagnostics";
  panel.style.cssText =
    "position:fixed;right:12px;top:8px;z-index:50;max-width:520px;max-height:45vh;overflow:auto;background:#14121fec;color:#ddd2ed;padding:8px 12px;font:11px monospace;border:1px solid #89739a66;border-radius:8px";
  panel.innerHTML =
    '<summary>渲染诊断</summary><pre id="renderMetrics" style="white-space:pre-wrap"></pre>';
  document.body.append(panel);
  const state = {
    maxScreens: 0,
    overlapSamples: 0,
    taskMounts: 0,
    longTasks: [],
    routeMs: [],
    frameMs: [],
    initialReadyMs: null,
  };
  const seenTasks = new WeakSet();
  let pendingRoute = null,
    raf = 0,
    until = 0,
    last = 0;
  const write = () => {
    document.querySelector("#renderMetrics").textContent = JSON.stringify(
      {
        ...state,
        screens: win.querySelectorAll("#screens .screen").length,
        scenes: win.querySelectorAll(".art-stage").length,
        dialogs: win.querySelectorAll(".task-scene").length,
        frameMs: state.frameMs.slice(-180),
      },
      null,
      2,
    );
  };
  function observe() {
    const count = win.querySelectorAll("#screens .screen").length;
    state.maxScreens = Math.max(state.maxScreens, count);
    if (count > 1) state.overlapSamples++;
    win.querySelectorAll(".task-scene").forEach((n) => {
      if (!seenTasks.has(n)) {
        seenTasks.add(n);
        state.taskMounts++;
      }
    });
    write();
  }
  new MutationObserver(observe).observe(win, {
    subtree: true,
    childList: true,
  });
  try {
    new PerformanceObserver((list) => {
      for (const e of list.getEntries())
        state.longTasks.push(Math.round(e.duration * 100) / 100);
      write();
    }).observe({ type: "longtask", buffered: true });
  } catch {}
  function frame(t) {
    if (last && t - last < 1000)
      state.frameMs.push(Math.round((t - last) * 100) / 100);
    last = t;
    if (t < until && !document.hidden) raf = requestAnimationFrame(frame);
    else {
      raf = 0;
      last = 0;
      write();
    }
  }
  function sample() {
    until = performance.now() + 700;
    if (!raf) raf = requestAnimationFrame(frame);
  }
  document.addEventListener(
    "click",
    (e) => {
      if (e.target.closest("#next,#back,#showSetup,#showDesk,#replay"))
        pendingRoute = performance.now();
      sample();
    },
    true,
  );
  document.addEventListener(
    "change",
    (e) => {
      if (e.target.id === "jump") pendingRoute = performance.now();
      sample();
    },
    true,
  );
  window.addEventListener("design-route", () => {
    if (pendingRoute !== null) {
      state.routeMs.push(
        Math.round((performance.now() - pendingRoute) * 100) / 100,
      );
      pendingRoute = null;
    }
    observe();
    sample();
  });
  window.addEventListener("ui-ready", () => {
    state.initialReadyMs = Math.round(performance.now() * 100) / 100;
    write();
  });
  document.addEventListener("visibilitychange", () => {
    if (document.hidden) {
      cancelAnimationFrame(raf);
      raf = 0;
      last = 0;
    }
  });
  observe();
  sample();
}
