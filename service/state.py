"""Versioned configuration, complete voice profiles and safe resource paths."""
import copy
import json
import math
import os
from pathlib import Path
import threading
import uuid

SOURCES = ("codex", "deepseek")

def defaults():
    return {"version":4,"port":8820,"token":uuid.uuid4().hex,
        "voice":{"engine":"cosyvoice","baseModel":"","gptModel":"","gptVariant":"v5turbo",
                 "speed":1,"style":"chat","reference":"","referenceText":"","instruction":"","leadingSilence":.35,"volume":1,"assetScope":"public","advancedEnabled":False},
        "engines":{"pythonService":"runtime/core/python.exe","pythonTts":"tts-engines/cosyvoice/runtime/tools/python.exe","cosyRepo":"tts-engines/cosyvoice/source","cosyModel":"tts-engines/cosyvoice/models/Fun-CosyVoice3-0.5B",
                   "pythonGpt":"tts-engines/gpt-sovits/runtime/Scripts/python.exe","gptRepo":"tts-engines/gpt-sovits/source","gptModel":"tts-engines/gpt-sovits/models/GPT-SoVITS",
                   "ffmpeg":"tools/ffmpeg.exe"},
        "appearance":{"accent":"#19C7F2","backgroundColor":"#05101D","textColor":"#EAF6FF","background":"","icon":"","loadingAnimation":""},
        "cleanup":{"maxAgeMinutes":30,"keepLatest":0,"maxTotalMb":200,"intervalSeconds":900,"keepLatestBeyondAge":False},
        "privacy":{"retainText":False,"deleteOnExit":False,"logRetentionDays":7,"harnessScope":"all","projectRoot":"","sessionId":""},
        "harness":{source:False for source in SOURCES},"airi":{"enabled":False,"directory":"","output":"local"},
        "phrases":["工作好久啦，起来动一动、喝口水吧～"],"profiles":[]}

def migrate_settings(saved):
    """One-way legacy migration; no removed processing path can be re-enabled."""
    value=copy.deepcopy(saved)
    cleanup=value.get('cleanup')
    if isinstance(cleanup,dict) and 'keepLatestBeyondAge' not in cleanup:
        cleanup['keepLatestBeyondAge']=bool(cleanup.get('keepLatest',0))
    if value.get('version',1) not in (1,2,3,4):return value
    value['version']=4;value.pop('modelRoots',None)
    if isinstance(value.get('profiles'),list):value['profiles']=[p for p in value['profiles'] if not isinstance(p,dict) or not isinstance(p.get('voice'),dict) or p['voice'].get('engine','cosyvoice') in ('cosyvoice','gpt-sovits')]
    obsolete={'model','index','pitch','formant','indexRate','rmsMix','f0Method','protect','processing','synthesisMode','piperModel','mossModel','mossVoice','mossReference','f5Model','f5Steps'}
    for voice in [value.get('voice',{})]+[p.get('voice',{}) for p in value.get('profiles',[]) if isinstance(p,dict)]:
        if voice.get('engine','cosyvoice') not in ('cosyvoice','gpt-sovits'):voice['engine']='cosyvoice'
        for key in obsolete:voice.pop(key,None)
    paths=value.get('engines',{})
    if 'pythonService' not in paths and paths.get('pythonRvc'):paths['pythonService']=paths['pythonRvc']
    if paths.get('pythonService','').replace('\\','/').endswith('tts-engines/cosyvoice/runtime/tools/python.exe'):paths['pythonService']='runtime/core/python.exe'
    for key in ('pythonRvc','rvcHome','pythonPiper','piperModel','pythonMoss','mossRepo','mossModel','pythonF5','f5Model','f5Vocoder'):paths.pop(key,None)
    return value

def merge(base, incoming):
    result = copy.deepcopy(base)
    for key, value in incoming.items():
        if key not in base:
            raise ValueError("未知配置字段：" + key)
        if isinstance(base[key], dict):
            if not isinstance(value, dict):
                raise ValueError("配置类型错误：" + key)
            result[key] = merge(base[key], value)
        else:
            result[key] = copy.deepcopy(value)
    return result

def number(value, low, high, name):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or not low <= value <= high:
        raise ValueError(name + "超出范围")

def validate_voice(voice):
    from engine_registry import ENGINE_IDS,spec
    if set(voice)!=set(defaults()['voice']):raise ValueError('声音配置包含缺失或已移除的字段')
    for key in ('engine','baseModel','gptModel','gptVariant','style','reference','referenceText','instruction','assetScope'):
        if not isinstance(voice[key],str) or len(voice[key])>8000:raise ValueError('声音配置字段错误：'+key)
    for key,low,high in (('speed',.7,1.3),('volume',0,1.5),('leadingSilence',.2,1)):number(voice[key],low,high,key)
    if voice['engine'] not in ENGINE_IDS:raise ValueError('只支持 CosyVoice、GPT-SoVITS v5 Turbo 和 ProPlus')
    if voice['gptVariant'] not in [v['id'] for v in spec('gpt-sovits')['variants']]:raise ValueError('GPT-SoVITS 版本无效')
    if not isinstance(voice['advancedEnabled'],bool) or voice['assetScope'] not in ('public','private'):raise ValueError('高级声音开关或资产范围无效')

def validate(data):
    if data["version"] != 4:
        raise ValueError("配置版本不兼容")
    number(data["port"],1024,65534,"端口")
    if not isinstance(data["token"], str) or len(data["token"]) < 24:
        raise ValueError("本地连接密钥无效")
    validate_voice(data["voice"])
    for key, value in data["engines"].items():
        if not isinstance(value,str) or len(value)>4000:
            raise ValueError("引擎路径错误：" + key)
    for key, value in data["appearance"].items():
        if not isinstance(value,str):
            raise ValueError("外观配置类型错误")
        if key.endswith("Color") or key == "accent":
            import re
            if not re.fullmatch(r"#[0-9A-Fa-f]{6}",value):
                raise ValueError("颜色须为六位十六进制值")
    for key in SOURCES:
        if not isinstance(data["harness"][key],bool):
            raise ValueError("Harness 开关必须为布尔值")
    for key,low,high in (("maxAgeMinutes",1,43200),("keepLatest",0,10000),
                         ("maxTotalMb",1,100000),("intervalSeconds",10,86400)):
        number(data["cleanup"][key],low,high,"清理策略")
    if not isinstance(data['cleanup']['keepLatestBeyondAge'],bool):raise ValueError('最新文件保留选项无效')
    privacy=data['privacy']
    if any(not isinstance(privacy[k],bool) for k in ('retainText','deleteOnExit')):raise ValueError('隐私开关无效')
    number(privacy['logRetentionDays'],0,365,'日志保留天数')
    if privacy['harnessScope'] not in ('all','project','session'):raise ValueError('自动朗读范围无效')
    if any(not isinstance(privacy[k],str) or len(privacy[k])>4000 for k in ('projectRoot','sessionId')):raise ValueError('自动朗读范围输入无效')
    if privacy['harnessScope']=='project' and not privacy['projectRoot'].strip():raise ValueError('请先选择自动朗读项目目录')
    if privacy['harnessScope']=='session' and not privacy['sessionId'].strip():raise ValueError('请先填写自动朗读会话 ID')
    if data["airi"]["output"] not in ("local", "airi") or not isinstance(data["airi"]["enabled"],bool) or not isinstance(data["airi"]["directory"],str):
        raise ValueError("桌宠配置无效")
    if not isinstance(data["phrases"],list) or len(data["phrases"])>200 or any(not isinstance(p,str) or len(p)>4000 for p in data["phrases"]):
        raise ValueError("常用句配置无效")
    if not isinstance(data["profiles"],list) or len(data["profiles"])>200:
        raise ValueError("模型配置列表无效")
    seen = set()
    for profile in data["profiles"]:
        if not isinstance(profile,dict) or set(profile) != {"id","name","voice"} or not isinstance(profile["name"],str) or not 0<len(profile["name"])<=80:
            raise ValueError("已保存模型配置无效")
        if not isinstance(profile["id"],str) or profile["id"] in seen:
            raise ValueError("模型配置 ID 重复或无效")
        seen.add(profile["id"])
        original=profile['voice']
        profile['voice']=merge(defaults()["voice"],original)
        validate_voice(profile['voice'])
    return data

def atomic_json(path, data):
    path = Path(path)
    path.parent.mkdir(parents=True,exist_ok=True)
    temp = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with temp.open("w",encoding="utf-8") as stream:
            json.dump(data,stream,ensure_ascii=False,indent=2)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temp,path)
    finally:
        if temp.exists():
            temp.unlink()

class Store:
    def __init__(self,root):
        self.root = Path(root).resolve()
        self.path = self.root / "data/settings.json"
        self.lock = threading.RLock()
        if self.path.exists():
            # Corrupt data is reported, never silently reset and overwritten.
            saved=json.loads(self.path.read_text(encoding="utf-8-sig"))
            # Versions before the supported-scope audit exposed three
            # unverified Harness adapters.  Drop those switches during load.
            if isinstance(saved.get("harness"),dict):
                saved["harness"]={key:value for key,value in saved["harness"].items() if key in SOURCES}
            migrated=migrate_settings(saved)
            self.data=validate(merge(defaults(),migrated))
            if migrated!=saved:
                backup=self.root/'data/migrations/settings-before-stability.json'
                if not backup.exists():atomic_json(backup,saved)
                atomic_json(self.path,self.data)
        else:
            self.data = defaults()
            atomic_json(self.path,self.data)

    def resolve(self,value):
        return (self.root / value).resolve() if value else None

    def portable(self,path):
        path = Path(path).resolve()
        try:
            return path.relative_to(self.root).as_posix()
        except ValueError:
            return str(path)

    def get(self):
        with self.lock:
            return copy.deepcopy(self.data)

    def update(self,patch):
        with self.lock:
            proposed = validate(merge(self.data,patch))
            if proposed["port"] != self.data["port"] or proposed["token"] != self.data["token"]:
                raise ValueError("运行期间不能修改服务端口或密钥")
            atomic_json(self.path,proposed)
            self.data = proposed
            return self.get()

    def missing(self,voice=None):
        data=self.get();voice=voice or data['voice'];missing=[]
        if not data['engines']['pythonService'] or not self.resolve(data['engines']['pythonService']).is_file():missing.append('主服务 Python 运行时')
        if voice['engine']=='cosyvoice':
            for key in ('pythonTts','cosyRepo'):
                if not data['engines'][key] or not self.resolve(data['engines'][key]).exists():missing.append(key)
            model=voice.get('baseModel') or data['engines']['cosyModel']
            if not model or not self.resolve(model).is_dir():missing.append('CosyVoice 本地模型目录')
        else:
            from local_tts import worker_config,required_files
            cfg=worker_config(self,voice)
            if not Path(cfg['python']).is_file():missing.append(voice['engine']+' Python 运行时')
            for file in required_files(cfg):
                if not file.is_file():missing.append(str(file))
        if not voice['reference'] or not self.resolve(voice['reference']).is_file():missing.append('参考录音')
        if not voice['referenceText'].strip():missing.append('参考录音对应台词')
        return missing
