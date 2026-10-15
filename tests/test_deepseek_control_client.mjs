import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';

let exported,registered;
const effects=[];
const React={
  createElement:(type,props,...children)=>({type,props:props||{},children:children.flat()}),
  useState:value=>[typeof value==='function'?value():value,()=>{}],
  useRef:value=>({current:value}),
  useEffect:fn=>{const dispose=fn();if(typeof dispose==='function')effects.push(dispose);},
};
const context={
  window:{__ModuleLoader__:{load(spec){assert.equal(spec.id,'dsh-agent-voice-public');exported=spec.factory(name=>name==='react'?React:{});}},addEventListener(){},removeEventListener(){}},
  document:{querySelector:()=>({}),addEventListener(){},removeEventListener(){}},
  setTimeout:fn=>{fn();return 1;},clearTimeout(){},console,
};
context.window.window=context.window;context.globalThis=context;
vm.runInNewContext(fs.readFileSync(new URL('../integrations/deepseek-control-client.js',import.meta.url),'utf8'),context);
assert.deepEqual(Array.from(exported.inject),['slots']);
exported.apply({inject(_deps,callback){callback({slots:{inject(name,fn){assert.equal(name,'conversation.input.left');fn();},register(spec,component){registered={spec,component};return {dispose(){}};}}});}});
assert.equal(registered.spec.id,'agent-voice-control');

let draft='',submitted=0;
React.useState=()=>[true,()=>{}];
const tree=registered.component({inputActions:{setDraft:value=>{draft=value;},submit:()=>{submitted++;}}});
function find(node,label){
  if(!node||typeof node!=='object')return null;
  if(node.type==='button'&&node.children.some(child=>child===label))return node;
  for(const child of node.children||[]){const match=find(child,label);if(match)return match;}
  return null;
}
const quiet=find(tree,'停止朗读');assert.ok(quiet,'composer menu contains stop-speech button');quiet.props.onClick();
assert.equal(draft,'/voice quiet');assert.equal(submitted,1);
for(const dispose of effects)dispose();
console.log(JSON.stringify({ok:true,checks:['composer slot button','visible control menu','MCP command executes from button']}));
