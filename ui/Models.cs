using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AgentVoice {
    public static class AppPaths {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern uint GetFinalPathNameByHandle(SafeFileHandle handle,StringBuilder path,uint capacity,uint flags);
        public static string CanonicalDirectory(string directory){
            string absolute=Path.GetFullPath(directory);if(absolute.Length>Path.GetPathRoot(absolute).Length)absolute=absolute.TrimEnd('\\','/');
            using(var handle=CreateFile(absolute,0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero)){
                if(handle.IsInvalid)return absolute;var buffer=new StringBuilder(32768);uint count=GetFinalPathNameByHandle(handle,buffer,(uint)buffer.Capacity,0);
                if(count==0||count>=buffer.Capacity)return absolute;
                string resolved=buffer.ToString();if(resolved.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase))resolved=@"\\"+resolved.Substring(8);else if(resolved.StartsWith(@"\\?\"))resolved=resolved.Substring(4);
                return Path.GetFullPath(resolved);
            }
        }
    }
    public sealed class VoiceConfig {
        public string engine="cosyvoice",style="chat",reference="",referenceText="",instruction="",assetScope="public";
        public string baseModel="",gptModel="",gptVariant="v5turbo";
        public bool advancedEnabled=false;
        public double speed=1,leadingSilence=.35,volume=1;
        public VoiceConfig Clone() { return Json.Copy(this); }
    }
    public sealed class Engines {
        public string pythonService="runtime/core/python.exe",pythonTts="tts-engines/cosyvoice/runtime/tools/python.exe",cosyRepo="tts-engines/cosyvoice/source",cosyModel="tts-engines/cosyvoice/models/Fun-CosyVoice3-0.5B";
        public string pythonGpt="tts-engines/gpt-sovits/runtime/Scripts/python.exe",gptRepo="tts-engines/gpt-sovits/source",gptModel="tts-engines/gpt-sovits/models/GPT-SoVITS";
        public string ffmpeg="tools/ffmpeg.exe";
    }
    public sealed class Appearance {
        public string accent="#19C7F2", backgroundColor="#05101D", textColor="#EAF6FF", background="", icon="", loadingAnimation="";
    }
    public sealed class Cleanup {
        public int maxAgeMinutes=30, keepLatest=0, maxTotalMb=200, intervalSeconds=900;
        public bool keepLatestBeyondAge=false;
    }
    public sealed class PrivacyConfig {
        public bool retainText=false,deleteOnExit=false;
        public int logRetentionDays=7;
        public string harnessScope="all",projectRoot="",sessionId="";
    }
    public sealed class AiriConfig {
        public bool enabled=false;
        public string directory="", output="local";
    }
    public sealed class SavedVoice {
        public string id=Guid.NewGuid().ToString("N"), name="";
        public VoiceConfig voice=new VoiceConfig();
        public override string ToString() { return voice.engine+" · "+name; }
    }
    public sealed class Settings {
        public int version=4, port=8820;
        public string token=Guid.NewGuid().ToString("N");
        public VoiceConfig voice=new VoiceConfig();
        public Engines engines=new Engines();
        public Appearance appearance=new Appearance();
        public Cleanup cleanup=new Cleanup();
        public PrivacyConfig privacy=new PrivacyConfig();
        public AiriConfig airi=new AiriConfig();
        public Dictionary<string,bool> harness=new Dictionary<string,bool>{{"codex",false},{"deepseek",false}};
        public List<string> phrases=new List<string>{"工作好久啦，起来动一动、喝口水吧～"};
        public List<SavedVoice> profiles=new List<SavedVoice>();
    }
    public static class Json {
        public static readonly JavaScriptSerializer Serializer=new JavaScriptSerializer{MaxJsonLength=8*1024*1024};
        public static string Encode(object value) { return new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Serialize(value); }
        // CLR reflection and JSON property order are not an identity contract.
        // Normalize into dictionaries before sorting, including nested objects.
        public static string Canonical(object value){return Encode(Ordered(Decode<object>(Encode(value))));}
        static object Ordered(object value){
            var map=value as IDictionary<string,object>;
            if(map!=null){var sorted=new SortedDictionary<string,object>(StringComparer.Ordinal);foreach(var item in map)sorted[item.Key]=Ordered(item.Value);return sorted;}
            var items=value as object[];return items==null?value:items.Select(Ordered).ToArray();
        }
        public static T Decode<T>(string text) { return new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Deserialize<T>(text); }
        public static T Copy<T>(T obj) { return Decode<T>(Encode(obj)); }
        public static Dictionary<string,object> Object(string text) { return Decode<Dictionary<string,object>>(text); }
        public static string Text(Dictionary<string,object> obj,string key) { object value;return obj.TryGetValue(key,out value)&&value!=null?Convert.ToString(value):""; }
    }
    public sealed class SettingsStore {
        public readonly string Root,FilePath;
        public SettingsStore(string root) {Root=AppPaths.CanonicalDirectory(root);FilePath=Path.Combine(Root,"data","settings.json");}
        public string Resolve(string path) {return string.IsNullOrEmpty(path)?"":Path.GetFullPath(Path.IsPathRooted(path)?path:Path.Combine(Root,path));}
        public string Portable(string path) {string absolute=Resolve(path),prefix=Root.TrimEnd('\\')+"\\";return absolute.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)?absolute.Substring(prefix.Length).Replace('\\','/'):absolute;}
        Settings FirstRunSettings(){return new Settings();}
        public Settings Read() {var settings=File.Exists(FilePath)?Json.Decode<Settings>(File.ReadAllText(FilePath,Encoding.UTF8)):FirstRunSettings();var supported=new Dictionary<string,bool>();supported["codex"]=settings.harness.ContainsKey("codex")&&settings.harness["codex"];supported["deepseek"]=settings.harness.ContainsKey("deepseek")&&settings.harness["deepseek"];settings.harness=supported;
            if(settings.version<4){string backup=Path.Combine(Root,"data","migrations","settings-before-stability.json");Directory.CreateDirectory(Path.GetDirectoryName(backup));if(File.Exists(FilePath)&&!File.Exists(backup))File.Copy(FilePath,backup);var raw=Json.Object(File.ReadAllText(FilePath,Encoding.UTF8));object pathObject;if(raw.TryGetValue("engines",out pathObject)){var paths=pathObject as Dictionary<string,object>;if(paths!=null&&!paths.ContainsKey("pythonService")&&paths.ContainsKey("pythonRvc"))settings.engines.pythonService=Convert.ToString(paths["pythonRvc"]);}settings.version=4;}
            if(File.Exists(FilePath)){var raw=Json.Object(File.ReadAllText(FilePath,Encoding.UTF8));object cleanupValue;if(raw.TryGetValue("cleanup",out cleanupValue)){var old=cleanupValue as Dictionary<string,object>;if(old!=null&&!old.ContainsKey("keepLatestBeyondAge"))settings.cleanup.keepLatestBeyondAge=settings.cleanup.keepLatest>0;}}
            if(settings.privacy==null)settings.privacy=new PrivacyConfig();
            if(settings.engines.pythonService.Replace('\\','/').EndsWith("tts-engines/cosyvoice/runtime/tools/python.exe",StringComparison.OrdinalIgnoreCase))settings.engines.pythonService="runtime/core/python.exe";
            settings.profiles.RemoveAll(p=>!new[]{"cosyvoice","gpt-sovits"}.Contains(p.voice.engine));if(!new[]{"cosyvoice","gpt-sovits"}.Contains(settings.voice.engine))settings.voice.engine="cosyvoice";
            return settings;}
        public void Write(Settings settings) {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            using(var guard=new FileStream(Path.Combine(Root,"data","engine-operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite)){
            try{guard.Lock(0,1);}catch(IOException error){throw new InvalidOperationException("服务或文件操作仍在运行，请稍后重试；未覆盖配置。",error);}
            string temp=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {File.WriteAllText(temp,Json.Encode(settings),new UTF8Encoding(false));if(File.Exists(FilePath))File.Replace(temp,FilePath,null);else File.Move(temp,FilePath);}
            finally {if(File.Exists(temp))File.Delete(temp);guard.Unlock(0,1);}}
        }
    }
}
