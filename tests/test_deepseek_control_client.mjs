import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';

let exported,registered;
const effects=[];
const style={dataset:{},textContent:'old cached css'};
const React={
  createElement:(type,props,...children)=>({type,props:props||{},children:children.flat()}),
  useState:value=>[typeof value==='function'?value():value,()=>{}],
  useRef:value=>({current:value}),
  useEffect:fn=>{effects.push(fn);},
};
const context={
  window:{__ModuleLoader__:{load(spec){assert.equal(spec.id,'dsh-agent-voice-public');exported=spec.factory(name=>name==='react'?React:{});}},addEventListener(){},removeEventListener(){}},
  document:{querySelector:()=>style,addEventListener(){},removeEventListener(){}},
  setTimeout:fn=>{fn();return 1;},clearTimeout(){},console,AbortSignal,
};
context.window.window=context.window;context.globalThis=context;
vm.runInNewContext(fs.readFileSync(new URL('../integrations/deepseek-control-client.js',import.meta.url),'utf8'),context);
assert.match(style.textContent,/background-color:var\(--av-surface\)/,'menu has an opaque backing');
assert.match(style.textContent,/body\[data-ds-dark-theme\]/,'theme follows host dark-mode setting');
assert.match(style.textContent,/\.avc-trigger\{[^}]*border:0;[^}]*background:transparent/,'entry has no frame or background');
assert.match(style.textContent,/\.avc-trigger:hover[^}]*background:transparent/,'entry stays transparent when hovered or open');
assert.ok(!style.textContent.includes('old cached css'),'existing stylesheet is refreshed');
assert.deepEqual(Array.from(exported.inject),['slots','remote','remote.commands','connection']);
const calls=[],snapshots=[];
exported.apply({inject(_deps,callback){callback({connection:{rpc:{async call(channel,endpoint,payload){snapshots.push({channel,endpoint,payload});return {ok:true,value:{running:true,playback:{state:'playing'}}};}}},remote:{commands:{async execute(agentId,line,attachments){calls.push({agentId,line,attachments});return {ok:true,value:{result:{kind:'success',text:'{"ok":true}'}}};}}},slots:{inject(name,fn){assert.equal(name,'conversation.input.left');fn();},register(spec,component){registered={spec,component};return {dispose(){}};}}});}});
assert.equal(registered.spec.id,'agent-voice-control');

let submitted=0,stateIndex=0;
React.useState=value=>[stateIndex++===0?true:value,()=>{}];
const wrapper=registered.component({sessionId:'test-session',inputActions:{setDraft:()=>{submitted++;},submit:()=>{submitted++;}}});
const tree=wrapper.type(wrapper.props);
function find(node,label){
  if(!node||typeof node!=='object')return null;
  if(node.type==='button'&&node.children.some(child=>child===label))return node;
  for(const child of node.children||[]){const match=find(child,label);if(match)return match;}
  return null;
}
const quiet=find(tree,'停止朗读');assert.ok(quiet,'composer menu contains stop-speech button');await quiet.props.onClick();
assert.equal(submitted,0,'controls must not write or submit a chat draft');
assert.equal(calls[0].agentId,'test-session');assert.equal(JSON.parse(calls[0].line.slice('/agent-voice ui '.length)).tool,'stop_speech');assert.equal(calls[0].attachments.length,0);
assert.equal((await wrapper.props.request('voice_playback_status',{})).playback.state,'playing');
assert.equal(calls.length,1,'status polling must not execute or log chat commands');
assert.equal(snapshots[0].channel,'/api');assert.equal(snapshots[0].endpoint,'agent-voice-playback');
assert.match(style.textContent,/@keyframes avc-wave/);
assert.match(style.textContent,/prefers-reduced-motion:reduce/);
assert.equal(exported.playbackPhase({running:true,busy:true,playback:{state:'generating'}}),'generating');
assert.equal(exported.playbackPhase({running:true,busy:true}),'unknown','busy alone never means playing');
assert.equal(exported.playbackPhase({running:false,playback:{state:'playing'}}),'offline');
const updates=[],failures=[],timers=new Map(),requests=[];let next=1,visible=true;
const watcher=exported.watchPlayback((...args)=>new Promise((resolve,reject)=>requests.push({args,resolve,reject})),value=>updates.push(value),error=>failures.push(error.message),{
  visible:()=>visible,schedule:(fn,delay)=>{const id=next++;timers.set(id,{fn,delay});return id;},cancel:id=>timers.delete(id),
});
async function flush(){for(let i=0;i<8;i++)await Promise.resolve();}
function nextPoll(){const [id,item]=timers.entries().next().value;timers.delete(id);item.fn();return item.delay;}
assert.equal(requests.length,1);assert.equal(requests[0].args[0],'voice_playback_status');
requests[0].resolve({running:true,playback:{state:'generating'}});await flush();assert.equal(nextPoll(),1100);
requests[1].resolve({running:true,playback:{state:'playing'}});await flush();assert.equal(nextPoll(),650);
watcher.refresh();assert.equal(requests.length,3,'never overlap polling requests');
requests[2].resolve({running:true,playback:{state:'playing'}});await flush();assert.equal(updates.length,2,'ignore response started before stop/refresh');
assert.equal(nextPoll(),0);requests[3].resolve({running:true,playback:{state:'idle'}});await flush();assert.equal(updates.at(-1).playback.state,'idle');
visible=false;watcher.refresh();assert.equal(timers.size,0);assert.equal(requests.length,4,'hidden page does not poll');
visible=true;watcher.refresh();requests[4].reject(new Error('disconnected'));await flush();assert.equal(failures.at(-1),'disconnected');
assert.equal(nextPoll(),3000);watcher.dispose();requests[5].resolve({running:true,playback:{state:'playing'}});await flush();assert.equal(updates.length,3);assert.equal(timers.size,0,'unmount releases polling timer');
console.log(JSON.stringify({ok:true,checks:['composer slot button','opaque menu and inherited theme','authenticated RPC control','no model submission','CSS playback wave and reduced motion','generation distinct from playback','adaptive polling without overlap','stale response ignored after stop','hidden page suspended','disconnect handled','unmount cleanup']}));
