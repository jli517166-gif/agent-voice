using Button = AgentVoice.AnimeButton;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Threading.Tasks;
namespace AgentVoice {
    public sealed partial class MainForm {
        static int CountControls(Control control){return 1+control.Controls.Cast<Control>().Sum(c=>CountControls(c));}
        static Button FindButton(Control root,string text){foreach(Control c in root.Controls){var b=c as Button;if(b!=null&&b.Text==text)return b;var found=FindButton(c,text);if(found!=null)return found;}return null;}
        public object Verify(string directory){
            Directory.CreateDirectory(directory);var errors=new List<string>();int before=CountControls(this);string draft=body.Text;var original=ReadVoice();
            string[] expected=EngineChoices;if(!engineChoice.Items.Cast<string>().SequenceEqual(expected))errors.Add("TTS engine choices incorrect");
            foreach(string engine in expected){ShowPage("home");engineChoice.SelectedItem=engine;Application.DoEvents();if(cosyOnlyControls.Any(c=>c.Visible!=(SelectedEngine()=="cosyvoice"))||gptOnlyControls.Any(c=>c.Visible!=(SelectedEngine()=="gpt-sovits")))errors.Add("Engine controls overlap: "+engine);if(!new[]{"v5turbo","proplus"}.Contains(ReadVoice().gptVariant))errors.Add("CosyVoice selection corrupts the GPT variant field");}
            for(int n=0;n<30;n++){engineChoice.SelectedIndex=n%expected.Length;var selected=ReadVoice();if(!new[]{"v5turbo","proplus"}.Contains(selected.gptVariant))errors.Add("Repeated engine switch corrupts saved settings");}
            ShowPage("training");var trainingSelect=Controls.Find("trainingEngineChoice",true).FirstOrDefault() as ComboBox;
            if(trainingSelect==null||trainingSelect.Items.Count!=3)errors.Add("Training engine selector missing");
            else{for(int n=0;n<3;n++){trainingSelect.SelectedIndex=n;Application.DoEvents();var cosyGuide=Controls.Find("cosyTrainingGuide",true).First();var gptGuide=Controls.Find("gptTrainingGuide",true).First();if(cosyGuide.Visible!=(n==0)||gptGuide.Visible!=(n>0))errors.Add("Wrong training guidance displayed");}}
            engineChoice.SelectedIndex=1;var snapshot=ReadVoice();if(snapshot.gptVariant!="v5turbo"||snapshot.engine!="gpt-sovits")errors.Add("TTS variant binding incorrect");
            var fixture=new SettingsStore(Path.Combine(directory,"settings-fixture"));var cfg=Json.Copy(Config);cfg.voice=snapshot;cfg.profiles.Add(new SavedVoice{name="TTS check",voice=snapshot.Clone()});fixture.Write(cfg);if(Json.Encode(fixture.Read())!=Json.Encode(cfg))errors.Add("TTS settings roundtrip mismatch");
            body.Text="草稿保持不变";var ids=pages.Keys.ToArray();for(int i=0;i<120;i++){ShowPage(ids[i%ids.Length]);Application.DoEvents();}if(CountControls(this)!=before||body.Text!="草稿保持不变")errors.Add("Page switching lost controls or draft");
            if(pages.ContainsKey("profiles")||pages.ContainsKey("outputMode"))errors.Add("Removed processing page still exists");
            if(!HarnessNeedsDesktop(new Dictionary<string,object>{{"manualRequired",true}})||HarnessNeedsDesktop(new Dictionary<string,object>{{"ok",true}}))errors.Add("Harness desktop-account verification state is not parsed correctly");
            int stopClicks=0;ShowBusy("长文生成验收");busyOverlay.SetStopAction(()=>stopClicks++);busyOverlay.SetStopAction(()=>stopClicks++);
            var overlayStops=busyOverlay.Controls.OfType<System.Windows.Forms.Button>().ToArray();
            if(overlayStops.Length!=1)errors.Add("Busy overlay must expose exactly one stop action");else overlayStops[0].PerformClick();
            if(stopClicks!=1)errors.Add("Busy overlay stop action did not execute");
            string staleTargetDirectory=Path.Combine(directory,"target-error-fixture");Directory.CreateDirectory(staleTargetDirectory);string staleTargetError=Path.Combine(staleTargetDirectory,"runner-error.txt");File.WriteAllText(staleTargetError,"旧验收错误");
            if(UiChecks.PrepareTargetErrorPath(staleTargetDirectory)!=staleTargetError||File.Exists(staleTargetError))errors.Add("Target acceptance does not clear a stale runner error before a new run");
            PerformLayout();Application.DoEvents();
            using(var bitmap=new Bitmap(busyOverlay.Width,busyOverlay.Height)){busyOverlay.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(Path.Combine(directory,"busy-stop.png"));}HideBusy();
            if(CountControls(this)!=before)errors.Add("Busy overlay leaked controls");
            foreach(string label in new[]{"RVC 变声","启用 RVC","选择输出方式","选择 Piper 模型"})if(FindButton(this,label)!=null)errors.Add("Removed button remains: "+label);
            SelectEngineVoice(original);body.Text=draft;ShowPage("home");Application.DoEvents();
            foreach(string id in new[]{"home","voices","engine","advanced","connections","environmentRepair"}){ShowPage(id);Application.DoEvents();using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(Path.Combine(directory,id+".png"));}}
            ShowPage("home");return new{ok=errors.Count==0,pages=pages.Count,switches=120,engines=expected,errors=errors};
        }
        public async Task<object> VerifyFlow(string directory){Directory.CreateDirectory(directory);var original=Json.Copy(Config);bool running=await Service.Running();Exception failure=null;try{await Service.Start();var result=await Service.Request("/speak",new{text="工作好久啦，起来动一动、喝口水吧。",play=false,source="ui-test"});await WaitJob(Json.Text(result,"jobId"));await Service.Request("/stop",new{});}catch(Exception error){failure=error;}if(await Service.Running())await Service.Stop();Store.Write(original);Config=original;BindVoice();if(running)await Service.Start();if(failure!=null)throw failure;return new{ok=true,ttsGeneration=true,stop=true};}
        public async Task<object> VerifyBundle(string file,string directory){Directory.CreateDirectory(directory);var original=Json.Copy(Config);bool running=await Service.Running();if(running)await Service.Stop();try{if(!File.Exists(file))await ExportConfiguration(file,"lightweight");await ImportConfiguration(file);if(Config.voice.engine!=original.voice.engine||(Config.voice.engine=="gpt-sovits"&&Config.voice.gptVariant!=original.voice.gptVariant)||Json.Encode(Config.harness)!=Json.Encode(original.harness)||Config.token!=original.token)throw new InvalidOperationException("音色包改变了连接设置");return new{ok=true,engine=Config.voice.engine,variant=Config.voice.gptVariant,voiceImportExport=true};}finally{Store.Write(original);Config=original;BindVoice();}}
    }
    static class UiChecks {
        public static int SetupVoice(SettingsStore store,string[] args){
            string directory=args.Length>1?args[1]:store.Resolve("acceptance/setup-voice");Directory.CreateDirectory(directory);int exit=1;
            using(var form=new MainForm(store,true)){
                form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);
                form.Shown+=async(s,e)=>{try{await form.VerifySetupPlayback();File.WriteAllText(Path.Combine(directory,"setup-voice-results.json"),Json.Encode(new{ok=true,realPlayback=true,userConfirmationTested=true}));exit=0;}catch(Exception error){File.WriteAllText(Path.Combine(directory,"setup-voice-error.txt"),error.ToString());}try{if(await form.Service.Running())await form.Service.Stop();}catch{}form.Close();};Application.Run(form);
            }return exit;
        }
        public static int Environment(SettingsStore store,string[] args){
            string directory=args.Length>1?args[1]:Path.Combine(store.Root,"acceptance","environment");Directory.CreateDirectory(directory);
            try{
                if(File.Exists(store.Resolve(store.Read().engines.pythonService)))throw new InvalidOperationException("此验收仅用于空白安装目录，避免修改现有环境。");
                EnvironmentBootstrap.EnsureCore(store,value=>{}).GetAwaiter().GetResult();
                var client=new ServiceClient(store);string text=client.RunTool("environment_repair.py","repair --root "+ServiceClient.Quote(store.Root)+" --engine core").GetAwaiter().GetResult();
                var result=Json.Object(text);File.WriteAllText(Path.Combine(directory,"environment-results.json"),Json.Encode(result));return Convert.ToBoolean(result["ok"])?0:1;
            }catch(Exception error){File.WriteAllText(Path.Combine(directory,"environment-error.txt"),error.ToString());return 1;}
        }
        public static int Bundle(SettingsStore store,string[] args){
            if(args.Length<3)return 2;string file=args[1],directory=args[2];int exit=1;
            using(var form=new MainForm(store,true)){
                form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);
                form.Shown+=async(s,e)=>{try{var result=await form.VerifyBundle(file,directory);Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"bundle-results.json"),Json.Encode(result));exit=0;}catch(Exception error){Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"bundle-error.txt"),error.ToString());}try{if(await form.Service.Running())await form.Service.Stop();}catch{}form.Close();};Application.Run(form);
            }return exit;
        }
        public static int Flow(SettingsStore store,string[] args){string directory=args.Length>1?args[1]:Path.Combine(store.Root,"acceptance","ui-flow");int exit=1;using(var form=new MainForm(store,true)){form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);form.Shown+=async(s,e)=>{try{var results=await form.VerifyFlow(directory);File.WriteAllText(Path.Combine(directory,"flow-results.json"),Json.Encode(results));exit=0;}catch(Exception error){Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"flow-error.txt"),error.ToString());}try{if(await form.Service.Running())await form.Service.Stop();}catch{}form.Close();};Application.Run(form);}return exit;}
        internal static string PrepareTargetErrorPath(string directory){Directory.CreateDirectory(directory);string errorPath=Path.Combine(directory,"runner-error.txt");if(File.Exists(errorPath))File.Delete(errorPath);return errorPath;}
        public static int Target(SettingsStore store,string[] args){int exit=1;string errorPath=PrepareTargetErrorPath(store.Resolve("data/target-acceptance"));using(var form=new MainForm(store,true)){form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);form.Shown+=async(s,e)=>{try{await form.RunTargetAcceptance();exit=0;}catch(Exception error){File.WriteAllText(errorPath,error.ToString());}form.Close();};Application.Run(form);}return exit;}
        public static int Run(SettingsStore store,string[] args){string directory=args.Length>1?args[1]:Path.Combine(store.Root,"acceptance","ui");try{using(var form=new MainForm(store,true)){form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);form.Show();Application.DoEvents();object result=form.Verify(directory);string json=Json.Encode(result);File.WriteAllText(Path.Combine(directory,"ui-results.json"),json);form.Dispose();return json.Contains("\"ok\":true")?0:1;}}catch(Exception exc){Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"ui-error.txt"),exc.ToString());return 1;}}
    }
}
