"""Optional dsh-pet TTS adapter using its documented loopback HTTP API."""
import json
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
        from pet_endpoint import validate_endpoint
        check()
        self.endpoint=validate_endpoint(voice['petEndpoint'])
        self.voice=voice['petVoice']
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
        audio,kind=self._request('/voice/speak',payload,timeout=18)
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
        if time.monotonic()-self.checked_at<4:return True
        try:
            raw,_=self._request('/voice/voices',timeout=1)
            if json.loads(raw).get('ok') is not True:return False
            self.checked_at=time.monotonic()
            return True
        except (OSError,RuntimeError,ValueError):return False
    def close(self):self.checked_at=0
