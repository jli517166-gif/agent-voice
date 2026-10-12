import { send, startHeartbeat } from './connector.mjs';

export default function voiceExtension(pi: any) {
  startHeartbeat('pi');
  pi.on('agent_end', (event: any, ctx: any) => {
    void send('pi', { type: 'agent_end', session_id: ctx.sessionManager?.getSessionId?.() || 'pi', messages: event.messages });
  });
}
