"""Build a separate AIRI 0.10.2 companion; retain the original installation."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import shutil
import struct
import os

def read_header(path):
    with path.open('rb') as stream:
        prefix=stream.read(16)
        size=struct.unpack_from('<I',prefix,12)[0]
        if size>16*1024*1024:raise ValueError('Unexpected AIRI archive')
        return json.loads(stream.read(size)),8+struct.unpack_from('<I',prefix,4)[0]

def leaves(files,path=''):
    for name,entry in files.items():
        full=path+'/'+name if path else name
        if 'files' in entry:yield from leaves(entry['files'],full)
        else:yield full,entry

def rebuild(source,dest,connection,bridge,mouth):
    header,base=read_header(source)
    listing=list(leaves(header['files']))
    with source.open('rb') as stream:
        def read(entry):
            if entry.get('unpacked'):return None
            stream.seek(base+int(entry['offset']));return stream.read(entry['size'])
        package=next(e for n,e in listing if n=='package.json')
        if json.loads(read(package))['version']!='0.10.2':
            raise ValueError('仅验证了 AIRI 0.10.2；其他版本须适配后再连接')
        replacements={};found=0;renderer_found=0
        for name,entry in listing:
            if name=='out/main/index.js':
                body=read(entry).decode()
                # Normal launch retains AIRI's original package name/userData,
                # and therefore the existing login, character and chat settings.
                # Probes may explicitly use a disposable profile.
                body='const voiceProfileArg=process.argv.indexOf("--agent-voice-user-data");\nif(voiceProfileArg>=0 && process.argv[voiceProfileArg+1]) app.setPath("userData",process.argv[voiceProfileArg+1]);\n'+body
                smoke='''
if (process.argv.includes("--agent-voice-smoke") || process.argv.includes("--agent-voice-playback-probe")) {
    BrowserWindow.prototype.show = function () {};
    BrowserWindow.prototype.showInactive = function () {};
    const playingProbe = process.argv.includes("--agent-voice-playback-probe");
    const index = process.argv.indexOf(playingProbe ? "--agent-voice-playback-probe" : "--agent-voice-smoke");
    const report = process.argv[index + 1];
    const connectionFile = playingProbe ? process.argv[index + 2] : null;
    app.on("browser-window-created", (_event, window) => window.hide());
    app.whenReady().then(async () => {
        const fs = await import("node:fs/promises");
        const start = Date.now();
        const configured = new Set();
        let busy = false;
        const timer = setInterval(async () => {
            if (busy) return; busy = true;
            let states = [];
            for (const window of BrowserWindow.getAllWindows()) {
                try {
                    const state = await window.webContents.executeJavaScript(`({url:location.href,bridge:!!globalThis.__agentVoiceBridge,stats:globalThis.__agentVoiceBridge?{received:globalThis.__agentVoiceBridge.received,completed:globalThis.__agentVoiceBridge.completed,failed:globalThis.__agentVoiceBridge.failed,maxMouth:globalThis.__agentVoiceBridge.maxMouth,nativeCompleted:!!globalThis.__agentVoiceBridge.nativeTtsCompleted,nativeTts:globalThis.__agentVoiceBridge.nativeTts}:null})`);
                    if (playingProbe && state.bridge && !configured.has(window.id)) {
                        const cfg = JSON.parse(await fs.readFile(connectionFile,"utf8"));
                        await window.webContents.executeJavaScript("globalThis.__agentVoiceConnection="+JSON.stringify({base:"http://127.0.0.1:"+cfg.port,token:cfg.token}));
                        configured.add(window.id);
                        state.nativeTts = await window.webContents.executeJavaScript('globalThis.__agentVoiceBridge.nativeProbe("这是桌宠自己的语音接口测试。")');
                        await window.webContents.executeJavaScript('globalThis.__agentVoiceBridge.nativeTtsCompleted = true');
                    }
                    states.push(state);
                } catch {}
            }
            const ok = playingProbe ? states.some(state => state.stats?.completed > 0 && state.stats?.nativeCompleted) : states.some(state => state.bridge);
            if (ok || Date.now() - start > (playingProbe ? 180000 : 45000)) {
                clearInterval(timer);
                await fs.writeFile(report, JSON.stringify({ok,states},null,2));
                app.exit(ok ? 0 : 1);
            }
            busy = false;
        }, 800);
    });
}
'''
                body=smoke+body
                mouth_probe='''
if (process.argv.includes("--agent-voice-mouth-probe")) {
    const report = process.argv[process.argv.indexOf("--agent-voice-mouth-probe") + 1];
    app.whenReady().then(async () => {
        const fs = await import("node:fs/promises");
        const frames = []; let seenPlaying = false, ended = 0, busy = false, openCaptured = false;
        const timer = setInterval(async () => {
            if (busy) return; busy = true;
            try {
                for (const window of BrowserWindow.getAllWindows()) {
                    const sample = await window.webContents.executeJavaScript('globalThis.__agentVoiceBridge?.mouthInfo?.()');
                    if (!sample) continue;
                    frames.push({time:Date.now(),...sample});
                    if (frames.length > 2400) frames.shift();
                    if (sample.motion.playing) { seenPlaying = true; ended = 0; }
                    if (!openCaptured && sample.drawnMouth > 0.25) {
                        const image = await window.webContents.capturePage();
                        await fs.writeFile(report + ".open.png", image.toPNG()); openCaptured = true;
                    }
                    if (seenPlaying && !sample.motion.playing) {
                        if (!ended) ended = Date.now();
                        if (Date.now() - ended > 400) {
                            const image = await window.webContents.capturePage();
                            await fs.writeFile(report + ".closed.png", image.toPNG());
                            await fs.writeFile(report,JSON.stringify({ok:sample.drawnMouth===0 && sample.motion.finalValue===0 && sample.appliedMouth===0,frames},null,2));
                            clearInterval(timer);
                        }
                    }
                }
            } catch (error) { await fs.writeFile(report + ".error.txt", String(error)); }
            finally { busy = false; }
        },100);
    });
}
'''
                body=mouth_probe+body
                replacements[name]=body.encode()
            if name.startswith('out/renderer/assets/pages') and name.endswith('.js'):
                body=read(entry).decode()
                marker='\t\tconst speechPipeline = createSpeechPipeline({'
                if marker not in body:continue
                if body.count(marker)!=1:raise ValueError('AIRI playback host is ambiguous')
                for original,replacement in (
                    ('const lipSyncNode = ref();','const lipSyncNode = ref();\n        const agentVoiceMouth = createMouthMotion(audioContext, value => { if (stageModelRenderer.value === "live2d") mouthOpenSize.value = value; });'),
                    ('source.start(0);','agentVoiceMouth.begin(item.audio, audioContext.currentTime, source);\n                    source.start(0);'),
                    ('const stopPlayback = () => {','const stopPlayback = () => {\n                    agentVoiceMouth.stop(source);'),
                    ('else mouthOpenSize.value = live2dLipSync.value.getMouthOpen();','else mouthOpenSize.value = agentVoiceMouth.read();'),
                    ('if (!nowSpeaking.value || !live2dLipSync.value) mouthOpenSize.value = 0;','if (!nowSpeaking.value) mouthOpenSize.value = 0;'),
                ):
                    if body.count(original)!=1:raise ValueError('AIRI mouth marker missing or ambiguous: '+original)
                    body=body.replace(original,replacement)
                host='''\t\tfunction configureAgentVoice(voiceConnection) {
            providersStore.providers["openai-compatible-audio-speech"] = {
                apiKey: voiceConnection.token, baseUrl: voiceConnection.base + "/v1/",
                model: "agent-voice-tts", voice: "current", speed: 1,
            };
            providersStore.forceProviderConfigured("openai-compatible-audio-speech");
            activeSpeechProvider.value = "openai-compatible-audio-speech";
            activeSpeechModel.value = "agent-voice-tts";
            ssmlEnabled.value = false;
        }
        const voiceConnection = globalThis.__agentVoiceConnection;
        if (voiceConnection) configureAgentVoice(voiceConnection);
        installAgentVoiceBridge({
            configure: configureAgentVoice,
            synthesize: async text => {
                // Diagnostic route uses the same provider SDK as AIRI chat.
                stageModelRenderer.value = "live2d";
                const config = providersStore.getProviderConfig("openai-compatible-audio-speech");
                const provider = await providersStore.getProviderInstance("openai-compatible-audio-speech");
                const bytes = await generateSpeech({...provider.speech("agent-voice-tts",config),input:text,voice:"current"});
                const byteLength = bytes.byteLength;
                return {audio:await audioContext.decodeAudioData(bytes),bytes:byteLength};
            },
            prepare: async () => { setupAnalyser(); await setupLipSync(); },
            decode: bytes => audioContext.decodeAudioData(bytes),
            schedule: item => playbackManager.schedule(item),
            stop: reason => playbackManager.stopByOwner("agent-voice-public", reason),
            onEnd: fn => playbackManager.onEnd(({item}) => fn(item)),
            onInterrupt: fn => playbackManager.onInterrupt(({item,reason}) => fn(item,reason)),
            mouth: () => mouthOpenSize.value,
            mouthInfo: () => ({renderer:stageModelRenderer.value,drawnMouth:mouthOpenSize.value,motion:agentVoiceMouth.status(),appliedMouth:globalThis.__agentVoiceLive2DMouth?.applied,renderFrames:globalThis.__agentVoiceLive2DMouth?.frames}),
        });
'''
                body='import { createMouthMotion } from "./agent-voice-mouth.js";\nimport { installAgentVoiceBridge } from "./agent-voice-bridge.js";\n'+body.replace(marker,host+marker)
                body=body.replace('playbackManager.stopAll("new-message");','globalThis.__agentVoiceBridge?.cancelCurrent("new-message");\n\t\t\tplaybackManager.stopAll("new-message");')
                body='globalThis.__agentVoiceConnection='+json.dumps(connection)+';\n'+body
                replacements[name]=body.encode();found+=1
            if name.startswith('out/renderer/assets/') and name.endswith('.js') and not entry.get('unpacked'):
                body=read(entry).decode()
                mouth_marker='coreModel.setParameterValueById("ParamMouthOpenY", mouthOpenSize.value);'
                if mouth_marker in body:
                    if body.count(mouth_marker)!=1:raise ValueError('Live2D mouth renderer is ambiguous')
                    driver='''
                // Motions and expressions run before this event and otherwise
                // overwrite a one-off Vue watcher, including its final zero.
                internalModel.on("beforeModelUpdate", () => {
                    if (!globalThis.__agentVoiceBridge) return;
                    const opening = Math.max(0, Math.min(1, mouthOpenSize.value));
                    coreModel.setParameterValueById("ParamMouthOpenY", opening);
                    const state = globalThis.__agentVoiceLive2DMouth ??= {applied:0,frames:0};
                    state.applied = opening; state.frames++;
                });
'''
                    body=body.replace(mouth_marker,mouth_marker+driver)
                    replacements[name]=body.encode();renderer_found+=1
        if found!=1:raise ValueError('AIRI playback host not found uniquely')
        if renderer_found!=1:raise ValueError('Live2D mouth renderer not found uniquely')
        bridge_path='out/renderer/assets/agent-voice-bridge.js'
        mouth_path='out/renderer/assets/agent-voice-mouth.js'
        extras={bridge_path:bridge,mouth_path:mouth}
        for name,body in extras.items():header['files']['out']['files']['renderer']['files']['assets']['files'][name.rsplit('/',1)[1]]={'size':len(body),'offset':'0'}
        listing=list(leaves(header['files']))
        offset=0
        for name,entry in listing:
            if entry.get('unpacked') or 'link' in entry:continue
            if name in extras:body=extras[name]
            elif name in replacements:body=replacements[name]
            else:body=read(entry)
            entry['_oldOffset']=entry['offset'];entry['size']=len(body);entry['offset']=str(offset);offset+=len(body)
            if 'integrity' in entry:
                block_size=entry['integrity'].get('blockSize',4194304)
                entry['integrity']={'algorithm':'SHA256','hash':hashlib.sha256(body).hexdigest(),
                    'blockSize':block_size,'blocks':[hashlib.sha256(body[i:i+block_size]).hexdigest() for i in range(0,len(body),block_size)]}
        saved={n:e.pop('_oldOffset') for n,e in listing if '_oldOffset' in e}
        encoded=json.dumps(header,ensure_ascii=False,separators=(',',':')).encode()
        pad=(-len(encoded))%4
        payload=struct.pack('<I',len(encoded))+encoded+b'\0'*pad
        serialized=struct.pack('<I',len(payload))+payload
        with dest.open('wb') as output:
            output.write(struct.pack('<II',4,len(serialized)));output.write(serialized)
            for name,entry in listing:
                if entry.get('unpacked') or 'link' in entry:continue
                if name in extras:body=extras[name]
                elif name in replacements:body=replacements[name]
                else:
                    stream.seek(base+int(saved[name]));body=stream.read(entry['size'])
                output.write(body)

def prepare(source,target,root,refresh=False):
    source=Path(source).resolve();target=Path(target).resolve();root=Path(root).resolve()
    if target==source or source in target.parents or target in source.parents:
        raise ValueError('联动副本须位于原 AIRI 目录之外')
    if target.exists():
        marker=target/'agent-voice-integration.json'
        if not refresh or not marker.is_file() or Path(json.loads(marker.read_text(encoding='utf-8'))['original']).resolve()!=source:
            raise ValueError('目录已存在；仅可更新本程序已创建的联动副本')
    config=json.loads((root/'data/settings.json').read_text(encoding='utf-8-sig'))
    source_archive=source/'resources/app.asar'
    header,_=read_header(source_archive)
    # Validate before copying a large installation.
    package=header['files']['package.json']
    with source_archive.open('rb') as stream:
        _,offset=read_header(source_archive);stream.seek(offset+int(package['offset']))
        if json.loads(stream.read(package['size']))['version']!='0.10.2':raise ValueError('AIRI 版本未适配')
    target.mkdir(parents=True,exist_ok=True)
    for item in source.iterdir():
        if item.name=='resources':continue
        if (target/item.name).exists():continue
        if item.is_dir():shutil.copytree(item,target/item.name)
        else:shutil.copy2(item,target/item.name)
    (target/'resources').mkdir(exist_ok=True)
    for item in (source/'resources').iterdir():
        if item.name=='app.asar':continue
        if (target/'resources'/item.name).exists():continue
        if item.is_dir():shutil.copytree(item,target/'resources'/item.name)
        else:shutil.copy2(item,target/'resources'/item.name)
    bridge=(root/'integrations/airi-bridge.js').read_bytes()
    mouth=(root/'integrations/mouth-motion.js').read_bytes()
    staged=target/'resources/app.asar.new'
    rebuild(source_archive,staged,{'base':'http://127.0.0.1:'+str(config['port']),'token':config['token']},bridge,mouth)
    os.replace(staged,target/'resources/app.asar')
    (target/'agent-voice-integration.json').write_text(json.dumps({'original':str(source),'version':'0.10.2','voiceRoot':str(root)},ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({'ok':True,'directory':str(target),'originalUnchanged':True},ensure_ascii=False))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--source',required=True);parser.add_argument('--target',required=True);parser.add_argument('--root',required=True);parser.add_argument('--refresh',action='store_true')
    args=parser.parse_args();prepare(args.source,args.target,args.root,args.refresh)
