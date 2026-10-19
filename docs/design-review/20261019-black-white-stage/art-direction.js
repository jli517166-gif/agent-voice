// Agent Voice design review, independent from the installed program.
const direction=[
 {main:'headphone',small:'music',name:'声音入场'},
 {main:'computer',small:'setting',name:'电脑与运行条件'},
 {main:'setting',small:'mic',name:'引擎驱动声音'},
 {main:'folder',small:'copy',name:'资源进入本机'},
 {main:'tools',small:'puzzle',name:'环境装配'},
 {main:'mic',small:'music',name:'选择属于你的声音'},
 {main:'headphone',small:'play',name:'试听与播放'},
 {main:'chat',small:'link',name:'对话交接'},
 {main:'airi-11-icon.png',small:null,name:'AIRI 原版图标'},
 {main:'puzzle',small:'music',name:'配置组合'}
];
let current=null;
const image=(name,cls)=>`<img class="${cls}" src="assets/${name.includes('.')?name:name+'.webp'}" alt="" draggable="false">`;
const ticks=()=>Array.from({length:19},(_,i)=>`<i style="--i:${i};--h:${8+Math.sin((i+1)/20*Math.PI)*Math.abs(Math.sin(i*.72))*42}px"></i>`).join('');
function visualMarkup(step){
 const p=direction[step];let extra='';
 if(step===0)extra='<div class="intro-staff">'+ticks()+'</div>';
 if(step===1)extra='<div class="diagnostic-scan"></div><div class="spec-tags"><span>硬件</span><span>驱动</span><span>存储</span></div>';
 if(step===2)extra='<div class="engine-route"><span>文字</span><i></i><span>声音</span></div>';
 if(step===3)extra='<div class="resource-strips"><i></i><i></i><i></i></div>';
 if(step===4)extra='<div class="dependency-pins"><span>Python</span><span>GPU</span><span>依赖库</span></div>';
 if(step===5)extra='<div class="voice-signature">'+ticks()+'</div>';
 if(step===6)extra='<div class="listening-wave">'+ticks()+'</div>';
 if(step===7)extra='<div class="client-tags"><span data-client="codex">Codex</span><span data-client="deepseek">DeepSeek</span></div><div class="conversation-path"><i></i></div>';
 if(step===8)extra='<div class="airi-label">AIRI</div><div class="airi-sound">'+ticks()+'</div><div class="airi-caption">使用你的原 AIRI</div>';
 if(step===9)extra='<div class="package-names"><span>软件</span><span>引擎</span><span>音色</span></div>';
 return image(p.main,'hero-object')+(p.small?image(p.small,'support-object'):'')+extra;
}
function teardown(){current?.();current=null}
function mount(){
 teardown();const screen=[...document.querySelectorAll('#screens .screen')].findLast(n=>n.getAttribute('aria-hidden')!=='true');
 if(!screen||document.querySelector('#window').classList.contains('workspace'))return;
 const step=+(screen.dataset.step||0);screen.dataset.direction=step;
 let host=screen.querySelector('.boot-art,.step-symbol');
 if(!host){const grid=screen.querySelector('.step-grid.no-art');if(grid){grid.querySelector('.sound-bars')?.remove();const copy=document.createElement('div');while(grid.firstChild)copy.append(grid.firstChild);grid.replaceChildren(copy);grid.classList.remove('no-art');host=document.createElement('div');host.className='step-symbol';grid.append(host)}}
 if(!host)return;host.replaceChildren();host.classList.add('art-host');host.removeAttribute('aria-hidden');
 const floor=document.createElement('div');floor.className='art-field';floor.setAttribute('aria-hidden','true');screen.prepend(floor);
 const art=document.createElement('figure');art.className='art-stage';art.dataset.scene=step;art.setAttribute('aria-label',direction[step].name+'（设计示意）');art.innerHTML=visualMarkup(step);host.append(art);
 function fitField(){
  if(innerWidth<=620){art.style.clipPath='none';return}
  const s=screen.getBoundingClientRect(),a=art.getBoundingClientRect();
  const start=step===8?.63:.65,end=step===8?.52:.54;
  const pts=[[s.left+s.width*start,s.top],[s.right,s.top],[s.right,s.bottom],[s.left+s.width*end,s.bottom]];
  art.style.clipPath=`polygon(${pts.map(([x,y])=>`${x-a.left}px ${y-a.top}px`).join(',')})`;
 }
 const sizing=new ResizeObserver(fitField);sizing.observe(screen);fitField();
 if(step===7){const on=[...screen.querySelectorAll('[role="checkbox"]')].map(n=>n.getAttribute('aria-checked')==='true');art.dataset.codex=on[0]?'on':'off';art.dataset.deepseek=on[1]?'on':'off'}
 if(step===8){art.dataset.airi=screen.querySelector('[role="switch"]')?.getAttribute('aria-checked')==='true'?'on':'off'}
 let timer=0,animations=[];
 function freeze(){clearTimeout(timer);animations.forEach(a=>{try{a.finish()}catch{}});art.dataset.playing='false'}
 function play(){
  freeze();art.classList.remove('art-reveal');floor.classList.remove('field-reveal');void art.getBoundingClientRect();
  const reduced=document.body.classList.contains('reduced')||matchMedia('(prefers-reduced-motion:reduce)').matches;
  art.classList.toggle('still',reduced);art.classList.add('art-reveal');floor.classList.add('field-reveal');
  animations=[...art.getAnimations({subtree:true}),...floor.getAnimations()];
  if(reduced){freeze();return}
  const slow=new URLSearchParams(location.search).get('slow')==='1';if(slow)animations.forEach(a=>a.playbackRate=.5);
  art.dataset.playing='true';timer=setTimeout(freeze,slow?3200:1600);
 }
 const onAction=e=>{const a=e.target.closest('[data-action]')?.dataset.action;if((step===1&&a==='computer')||(step===6&&a==='preview'))play()};
 screen.addEventListener('click',onAction);play();
 const cleanup=()=>{freeze();sizing.disconnect();animations.forEach(a=>a.cancel());screen.removeEventListener('click',onAction);floor.remove();art.remove()};cleanup.freeze=freeze;current=cleanup;
}
window.addEventListener('design-route',mount);window.addEventListener('design-dispose',teardown);window.addEventListener('pagehide',teardown);
document.addEventListener('keydown',e=>{if(e.key==='Escape')current?.freeze?.()});
document.addEventListener('visibilitychange',()=>{if(document.hidden)current?.freeze?.();else mount()});
function modal(title,content){document.querySelector('#modalTitle').textContent=title;document.querySelector('#modalContent').innerHTML=content;document.querySelector('#modal').hidden=false;document.querySelector('#closeModal').focus()}
document.querySelector('#references').onclick=()=>modal('素材与设计说明',`<p>这轮保留已认可的文字层级与步骤转场，改为黑白分区、大幅主体和逐页独立编排。</p><p>AIRI 直接使用本机 E:\\App\\AIRI 中的原版图标，保留粉色。其他页面使用浏览器中核对过的 <a href="https://3dicons.co/collection/b68cf8-v1" target="_blank" rel="noreferrer">3dicons V1</a> 白色实体素材，许可为 CC0。</p><p>构图草图由 imagegen 辅助设计。实页沿用原有无衬线字体；草图中的衬线字体不作为字体变更。</p><p>12ui 已实际调用，但云端并发限流，未取得候选图；没有把失败运行当作设计结果。</p><p>动画是动作示意，不表示检测、安装或连接成功。全部数据与操作为审核演示。</p><p><a href="设计与素材说明.md" target="_blank">本轮设计、来源和检查记录</a></p>`);
document.querySelector('#airiConcept').onclick=()=>modal('AIRI 页面 · 构图草图','<p>用于审核主体比例、黑白分区和品牌色。实页保留原来的文字字体与导航逻辑。</p><img src="assets/AIRI-构图草图.png" alt="AIRI 联动页面构图草图" style="width:100%;height:auto;display:block"><p><a href="assets/AIRI-构图草图.png" target="_blank">打开大图</a></p>');
mount();
