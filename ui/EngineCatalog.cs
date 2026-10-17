using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace AgentVoice {
    public sealed class EngineVariant {public string id,label,packageType;}
    public sealed class EngineSpec {public string id,label,adapter,pythonKey,sourceKey,modelKey,modelField;public Dictionary<string,bool> capabilities;public List<EngineVariant> variants;}
    public sealed class EngineCatalog {
        public int version;public List<EngineSpec> engines;
        public static EngineCatalog Load(){var p=Path.Combine(Application.StartupPath,"service","engine-catalog.json");var c=Json.Decode<EngineCatalog>(File.ReadAllText(p));if(c.version!=1||c.engines==null||c.engines.Count==0)throw new InvalidOperationException("引擎目录清单损坏，请恢复程序文件。");return c;}
    }
    public sealed partial class MainForm {
        static readonly EngineCatalog Catalog=EngineCatalog.Load();
        static readonly EngineSpec[] ChoiceSpecs=Catalog.engines.SelectMany(e=>e.variants.Select(v=>e)).ToArray();
        static readonly EngineVariant[] ChoiceVariants=Catalog.engines.SelectMany(e=>e.variants).ToArray();
        static readonly string[] EngineChoices=ChoiceVariants.Select(v=>v.label).ToArray();
        int EngineIndex(){return engineChoice==null||engineChoice.SelectedIndex<0?0:engineChoice.SelectedIndex;}
        bool Capability(string key){bool enabled;return ChoiceSpecs[EngineIndex()].capabilities.TryGetValue(key,out enabled)&&enabled;}
        string SelectedEngine(){return ChoiceSpecs[EngineIndex()].id;}
        string SelectedVariant(){return SelectedEngine()=="gpt-sovits"?ChoiceVariants[EngineIndex()].id:Config.voice.gptVariant;}
        void SelectEngineVoice(VoiceConfig v){int i=Array.FindIndex(ChoiceSpecs,e=>e.id==v.engine);if(v.engine=="gpt-sovits")for(int n=0;n<ChoiceSpecs.Length;n++)if(ChoiceSpecs[n].id==v.engine&&ChoiceVariants[n].id==v.gptVariant)i=n;engineChoice.SelectedIndex=Math.Max(0,i);}
    }
}
