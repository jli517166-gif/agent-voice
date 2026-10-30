using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AgentVoice {
    // Per-engine recall stays local; selecting installation targets never changes the active voice.
    internal static class EngineSwitching {
        internal static int Index(VoiceConfig voice){return voice.engine=="cosyvoice"?0:voice.engine=="dsh-pet"?3:voice.gptVariant=="proplus"?2:1;}
        internal static string Label(int index){return index==0?"CosyVoice":index==1?"GPT-SoVITS · v5 Turbo":index==2?"GPT-SoVITS · ProPlus":"dsh-pet 桌宠语音";}
        internal static string[] Families(IEnumerable<int> indices){return indices.Where(i=>i>=0&&i<=2).Select(i=>i==0?"cosyvoice":"gpt-sovits").Distinct().ToArray();}
        static Dictionary<string,VoiceConfig> Read(SettingsStore store){string file=store.Resolve("data/engine-voices.json");return File.Exists(file)?Json.Decode<Dictionary<string,VoiceConfig>>(File.ReadAllText(file,Encoding.UTF8)):new Dictionary<string,VoiceConfig>();}
        internal static VoiceConfig Target(SettingsStore store,Settings config,int index){
            if(index<0||index>3)throw new InvalidOperationException("请选择支持的 TTS 引擎。");
            if(Index(config.voice)==index)return config.voice.Clone();
            var saved=Read(store);VoiceConfig target;
            if(saved.TryGetValue(index.ToString(),out target)&&Index(target)==index)return target.Clone();
            var profile=config.profiles.LastOrDefault(p=>Index(p.voice)==index);
            if(profile!=null)return profile.voice.Clone();
            target=config.voice.Clone();target.engine=index==0?"cosyvoice":index==3?"dsh-pet":"gpt-sovits";if(index==1||index==2)target.gptVariant=index==1?"v5turbo":"proplus";
            // A trained model from the other engine/variant is never carried across implicitly.
            if(index==0)target.baseModel="";else target.gptModel="";
            return target;
        }
        internal static void Remember(SettingsStore store,VoiceConfig voice){
            var saved=Read(store);saved[Index(voice).ToString()]=voice.Clone();
            string file=store.Resolve("data/engine-voices.json"),temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";Directory.CreateDirectory(Path.GetDirectoryName(file));
            try{File.WriteAllText(temporary,Json.Encode(saved),new UTF8Encoding(false));if(File.Exists(file))File.Replace(temporary,file,null);else File.Move(temporary,file);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}

namespace AgentVoice {
    public sealed partial class MainForm {
        async System.Threading.Tasks.Task QuickSwitchEngine(){
            if(quickEngine.SelectedIndex<0)throw new InvalidOperationException("请选择要切换的引擎。");
            int index=quickEngine.SelectedIndex;
            if(index==EngineSwitching.Index(Config.voice)){
                if(await Service.Running()){await WaitForVoice(Config.voice);Status("当前引擎已就绪："+EngineSwitching.Label(index));}
                else Status("当前引擎已保存："+EngineSwitching.Label(index)+"。服务尚未启动。");
                return;
            }
            var next=Json.Copy(Config);next.voice=EngineSwitching.Target(Store,Config,index);ValidatePaths(next.voice);
            EngineSwitching.Remember(Store,Config.voice);
            await Persist(next,new{voice=next.voice});
            BindVoice();
            if(await Service.Running()){await WaitForVoice(next.voice);Status("已切换并就绪："+EngineSwitching.Label(index));}
            else Status("当前引擎已保存："+EngineSwitching.Label(index)+"。服务尚未启动，启动后加载。");
        }
    }
}
