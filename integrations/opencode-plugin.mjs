import { send, startHeartbeat } from './connector.mjs';

export const AgentVoicePlugin = async ({ client }) => {
  startHeartbeat('opencode');
  return {
  event: async ({ event }) => {
    if (event.type !== 'session.idle') return;
    try {
      const sessionId = event.properties.sessionID;
      const response = await client.session.messages({ path: { id: sessionId } });
      void send('opencode', { type: 'session.idle', sessionId, messages: response.data || [] });
    } catch { /* Do not alter the assistant's messages or completion flow. */ }
  },
  };
};
