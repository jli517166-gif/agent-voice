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
      ".avc-root{--av-surface:#fff;--av-control:#f4f5f7;--av-hover:#e9ebee;--av-border:var(--dsw-alias-border-l2,#d9dce1);--av-text:var(--dsw-alias-label-primary,#20242a);--av-muted:var(--dsw-alias-label-tertiary,#69717e);--av-accent:var(--dsw-alias-state-business-primary,#4176e6);position:relative;display:inline-flex;align-items:center;flex:none;font-family:inherit;color:var(--av-text);color-scheme:inherit}",
      "body[data-ds-dark-theme] .avc-root{--av-surface:#292a2d;--av-control:#343539;--av-hover:#404146;--av-border:var(--dsw-alias-border-l2,#484a50)}",
      ".avc-root button,.avc-root select,.avc-menu{corner-shape:round}",
      ".avc-trigger{appearance:none;height:32px;padding:0 10px;display:inline-flex;align-items:center;gap:6px;border:1px solid var(--av-border);border-radius:9px;outline:0;box-shadow:none;background:var(--av-control);color:var(--av-text);cursor:pointer;font-family:inherit;font-size:12px;font-weight:600;line-height:1;transition:background .15s ease}",
      ".avc-trigger:hover,.avc-trigger[aria-expanded=true]{background:var(--av-hover)}",
      ".avc-trigger:focus-visible{outline:2px solid var(--av-accent);outline-offset:2px}",
      ".avc-icon{width:17px;height:17px;display:block;flex:none}",
      ".avc-menu{position:absolute;left:0;bottom:calc(100% + 10px);z-index:2147483000;width:270px;padding:12px;border:1px solid var(--av-border);border-radius:12px;outline:0;background-color:var(--av-surface);background-image:linear-gradient(var(--dsw-specific-menu,transparent),var(--dsw-specific-menu,transparent));opacity:1;isolation:isolate;box-shadow:var(--dsw-shadow-lv3,0 8px 24px #0003);color:var(--av-text);overflow:hidden}",
      ".avc-head{display:flex;align-items:center;justify-content:space-between;padding:1px 2px 10px;border-bottom:1px solid var(--av-border)}",
      ".avc-title{display:flex;align-items:center;gap:7px;font-size:13px;font-weight:650;color:var(--av-text)}",
      ".avc-state{font-size:10px;color:var(--av-muted)}",
      ".avc-grid{display:grid;grid-template-columns:1fr 1fr;gap:7px;padding-top:10px}",
      ".avc-action{appearance:none;min-height:36px;padding:7px 9px;border:1px solid var(--av-border);border-radius:8px;outline:0;box-shadow:none;background:var(--av-control);color:var(--av-text);cursor:pointer;text-align:center;font-family:inherit;font-size:12px;font-weight:500;line-height:1.2;transition:background .15s ease}",
      ".avc-action:hover{background:var(--av-hover)}",
      ".avc-action:focus-visible{outline:2px solid var(--av-accent);outline-offset:-2px}",
      ".avc-action:disabled{cursor:wait;opacity:.55}",
      ".avc-profiles{display:flex;gap:6px;margin-top:9px}.avc-profiles select{width:150px;min-width:0;background:var(--av-control);color:var(--av-text);border:1px solid var(--av-border);border-radius:8px;padding:7px 4px}.avc-profiles button{flex:1}",
      ".avc-hint{margin-top:10px;padding:8px 9px;border-radius:8px;background:var(--av-control);color:var(--av-muted);font-size:11px;line-height:16px;text-align:center;white-space:pre-wrap}"
    ].join("");
    const styleId = "agent-voice-control.css";
    if (typeof document !== "undefined") {
      const existing = document.querySelector(`style[data-plugin-css="${styleId}"]`);
      const tag = existing || document.createElement("style");
      tag.dataset.pluginCss = styleId;
      tag.textContent = css;
      if (!existing) document.head.appendChild(tag);
    }

    function VoiceWaveIcon() {
      return h("svg", {className:"avc-icon", viewBox:"0 0 24 24", fill:"none", "aria-hidden":"true"},
        h("path", {d:"M4 10v4m4-7v10m4-13v16m4-13v10m4-7v4", stroke:"currentColor", strokeWidth:"1.7", strokeLinecap:"round"})
      );
    }

    function VoiceControl({request}) {
      const [open,setOpen] = React.useState(false);
      const [busy,setBusy] = React.useState(false);
      const [message,setMessage] = React.useState('直接控制本机服务，不发送聊天消息。');
      const [profiles,setProfiles] = React.useState(null);
      const [selected,setSelected] = React.useState('');
      const root = React.useRef(null);
      React.useEffect(() => {
        if (!open) return;
        const close = event => { if (root.current && !root.current.contains(event.target)) setOpen(false); };
        const escape = event => { if (event.key === "Escape") setOpen(false); };
        document.addEventListener("mousedown", close);
        window.addEventListener("keydown", escape);
        return () => { document.removeEventListener("mousedown", close); window.removeEventListener("keydown", escape); };
      }, [open]);
      const run = async (tool,args={}) => {
        if(busy)return;
        setBusy(true);setMessage('正在执行…');
        try{
          const value=await request(tool,args);
          if(tool==='list_voice_profiles'){const items=value.profiles||[];setProfiles(items);setSelected((items.find(p=>p.active)||items[0])?.id||'');setMessage(items.length?'选择声音方案后点击“使用”。':'还没有声音方案，请在桌面端保存。');}
          else if(tool==='voice_service_status')setMessage(`服务${value.running?'已开启':'已关闭'} · 自动朗读${value.autoReading?'开启':'关闭'}\n当前声音：${value.currentProfile?.name||'当前自定义设置'} · 队列 ${value.queued||0}`);
          else setMessage(({start_voice_service:'服务已开启',stop_voice_service:'服务已关闭',stop_speech:'朗读已停止，队列已清空',apply_voice_profile:'声音方案已切换',set_auto_reading:`自动朗读${args.enabled?'已开启':'已关闭'}`})[tool]||'操作完成');
        }catch(error){setMessage('操作失败：'+error.message);}
        finally{setBusy(false);}
      };
      const actions = [
        ['voice_service_status','查看状态'], ['start_voice_service','开启服务'], ['stop_speech','停止朗读'],
        ['stop_voice_service','关闭服务'], ['list_voice_profiles','声音方案'], ['set_auto_reading','开启自动朗读',{enabled:true}],
        ['set_auto_reading','关闭自动朗读',{enabled:false}]
      ];
      return h("div", {className:"avc-root", ref:root},
        h("button", {type:"button", className:"avc-trigger", title:"Agent Voice 控制", "aria-label":"打开 Agent Voice 控制", "aria-expanded":open, onMouseDown:e=>e.preventDefault(), onClick:()=>setOpen(!open)}, h(VoiceWaveIcon), h("span", null, "语音")),
        open && h("div", {className:"avc-menu", role:"menu", "aria-label":"Agent Voice 控制"},
          h("div", {className:"avc-head"}, h("div", {className:"avc-title"}, h(VoiceWaveIcon), "语音控制"), h("span", {className:"avc-state"}, "本机 MCP")),
          h("div", {className:"avc-grid"}, actions.map(([tool,label,args]) => h("button", {key:label, type:"button", className:"avc-action", role:"menuitem", disabled:busy, onMouseDown:e=>e.preventDefault(), onClick:()=>run(tool,args)}, label))),
          profiles?.length>0 && h('div',{className:'avc-profiles'},h('select',{'aria-label':'选择声音方案',value:selected,disabled:busy,onChange:e=>setSelected(e.target.value)},profiles.map(p=>h('option',{key:p.id,value:p.id},p.name))),h('button',{type:'button',className:'avc-action',disabled:busy||!selected,onClick:()=>run('apply_voice_profile',{profile_id:selected})},'使用')),
          h("div", {className:"avc-hint",role:'status','aria-live':'polite'}, message)
        )
      );
    }

    const inject = ["slots","remote","remote.commands"];
    function apply(ctx) {
      ctx.inject(["slots","remote","remote.commands"], scope => {
        scope.slots.inject("conversation.input.left", () => scope.slots.register({
          name:"conversation.input.left", id:"agent-voice-control", order:18
        }, props=>h(VoiceControl,{...props,request:async(tool,args)=>{
          if(!props.sessionId)throw new Error('请先选择一个会话');
          const result=await scope.remote.commands.execute(props.sessionId,'/agent-voice ui '+JSON.stringify({tool,args}),[]);
          if(!result.ok)throw new Error(result.error?.message||'MCP 控制通道不可用');
          const settled=result.value?.result;
          if(!settled){const catalog=await scope.remote.commands.list(props.sessionId);throw new Error('语音控制命令未加载。可用命令：'+(catalog.value||[]).map(item=>item.name).join('、'));}
          if(settled.kind!=='success')throw new Error(settled.text||'语音控制失败');
          return JSON.parse(settled.text);
        }})));
      });
    }
    exports.VoiceControl = VoiceControl;
    exports.inject = inject;
    exports.apply = apply;
    return module.exports;
  }
});
