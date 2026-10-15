/* DeepSeek Harness browser half: a real composer button for Agent Voice. */
window.__ModuleLoader__.load({
  id: "dsh-agent-voice-public",
  factory: (require) => {
    var module = { exports: {} };
    var exports = module.exports;
    Object.defineProperty(exports, Symbol.toStringTag, { value: "Module" });
    const React = require("react");
    const h = React.createElement;

    const css = [
      ".avc-root{position:relative;display:inline-flex;align-items:center;flex:none}",
      ".avc-trigger{height:30px;padding:0 10px;display:inline-flex;align-items:center;gap:6px;border:1px solid rgba(22,199,217,.42);border-radius:9px;background:rgba(22,199,217,.10);color:var(--dsw-alias-label-primary);cursor:pointer;font-size:12px;font-weight:600}",
      ".avc-trigger:hover,.avc-trigger[aria-expanded=true]{background:rgba(22,199,217,.18);border-color:#16c7d9}",
      ".avc-trigger:focus-visible{outline:2px solid #16c7d9;outline-offset:2px}",
      ".avc-icon{width:16px;height:16px;display:block}",
      ".avc-menu{position:absolute;left:0;bottom:calc(100% + 8px);z-index:60;width:224px;padding:8px;border:1px solid var(--dsw-alias-border-inverted);border-radius:12px;background:var(--dsw-specific-menu);box-shadow:var(--dsw-shadow-lv3);color:var(--dsw-alias-label-primary)}",
      ".avc-title{padding:3px 7px 7px;font-size:12px;font-weight:700;color:#16c7d9}",
      ".avc-grid{display:grid;grid-template-columns:1fr 1fr;gap:5px}",
      ".avc-action{min-height:34px;padding:6px 8px;border:0;border-radius:8px;background:transparent;color:var(--dsw-alias-label-primary);cursor:pointer;text-align:left;font-size:12px}",
      ".avc-action:hover{background:var(--dsw-alias-interactive-bg-hover)}",
      ".avc-action:focus-visible{outline:2px solid #16c7d9;outline-offset:-2px}",
      ".avc-hint{padding:8px 7px 2px;color:var(--dsw-alias-label-tertiary);font-size:11px;line-height:16px}"
    ].join("");
    const styleId = "agent-voice-control.css";
    if (typeof document !== "undefined" && !document.querySelector(`style[data-plugin-css="${styleId}"]`)) {
      const tag = document.createElement("style");
      tag.dataset.pluginCss = styleId;
      tag.textContent = css;
      document.head.appendChild(tag);
    }

    function VoiceWaveIcon() {
      return h("svg", {className:"avc-icon", viewBox:"0 0 24 24", fill:"none", "aria-hidden":"true"},
        h("circle", {cx:"12", cy:"12", r:"9", fill:"rgba(22,199,217,.14)", stroke:"#16c7d9", strokeWidth:"1.4"}),
        h("path", {d:"M6.8 12h1.4m1.4-3.2v6.4m2-9v11.6m2-8.7v5.8m2-3.9v2m1.8-1h.8", stroke:"#16c7d9", strokeWidth:"1.65", strokeLinecap:"round"})
      );
    }

    function VoiceControl({inputActions}) {
      const [open,setOpen] = React.useState(false);
      const root = React.useRef(null);
      React.useEffect(() => {
        if (!open) return;
        const close = event => { if (root.current && !root.current.contains(event.target)) setOpen(false); };
        const escape = event => { if (event.key === "Escape") setOpen(false); };
        document.addEventListener("mousedown", close);
        window.addEventListener("keydown", escape);
        return () => { document.removeEventListener("mousedown", close); window.removeEventListener("keydown", escape); };
      }, [open]);
      const run = command => {
        setOpen(false);
        if (!inputActions || typeof inputActions.setDraft !== "function") return;
        inputActions.setDraft(`/voice ${command}`);
        if (typeof inputActions.submit === "function") setTimeout(() => inputActions.submit(), 0);
      };
      const actions = [
        ["status","查看状态"], ["start","开启服务"], ["quiet","停止朗读"],
        ["stop","关闭服务"], ["profiles","声音方案"], ["auto on","开启自动朗读"],
        ["auto off","关闭自动朗读"]
      ];
      return h("div", {className:"avc-root", ref:root},
        h("button", {type:"button", className:"avc-trigger", title:"Agent Voice 控制", "aria-label":"打开 Agent Voice 控制", "aria-expanded":open, onMouseDown:e=>e.preventDefault(), onClick:()=>setOpen(!open)}, h(VoiceWaveIcon), h("span", null, "语音")),
        open && h("div", {className:"avc-menu", role:"menu", "aria-label":"Agent Voice 控制"},
          h("div", {className:"avc-title"}, "Agent Voice"),
          h("div", {className:"avc-grid"}, actions.map(([command,label]) => h("button", {key:command, type:"button", className:"avc-action", role:"menuitem", onMouseDown:e=>e.preventDefault(), onClick:()=>run(command)}, label))),
          h("div", {className:"avc-hint"}, "点击后直接执行 MCP 控制，不发送给模型。")
        )
      );
    }

    const inject = ["slots"];
    function apply(ctx) {
      ctx.inject(["slots"], scope => {
        scope.slots.inject("conversation.input.left", () => scope.slots.register({
          name:"conversation.input.left", id:"agent-voice-control", order:18
        }, VoiceControl));
      });
    }
    exports.VoiceControl = VoiceControl;
    exports.inject = inject;
    exports.apply = apply;
    return module.exports;
  }
});
