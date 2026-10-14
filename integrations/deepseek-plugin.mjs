import { send, startHeartbeat, mcpControl } from './connector.mjs';
export const name = 'agent-voice-public';
export const inject = ['sessions', 'commands'];

function commandHelp() {
  return ['🎙 Agent Voice 控制台', 'status  状态', 'start  开启服务', 'stop  关闭服务',
    'quiet  停止当前朗读', 'profiles  声音方案', 'profile <ID>  切换声音', 'auto on|off  自动朗读'].join('\n');
}
export async function runVoiceCommand(rawInput = '') {
  const [action = 'status', ...values] = rawInput.trim().split(/\s+/).filter(Boolean);
  const normalized = action.toLowerCase();
  let tool = 'voice_service_status', args = {};
  if (normalized === 'help') return {kind:'success', text:commandHelp()};
  if (normalized === 'start') tool = 'start_voice_service';
  else if (normalized === 'stop') tool = 'stop_voice_service';
  else if (normalized === 'quiet' || normalized === 'mute') tool = 'stop_speech';
  else if (normalized === 'profiles') tool = 'list_voice_profiles';
  else if (normalized === 'profile') {
    if (!values[0]) return {kind:'error', text:'请填写声音方案 ID。先输入 /voice profiles 查看。'};
    tool = 'apply_voice_profile'; args = {profile_id:values.join(' ')};
  } else if (normalized === 'auto') {
    if (!['on','off'].includes((values[0] || '').toLowerCase())) return {kind:'error', text:'用法：/voice auto on 或 /voice auto off'};
    tool = 'set_auto_reading'; args = {enabled:values[0].toLowerCase() === 'on'};
  } else if (normalized !== 'status') return {kind:'error', text:'未知操作。\n' + commandHelp()};
  try {
    const result = await mcpControl('deepseek', tool, args);
    if (tool === 'voice_service_status') return {kind:'success', text:`🎙 ${result.running ? '服务运行中' : '服务未启动'} · 自动朗读${result.autoReading ? '已开启' : '已关闭'} · 队列 ${result.queued ?? 0}`};
    if (tool === 'list_voice_profiles') return {kind:'success', text:'🎼 声音方案\n' + (result.profiles || []).map(item => `${item.active ? '●' : '○'} ${item.name}  (${item.id})`).join('\n')};
    return {kind:'success', text:'✨ 操作完成：' + ({start_voice_service:'服务已开启',stop_voice_service:'服务已关闭',stop_speech:'朗读已停止',apply_voice_profile:'声音已切换',set_auto_reading:`自动朗读${args.enabled ? '已开启' : '已关闭'}`}[tool] || tool)};
  } catch (error) { return {kind:'error', text:'语音控制失败：' + error.message}; }
}

export function apply(ctx) {
  const stopHeartbeat = startHeartbeat('deepseek');
  const pending = new Map();
  const command = ctx.commands.register({
    definitionId: 'agent-voice.control', name: 'voice',
    description: '控制语音服务、停止朗读或切换声音', input: {hint: 'status | start | stop | quiet | profiles | profile <ID> | auto on/off'},
    handler: ({rawInput}) => runVoiceCommand(rawInput),
  });
  ctx.on('session/event', (session, event) => {
    if (session?.header?.origin === 'subagent' || session?.header?.parentSession || session?.header?.delegationDepth > 0) return;
    const data = event.data ?? event;
    const turn = data.turn ?? event.turnId ?? '';
    const key = `${session.id}:${turn}`;
    if (event.type === 'assistant/message') {
      const content = data.message?.content ?? data.content ?? data.text;
      const text = typeof content === 'string' ? content : Array.isArray(content)
        ? content.filter(p => p.type === 'text' || p.type === 'output_text').map(p => p.text).join('\n') : '';
      if (text) pending.set(key, text);
      while (pending.size > 256) pending.delete(pending.keys().next().value);
    }
    if (event.type === 'turn/end') {
      const text = pending.get(key); pending.delete(key);
      if (text) void send('deepseek', { type: 'turn/end', session_id: session.id, turn, text });
    }
  });
  ctx.on('dispose', () => { command?.dispose?.(); stopHeartbeat(); pending.clear(); });
}
export default apply;
