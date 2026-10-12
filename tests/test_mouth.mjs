import assert from 'node:assert/strict';
import { createMouthMotion } from '../integrations/mouth-motion.js';

const oldSet = globalThis.setInterval, oldClear = globalThis.clearInterval;
globalThis.setInterval = () => 1; globalThis.clearInterval = () => {};
try {
  const context = { currentTime: 0, outputLatency: 0 };
  const rate = 16000, samples = new Float32Array(rate * 1.2);
  for (let i = 0; i < samples.length; i++) {
    const t = i / rate;
    const amplitude = t >= .1 && t < .4 ? .04 + .15 * Math.abs(Math.sin(18 * t)) : t >= .65 && t < .95 ? .1 : 0;
    samples[i] = amplitude * Math.sin(2 * Math.PI * 200 * t);
  }
  const audio = { duration: 1.2, length: samples.length, sampleRate: rate, numberOfChannels: 1, getChannelData: () => samples };
  let displayed = 0; const motion = createMouthMotion(context, value => displayed = value);
  const owner = {}; motion.begin(audio, 0, owner);
  const voiced = [], silent = [];
  for (let n = 0; n <= 72; n++) {
    context.currentTime = n / 60; const value = motion.read();
    assert.ok(Number.isFinite(value) && value >= 0 && value <= .62);
    if (context.currentTime > .2 && context.currentTime < .4) voiced.push(value);
    if (context.currentTime > .53 && context.currentTime < .65) silent.push(value);
  }
  assert.ok(Math.max(...voiced) > .3 && Math.max(...voiced) - Math.min(...voiced) > .1, 'voiced syllables need varying openings');
  assert.ok(silent.every(v => v < .025), 'silent gap must close despite prior vowel');
  context.currentTime = 1.26; motion.read();
  assert.equal(displayed, 0); assert.equal(motion.status().playing, false);
  context.currentTime = 2; motion.begin(audio, 2, owner);
  context.currentTime = 2.3; motion.read(); assert.ok(displayed > 0);
  const next = {}; motion.begin(audio, 2.3, next); motion.stop(owner);
  assert.equal(motion.status().playing, true, 'old onended must not close new source');
  context.currentTime = 2.6; motion.read(); assert.ok(displayed > 0);
  motion.stop(next); assert.equal(displayed, 0); assert.equal(motion.status().finalValue, 0);
  console.log(JSON.stringify({ ok: true, checks: ['syllable variation','bounded opening','silence closes','audio-clock end closes','stale-source isolation','stop closes'] }));
} finally { globalThis.setInterval = oldSet; globalThis.clearInterval = oldClear; }
