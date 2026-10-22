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
  const failed=window.setupBridge.request('check-mcp',{});
  receive({data:{type:'result',id:sent[1].id,ok:false,error:'MCP disabled'}});
  await assert.rejects(failed,/MCP disabled/);
});
test('frontend ships local CSP, ten steps, bounded illustrations and reduced motion',async()=>{
  const html=await readFile(new URL('index.html',root),'utf8');
  const css=await readFile(new URL('setup.css',root),'utf8');
  const js=await readFile(new URL('setup.js',root),'utf8');
  assert.match(html,/Content-Security-Policy/);assert.doesNotMatch(html,/https:\/\/cdn/);
  assert.match(css,/prefers-reduced-motion/);assert.match(css,/object-fit:\s*contain/);
  assert.match(js,/使用须知/);assert.match(js,/Apache-2.0/);assert.match(js,/MIT/);assert.match(js,/connect-airi/);
  assert.match(js,/state.stepLabel/);assert.match(js,/state.legalAccepted/);
  assert.match(js,/check-mcp/);assert.match(js,/不发聊天消息、不生成语音/);
  assert.doesNotMatch(js,/save-reference|openReference|openProfiles|btn\('preview'|btn\('confirm'|copy-challenge/);
  assert.doesNotMatch(js,/import-voice|导入音色包|\.ttsmodel/);
});
