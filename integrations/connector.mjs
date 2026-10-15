import { readFile } from 'node:fs/promises';
import { spawn } from 'node:child_process';
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

export async function mcpControl(source, tool, args = {}, root = appRoot) {
  const cfg = JSON.parse(await readFile(path.join(root, 'data/settings.json'), 'utf8'));
  const pythonValue = cfg.engines?.pythonRvc;
  if (!pythonValue) throw new Error('尚未配置本地 TTS Python 环境');
  const python = path.isAbsolute(pythonValue) ? pythonValue : path.join(root, pythonValue);
  const server = path.join(root, 'integrations', 'agent_voice_mcp.py');
  return await new Promise((resolve, reject) => {
    const child = spawn(python, ['-X', 'utf8', server, '--root', root, '--host', source], {
      cwd: root, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'],
    });
    let output = '', errors = '', settled = false;
    const finish = (error, value) => { if (settled) return; settled = true; clearTimeout(timer); child.kill(); error ? reject(error) : resolve(value); };
    const timer = setTimeout(() => finish(new Error('语音控制超时，请检查桌面端服务状态')), 20000);
    child.stdout.setEncoding('utf8'); child.stderr.setEncoding('utf8');
    child.stdout.on('data', chunk => {
      output += chunk;
      const newline = output.indexOf('\n');
      if (newline < 0) return;
      try {
        const message = JSON.parse(output.slice(0, newline));
        const result = message.result;
        if (!result) return finish(new Error(message.error?.message || '语音控制没有返回结果'));
        if (result.isError) return finish(new Error(result.content?.[0]?.text || '语音控制失败'));
        finish(null, result.structuredContent ?? JSON.parse(result.content?.[0]?.text || '{}'));
      } catch (error) { finish(error); }
    });
    child.stderr.on('data', chunk => { errors += chunk; });
    child.on('error', error => finish(error));
    child.on('exit', code => { if (!settled) finish(new Error(errors.trim() || `语音控制进程提前退出（${code}）`)); });
    child.stdin.end(JSON.stringify({jsonrpc:'2.0', id:1, method:'tools/call', params:{name:tool, arguments:args}}) + '\n');
  });
}

// Read-only animation telemetry stays in the trusted backend. Do not launch a
// Python MCP process for every frame/status poll or expose health bodies/token.
export async function playbackStatus(root = appRoot) {
  const cfg = JSON.parse(await readFile(path.join(root, 'data/settings.json'), 'utf8'));
  let response;
  try {
    response = await fetch(`http://127.0.0.1:${cfg.port}/health`, {
      headers:{Authorization:`Bearer ${cfg.token}`},signal:AbortSignal.timeout(2500),
    });
  } catch(error) {
    if(error.cause?.code==='ECONNREFUSED')return {running:false,playback:{state:'offline'}};
    throw new Error('语音播放状态连接中断');
  }
  if(!response.ok)throw new Error('语音播放状态读取失败');
  const health=await response.json();
  if(health.service!=='agent-voice-public')throw new Error('当前地址不是 Agent Voice 服务');
  const state=health.playback?.state;
  return {running:true,playback:{state:['idle','queued','generating','playing'].includes(state)?state:'unknown'},queued:health.queued??0};
}
