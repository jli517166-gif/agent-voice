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
    return {"version":1, "port":8820, "token":uuid.uuid4().hex,
        "voice":{"model":"", "index":"", "pitch":0, "formant":0,
                 "indexRate":0, "rmsMix":1, "f0Method":"rmvpe", "engine":"cosyvoice",
                 "speed":1, "style":"chat", "reference":"", "referenceText":"",
                 "instruction":"", "leadingSilence":0.35, "volume":1,
                 "processing":"standard", "protect":0.33, "assetScope":"public",
                 "synthesisMode":"base", "baseModel":"", "advancedEnabled":False},
        "engines":{"pythonRvc":"tts-engines/cosyvoice/runtime/tools/python.exe", "rvcHome":"engines/rvc",
                   "pythonTts":"tts-engines/cosyvoice/runtime/tools/python.exe", "cosyRepo":"tts-engines/cosyvoice/source",
                   "cosyModel":"tts-engines/cosyvoice/models/Fun-CosyVoice3-0.5B"},
        "appearance":{"accent":"#0B9FD8", "backgroundColor":"#F7FBFE", "textColor":"#101820",
                      "background":"", "icon":"", "loadingAnimation":""},
        "cleanup":{"maxAgeMinutes":30, "keepLatest":3, "maxTotalMb":200, "intervalSeconds":900},
        "harness":{source:source == "codex" for source in SOURCES},
        "airi":{"enabled":False, "directory":"", "output":"local"},
        "modelRoots":["models"], "phrases":["工作好久啦，起来动一动、喝口水吧～"], "profiles":[]}

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
    for key in ("model", "index", "engine", "reference", "referenceText", "instruction", "style", "f0Method", "processing", "assetScope", "synthesisMode", "baseModel"):
        if not isinstance(voice[key], str) or len(voice[key]) > 8000:
            raise ValueError("声音配置字段错误：" + key)
    for key, low, high in (("pitch",-16,16),("formant",-2,2),("indexRate",0,1),
                         ("rmsMix",0,1),("speed",.7,1.3),("volume",0,1.5),("leadingSilence",.2,1),("protect",0,.5)):
        number(voice[key],low,high,key)
    if voice["f0Method"] not in ("rmvpe", "harvest", "crepe", "pm"):
        raise ValueError("不支持的基频算法")
    if voice["engine"] not in ("cosyvoice", "sapi"):
        raise ValueError("该本地 TTS 引擎尚未适配")
    if voice["synthesisMode"] not in ("rvc", "base") or not isinstance(voice["advancedEnabled"],bool):
        raise ValueError("音频输出模式或高级声音开关无效")
    if voice["processing"] not in ("standard", "accepted") or voice["assetScope"] not in ("public", "private"):
        raise ValueError("声音处理模式或资产范围无效")
    if voice["processing"] == "accepted" and voice["formant"] != 0:
        raise ValueError("认可版兼容链保持原算法；调整声线粗细请先切换标准 RVC 模式")

def validate(data):
    if data["version"] != 1:
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
    if data["airi"]["output"] not in ("local", "airi") or not isinstance(data["airi"]["enabled"],bool) or not isinstance(data["airi"]["directory"],str):
        raise ValueError("桌宠配置无效")
    if not isinstance(data["phrases"],list) or len(data["phrases"])>200 or any(not isinstance(p,str) or len(p)>4000 for p in data["phrases"]):
        raise ValueError("常用句配置无效")
    if not isinstance(data["modelRoots"],list) or len(data["modelRoots"])>100 or any(not isinstance(p,str) for p in data["modelRoots"]):
        raise ValueError("模型目录配置无效")
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
        if 'synthesisMode' not in original:profile['voice']['synthesisMode']='rvc'
        if 'advancedEnabled' not in original:profile['voice']['advancedEnabled']=True
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
            self.data = validate(merge(defaults(),saved))
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
        data=self.get(); voice=voice or data["voice"]; missing=[]
        if voice.get("synthesisMode","rvc")=="rvc" and (not voice["model"] or not self.resolve(voice["model"]).is_file()):
            missing.append("RVC 模型 .pth")
        if voice.get("synthesisMode","rvc")=="rvc" and voice["index"] and not self.resolve(voice["index"]).is_file():
            missing.append("RVC 索引 .index")
        for key in (("pythonRvc","rvcHome") if voice.get("synthesisMode","rvc")=="rvc" else ("pythonRvc",)):
            if not data["engines"][key] or not self.resolve(data["engines"][key]).exists():
                missing.append(key)
        if voice["engine"] == "cosyvoice":
            for key in ("pythonTts","cosyRepo"):
                if not data["engines"][key] or not self.resolve(data["engines"][key]).exists():
                    missing.append(key)
            model=voice.get("baseModel") or data["engines"]["cosyModel"]
            if not model or not self.resolve(model).is_dir():missing.append("CosyVoice 本地模型目录")
            if not voice["reference"] or not self.resolve(voice["reference"]).is_file():
                missing.append("公共参考录音")
            if not voice["referenceText"].strip():
                missing.append("参考录音对应台词")
        return missing
