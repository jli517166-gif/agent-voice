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
      ".avc-root,.avc-root *{box-sizing:border-box}",
      ".avc-root{position:relative;display:inline-flex;align-items:center;flex:none;font-family:inherit}",
      ".avc-trigger{appearance:none;height:32px;padding:0 11px;display:inline-flex;align-items:center;gap:7px;border:1px solid rgba(25,199,242,.72);border-radius:10px;outline:0;box-shadow:none;background:#08243a;color:#eaf6ff;cursor:pointer;font:600 12px/1 inherit;transition:background .15s ease,border-color .15s ease,transform .15s ease}",
      ".avc-trigger:hover,.avc-trigger[aria-expanded=true]{background:#0b314b;border-color:#19c7f2;transform:translateY(-1px)}",
      ".avc-trigger:focus-visible{border-color:#78e7ff;box-shadow:0 0 0 2px rgba(25,199,242,.22)}",
      ".avc-icon{width:17px;height:17px;display:block;flex:none}",
      ".avc-menu{position:absolute;left:0;bottom:calc(100% + 10px);z-index:60;width:250px;padding:12px;border:1px solid rgba(25,199,242,.62);border-radius:15px;outline:0;background:linear-gradient(155deg,#071827,#0a263b);box-shadow:0 16px 44px rgba(0,0,0,.42);color:#eaf6ff;overflow:hidden}",
      ".avc-head{display:flex;align-items:center;justify-content:space-between;padding:1px 2px 10px;border-bottom:1px solid rgba(25,199,242,.18)}",
      ".avc-title{display:flex;align-items:center;gap:7px;font-size:13px;font-weight:750;letter-spacing:.2px;color:#fff}",
      ".avc-state{display:inline-flex;align-items:center;gap:5px;font-size:10px;color:#9fc4d5}",
      ".avc-state:before{content:'';width:6px;height:6px;border-radius:50%;background:#19c7f2;box-shadow:0 0 8px rgba(25,199,242,.75)}",
      ".avc-grid{display:grid;grid-template-columns:1fr 1fr;gap:7px;padding-top:10px}",
      ".avc-action{appearance:none;min-height:36px;padding:7px 9px;border:1px solid rgba(25,199,242,.20);border-radius:9px;outline:0;box-shadow:none;background:rgba(255,255,255,.035);color:#eaf6ff;cursor:pointer;text-align:center;font:500 12px/1.2 inherit;transition:background .15s ease,border-color .15s ease}",
      ".avc-action:first-child,.avc-action:nth-child(2){border-color:rgba(25,199,242,.52);background:rgba(25,199,242,.11)}",
      ".avc-action:hover{background:rgba(25,199,242,.17);border-color:#19c7f2}",
      ".avc-action:focus-visible{border-color:#78e7ff;box-shadow:inset 0 0 0 1px #78e7ff}",
      ".avc-hint{margin-top:10px;padding:8px 9px;border-radius:8px;background:rgba(0,0,0,.18);color:#91b6c8;font-size:10.5px;line-height:15px;text-align:center}"
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
          h("div", {className:"avc-head"}, h("div", {className:"avc-title"}, h(VoiceWaveIcon), "语音控制"), h("span", {className:"avc-state"}, "本机 MCP")),
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
