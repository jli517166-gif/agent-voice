using System;
using System.IO;
using System.Text;
using System.Threading;

namespace AgentVoice {
    // UI-only preferences never enter the voice configuration or exported voice packages.
    public sealed class WelcomePreferences {
        public int version=1;
        public bool seen=false,reduceMotion=false;
        public string lastChoice="",lastStep="setup";
        public int setupStep=0;
        public System.Collections.Generic.Dictionary<string,string> setupStates=new System.Collections.Generic.Dictionary<string,string>();
        public string voiceEvidence="",voiceJobId="",voiceVerifiedAt="";
        public bool heardVoice=false,offline=false;
        public bool interfaceSounds=true;
        public int onboardingVersion=0,onboardingStep=0;
        public int[] installEngines=null;
        public bool onboardingComplete=false,onboardingDeferred=false;
    }
    public sealed class WelcomeState {
        public readonly string FilePath;
        public WelcomeState(SettingsStore store){FilePath=store.Resolve("data/welcome.json");}
        public WelcomePreferences Read(){
            if(!File.Exists(FilePath))return new WelcomePreferences();
            var value=Json.Decode<WelcomePreferences>(File.ReadAllText(FilePath,Encoding.UTF8));
            if(value==null||value.version!=1)throw new InvalidDataException("欢迎页偏好格式不兼容，请保留原文件并更新程序。");
            return value;
        }
        public void Write(WelcomePreferences value){
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));string temporary=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{
                File.WriteAllText(temporary,Json.Encode(value),new UTF8Encoding(false));
                for(int attempt=0;;attempt++){
                    try{if(File.Exists(FilePath))File.Replace(temporary,FilePath,null);else File.Move(temporary,FilePath);break;}
                    catch(IOException){if(attempt>=9||!File.Exists(temporary))throw;Thread.Sleep(50);}
                }
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        public void Choose(string choice,bool reduced){
            if(choice!="setup"&&choice!="import"&&choice!="home")throw new ArgumentException("未知欢迎页入口");
            var value=Read();value.seen=true;value.reduceMotion=reduced;value.lastChoice=choice;Write(value);
        }
    }
}
