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
  const activePicker=()=>'<label class="field">当前使用引擎<select id="activeEngine">'+engineNames.map((name,i)=>'<option value="'+i+'" '+(state.engine===i?'selected':'')+'>'+name+'</option>').join('')+'</select></label>'+actions([btn('switch-engine','切换当前引擎')]);
  const pages=[
    {title:'让对话，在耳边发生。',lead:'给 Harness 选一种你喜欢的声音。引擎、音色与陪伴的方式，都由你慢慢定。',art:'headphone.webp',label:'声音的模样，由你定义',body:()=>`<p class="muted">从熟悉的音色开始，也可以从一张白纸开始。软件、引擎、音色与环境各自独立；每一步都能跳过，随时回来续上。</p><div class="chips"><span>本地运行</span><span>模块安装</span><span>自由选择</span></div>${actions([doc('模块安装与配置指南.md','了解模块安装')])}`},
    {title:'声音，从这台电脑启程。',lead:'先看看这台电脑能托起怎样的声音。检查显卡、驱动与空间，再为喜欢的引擎安个家。',art:'computer.webp',label:'为声音，找到落脚处',body:()=>`<p class="muted">这里只查看电脑条件，不安装驱动。等引擎与音色就位，我们还会真正读上一段，确认声音能抵达。</p>${actions([btn('computer','检查这台电脑',true),btn('recover','恢复未完成安装')])}`},
    {title:'声音的笔触，由你挑选。',lead:'引擎把文字写成声音。选一种，也可以多留几种，让同一段对话有不同的表达。',art:'puzzle.webp',label:'多留几种可能',body:()=>`<div class="tile-list">${engineNames.map((name,i)=>`<button class="tile ${selection.includes(i)?'selected':''}" data-engine="${i}" role="checkbox" aria-checked="${selection.includes(i)}"><strong>${selection.includes(i)?'✓ ':''}${name}</strong><small>${i===0?'用参考录音或训练模型，延续喜欢的音色':'共用基础环境，各自使用对应版本的权重'}</small></button>`).join('')}</div><p class="muted">可以多选。下一步会记下安装清单，当前声音保持原样。还没想好，就先不选，以后再添。</p>`},
    {title:'把声音的来处，安放好。',lead:'把选好的引擎带到这台电脑。在线下载，或导入手边的引擎包，走你方便的那条路。',art:'folder.webp',label:'引擎文件',body:()=>`<p class="muted">安装清单：${esc(selectedNames())}</p>${resources()}${actions([btn('download-engine','下载所选引擎',true),btn('resources','检查已有文件'),btn('stop-service','停止服务以安装')])}<p class="muted">源码与基础模型会依次下载，已有的就留下；GPT 两个版本的共用文件只准备一次。下载可能需要数 GB，运行依赖留到下一步。失败项可以重试，已完成的会保留。</p><details id="localEngine" ${state.offline?'open':''}><summary>手边已有引擎包，或下载遇到了阻碍？</summary><p class="muted">把已有的引擎包带进来就好。可以逐个导入，彼此共存，也可以直接找到已经安装的引擎。</p>${actions([btn('install-engine','导入本地引擎包'),btn('paths','定位已安装引擎')])}</details>${actions([doc('TTS使用手册.md','下载与安装说明')])}`},
    {title:'为每一句话，铺好来路。',lead:'让引擎与这台电脑配合起来。按安装清单检查运行环境，把缺少的部分一项项补齐。',art:'puzzle.webp',label:'运行环境',body:()=>`<p class="muted">安装清单：${esc(selectedNames())}</p><label><input type="checkbox" id="offline" ${state.offline?'checked':''}>严格离线：只使用本地文件</label>${resources()}${actions([btn('inspect-environment','检查已有环境',true),btn('repair-environment','补齐缺失环境'),btn('stop-service','停止服务以安装')])}<details><summary>网络暂时走不通？带上离线环境包</summary>${actions([btn('install-environment','导入离线环境包')])}</details>${actions([doc('环境补齐说明.md','环境安装说明')])}`},
    {title:'把偏爱，调成声音。',lead:'先选当前使用的引擎，再选择你自己的参考录音。让 Harness 的每一次开口，都带着你的选择。',art:'mic.webp',label:'让每次开口，都有你的选择',body:()=>`${activePicker()}${resources()}${actions([btn('profiles','选择已保存的声音'),btn('reference','配置参考声音')])}<p class="muted">参考声音由你自己提供，软件不附带默认音色。下一步可以实际试听。</p>`},
    {title:'让耳朵，替你点头。',lead:'音色像不像，停顿是否舒服，语气是不是你想要的样子。先听一段，再由你决定。',art:'headphone.webp',label:'喜欢与否，听过再说',body:()=>`<p>点下试听，服务会启动，为你读一段话。可以停下、换一种声音，再听一次。</p>${actions([btn('preview','生成并播放',true),btn('stop-preview','停止试听')])}${actions([btn('confirm','确认声音合适')])}<p class="muted" data-heard>${state.heardVoice?'这份声音，你已亲耳听过并确认。':'这份声音，还在等你听过后点头。'}</p><p class="muted">程序会核对播放是否完成；喜欢与否，由你确认。换过引擎、模型或参考录音，再听一次，让选择落在耳朵里。</p>`},
    {title:'让 Harness，开口回应。',lead:'把屏幕里的文字，接成耳边的回应。目前实测支持 Codex 与 DeepSeek Harness；也可以先跳过，稍后再接。',art:'link.webp',label:'让文字，走近一点',body:()=>`${actions([btn('connect-harness','选择目录并接入验证',true),doc('接入说明.md','查看接入步骤')])}<p class="muted">选好安装目录，我们来识别 Harness、装好连接器，再走一遍回复与朗读。收到实际播放完成回执，才为这次相遇庆祝。</p>${actions([btn('copy-challenge','复制手动验证短句')])}<p class="muted">有些账号需要你亲手送出验证短句，点击“复制手动验证短句”即可复制。若卡在登录或重载，执行过程会写明原因和下一步。</p>`},
    {title:'让声音，有个身影。',lead:'如果你愿意，让 AIRI 接住这份声音。它的回复、Harness 的话语，都可以由桌宠陪你说出来。',art:'airi-11-icon.png',label:'AIRI，另一种陪伴',body:()=>`<label><input type="checkbox" id="airiEnabled" ${state.airiEnabled?'checked':''}>启用 AIRI 桌宠联动</label>${actions([btn('connect-airi','选择原 AIRI 并验证',true)])}<p class="muted">找到你已经登录、包含 airi.exe 的那个 AIRI，我们就在原处接上声音。播放与嘴型都收到回执后，才确认联动成功。</p><p class="muted">这份陪伴由你开启；保持关闭，仍能在本机听见声音。AIRI 更新后若联动失效，回来重新接入验证即可。</p>`},
    {title:'下一段对话，听你的。',lead:'把想要的留下，把未定的留给以后。走进工作台，让 Harness 按你的心意说话。',art:'music.webp',label:'余下的，留给日常',body:()=>`${resources()}${actions([btn('summary','刷新实际状态',true),doc('模块安装与配置指南.md','安装与故障说明')])}<p class="muted">这里记下的是实际进度。暂时跳过的，依旧等你补齐；随时打开设置，接着把声音调成喜欢的模样。</p>`}
  ];
  const pathLabels={pythonService:'主服务 Python',pythonTts:'CosyVoice Python',cosyRepo:'CosyVoice 源码',cosyModel:'CosyVoice 基础模型',pythonGpt:'GPT-SoVITS Python',gptRepo:'GPT-SoVITS 源码',gptModel:'GPT-SoVITS 基础模型',ffmpeg:'FFmpeg',baseModel:'CosyVoice 当前声音模型（可留空）',voiceGptModel:'GPT 当前声音模型（可留空）'};
  const wave=()=>'<span class="scene-wave">'+Array.from({length:13},()=>'<i></i>').join('')+'</span>';
  function scene(step,page){
    const motifs=[wave(),'<span class="scan-line"></span><span class="scene-tags"><b>硬件</b><b>驱动</b><b>存储</b></span>','<span class="signal-route"><b>文字</b><i></i><b>声音</b></span>','<span class="transfer-docs"><i></i><i></i><i></i></span><span class="scene-tags"><b>源码</b><b>基础模型</b></span>','<span class="dependency-grid"><b>Python</b><b>GPU 库</b><b>音频依赖</b></span>',wave(),wave(),'<span class="message-trail"><i>Codex</i><i>DeepSeek</i><i>语音服务</i></span>',wave()+'<svg class="airi-ribbons" viewBox="0 0 650 400"><path d="M-60 340C150 420 260-100 710 160"/><path d="M-50 370C160 450 290-70 720 190"/></svg>','<span class="sound-settle"><i></i><i></i><i></i><i></i><i></i></span>'];
    const path=step===8?'art/airi-mark.svg':'art/cutouts/'+page.art.replace('.webp','.png');
    return `<figure class="art scene-${step}" aria-hidden="true"><img class="hero-object" src="${path}" alt="">${motifs[step]}<span class="art-label">${page.label}</span></figure>`;
  }
  function render(next){
    const changed=shownStep!==next.step;state=next;
    $('#motion').checked=state.reduceMotion;$('#sound').checked=state.sound;
    document.body.classList.toggle('reduced',state.reduceMotion);
    $('#position').textContent=state.step===0?'首次配置':state.step===9?'配置回顾':`${state.step} / 9`;
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
      const heard=$('[data-heard]');if(heard)heard.textContent=state.heardVoice?'这份声音，你已亲耳听过并确认。':'这份声音，还在等你听过后点头。';
    }
    updateDisabled();
  }
  function updateDisabled(){
    document.querySelectorAll('button,input,select,textarea').forEach(item=>{
      const exempt=['stop-preview','copy-challenge'].includes(item.dataset.action)||['showLog','closeDrawer'].includes(item.id);
      item.disabled=busy&&!exempt;
    });
  }
  function append(message){logs.push(String(message));if(logs.length>200)logs.shift();$('#result').textContent=String(message);if($('#drawer').open&&$('#drawer').dataset.mode==='log')$('#drawerBody pre').textContent=logs.join('\n\n');}
  function feedback(title,message,error=false){$('#feedback').hidden=false;$('#feedback').classList.toggle('error',error);$('#taskTitle').textContent=title;append(message);}
  function toast(message){clearTimeout(toastTimer);$('#toast').textContent=message;$('#toast').hidden=false;toastTimer=setTimeout(()=>$('#toast').hidden=true,2400);}
  async function run(action,args={},label='正在处理'){
    const parallel=action==='stop-preview'||action==='copy-challenge';if(busy&&!parallel)return null;
    const quiet=['next','back','skip','defer','go','preferences'].includes(action);
    if(!parallel){busy=true;document.body.classList.add('working');document.body.dataset.task=action;logs=[];if(quiet)$('#feedback').hidden=true;else feedback(label,'正在执行，请等待真实结果。');$('#feedback .progress').hidden=quiet;updateDisabled();}
    try{
      const result=await B.request(action,args);if(result?.state)render(result.state);
      if(result?.message)feedback('操作完成',result.message);
      if(result?.celebrate)celebrate();
      if(['save-reference','save-paths','apply-profile'].includes(action))drafts={};
      if(['save-reference','save-paths','preferences','copy-challenge','save-airi'].includes(action))toast(result?.message||'已保存');
      return result;
    }catch(error){if(action==='download-engine'&&$('#localEngine'))$('#localEngine').open=true;if($('#drawer').open){let note=$('#drawerError');if(!note){note=document.createElement('p');note.id='drawerError';note.className='error-copy';note.setAttribute('role','alert');$('#drawerBody').prepend(note);}note.textContent=error.message;}if(error.state)render(error.state);feedback('未完成 · 请修复后重试',error.message,true);return null;}
    finally{if(!parallel){busy=false;document.body.classList.remove('working');delete document.body.dataset.task;$('#feedback .progress').hidden=true;updateDisabled();}}
  }
  async function browse(kind){const value=await B.request('browse',{kind});return value.path||'';}
  const field=(id,label,value,multiline=false)=>`<label class="field">${label}${multiline?`<textarea id="${id}">${esc(drafts[id]??value)}</textarea>`:`<input id="${id}" value="${esc(drafts[id]??value)}">`}</label>`;
  function drawer(title,body,mode){$('#drawerTitle').textContent=title;$('#drawerBody').innerHTML=body;$('#drawer').dataset.mode=mode;$('#drawer').showModal();updateDisabled();}
  function openReference(){drawer('参考声音',`<p class="muted">选一段能代表你所想的声音，再写下录音里逐字说出的台词。两者要一致，保存后我们再亲耳试听。</p><div class="field-row">${field('referencePath','参考录音',state.reference)}${btn('browse-reference','选择')}</div>${field('referenceText','与录音逐字一致的台词',state.transcript,true)}<div class="drawer-actions">${btn('save-reference','保存参考声音',true)}</div>`,'reference');}
  function openPaths(){
    const fields=Object.entries(pathLabels).map(([key,label])=>`<div class="field-row">${field('path_'+key,label,key==='baseModel'?state.baseModel:key==='voiceGptModel'?state.gptModel:state.paths[key])}${btn('browse-path','选择',false,`data-field="${key}"`)}</div>`).join('');
    drawer('已有引擎与模型路径',`<p class="muted">已经备好引擎，就把它所在的位置告诉我们。先停止服务再保存；当前声音模型留空时，会使用对应的基础模型。</p>${fields}<div class="drawer-actions">${btn('save-paths','保存路径',true)}${btn('stop-service','停止服务')}</div>`,'paths');
  }
  function openProfiles(){drawer('已保存的声音',state.profiles.length?`<label class="field">声音方案<select id="savedProfile">${state.profiles.map(item=>`<option value="${esc(item.id)}">${esc(item.name)}</option>`).join('')}</select></label><div class="drawer-actions">${btn('apply-profile','应用选中声音',true)}</div>`:'<p>这里还没有留下声音。准备自己的参考录音，再在控制台把喜欢的配置存下来。</p>','profiles');}
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
      if(action==='paths'){openPaths();return;}if(action==='reference'){openReference();return;}if(action==='profiles'){openProfiles();return;}
      if(action==='install-engine'||action==='install-environment'){
        const kind=action==='install-engine'?'engine':'environment';const file=await browse(kind);if(file)await run(action,{file},target.textContent);return;
      }
      if(action==='connect-harness'||action==='connect-airi'){const directory=await browse(action==='connect-airi'?'airi':'harness');if(directory)await run(action,{directory},target.textContent);return;}
      if(action==='browse-reference'){const path=await browse('reference');if(path){$('#referencePath').value=path;drafts.referencePath=path;}return;}
      if(action==='browse-path'){const key=target.dataset.field;const path=await browse(key.startsWith('python')||key==='ffmpeg'?'executable':'folder');if(path){$('#path_'+key).value=path;drafts['path_'+key]=path;}return;}
      if(action==='save-reference'){const result=await run(action,{reference:$('#referencePath').value,transcript:$('#referenceText').value},'保存参考声音');if(result)$('#drawer').close();return;}
      if(action==='save-paths'){const args={};for(const key of Object.keys(pathLabels))args[key]=$('#path_'+key).value;const result=await run(action,args,'保存引擎路径');if(result)$('#drawer').close();return;}
      if(action==='switch-engine'){await run(action,{engine:Number($('#activeEngine').value)},'切换当前引擎');return;}
      if(action==='apply-profile'){const result=await run(action,{id:$('#savedProfile').value},'应用声音');if(result)$('#drawer').close();return;}
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
