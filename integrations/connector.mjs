import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const appRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const heartbeats = new Map();
export function startHeartbeat(source, root = appRoot) {
  const key = `${root}\0${source}`;
  heartbeats.get(key)?.();
  let stopped = false, busy = false;
  const ping = async () => {
    if (stopped || busy) return;
    busy = true;
    try {
      const cfg = JSON.parse(await readFile(path.join(root, 'data/settings.json'), 'utf8'));
      if (!cfg.harness[source] || stopped) return;
      await fetch(`http://127.0.0.1:${cfg.port}/api/harness/${source}/heartbeat`, {
        method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${cfg.token}` },
        body: '{}', signal: AbortSignal.timeout(3000),
      });
    } catch { /* Connection diagnostics must never interrupt the Harness. */ }
    finally { busy = false; }
  };
  void ping();
  const timer = setInterval(() => void ping(), 30000);
  timer.unref?.();
  const stop = () => { stopped = true; clearInterval(timer); if (heartbeats.get(key) === stop) heartbeats.delete(key); };
  heartbeats.set(key, stop);
  return stop;
}
export async function send(source, event, root = appRoot) {
  try {
    const cfg = JSON.parse(await readFile(path.join(root, 'data/settings.json'), 'utf8'));
    if (!cfg.harness[source]) return;
    await fetch(`http://127.0.0.1:${cfg.port}/api/harness/${source}`, {
      method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${cfg.token}` },
      body: JSON.stringify(event), signal: AbortSignal.timeout(3000),
    });
  } catch { /* An intentionally stopped voice service never breaks the Harness. */ }
}
