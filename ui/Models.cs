using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
        public string model="", index="", f0Method="rmvpe", engine="cosyvoice", style="chat", reference="", referenceText="", instruction="";
        public string processing="standard", assetScope="public";
        public string synthesisMode="base", baseModel="";
        public bool advancedEnabled=false;
        public double pitch=0, formant=0, indexRate=0, rmsMix=1, speed=1, leadingSilence=.35, volume=1, protect=.33;
        public VoiceConfig Clone() { return Json.Copy(this); }
    }
    public sealed class Engines {
        public string pythonRvc="tts-engines/cosyvoice/runtime/tools/python.exe", rvcHome="engines/rvc", pythonTts="tts-engines/cosyvoice/runtime/tools/python.exe", cosyRepo="tts-engines/cosyvoice/source", cosyModel="tts-engines/cosyvoice/models/Fun-CosyVoice3-0.5B";
    }
    public sealed class Appearance {
        public string accent="#0B9FD8", backgroundColor="#F7FBFE", textColor="#101820", background="", icon="", loadingAnimation="";
    }
    public sealed class Cleanup {
        public int maxAgeMinutes=30, keepLatest=3, maxTotalMb=200, intervalSeconds=900;
    }
    public sealed class AiriConfig {
        public bool enabled=false;
        public string directory="", output="local";
    }
    public sealed class SavedVoice {
        public string id=Guid.NewGuid().ToString("N"), name="";
        public VoiceConfig voice=new VoiceConfig();
        public override string ToString() { return (voice.synthesisMode=="base"?"本地 TTS · ":"RVC＋本地 TTS · ")+name; }
    }
    public sealed class Settings {
        public int version=1, port=8820;
        public string token=Guid.NewGuid().ToString("N");
        public VoiceConfig voice=new VoiceConfig();
        public Engines engines=new Engines();
        public Appearance appearance=new Appearance();
        public Cleanup cleanup=new Cleanup();
        public AiriConfig airi=new AiriConfig();
        public Dictionary<string,bool> harness=new Dictionary<string,bool>{{"codex",true},{"deepseek",false}};
        public List<string> modelRoots=new List<string>{"models"};
        public List<string> phrases=new List<string>{"工作好久啦，起来动一动、喝口水吧～"};
        public List<SavedVoice> profiles=new List<SavedVoice>();
    }
    public static class Json {
        public static readonly JavaScriptSerializer Serializer=new JavaScriptSerializer{MaxJsonLength=8*1024*1024};
        public static string Encode(object value) { return new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Serialize(value); }
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
        public Settings Read() {var settings=File.Exists(FilePath)?Json.Decode<Settings>(File.ReadAllText(FilePath,Encoding.UTF8)):new Settings();var supported=new Dictionary<string,bool>();supported["codex"]=settings.harness.ContainsKey("codex")&&settings.harness["codex"];supported["deepseek"]=settings.harness.ContainsKey("deepseek")&&settings.harness["deepseek"];settings.harness=supported;return settings;}
        public void Write(Settings settings) {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temp=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {File.WriteAllText(temp,Json.Encode(settings),new UTF8Encoding(false));if(File.Exists(FilePath))File.Replace(temp,FilePath,null);else File.Move(temp,FilePath);}
            finally {if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
