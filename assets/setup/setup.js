(() => {
  'use strict';
  const $=selector=>document.querySelector(selector), B=window.setupBridge;
  const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  let state=null,shownStep=-1,selection=[],busy=false,logs=[],drafts={},toastTimer,resizeTimer;
  const btn=(action,text,primary=false,extra='')=>`<button data-action="${action}" class="${primary?'primary':'secondary'}" title="${text}" ${extra}>${text}</button>`;
  const actions=items=>`<div class="actions">${items.join('')}</div>`;
  const doc=(name,text)=>btn('doc',text,false,`data-doc="${name}"`);
  const resources=()=>`<div class="status-note" data-resources>${esc(state.resources)}</div>`;
  const engineNames=['CosyVoice','GPT-SoVITS · v5 Turbo','GPT-SoVITS · ProPlus'];
  const selectedNames=()=>(state.installEngines??[state.engine]).map(i=>engineNames[i]).join('、')||'尚未选择';
  const pageOrder=[0,5,6,1,2,3,4,7,8,9];
  const pages=[
    {title:'让对话，在耳边发生。',lead:'给 Harness 选一种你喜欢的声音。引擎、音色与陪伴的方式，都由你慢慢定。',art:'headphone.webp',label:'声音的模样，由你定义',body:()=>`<p class="muted">从熟悉的音色开始，也可以从一张白纸开始。软件、引擎、音色与环境各自独立；每一步都能跳过，随时回来续上。使用前请阅读法律须知与引擎许可。</p><div class="chips"><span>本地运行</span><span>模块安装</span><span>自由选择</span></div>${actions([doc('模块安装与配置指南.md','了解模块安装'),doc('docs/使用须知与TTS许可.md','使用须知与许可')])}`},
    {title:'声音，从这台电脑启程。',lead:'先看看这台电脑能托起怎样的声音。检查显卡、驱动与空间，再为喜欢的引擎安个家。',art:'computer.webp',label:'为声音，找到落脚处',body:()=>`<p class="muted">这里只查看电脑条件，不安装驱动。硬件检查不代表声音可用；Harness 接入另通过 MCP 自检，不会在配置流程自动朗读。</p>${actions([btn('computer','检查这台电脑',true),btn('recover','恢复未完成安装')])}`},
    {title:'声音的笔触，由你挑选。',lead:'引擎把文字写成声音。选一种，也可以多留几种，让同一段对话有不同的表达。',art:'puzzle.webp',label:'多留几种可能',body:()=>`<div class="tile-list">${engineNames.map((name,i)=>`<button class="tile ${selection.includes(i)?'selected':''}" data-engine="${i}" role="checkbox" aria-checked="${selection.includes(i)}"><strong>${selection.includes(i)?'✓ ':''}${name}</strong><small>${i===0?'用参考录音或训练模型，延续喜欢的音色':'共用基础环境，各自使用对应版本的权重'}</small></button>`).join('')}</div><p class="muted">可以多选。下载前请核对许可：CosyVoice 源码为 Apache-2.0，GPT-SoVITS 源码为 MIT，权重与依赖另审。下一步会记下安装清单，当前声音保持原样。还没想好，就先不选，以后再添。</p>`},
    {title:'把声音的来处，安放好。',lead:'把选好的引擎带到这台电脑。在线下载，或导入手边的引擎包，走你方便的那条路。',art:'folder.webp',label:'引擎文件',body:()=>`<p class="muted">安装清单：${esc(selectedNames())}</p>${resources()}${actions([btn('download-engine','下载所选引擎',true),btn('resources','检查已有文件'),btn('stop-service','停止服务以安装')])}<p class="muted">源码与基础模型会依次下载，已有的就留下；GPT 两个版本的共用文件只准备一次。下载可能需要数 GB，运行依赖留到下一步。失败项可以重试，已完成的会保留。</p><details id="localEngine" ${state.offline?'open':''}><summary>手边已有引擎包，或下载遇到了阻碍？</summary><p class="muted">把已有的引擎包带进来就好。可以逐个导入，彼此共存，也可以直接找到已经安装的引擎。</p>${actions([btn('install-engine','导入本地引擎包'),btn('paths','定位已安装引擎')])}</details>${actions([doc('TTS使用手册.md','下载与安装说明')])}`},
    {title:'为每一句话，铺好来路。',lead:'让引擎与这台电脑配合起来。按安装清单检查运行环境，把缺少的部分一项项补齐。',art:'puzzle.webp',label:'运行环境',body:()=>`<p class="muted">安装清单：${esc(selectedNames())}</p><label><input type="checkbox" id="offline" ${state.offline?'checked':''}>严格离线：只使用本地文件</label>${resources()}${actions([btn('inspect-environment','检查已有环境',true),btn('repair-environment','补齐缺失环境'),btn('stop-service','停止服务以安装')])}<details><summary>网络暂时走不通？带上离线环境包</summary>${actions([btn('install-environment','导入离线环境包')])}</details>${actions([doc('环境补齐说明.md','环境安装说明')])}`},
    {title:'让声音，也有分寸。',lead:'让 Harness 开口之前，先把授权与边界说清楚。自由选择声音，也尊重每一种声音的来处。',art:'folder.webp',label:'使用须知',body:()=>`<p><strong>只使用有权使用的录音、模型与文字。</strong>录音公开可下载、个人使用或机器合成，都不自动等于获得克隆、训练和分发许可。</p><p>训练、生成、公开发布、商用与权重分发应分别核对授权。不冒充他人，不用于诈骗或侵犯隐私。发布时按适用规则声明 AI 生成；在线服务另行审查。</p><p class="muted">软件不附带默认音色。本页告知不能替代授权、法律审查或完整的生成内容标识实现。</p>${actions([doc('docs/使用须知与TTS许可.md','阅读全文与适用范围'),btn('legal-read','我已阅读须知')])}`},
    {title:'开源，有各自的约定。',lead:'源码、权重与依赖，各有自己的许可。选择引擎，也把它的约定一起带上。',art:'puzzle.webp',label:'TTS 许可',body:()=>`<p><strong>CosyVoice 源码：Apache-2.0。</strong>指定 Fun-CosyVoice3-0.5B-2512 基础模型卡同标 Apache-2.0；保留许可、适用 NOTICE 与修改说明。</p><p><strong>GPT-SoVITS 源码：MIT。</strong>保留版权与许可。v5 Turbo、ProPlus、附属权重和声码器仍需按实际文件核查，不能由代码 MIT 推定全部可商用。</p><p class="muted">上述源码许可允许依约商用，不授予他人声音权利。Python、CUDA、FFmpeg 等依赖另遵各自条款；许可缺失的权重与自定义素材标为待核查。</p>${actions([doc('docs/使用须知与TTS许可.md','许可与适用范围'),doc('docs/licenses/CosyVoice-Apache-2.0.txt','Apache-2.0 原文'),doc('docs/licenses/GPT-SoVITS-MIT.txt','MIT 原文')])}`},
    {title:'给对话，接上控制。',lead:'推荐用 MCP 连接 Harness。目前实测支持 Codex 与 DeepSeek Harness；不需要先准备音色，也不用发送验证短句。',art:'link.webp',label:'MCP 控制',body:()=>`${actions([btn('connect-harness','选择目录并安装 MCP',true),doc('接入说明.md','查看接入步骤')])}<p>自检会核对安装路径、启用状态、协议握手、8 个工具和只读状态调用。收到客户端读取工具的回执后，才庆祝接入成功。</p>${actions([btn('check-mcp-codex','Codex MCP 自检'),btn('check-mcp-deepseek','DeepSeek MCP 自检')])}<p class="muted">安装后可能需要重载插件或重启 Harness，再点击自检。此过程不发聊天消息、不生成语音；自动朗读与声音可用性另行配置。</p>`},
    {title:'让声音，有个身影。',lead:'如果你愿意，让 AIRI 接住这份声音。它的回复、Harness 的话语，都可以由桌宠陪你说出来。',art:'airi-11-icon.png',label:'AIRI，另一种陪伴',body:()=>`<label><input type="checkbox" id="airiEnabled" ${state.airiEnabled?'checked':''}>启用 AIRI 桌宠联动</label>${actions([btn('connect-airi','选择原 AIRI 并验证',true)])}<p class="muted">找到你已经登录、包含 airi.exe 的那个 AIRI，我们就在原处接上声音。播放与嘴型都收到回执后，才确认联动成功。</p><p class="muted">这份陪伴由你开启；保持关闭，仍能在本机听见声音。AIRI 更新后若联动失效，回来重新接入验证即可。</p>`},
    {title:'下一段对话，听你的。',lead:'把想要的留下，把未定的留给以后。走进工作台，让 Harness 按你的心意说话。',art:'music.webp',label:'余下的，留给日常',body:()=>`${resources()}${actions([btn('summary','刷新实际状态',true),doc('模块安装与配置指南.md','安装与故障说明')])}<p class="muted">这里记下的是实际进度。暂时跳过的，依旧等你补齐；随时打开设置，接着把声音调成喜欢的模样。</p>`}
  ];
  const pathLabels={pythonService:'主服务 Python',pythonTts:'CosyVoice Python',cosyRepo:'CosyVoice 源码',cosyModel:'CosyVoice 基础模型',pythonGpt:'GPT-SoVITS Python',gptRepo:'GPT-SoVITS 源码',gptModel:'GPT-SoVITS 基础模型',ffmpeg:'FFmpeg'};
  const wave=()=>'<span class="scene-wave">'+Array.from({length:13},()=>'<i></i>').join('')+'</span>';
  function scene(step,page){
    const motifs=[wave(),'<span class="scan-line"></span><span class="scene-tags"><b>硬件</b><b>驱动</b><b>存储</b></span>','<span class="signal-route"><b>文字</b><i></i><b>声音</b></span>','<span class="transfer-docs"><i></i><i></i><i></i></span><span class="scene-tags"><b>源码</b><b>基础模型</b></span>','<span class="dependency-grid"><b>Python</b><b>GPU 库</b><b>音频依赖</b></span>',wave(),wave(),'<span class="message-trail"><i>Codex</i><i>DeepSeek</i><i>语音服务</i></span>',wave()+'<svg class="airi-ribbons" viewBox="0 0 650 400"><path d="M-60 340C150 420 260-100 710 160"/><path d="M-50 370C160 450 290-70 720 190"/></svg>','<span class="sound-settle"><i></i><i></i><i></i><i></i><i></i></span>'];
    if(step===5||step===6)motifs[step]='<span class="scene-tags"><b>'+ (step===5?'来源 · 授权 · 用途':'源码 · 权重 · 依赖')+'</b></span>';
    const path=step===8?'art/airi-mark.svg':'art/cutouts/'+page.art.replace('.webp','.png');
    return `<figure class="art scene-${step}" aria-hidden="true"><img class="hero-object" src="${path}" alt="">${motifs[step]}<span class="art-label">${page.label}</span></figure>`;
  }
  function render(next){
    const changed=shownStep!==next.step;state=next;
    $('#motion').checked=state.reduceMotion;$('#sound').checked=state.sound;
    document.body.classList.toggle('reduced',state.reduceMotion);
    $('#position').textContent=state.step===0?'首次配置':state.step===9?'配置回顾':`${pageOrder.indexOf(state.step)} / 9`;
    $('#back').hidden=state.step===0;$('#skip').hidden=state.step===0||state.step===9;
    $('#next').textContent=state.step===0?'开始配置':state.step===9?'进入工作台':'下一步';
    if(changed){
      selection=[...(state.installEngines??[state.engine])];shownStep=state.step;const page=pages[state.step];
      $('#page').className='step-'+state.step;
      $('#page').innerHTML=`<div class="copy"><h1 tabindex="-1">${page.title}</h1><p class="lead">${page.lead}</p><div class="body">${page.body()}</div></div>${scene(state.step,page)}`;
      $('#viewport').scrollTop=0;$('#page h1').focus({preventScroll:true});
      if(!busy){$('#feedback').hidden=true;$('#result').textContent='';}
    }else{
      document.querySelectorAll('[data-resources]').forEach(item=>item.textContent=state.resources);
      const active=$('#activeEngine');if(active)active.value=String(state.engine);
    }
    updateDisabled();
  }
  function updateDisabled(){
    document.querySelectorAll('button,input,select,textarea').forEach(item=>{
      const exempt=['showLog','closeDrawer'].includes(item.id);
      item.disabled=busy&&!exempt;
    });
  }
  function append(message){logs.push(String(message));if(logs.length>200)logs.shift();$('#result').textContent=String(message);if($('#drawer').open&&$('#drawer').dataset.mode==='log')$('#drawerBody pre').textContent=logs.join('\n\n');}
  function feedback(title,message,error=false){$('#feedback').hidden=false;$('#feedback').classList.toggle('error',error);$('#taskTitle').textContent=title;append(message);}
  function toast(message){clearTimeout(toastTimer);$('#toast').textContent=message;$('#toast').hidden=false;toastTimer=setTimeout(()=>$('#toast').hidden=true,2400);}
  async function run(action,args={},label='正在处理'){
    const parallel=false;if(busy&&!parallel)return null;
    const quiet=['next','back','skip','defer','go','preferences'].includes(action);
    if(!parallel){busy=true;document.body.classList.add('working');document.body.dataset.task=action;logs=[];if(quiet)$('#feedback').hidden=true;else feedback(label,'正在执行，请等待真实结果。');$('#feedback .progress').hidden=quiet;updateDisabled();}
    try{
      const result=await B.request(action,args);if(result?.state)render(result.state);
      if(result?.message)feedback('操作完成',result.message);
      if(result?.celebrate)celebrate();
      if(['save-paths'].includes(action))drafts={};
      if(['save-paths','preferences','save-airi','legal-read'].includes(action))toast(result?.message||'已保存');
      return result;
    }catch(error){if(action==='download-engine'&&$('#localEngine'))$('#localEngine').open=true;if($('#drawer').open){let note=$('#drawerError');if(!note){note=document.createElement('p');note.id='drawerError';note.className='error-copy';note.setAttribute('role','alert');$('#drawerBody').prepend(note);}note.textContent=error.message;}if(error.state)render(error.state);feedback('未完成 · 请修复后重试',error.message,true);return null;}
    finally{if(!parallel){busy=false;document.body.classList.remove('working');delete document.body.dataset.task;$('#feedback .progress').hidden=true;updateDisabled();}}
  }
  async function browse(kind){const value=await B.request('browse',{kind});return value.path||'';}
  const field=(id,label,value,multiline=false)=>`<label class="field">${label}${multiline?`<textarea id="${id}">${esc(drafts[id]??value)}</textarea>`:`<input id="${id}" value="${esc(drafts[id]??value)}">`}</label>`;
  function drawer(title,body,mode){$('#drawerTitle').textContent=title;$('#drawerBody').innerHTML=body;$('#drawer').dataset.mode=mode;$('#drawer').showModal();updateDisabled();}
  function openPaths(){
    const fields=Object.entries(pathLabels).map(([key,label])=>`<div class="field-row">${field('path_'+key,label,state.paths[key])}${btn('browse-path','选择',false,`data-field="${key}"`)}</div>`).join('');
    drawer('已有引擎与模型路径',`<p class="muted">已经备好引擎，就把它所在的位置告诉我们。先停止服务再保存；这里仅配置引擎与运行环境路径。</p>${fields}<div class="drawer-actions">${btn('save-paths','保存路径',true)}${btn('stop-service','停止服务')}</div>`,'paths');
  }
  function celebrate(){
    const layer=$('#celebration .celebration-lines');layer.replaceChildren();
    if(!state.reduceMotion&&!matchMedia('(prefers-reduced-motion:reduce)').matches)for(let i=0;i<70;i++){
      const piece=document.createElement('i');piece.className='confetti';piece.style.setProperty('--dx',(Math.random()-.5)*innerWidth*1.6+'px');piece.style.setProperty('--dy',(Math.random()-.55)*innerHeight*1.5+'px');piece.style.setProperty('--rotation',Math.random()*600+'deg');layer.append(piece);
    }
    $('#celebration').hidden=false;
  }
  document.addEventListener('click',async event=>{
    const target=event.target.closest('button');if(!target||target.disabled)return;
    if(target.dataset.engine!==undefined){const index=Number(target.dataset.engine);selection=selection.includes(index)?selection.filter(i=>i!==index):[...selection,index].sort();document.querySelectorAll('[data-engine]').forEach(item=>{const n=Number(item.dataset.engine),chosen=selection.includes(n);item.classList.toggle('selected',chosen);item.setAttribute('aria-checked',chosen);item.querySelector('strong').textContent=(chosen?'✓ ':'')+engineNames[n];});return;}
    const action=target.dataset.action;if(!action)return;
    try{
      if(action==='doc'){await run('open-doc',{name:target.dataset.doc},'打开说明');return;}
      if(action==='paths'){openPaths();return;}
      if(action==='install-engine'||action==='install-environment'){
        const kind=action==='install-engine'?'engine':'environment';const file=await browse(kind);if(file)await run(action,{file},target.textContent);return;
      }
      if(action==='connect-harness'||action==='connect-airi'){const directory=await browse(action==='connect-airi'?'airi':'harness');if(directory)await run(action,{directory},target.textContent);return;}
      if(action==='browse-path'){const key=target.dataset.field;const path=await browse(key.startsWith('python')||key==='ffmpeg'?'executable':'folder');if(path){$('#path_'+key).value=path;drafts['path_'+key]=path;}return;}
      if(action==='save-paths'){const args={};for(const key of Object.keys(pathLabels))args[key]=$('#path_'+key).value;const result=await run(action,args,'保存引擎路径');if(result)$('#drawer').close();return;}
      if(action==='switch-engine'){await run(action,{engine:Number($('#activeEngine').value)},'切换当前引擎');return;}
      if(action.startsWith('check-mcp-')){await run('check-mcp',{source:action.slice(10)},target.textContent);return;}
      await run(action,{},target.textContent);
    }catch(error){feedback('未完成 · 请重试',error.message,true);}
  });
  $('#next').onclick=()=>run('next',{engines:selection},state?.step===2?'保存安装清单':'继续配置');
  $('#back').onclick=()=>run('back',{},'返回上一步');$('#skip').onclick=()=>run('skip',{},'跳过本步');$('#defer').onclick=()=>run('defer',{},'保存进度，稍后配置');
  const preferences=()=>run('preferences',{offline:$('#offline')?.checked??state.offline,reduceMotion:$('#motion').checked,sound:$('#sound').checked},'保存偏好');
  $('#motion').onchange=preferences;$('#sound').onchange=preferences;
  document.addEventListener('change',event=>{if(event.target.id==='offline')preferences();if(event.target.id==='airiEnabled')run('save-airi',{enabled:event.target.checked},'保存 AIRI 开关');});
  document.addEventListener('input',event=>{if($('#drawer').contains(event.target)&&event.target.id)drafts[event.target.id]=event.target.value;});
  $('#showLog').onclick=()=>drawer('执行过程',`<pre>${esc(logs.join('\n\n'))}</pre>`,'log');$('#closeDrawer').onclick=()=>$('#drawer').close();
  $('#closeCelebration').onclick=()=>$('#celebration').hidden=true;
  addEventListener('resize',()=>{document.body.classList.add('resizing');clearTimeout(resizeTimer);resizeTimer=setTimeout(()=>document.body.classList.remove('resizing'),160);});
  document.addEventListener('visibilitychange',()=>{document.body.classList.toggle('resizing',document.hidden);});
  B.subscribe(message=>{if(message.type==='progress')append(message.message);if(message.type==='notice')feedback('请稍候',message.message,true);});
  B.request('state').then(result=>{render(result.state);if(state.step===9)run('summary',{},'读取真实配置状态');}).catch(error=>feedback('配置页面未连接到程序',error.message,true));
})();
