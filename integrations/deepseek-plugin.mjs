import { send, startHeartbeat, mcpControl, playbackStatus, synthesizePetVoice } from './connector.mjs';
import { writeFile } from 'node:fs/promises';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { appRoot } from './connector.mjs';
import { createHash } from 'node:crypto';
export const name = 'agent-voice-public';
export const inject = [];
const controlTools=new Set(['voice_service_status','start_voice_service','stop_voice_service','stop_speech','list_voice_profiles','apply_voice_profile','set_auto_reading','open_singing_app','list_singing_engines']);

function commandHelp() {
  return ['🎙 Agent Voice 控制台', 'status  状态', 'start  开启服务', 'stop  关闭服务',
    'quiet  停止当前朗读', 'profiles  声音方案', 'profile <ID>  切换声音', 'auto on|off  自动朗读',
    'sing  打开独立唱歌软件', 'sing engines  查看唱歌引擎'].join('\n');
}
export async function runVoiceCommand(rawInput = '') {
  if(rawInput.trim().startsWith('ui ')){
    try{
      const payload=JSON.parse(rawInput.trim().slice(3));
      if(!controlTools.has(payload.tool))throw new Error('未知语音控制操作');
      return {kind:'success',text:JSON.stringify(await mcpControl('deepseek',payload.tool,payload.args??{}))};
    }catch(error){return {kind:'error',text:error.message};}
  }
  const [action = 'status', ...values] = rawInput.trim().split(/\s+/).filter(Boolean);
  const normalized = action.toLowerCase();
  let tool = 'voice_service_status', args = {};
  if (normalized === 'help') return {kind:'success', text:commandHelp()};
  if (normalized === 'start') tool = 'start_voice_service';
  else if (normalized === 'stop') tool = 'stop_voice_service';
  else if (normalized === 'quiet' || normalized === 'mute') tool = 'stop_speech';
  else if (normalized === 'profiles') tool = 'list_voice_profiles';
  else if (normalized === 'profile') {
    if (!values[0]) return {kind:'error', text:'请填写声音方案 ID。先输入 /agent-voice profiles 查看。'};
    tool = 'apply_voice_profile'; args = {profile_id:values.join(' ')};
  } else if (normalized === 'auto') {
    if (!['on','off'].includes((values[0] || '').toLowerCase())) return {kind:'error', text:'用法：/agent-voice auto on 或 /agent-voice auto off'};
    tool = 'set_auto_reading'; args = {enabled:values[0].toLowerCase() === 'on'};
  } else if (normalized === 'sing') {
    tool = (values[0] || '').toLowerCase() === 'engines' ? 'list_singing_engines' : 'open_singing_app';
  } else if (normalized !== 'status') return {kind:'error', text:'未知操作。\n' + commandHelp()};
  try {
    const result = await mcpControl('deepseek', tool, args);
    if (tool === 'voice_service_status') return {kind:'success', text:`🎙 ${result.running ? '服务运行中' : '服务未启动'} · 自动朗读${result.autoReading ? '已开启' : '已关闭'} · 队列 ${result.queued ?? 0}`};
    if (tool === 'list_voice_profiles') return {kind:'success', text:'🎼 声音方案\n' + (result.profiles || []).map(item => `${item.active ? '●' : '○'} ${item.name}  (${item.id})`).join('\n')};
    if(tool==='list_singing_engines')return {kind:'success',text:'🎤 唱歌引擎\n'+(result.engines||[]).map(item=>`${item.active?'●':'○'} ${item.name} · ${item.installed?'已安装':'未安装'}`).join('\n')};
    return {kind:'success', text:'✨ 操作完成：' + ({start_voice_service:'服务已开启',stop_voice_service:'服务已关闭',stop_speech:'朗读已停止',apply_voice_profile:'声音已切换',set_auto_reading:`自动朗读${args.enabled ? '已开启' : '已关闭'}`,open_singing_app:'唱歌软件已打开'}[tool] || tool)};
  } catch (error) { return {kind:'error', text:'语音控制失败：' + error.message}; }
}

// Only committed visible assistant text: never reasoning, tools, attempts or
// compaction replacements. Serial delivery preserves step order before turn/end.
export function createReplyReader(deliver = event => send('deepseek', event)) {
  let delivery = Promise.resolve();
  const seen = new Set();
  const read = (session, event) => {
    if (session?.header?.origin === 'subagent' || session?.header?.parentSession || session?.header?.delegationDepth > 0) return;
    if (event.type !== 'assistant/message' || (event.surfaceOp && event.surfaceOp !== 'append')) return;
    const data = event.data ?? event;
    const content = data.message?.content ?? data.content ?? data.text;
    const text = typeof content === 'string' ? content : Array.isArray(content)
      ? content.filter(p => p.type === 'text' || p.type === 'output_text').map(p => p.text).join('\n') : '';
    if (!text.trim()) return;
    const turn = data.turn ?? event.turnId ?? '';
    const messageId = String(event.seq ?? `${turn}:${data.step ?? ''}:${createHash('sha256').update(text).digest('hex')}`);
    const key = `${session.id}:${messageId}`;
    if (seen.has(key)) return delivery;
    seen.add(key);if(seen.size > 512)seen.delete(seen.values().next().value);
    delivery = delivery.then(() => deliver({type:'assistant/message',session_id:session.id,turn,messageId,text})).catch(() => {});
    return delivery;
  };
  read.flush = () => delivery;
  return read;
}

export function apply(ctx) {
  const stopHeartbeat = startHeartbeat('deepseek');
  // dsh-pet's documented host-side extension seam.  This stays optional: the
  // voice/MCP connector continues to work when dsh-pet is not installed.
  ctx.inject(['petApi7340'], scope => {
    const api = scope.petApi7340 ?? scope.get?.('petApi7340');
    if (!api?.voice?.register) return;
    let unregister = null;
    const sync = () => {
      let useAgentVoice = true;
      try { useAgentVoice = JSON.parse(readFileSync(path.join(appRoot, 'data/settings.json'), 'utf8')).voice?.engine !== 'dsh-pet'; }
      catch { /* On first run, retain the documented Agent Voice provider. */ }
      if (useAgentVoice && !unregister) unregister = api.voice.register({
        id: 'agent-voice-local', name: 'Agent Voice 当前声音',
        synthesize: request => synthesizePetVoice(request),
      });
      if (!useAgentVoice && unregister) { unregister(); unregister = null; }
    };
    sync();
    const timer = setInterval(sync, 2000);
    timer.unref?.();
    const cleanup = () => { clearInterval(timer); unregister?.(); unregister = null; };
    if (typeof ctx.effect === 'function') ctx.effect(() => cleanup);
    else ctx.on('dispose', cleanup);
  });
  // Read-only native RPC avoids filling chat history with polling commands.
  ctx.inject(['connection'], scope => {
    if(!scope.connection?.fetch)return;
    // The desktop shell carries exact /api routes; a standalone RPC channel
    // works on web but is rejected by the desktop carrier with HTTP 405.
    scope.connection.fetch.register({path:'/api/agent-voice-playback',methods:['POST'],requestBody:'buffered',fetch:async request=>{
      const body=await request.json();
      if(body.type!=='client-request'||body.method!=='agent-voice-playback'||typeof body.rpcId!=='string')return new Response('invalid playback request',{status:400});
      let result;
      try{result={ok:true,value:await playbackStatus()};}
      catch(error){result={ok:false,error:{code:'INTERNAL_ERROR',message:error.message}};}
      return Response.json({type:'server-response',rpcId:body.rpcId,result});
    }});
    // Button actions use the native transport too. Executing a slash command
    // logged raw JSON as a command turn in the user's conversation history.
    scope.connection.fetch.register({path:'/api/agent-voice-control',methods:['POST'],requestBody:'buffered',fetch:async request=>{
      const origin=request.headers.get('origin');
      if(origin&&origin!=='dsh-app://app')return new Response('invalid control origin',{status:403});
      const body=await request.json();
      if(body.type!=='client-request'||body.method!=='agent-voice-control'||typeof body.rpcId!=='string'||!controlTools.has(body.payload?.tool))return new Response('invalid control request',{status:400});
      let result;
      try{result={ok:true,value:await mcpControl('deepseek',body.payload.tool,body.payload.args??{})};}
      catch(error){result={ok:false,error:{code:'INTERNAL_ERROR',message:error.message}};}
      return Response.json({type:'server-response',rpcId:body.rpcId,result});
    }});
  });
  // Headless verification can run without the interactive command service.
  // Its reply reader must remain active even when that service is absent.
  let command;
  ctx.inject(['commands'], scope => {
    command = scope.commands.register({
      definitionId: 'agent-voice.control', name: 'agent-voice',
      description: '控制语音服务、停止朗读或切换声音', input: {hint: 'status | start | stop | quiet | profiles | profile <ID> | auto on/off'},
      handler: ({rawInput}) => runVoiceCommand(rawInput),
    });
    void writeFile(new URL('../data/logs/deepseek-control.json',import.meta.url),JSON.stringify({time:new Date().toISOString(),stage:'command-registered'})).catch(()=>{});
  });
  const reader = createReplyReader();
  ctx.on('session/event', reader);
  ctx.on('session/flush', reader.flush);
  ctx.on('dispose', () => { if(typeof command === 'function') command(); else command?.dispose?.(); stopHeartbeat(); });
}
export default { name, inject, apply };
