import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, unlink, rmdir } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { startHeartbeat, send } from '../integrations/connector.mjs';

const root = await mkdtemp(path.join(os.tmpdir(), 'agent-voice-heartbeat-'));
const data = path.join(root, 'data'), settings = path.join(data, 'settings.json');
await mkdir(data);
await writeFile(settings, JSON.stringify({port: 12345, token: 'test-token', harness: {deepseek: true}}));
const original = {fetch: globalThis.fetch, setInterval: globalThis.setInterval, clearInterval: globalThis.clearInterval};
const requests = [], timers = new Map();
let stop;
const waitFor = async predicate => {
  const deadline = Date.now() + 3000;
  while (!predicate()) { if (Date.now() > deadline) throw new Error('connector timeout'); await new Promise(resolve => setTimeout(resolve, 10)); }
};
try {
  globalThis.fetch = async (url, options) => { requests.push({url, options}); return {ok: true}; };
  globalThis.setInterval = (callback, ms) => { assert.equal(ms, 30000); const timer = {unref(){this.unreferenced=true;}}; timers.set(timer, callback); return timer; };
  globalThis.clearInterval = timer => timers.delete(timer);
  stop = startHeartbeat('deepseek', root);
  await waitFor(() => requests.length === 1);
  assert.ok(requests[0].url.endsWith('/api/harness/deepseek/heartbeat'));
  assert.equal(requests[0].options.headers.Authorization, 'Bearer test-token');
  assert.equal([...timers.keys()][0].unreferenced, true);
  [...timers.values()][0](); await waitFor(() => requests.length === 2);
  const oldTimer = [...timers.keys()][0];
  stop = startHeartbeat('deepseek', root);
  assert.equal(timers.size, 1); assert.equal(timers.has(oldTimer), false);
  await waitFor(() => requests.length === 3);
  await send('deepseek', {type:'turn/end', text:'完成'}, root);
  assert.ok(requests[3].url.endsWith('/api/harness/deepseek'));
  assert.deepEqual(JSON.parse(requests[3].options.body), {type:'turn/end', text:'完成'});
  stop(); assert.equal(timers.size, 0);
  console.log(JSON.stringify({ok:true, checks:['startup heartbeat','periodic heartbeat','bearer authentication','non-blocking timer','reload disposes previous timer','final event unchanged','stop disposes timer']}));
} finally {
  stop?.(); Object.assign(globalThis, original);
  await unlink(settings); await rmdir(data); await rmdir(root);
}

const { default: hostPlugin, apply, inject, createReplyReader, runVoiceCommand } = await import('../integrations/deepseek-plugin.mjs');
assert.equal(hostPlugin.apply, apply);
assert.equal(hostPlugin.inject, inject);
assert.deepEqual(inject, []);
let registered, disposed;
const listeners = new Map();
const fake = {
  inject(dependencies,callback) { assert.deepEqual(dependencies,['commands']);callback(this); },
  commands: { register(definition) { registered = definition; return {dispose(){disposed = true;}}; } },
  on(name, callback) { listeners.set(name, callback); },
};
apply(fake);
assert.equal((await runVoiceCommand('ui {"tool":"execute_shell"}')).kind,'error');
assert.equal(registered.name, 'agent-voice');
assert.equal(registered.definitionId, 'agent-voice.control');
assert.match(registered.input.hint, /profile/);
const help = await runVoiceCommand('help');
assert.equal(help.kind, 'success'); assert.match(help.text, /Agent Voice/); assert.match(help.text, /停止当前朗读/);
listeners.get('dispose')(); assert.equal(disposed, true);
const headlessListeners=new Map();
apply({inject(){},on(name,callback){headlessListeners.set(name,callback);}});
assert.equal(typeof headlessListeners.get('session/event'),'function','reply reader loads without commands');
headlessListeners.get('dispose')();
console.log(JSON.stringify({ok:true, checks:['DeepSeek unique /agent-voice registered in composer','command help is local','command registration disposed']}));

const spoken=[];let releaseFirst;
const firstDelivered=new Promise(resolve=>{releaseFirst=resolve;});
const read=createReplyReader(async event=>{spoken.push(event);if(spoken.length===1)await firstDelivered;});
const session={id:'main',header:{}};
const message=(seq,text)=>({type:'assistant/message',seq,surfaceOp:'append',data:{turn:1,step:seq,message:{content:[{type:'reasoning',text:'内部推理'},{type:'text',text}]}}});
const early=read(session,message(10,'我先检查设置。'));
await Promise.resolve();assert.equal(spoken.length,1,'progress sent before turn end');
const later=read(session,message(20,'检查完成。'));
await Promise.resolve();assert.equal(spoken.length,1,'second message waits for first delivery');
releaseFirst();await later;await early;
await read(session,message(20,'检查完成。'));
await read(session,{type:'turn/end',data:{turn:1}});
await read(session,{...message(30,'压缩后的旧历史'),surfaceOp:{op:'replace'}});
await read({id:'child',header:{origin:'subagent'}},message(40,'子任务内部文字'));
await read(session,{type:'tool/result',data:{text:'工具输出'}});
assert.deepEqual(spoken.map(e=>e.text),['我先检查设置。','检查完成。']);
assert.deepEqual(spoken.map(e=>e.messageId),['10','20']);
console.log(JSON.stringify({ok:true,checks:['read progress before turn end','ordered delivery','no final replay','no reasoning, tool or compaction text','no subagent speech']}));
