import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
const root=new URL('../assets/setup/',import.meta.url);
test('native bridge correlates progress, resolves replies and rejects actual errors',async()=>{
  const sent=[];let receive;
  const window={chrome:{webview:{postMessage:m=>sent.push(m),addEventListener:(name,fn)=>receive=fn}}};
  vm.runInNewContext(await readFile(new URL('bridge.js',root),'utf8'),{window,setTimeout,clearTimeout,Map,Error});
  const pending=window.setupBridge.request('resources',{});
  assert.equal(sent.length,1);assert.equal(sent[0].action,'resources');
  receive({data:{type:'result',id:sent[0].id,ok:true,result:{message:'real result'}}});
  assert.equal((await pending).message,'real result');
  const failed=window.setupBridge.request('preview',{});
  receive({data:{type:'result',id:sent[1].id,ok:false,error:'generation failed'}});
  await assert.rejects(failed,/generation failed/);
});
test('frontend ships local CSP, ten steps, bounded illustrations and reduced motion',async()=>{
  const html=await readFile(new URL('index.html',root),'utf8');
  const css=await readFile(new URL('setup.css',root),'utf8');
  const js=await readFile(new URL('setup.js',root),'utf8');
  assert.match(html,/Content-Security-Policy/);assert.doesNotMatch(html,/https:\/\/cdn/);
  assert.match(css,/prefers-reduced-motion/);assert.match(css,/object-fit:\s*contain/);
  assert.match(js,/收到实际播放/);assert.match(js,/save-reference/);assert.match(js,/connect-airi/);
});
