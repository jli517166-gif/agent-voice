import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const html=fs.readFileSync(new URL('../integrations/voice-player.html',import.meta.url),'utf8');
const script=html.match(/<script>([\s\S]+)<\/script>/)[1];
async function fixture({supportsTools=true,rejectStop=false}={}){
  const nodes=Object.fromEntries(['read','stop','text','status','refresh'].map(id=>[id,{disabled:true,textContent:'',value:'',className:''}]));
  const calls=[],listeners=[],timers=new Map();let nextTimer=1;
  const parent={postMessage(message){
    if(message.method!=='tools/call'&&message.method!=='ui/initialize')return;
    let result;
    if(message.method==='ui/initialize')result={hostCapabilities:supportsTools?{serverTools:{}}:{},hostContext:{theme:'dark',styles:{variables:{'--color-text-primary':'#fff'}}}};
    else{
      calls.push(message.params);
      result={structuredContent:message.params.name==='voice_service_status'?{running:false}:message.params.name==='stop_speech'&&rejectStop?undefined:{ok:true,jobId:'job'},isError:message.params.name==='stop_speech'&&rejectStop,content:[{type:'text',text:'后端未能停止'}]};
    }
    queueMicrotask(()=>listeners.forEach(fn=>fn({source:parent,data:{jsonrpc:'2.0',id:message.id,result}})));
  }};
  const window={parent,addEventListener(_name,fn){listeners.push(fn);}};
  const style={setProperty(){}};
  const document={getElementById:id=>nodes[id],documentElement:{style,scrollHeight:330},body:{}};
  vm.runInNewContext(script,{window,document,console,setTimeout(fn){const id=nextTimer++;timers.set(id,fn);return id;},clearTimeout:id=>timers.delete(id),ResizeObserver:class{observe(){}},Map,Error});
  for(let i=0;i<8;i++)await Promise.resolve();
  return {nodes,calls,parent,listeners,timers};
}
const f=await fixture();
assert.equal(f.nodes.read.disabled,false);assert.equal(f.calls.length,1);assert.equal(f.calls[0].name,'voice_service_status','opening must never start playback');
f.nodes.text.value='工作好久啦，起来动一动、喝口水吧～';
await f.nodes.read.onclick();
assert.equal(f.calls.at(-1).name,'read_text');assert.equal(f.calls.at(-1).arguments.text,f.nodes.text.value);
await f.nodes.stop.onclick();assert.equal(f.calls.at(-1).name,'stop_speech');assert.match(f.nodes.status.textContent,/已停止/);
const count=f.calls.length;
for(const listener of f.listeners)listener({source:{},data:{jsonrpc:'2.0',method:'ui/notifications/tool-result',params:{structuredContent:{text:'恶意修改'}}}});
assert.notEqual(f.nodes.text.value,'恶意修改');assert.equal(f.calls.length,count);
for(const listener of f.listeners)listener({source:f.parent,data:{jsonrpc:'2.0',method:'ui/notifications/tool-result',params:{structuredContent:{text:'这是一段新正文。',status:{running:true,queued:2}}}}});
assert.equal(f.nodes.text.value,'这是一段新正文。');assert.equal(f.calls.length,count,'tool results must never autoplay');
const rejected=await fixture({rejectStop:true});await rejected.nodes.stop.onclick();assert.match(rejected.nodes.status.textContent,/后端未能停止/);assert.ok(!rejected.nodes.status.textContent.includes('已停止'));
const unsupported=await fixture({supportsTools:false});assert.equal(unsupported.nodes.read.disabled,true);assert.equal(unsupported.calls.length,0);assert.match(unsupported.nodes.status.textContent,/未提供/);
assert.equal(f.timers.size,0,'completed requests must not leak timeout handlers');
console.log(JSON.stringify({ok:true,checks:['host bridge handshake','exact text','stop through MCP','no autoplay on open or updates','reject foreign frame messages','surface backend errors','unsupported-host fallback','timeout cleanup']}));
