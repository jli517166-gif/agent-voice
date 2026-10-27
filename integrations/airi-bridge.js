// Installed in the selected AIRI; prepare_airi keeps a recoverable original archive.
export function installAgentVoiceBridge(host) {
  if (globalThis.__agentVoiceBridge) return;
  const state = { generation: null, current: null, stopped: false, received: 0, completed: 0, failed: 0, maxMouth: 0 };
  globalThis.__agentVoiceBridge = state;

  state.mouthInfo = host.mouthInfo;
  const client = crypto.randomUUID();
  async function request(path, data) {
    const cfg = globalThis.__agentVoiceConnection;
    if (!cfg) throw new Error('Voice connection is not configured');
    const res = await fetch(cfg.base + path, {
      method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${cfg.token}` },
      body: JSON.stringify(data), signal: AbortSignal.timeout(4000),
    });
    if (!res.ok) throw new Error(`Voice service: ${res.status}`);
    return res.json();
  }
  host.onEnd(item => {
    if (state.nativeId === item.id) { state.nativeCompleted = true; state.nativeResolve?.(); return; }
    if (state.current?.id !== item.id) return;
    const id = item.id; state.current = null;
    state.completed++;
    void request('/api/airi/ack', { client, id }).catch(() => {});
  });
  host.onInterrupt((item, reason) => {
    if (state.nativeId === item.id) { state.nativeReject?.(new Error(reason || '中断')); return; }
    if (state.current?.id !== item.id) return;
    const id = item.id; state.current = null;
    state.failed++;
    void request('/api/airi/ack', { client, id, error: reason || '播放已中断' }).catch(() => {});
  });
  state.cancelCurrent = reason => {
    if (!state.current) return;
    const id = state.current.id; state.current = null;
    void request('/api/airi/ack', { client, id, error: reason || '播放已中断' }).catch(() => {});
  };
  state.nativeProbe = async text => {
    host.configure(globalThis.__agentVoiceConnection);
    const result = await host.synthesize(text);
    state.nativeId = 'agent-voice-native-probe-' + crypto.randomUUID();
    state.nativeCompleted = false;
    state.nativeTts = { bytes: result.bytes, duration: result.audio.duration };
    const completed = new Promise((resolve, reject) => { state.nativeResolve = resolve; state.nativeReject = reject; });
    await host.prepare();
    host.schedule({ id: state.nativeId, intentId: state.nativeId, ownerId: 'agent-voice-native-test',
      priority: 100, audio: result.audio, text, special: null });
    await completed;
    return { ...state.nativeTts, completed: state.nativeCompleted };
  };
  async function tick() {
    if (state.stopped) return;
    try {
      const cfg = globalThis.__agentVoiceConnection;
      if (cfg && state.connection !== cfg) { host.configure(cfg); state.connection = cfg; }
      const reply = await request('/api/airi/poll', { client });
      if (state.generation !== null && state.generation !== reply.generation) {
        host.stop('agent-voice-stop'); state.current = null;
      }
      state.generation = reply.generation;
      if (reply.task) {
        const task = reply.task, cfg = globalThis.__agentVoiceConnection;
        state.current = task;
        try {
          const response = await fetch(cfg.base + task.audioUrl, {
            headers: { Authorization: `Bearer ${cfg.token}` }, signal: AbortSignal.timeout(10000),
          });
          if (!response.ok) throw new Error('音频下载失败');
          const audio = await host.decode(await response.arrayBuffer());
          const fresh = await request('/api/airi/poll', { client });
          state.generation = fresh.generation;
          if (state.generation !== task.generation || state.current?.id !== task.id) throw new Error('播放已取消');
          for (let channel = 0; channel < audio.numberOfChannels; channel++) {
            const samples = audio.getChannelData(channel);
            for (let n = 0; n < samples.length; n++) samples[n] = Math.max(-1, Math.min(1, samples[n] * task.volume));
          }
          await host.prepare();
          host.schedule({ id: task.id, intentId: task.id, ownerId: 'agent-voice-public',
            priority: 100, audio, text: task.text, special: null });
          state.received++;
        } catch (error) {
          state.current = null;
          await request('/api/airi/ack', { client, id: task.id, error: String(error) });
        }
      }
    } catch { /* Service-off stays quiet and never opens a bubble. */ }
    state.maxMouth = Math.max(state.maxMouth, Number(host.mouth?.() || 0));
    setTimeout(tick, 600);
  }
  void tick();
}
