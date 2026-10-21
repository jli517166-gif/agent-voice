(() => {
  'use strict';
  const $=selector=>document.querySelector(selector), B=window.setupBridge;
  const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  let state=null,shownStep=-1,selection=0,busy=false,logs=[],drafts={},toastTimer,resizeTimer;
  const btn=(action,text,primary=false,extra='')=>`<button data-action="${action}" class="${primary?'primary':'secondary'}" title="${text}" ${extra}>${text}</button>`;
  const actions=items=>`<div class="actions">${items.join('')}</div>`;
  const doc=(name,text)=>btn('doc',text,false,`data-doc="${name}"`);
  const resources=()=>`<div class="status-note" data-resources>${esc(state.resources)}</div>`;
  const pages=[
    {title:'先把声音，准备好。',lead:'一段独立的首次配置。按你的节奏完成，然后进入日常工作台。',art:'headphone.webp',label:'YOUR VOICE, YOUR WAY',body:()=>`<p class="muted">软件、TTS 引擎、音色与环境可以分开准备。你可以跳过任何步骤，稍后再回来。</p><div class="chips"><span>本地运行</span><span>模块安装</span><span>自由选择</span></div>${actions([doc('模块安装与配置指南.md','了解模块安装')])}`},
    {title:'先认识这台电脑。',lead:'检查显卡、驱动、可用空间与软件目录。先知道条件，再选择资源。',art:'computer.webp',label:'CHECK YOUR DEVICE',body:()=>`<p class="muted">检查不会安装驱动或修改系统。文件存在也不代表声音已经可用。</p>${actions([btn('computer','检查这台电脑',true),btn('recover','恢复未完成安装')])}`},
    {title:'先选一种声音引擎。',lead:'这里只选择生成声音的方式，不需要找文件。下一步会下载对应源码和模型。',art:'puzzle.webp',label:'CHOOSE AN ENGINE',body:()=>`<div class="tile-list">${[['CosyVoice','本地音色克隆，支持参考声音与训练权重。'],['GPT-SoVITS · v5 Turbo','使用对应版本模型和环境。'],['GPT-SoVITS · ProPlus','与 Turbo 独立选择，模型不混用。']].map((item,i)=>`<button class="tile ${selection===i?'selected':''}" data-engine="${i}" aria-pressed="${selection===i}"><strong>${item[0]}</strong><small>${item[1]}</small></button>`).join('')}</div><p class="muted">点击“下一步”才应用；跳过不会改变当前引擎。</p>`},
    {title:'下载所选引擎。',lead:'优先在线准备源码和基础模型。网络不可用，或你已有资源时，再选择本地安装。',art:'folder.webp',label:'引擎文件',body:()=>`${resources()}${actions([btn('download-engine','下载所选引擎',true),btn('resources','检查已有文件'),btn('stop-service','停止服务以安装')])}<p class="muted">这一步不安装 GPU 依赖。已有文件会复用，模型下载可能需要数 GB。</p><details id="localEngine" ${state.offline?'open':''}><summary>无法下载，或已经准备好本地资源？</summary><p class="muted">本地安装是替代途径，无需重复下载。</p>${actions([btn('install-engine','导入本地引擎包'),btn('paths','定位已安装引擎')])}</details>${actions([doc('TTS使用手册.md','下载与安装说明')])}`},
    {title:'检查运行依赖。',lead:'引擎文件已经单独准备。这一步只检查并补齐 Python、GPU 库与音频依赖。',art:'puzzle.webp',label:'运行环境',body:()=>`<label><input type="checkbox" id="offline" ${state.offline?'checked':''}>严格离线：只使用本地文件</label>${resources()}${actions([btn('inspect-environment','检查已有环境',true),btn('repair-environment','补齐缺失环境'),btn('stop-service','停止服务以安装')])}<details><summary>网络不可用？使用离线环境包</summary>${actions([btn('install-environment','导入离线环境包')])}</details>${actions([doc('环境补齐说明.md','环境安装说明')])}`},
    {title:'选择你的声音。',lead:'导入已有音色包，或准备自己的参考录音与逐字台词。',art:'mic.webp',label:'MAKE IT YOURS',body:()=>`${resources()}${actions([btn('import-voice','导入音色包',true),btn('profiles','选择已保存的声音'),btn('reference','配置参考声音')])}<p class="muted">音色包会匹配引擎和版本；导入后仍需真实试听。</p>`},
    {title:'听到，再确认。',lead:'程序检查实际播放是否完成，你来判断音色、语气和节奏。',art:'headphone.webp',label:'LISTEN BEFORE CONFIRMING',body:()=>`<p>试听会启动服务，并生成、播放一段测试文本。</p>${actions([btn('preview','生成并播放',true),btn('stop-preview','停止试听')])}${actions([btn('confirm','确认声音合适')])}<p class="muted" data-heard>${state.heardVoice?'已确认当前声音。':'当前声音尚未获得你的听感确认。'}</p><p class="muted">更换引擎、模型或参考条件后，需要重新试听。</p>`},
    {title:'让对话自动说出来。',lead:'可选。当前实测支持 Codex 与 DeepSeek Harness。',art:'link.webp',label:'CONNECT YOUR HARNESS',body:()=>`${actions([btn('connect-harness','选择目录并接入验证',true),doc('接入说明.md','查看接入步骤')])}<p class="muted">自动识别所选目录、安装连接器，再核对回复和播放。收到实际播放完成回执后才庆祝成功。</p>${actions([btn('copy-challenge','复制手动验证短句')])}<p class="muted">若桌面账号需要手动发送短句，可查看执行过程并复制；重载与登录问题会明确报告。</p>`},
    {title:'把声音交给桌宠。',lead:'AIRI 完全可选。保持关闭，也能在本机正常播放。',art:'airi-11-icon.png',label:'AIRI · OPTIONAL COMPANION',body:()=>`<label><input type="checkbox" id="airiEnabled" ${state.airiEnabled?'checked':''}>启用 AIRI 桌宠联动</label>${actions([btn('connect-airi','选择原 AIRI 并验证',true)])}<p class="muted">选择已经登录、包含 airi.exe 的原目录，不复制另一套 AIRI。播放与嘴型回执通过后才算成功。</p><p class="muted">AIRI 更新可能覆盖桥接；失效时重新接入验证。</p>`},
    {title:'准备告一段落。',lead:'完成或跳过，都如实记录。接下来进入你的语音工作台。',art:'music.webp',label:'READY WHEN YOU ARE',body:()=>`${resources()}${actions([btn('summary','刷新实际状态',true),doc('模块安装与配置指南.md','安装与故障说明')])}<p class="muted">进入软件不会把未完成的项目标记为通过；随时可从设置重新配置。</p>`}
  ];
  const pathLabels={pythonService:'主服务 Python',pythonTts:'CosyVoice Python',cosyRepo:'CosyVoice 源码',cosyModel:'CosyVoice 基础模型',pythonGpt:'GPT-SoVITS Python',gptRepo:'GPT-SoVITS 源码',gptModel:'GPT-SoVITS 基础模型',ffmpeg:'FFmpeg',baseModel:'CosyVoice 当前声音模型（可留空）',voiceGptModel:'GPT 当前声音模型（可留空）'};
  const wave=()=>'<span class="scene-wave">'+Array.from({length:13},()=>'<i></i>').join('')+'</span>';
  function scene(step,page){
    const motifs=[wave(),'<span class="scan-line"></span><span class="scene-tags"><b>硬件</b><b>驱动</b><b>存储</b></span>','<span class="signal-route"><b>文字</b><i></i><b>声音</b></span>','<span class="transfer-docs"><i></i><i></i><i></i></span><span class="scene-tags"><b>源码</b><b>基础模型</b></span>','<span class="dependency-grid"><b>Python</b><b>GPU 库</b><b>音频依赖</b></span>',wave(),wave(),'<span class="message-trail"><i>Codex</i><i>DeepSeek</i><i>语音服务</i></span>',wave()+'<svg class="airi-ribbons" viewBox="0 0 650 400"><path d="M-60 340C150 420 260-100 710 160"/><path d="M-50 370C160 450 290-70 720 190"/></svg>','<span class="receipt-stack"><i></i><i></i><i></i></span>'];
    const path=step===8?'art/airi-11-icon.png':'art/cutouts/'+page.art.replace('.webp','.png');
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
      selection=state.engine;shownStep=state.step;const page=pages[state.step];
      $('#page').className='step-'+state.step;
      $('#page').innerHTML=`<div class="copy"><h1 tabindex="-1">${page.title}</h1><p class="lead">${page.lead}</p><div class="body">${page.body()}</div></div>${scene(state.step,page)}`;
      $('#viewport').scrollTop=0;$('#page h1').focus({preventScroll:true});
      if(!busy){$('#feedback').hidden=true;$('#result').textContent='';}
    }else{
      document.querySelectorAll('[data-resources]').forEach(item=>item.textContent=state.resources);
      const heard=$('[data-heard]');if(heard)heard.textContent=state.heardVoice?'已确认当前声音。':'当前声音尚未获得你的听感确认。';
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
      if(['save-reference','save-paths','apply-profile','import-voice'].includes(action))drafts={};
      if(['save-reference','save-paths','preferences','copy-challenge','save-airi'].includes(action))toast(result?.message||'已保存');
      return result;
    }catch(error){if(action==='download-engine'&&$('#localEngine'))$('#localEngine').open=true;if($('#drawer').open){let note=$('#drawerError');if(!note){note=document.createElement('p');note.id='drawerError';note.className='error-copy';note.setAttribute('role','alert');$('#drawerBody').prepend(note);}note.textContent=error.message;}if(error.state)render(error.state);feedback('未完成 · 请修复后重试',error.message,true);return null;}
    finally{if(!parallel){busy=false;document.body.classList.remove('working');delete document.body.dataset.task;$('#feedback .progress').hidden=true;updateDisabled();}}
  }
  async function browse(kind){const value=await B.request('browse',{kind});return value.path||'';}
  const field=(id,label,value,multiline=false)=>`<label class="field">${label}${multiline?`<textarea id="${id}">${esc(drafts[id]??value)}</textarea>`:`<input id="${id}" value="${esc(drafts[id]??value)}">`}</label>`;
  function drawer(title,body,mode){$('#drawerTitle').textContent=title;$('#drawerBody').innerHTML=body;$('#drawer').dataset.mode=mode;$('#drawer').showModal();updateDisabled();}
  function openReference(){drawer('参考声音',`<p class="muted">录音与台词必须对应。这里只保存选择，下一步再真实试听。</p><div class="field-row">${field('referencePath','参考录音',state.reference)}${btn('browse-reference','选择')}</div>${field('referenceText','与录音逐字一致的台词',state.transcript,true)}<div class="drawer-actions">${btn('save-reference','保存参考声音',true)}</div>`,'reference');}
  function openPaths(){
    const fields=Object.entries(pathLabels).map(([key,label])=>`<div class="field-row">${field('path_'+key,label,key==='baseModel'?state.baseModel:key==='voiceGptModel'?state.gptModel:state.paths[key])}${btn('browse-path','选择',false,`data-field="${key}"`)}</div>`).join('');
    drawer('已有引擎与模型路径',`<p class="muted">先停止服务再保存。当前声音模型留空时使用对应基础模型。</p>${fields}<div class="drawer-actions">${btn('save-paths','保存路径',true)}${btn('stop-service','停止服务')}</div>`,'paths');
  }
  function openProfiles(){drawer('已保存的声音',state.profiles.length?`<label class="field">声音方案<select id="savedProfile">${state.profiles.map(item=>`<option value="${esc(item.id)}">${esc(item.name)}</option>`).join('')}</select></label><div class="drawer-actions">${btn('apply-profile','应用选中声音',true)}</div>`:'<p>还没有已保存的声音。请导入音色包或准备参考录音。</p>','profiles');}
  function celebrate(){
    const layer=$('#celebration .celebration-lines');layer.replaceChildren();
    if(!state.reduceMotion&&!matchMedia('(prefers-reduced-motion:reduce)').matches)for(let i=0;i<70;i++){
      const piece=document.createElement('i');piece.className='confetti';piece.style.setProperty('--dx',(Math.random()-.5)*innerWidth*1.6+'px');piece.style.setProperty('--dy',(Math.random()-.55)*innerHeight*1.5+'px');piece.style.setProperty('--rotation',Math.random()*600+'deg');layer.append(piece);
    }
    $('#celebration').hidden=false;
  }
  document.addEventListener('click',async event=>{
    const target=event.target.closest('button');if(!target||target.disabled)return;
    if(target.dataset.engine!==undefined){selection=Number(target.dataset.engine);document.querySelectorAll('[data-engine]').forEach(item=>{const chosen=Number(item.dataset.engine)===selection;item.classList.toggle('selected',chosen);item.setAttribute('aria-pressed',chosen);});return;}
    const action=target.dataset.action;if(!action)return;
    try{
      if(action==='doc'){await run('open-doc',{name:target.dataset.doc},'打开说明');return;}
      if(action==='paths'){openPaths();return;}if(action==='reference'){openReference();return;}if(action==='profiles'){openProfiles();return;}
      if(action==='install-engine'||action==='install-environment'||action==='import-voice'){
        const kind=action==='install-engine'?'engine':action==='install-environment'?'environment':'voice';const file=await browse(kind);if(file)await run(action,{file},target.textContent);return;
      }
      if(action==='connect-harness'||action==='connect-airi'){const directory=await browse(action==='connect-airi'?'airi':'harness');if(directory)await run(action,{directory},target.textContent);return;}
      if(action==='browse-reference'){const path=await browse('reference');if(path){$('#referencePath').value=path;drafts.referencePath=path;}return;}
      if(action==='browse-path'){const key=target.dataset.field;const path=await browse(key.startsWith('python')||key==='ffmpeg'?'executable':'folder');if(path){$('#path_'+key).value=path;drafts['path_'+key]=path;}return;}
      if(action==='save-reference'){const result=await run(action,{reference:$('#referencePath').value,transcript:$('#referenceText').value},'保存参考声音');if(result)$('#drawer').close();return;}
      if(action==='save-paths'){const args={};for(const key of Object.keys(pathLabels))args[key]=$('#path_'+key).value;const result=await run(action,args,'保存引擎路径');if(result)$('#drawer').close();return;}
      if(action==='apply-profile'){const result=await run(action,{id:$('#savedProfile').value},'应用声音');if(result)$('#drawer').close();return;}
      await run(action,{},target.textContent);
    }catch(error){feedback('未完成 · 请重试',error.message,true);}
  });
  $('#next').onclick=()=>run('next',{engine:selection},state?.step===2?'应用所选引擎':'继续配置');
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
