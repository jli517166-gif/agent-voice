"""Optional voice adapter for the host plugin or the standalone pet's Edge settings."""
import asyncio
import contextlib
import json
import os
import subprocess
import time
import urllib.error
import urllib.request
from pathlib import Path


class Adapter:
    def __init__(self,store):
        self.store=store
        self.endpoint=''
        self.voice=''
        self.checked_at=0
        self.standalone=False

    def _standalone_config(self):
        base=os.environ.get('APPDATA')
        if not base:raise RuntimeError('找不到桌宠用户配置目录 APPDATA')
        path=Path(base)/'dsh-pet-standalone-webm-chat'/'config.json'
        if not path.is_file():raise RuntimeError('没有找到 dsh-pet-indesktop Chat 版设置：'+str(path))
        try:config=json.loads(path.read_text(encoding='utf-8-sig'))
        except (OSError,ValueError) as error:raise RuntimeError('独立桌宠设置文件无法读取') from error
        backend=config.get('voice_chime_tts_backend','edge')
        if backend!='edge':raise RuntimeError('独立桌宠当前语音后端为 '+str(backend)+'；本版仅适配其 Edge 声音')
        return config

    def _standalone_audio(self,text,voice,target,check):
        try:import edge_tts
        except ImportError as error:raise RuntimeError('缺少桌宠 Edge 语音依赖，请在设置中运行“补齐环境”') from error
        config=self._standalone_config()
        name=voice['petVoice'] or str(config.get('voice_chime_voice') or 'zh-CN-XiaoxiaoNeural')
        rate=max(-100,min(100,int(config.get('voice_chime_rate') or 0)+round((voice['speed']-1)*100)))
        pitch=max(-50,min(50,int(config.get('voice_chime_pitch') or 0)))
        output=Path(target).with_suffix('.pet.mp3')
        output.parent.mkdir(parents=True,exist_ok=True)
        async def generate():
            task=asyncio.create_task(edge_tts.Communicate(text,name,rate=f'{rate:+d}%',pitch=f'{pitch:+d}Hz').save(str(output)))
            try:
                while not task.done():check();await asyncio.sleep(.1)
                await task
            finally:
                if not task.done():
                    task.cancel()
                    with contextlib.suppress(asyncio.CancelledError):await task
        try:
            asyncio.run(asyncio.wait_for(generate(),timeout=20))
            if not output.is_file() or not 0<output.stat().st_size<=20*1024*1024:raise RuntimeError('独立桌宠 Edge 服务未返回有效音频')
            return output.read_bytes(),'audio/mpeg'
        except Exception as error:
            from jobs import Cancelled
            if isinstance(error,Cancelled):raise
            raise RuntimeError('独立桌宠 Edge 声音生成失败，请检查网络、音色和代理设置') from None
        finally:output.unlink(missing_ok=True)

    def _request(self,path,payload=None,timeout=12):
        headers={'Accept':'application/json' if payload is None else 'audio/mpeg'}
        body=None
        if payload is not None:
            headers['Content-Type']='application/json'
            body=json.dumps(payload,ensure_ascii=False).encode('utf-8')
        request=urllib.request.Request(self.endpoint+path,data=body,headers=headers)
        try:
            with urllib.request.urlopen(request,timeout=timeout) as response:
                if response.status!=200:raise RuntimeError('桌宠语音接口返回 HTTP '+str(response.status))
                data=response.read(20*1024*1024+1)
                if len(data)>20*1024*1024:raise RuntimeError('桌宠返回的音频超过 20 MB')
                return data,response.headers.get('Content-Type','')
        except urllib.error.HTTPError as error:
            raise RuntimeError('桌宠语音接口返回 HTTP '+str(error.code)) from error
        except urllib.error.URLError as error:
            raise RuntimeError('未连接到 dsh-pet 的本地 Web 服务，请检查桌宠插件和地址') from error

    def prepare(self,voice,check):
        from pet_endpoint import validate_endpoint,STANDALONE
        check()
        self.endpoint=validate_endpoint(voice['petEndpoint'])
        self.voice=voice['petVoice']
        self.standalone=self.endpoint==STANDALONE
        if self.standalone:
            self._standalone_config()
            try:import edge_tts
            except ImportError as error:raise RuntimeError('缺少桌宠 Edge 语音依赖，请在设置中运行“补齐环境”') from error
            self.checked_at=time.monotonic();check();return
        raw,_=self._request('/voice/voices',timeout=3)
        try:response=json.loads(raw)
        except ValueError as error:raise RuntimeError('桌宠音色列表不是有效 JSON') from error
        if response.get('ok') is not True or not isinstance(response.get('voices'),list):
            raise RuntimeError('桌宠未提供音色列表；请确认 dsh-pet 插件已启用')
        available={item.get('id') for item in response['voices'] if isinstance(item,dict)}
        if self.voice and self.voice not in available:
            raise ValueError('桌宠音色不存在，请先从桌宠当前音色列表选择')
        self.checked_at=time.monotonic()
        check()

    def synthesize(self,text,voice,target,check):
        check()
        payload={'text':text,'rate':voice['speed']}
        if voice['petVoice']:payload['voice']=voice['petVoice']
        audio,kind=self._standalone_audio(text,voice,target,check) if self.standalone else self._request('/voice/speak',payload,timeout=18)
        if 'json' in kind.lower():
            try:detail=json.loads(audio)
            except ValueError:detail={}
            raise RuntimeError('桌宠语音生成失败：'+str(detail.get('reason') or detail.get('error') or '未知原因'))
        if not audio.startswith((b'ID3',b'\xff\xfb',b'\xff\xf3',b'\xff\xf2')):
            raise RuntimeError('桌宠没有返回 MP3 音频；请检查其语音提供者')
        check()
        ffmpeg=self.store.resolve(self.store.get()['engines']['ffmpeg'])
        if not ffmpeg or not ffmpeg.is_file():raise ValueError('缺少 FFmpeg，无法转换桌宠 MP3 音频')
        target=Path(target);target.parent.mkdir(parents=True,exist_ok=True)
        command=[str(ffmpeg),'-hide_banner','-loglevel','error','-f','mp3','-i','pipe:0','-ac','1','-ar','24000','-f','wav','-y',str(target)]
        try:result=subprocess.run(command,input=audio,stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=30,check=False)
        except subprocess.TimeoutExpired as error:raise RuntimeError('桌宠音频转换超过 30 秒') from error
        check()
        if result.returncode!=0 or not target.is_file():
            raise RuntimeError('桌宠 MP3 转 WAV 失败：'+result.stderr.decode('utf-8','replace')[:300])
        return target

    @property
    def gpu(self):return None
    def alive(self):
        if not self.checked_at:return False
        if self.standalone:
            try:self._standalone_config();return True
            except RuntimeError:return False
        if time.monotonic()-self.checked_at<4:return True
        try:
            raw,_=self._request('/voice/voices',timeout=1)
            if json.loads(raw).get('ok') is not True:return False
            self.checked_at=time.monotonic()
            return True
        except (OSError,RuntimeError,ValueError):return False
    def close(self):self.checked_at=0
