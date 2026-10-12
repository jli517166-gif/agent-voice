// Live2D opening follows the exact AudioBuffer and audio clock used for playback.
// Silence overrides stale vowel classifications; stopping always closes the mouth.
export function createMouthMotion(context, onValue = () => {}, options = {}) {
  const cap = options.cap ?? 0.62;
  const step = 0.01;
  let levels = [], reference = 0.05, gate = 0.002, duration = 0;
  let started = 0, previous = 0, value = 0, source = null, timer = null;
  let stats = { playing: false, reads: 0, peak: 0, silentReads: 0, closedSilentReads: 0, finalValue: 0 };
  function emit(next) { value = next; onValue(next); }
  function stop(owner) {
    if (owner && source !== owner) return;
    if (timer !== null) clearInterval(timer);
    timer = null; source = null; stats.playing = false; stats.finalValue = 0;
    emit(0);
  }
  function read() {
    if (!source) return 0;
    const elapsed = context.currentTime - started;
    if (elapsed >= duration + 0.04) { stop(source); return 0; }
    // currentTime is ahead of sound reaching the output device. Use its reported
    // latency, not an independent wall-clock timer or message receipt time.
    const latency = Math.min(0.08, Math.max(0, context.outputLatency || 0));
    const offset = elapsed - latency;
    const level = offset >= 0 && offset < duration ? levels[Math.floor(offset / step)] || 0 : 0;
    const active = level > gate;
    const target = active ? cap * Math.min(1, Math.max(0, (level - gate) / Math.max(0.008, reference - gate))) ** 1.05 : 0;
    const dt = Math.max(0, Math.min(0.1, context.currentTime - previous));
    previous = context.currentTime;
    const alpha = 1 - Math.exp(-dt / (target > value ? 0.024 : 0.032));
    const next = value + alpha * (target - value);
    emit(next < 0.008 ? 0 : next);
    stats.reads++; stats.peak = Math.max(stats.peak, value);
    if (!active) { stats.silentReads++; if (value <= 0.025) stats.closedSilentReads++; }
    return value;
  }
  function begin(audio, time, owner) {
    stop();
    duration = audio.duration; started = time; previous = time; source = owner;
    const frame = Math.max(1, Math.round(audio.sampleRate * step));
    levels = new Array(Math.ceil(audio.length / frame)).fill(0);
    for (let channel = 0; channel < audio.numberOfChannels; channel++) {
      const samples = audio.getChannelData(channel);
      for (let i = 0; i < levels.length; i++) {
        let sum = 0, count = 0;
        for (let n = i * frame; n < Math.min(samples.length, (i + 1) * frame); n += 4) { sum += samples[n] ** 2; count++; }
        levels[i] = Math.max(levels[i], Math.sqrt(sum / Math.max(1, count)));
      }
    }
    const voiced = levels.filter(v => v > 0.002).sort((a, b) => a - b);
    reference = Math.max(0.015, voiced[Math.floor(voiced.length * 0.9)] || 0.05);
    gate = Math.max(0.002, reference * 0.055);
    stats = { playing: true, reads: 0, peak: 0, silentReads: 0, closedSilentReads: 0, finalValue: null };
    emit(0);
    timer = setInterval(read, 16);
  }
  return { begin, read, stop, status: () => ({ ...stats, value, duration, elapsed: source ? context.currentTime - started : duration }) };
}
