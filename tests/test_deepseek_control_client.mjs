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
  useEffect:fn=>{const dispose=fn();if(typeof dispose==='function')effects.push(dispose);},
};
const context={
  window:{__ModuleLoader__:{load(spec){assert.equal(spec.id,'dsh-agent-voice-public');exported=spec.factory(name=>name==='react'?React:{});}},addEventListener(){},removeEventListener(){}},
  document:{querySelector:()=>style,addEventListener(){},removeEventListener(){}},
  setTimeout:fn=>{fn();return 1;},clearTimeout(){},console,
};
context.window.window=context.window;context.globalThis=context;
vm.runInNewContext(fs.readFileSync(new URL('../integrations/deepseek-control-client.js',import.meta.url),'utf8'),context);
assert.match(style.textContent,/background-color:var\(--av-surface\)/,'menu has an opaque backing');
assert.match(style.textContent,/body\[data-ds-dark-theme\]/,'theme follows host dark-mode setting');
assert.match(style.textContent,/\.avc-trigger\{[^}]*border:0;[^}]*background:transparent/,'entry has no frame or background');
assert.match(style.textContent,/\.avc-trigger:hover[^}]*background:transparent/,'entry stays transparent when hovered or open');
assert.ok(!style.textContent.includes('old cached css'),'existing stylesheet is refreshed');
assert.deepEqual(Array.from(exported.inject),['slots','remote','remote.commands']);
const calls=[];
exported.apply({inject(_deps,callback){callback({remote:{commands:{async execute(agentId,line,attachments){calls.push({agentId,line,attachments});return {ok:true,value:{result:{kind:'success',text:'{"ok":true}'}}};}}},slots:{inject(name,fn){assert.equal(name,'conversation.input.left');fn();},register(spec,component){registered={spec,component};return {dispose(){}};}}});}});
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
for(const dispose of effects)dispose();
console.log(JSON.stringify({ok:true,checks:['composer slot button','opaque menu and refreshed stylesheet','authenticated RPC control','no model submission']}));
