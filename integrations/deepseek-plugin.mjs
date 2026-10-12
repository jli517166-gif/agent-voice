import { send, startHeartbeat } from './connector.mjs';
export const name = 'agent-voice-public';
export const inject = ['sessions'];

export function apply(ctx) {
  const stopHeartbeat = startHeartbeat('deepseek');
  const pending = new Map();
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
  ctx.on('dispose', () => { stopHeartbeat(); pending.clear(); });
}
export default apply;
