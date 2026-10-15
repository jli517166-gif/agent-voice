using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentVoice {
    static class FloatingChecks {
        sealed class TestHost:Form {protected override bool ShowWithoutActivation{get{return true;}}}
        sealed class FakeService:IFloatingVoiceService {
            readonly SettingsStore store;
            public bool running;
            public readonly List<string> calls=new List<string>();
            public FakeService(SettingsStore value){store=value;}
            public Task<bool> Running(){return Task.FromResult(running);}
            public Task Start(){calls.Add("start");running=true;return Task.FromResult(0);}
            public Task Stop(){calls.Add("stop");running=false;return Task.FromResult(0);}
            public Task<Dictionary<string,object>> Request(string path,object data=null,int timeout=5000){calls.Add(path);if(path=="/api/config"){var patch=Json.Object(Json.Encode(data));var cfg=store.Read();if(patch.ContainsKey("voice"))cfg.voice=Json.Decode<VoiceConfig>(Json.Encode(patch["voice"]));if(patch.ContainsKey("harness"))cfg.harness=Json.Decode<Dictionary<string,bool>>(Json.Encode(patch["harness"]));store.Write(cfg);}return Task.FromResult(new Dictionary<string,object>{{"service","agent-voice-public"},{"queued",0},{"error",null}});}
        }
        static void Check(bool condition,string description,List<string> errors){if(!condition)errors.Add(description);}
        public static int Live(SettingsStore store,string directory){Directory.CreateDirectory(directory);try{return LiveAsync(store,directory).GetAwaiter().GetResult();}catch(Exception error){File.WriteAllText(Path.Combine(directory,"live-error.txt"),error.ToString());return 1;}}
        static async Task<int> LiveAsync(SettingsStore store,string directory){
            var original=store.Read();File.WriteAllText(Path.Combine(directory,"original-settings.json"),Json.Encode(original));var service=new ServiceClient(store);bool running=await service.Running();var errors=new List<string>();int changed=0;var actions=new FloatingVoiceActions(store,service,(cfg,voice)=>changed++);
            Exception failure=null;
            try{
                await actions.Execute("start");Check(await service.Running(),"真实服务未启动",errors);
                Check((await actions.Status()).Contains("已开启"),"真实状态未返回",errors);
                await actions.Execute("quiet");
                if(original.profiles.Count>0){var profile=original.profiles[0];await actions.Execute("profile",profile.id);Check(FloatingVoiceActions.SameVoice(store.Read().voice,profile.voice),"真实声音配置未应用",errors);}
                await actions.Execute("auto");var cfg=store.Read();Check(cfg.harness["codex"]!=original.harness["codex"]&&cfg.harness["deepseek"]==original.harness["deepseek"],"真实自动朗读开关错误",errors);
            }catch(Exception error){failure=error;}
            if(await service.Running()){await service.Request("/api/config",new{voice=original.voice,harness=original.harness},90000);await service.Request("/api/connections/reload",new{});}else{var cfg=store.Read();cfg.voice=original.voice;cfg.harness=original.harness;store.Write(cfg);}
            if(!running&&await service.Running())await service.Stop();
            if(failure!=null)throw failure;
            var restored=store.Read();Check(FloatingVoiceActions.SameVoice(restored.voice,original.voice),"未恢复真实声音："+string.Join(",",Array.ConvertAll(Array.FindAll(typeof(VoiceConfig).GetFields(),f=>!object.Equals(f.GetValue(restored.voice),f.GetValue(original.voice))),f=>f.Name)),errors);Check(Json.Encode(restored.harness)==Json.Encode(original.harness),"未恢复真实自动朗读开关",errors);Check(await service.Running()==running,"未恢复原服务状态",errors);
            File.WriteAllText(Path.Combine(directory,"floating-live-results.json"),Json.Encode(new{ok=errors.Count==0,operations=new[]{"start","status","stop-speech","apply-profile","toggle-auto-reading","restore-original-settings"},changedCallbacks=changed,errors=errors}));return errors.Count==0?0:1;
        }
        public static int Run(string directory){Directory.CreateDirectory(directory);int exit=1;using(var host=new TestHost{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-32000,-32000)}){
            host.Shown+=async(s,e)=>{try{var result=await Verify(directory);File.WriteAllText(Path.Combine(directory,"floating-results.json"),Json.Encode(result));exit=result.ok?0:1;}catch(Exception error){File.WriteAllText(Path.Combine(directory,"floating-error.txt"),error.ToString());}host.Close();};Application.Run(host);
        }return exit;}
        sealed class Result {public bool ok;public int layoutChecks,controlChecks;public bool actualCodexDetected;public List<string> errors;}
        static async Task<Result> Verify(string directory){
            var errors=new List<string>();int layouts=0;
            Check(CodexWindowTracker.IsCodexImage(@"C:\Program Files\WindowsApps\OpenAI.Codex_1.0_x64__abc\app\ChatGPT.exe"),"未识别 Codex 桌面包",errors);
            Check(!CodexWindowTracker.IsCodexImage(@"C:\Program Files\ChatGPT\ChatGPT.exe"),"误认普通 ChatGPT",errors);
            Check(!CodexWindowTracker.IsCodexImage(@"C:\Users\test\OpenAI\Codex\bin\123\codex.exe"),"误认命令行进程",errors);
            foreach(float scale in new[]{1F,1.5F,2F})foreach(var work in new[]{new Rectangle(0,0,1920,1040),new Rectangle(-1920,0,1920,1040)})foreach(bool maximized in new[]{false,true}){
                var window=maximized?work:new Rectangle(work.Left+100,work.Top+70,1000,800);
                var entry=FloatingLayout.Entry(window,work,new Size((int)(104*scale),(int)(38*scale)),scale,0,0);
                var panel=FloatingLayout.Panel(entry,work,new Size((int)(326*scale),(int)(335*scale)),(int)(8*scale));
                Check(work.Contains(entry)&&work.Contains(panel),"悬浮窗超出工作区",errors);Check(!entry.IntersectsWith(panel),"控制菜单遮挡入口",errors);layouts+=2;
            }
            var root=new SettingsStore(Path.Combine(directory,"fixture"));var cfg=new Settings();cfg.harness["deepseek"]=true;cfg.phrases.Add("保留的常用句");cfg.profiles.Add(new SavedVoice{id="one",name="测试声音一",voice=new VoiceConfig{pitch=2}});cfg.profiles.Add(new SavedVoice{id="two",name="测试声音二",voice=new VoiceConfig{pitch=7}});root.Write(cfg);
            string token=cfg.token;var client=new FakeService(root);int changes=0;var actions=new FloatingVoiceActions(root,client,(next,voice)=>changes++);
            await actions.Execute("start");Check(client.running,"启动未执行",errors);
            await actions.Execute("quiet");Check(client.calls.Contains("/stop"),"停止朗读未调用服务",errors);
            await actions.Execute("profile","two");Check(root.Read().voice.pitch==7&&changes==1,"切换声音未保存或未同步主界面",errors);
            await actions.Execute("auto");Check(!root.Read().harness["codex"]&&root.Read().harness["deepseek"]&&client.calls.Contains("/api/connections/reload"),"自动朗读开关影响其他 Harness 或未重载",errors);
            await actions.Execute("stop");Check(!client.running,"关闭服务未执行",errors);
            await actions.Execute("profile","one");Check(root.Read().voice.pitch==2&&!client.running,"离线切换声音未持久化或意外开启服务",errors);
            bool rejected=false;try{await actions.Execute("profile","missing");}catch(InvalidOperationException){rejected=true;}Check(rejected&&root.Read().voice.pitch==2,"无效声音方案改变了配置",errors);
            Check(root.Read().token==token&&root.Read().phrases.Count==2,"控制操作改变无关配置",errors);
            FloatingTarget target=new FloatingTarget{bounds=new Rectangle(-31800,-31900,1100,800),workArea=new Rectangle(-32000,-32000,1920,1080),active=false};
            using(var control=new CodexFloatingController(root,client,null,()=>{},(a,b)=>target)){
                control.Refresh();Check(!control.EntryVisible,"切换其他程序后入口未隐藏",errors);
                target.active=true;control.Refresh();Check(control.EntryVisible,"目标在前台时未显示入口",errors);
                var before=control.EntryBounds;target.bounds.Offset(50,30);control.Refresh();Check(control.EntryBounds.Location==new Point(before.X+50,before.Y+30),"移动目标窗口时入口未跟随",errors);
                control.OpenPanel(false);Application.DoEvents();var menu=Application.OpenForms.Cast<Form>().First(f=>f.Text=="Codex 语音控制");menu.Close();control.OpenPanel(false);Check(!menu.IsDisposed&&menu.Visible,"关闭菜单后无法重新打开",errors);control.SavePreview(directory);
                target.minimized=true;control.Refresh();Check(!control.EntryVisible,"最小化后入口未隐藏",errors);
                target.minimized=false;target.active=true;control.Refresh();target=null;control.Refresh();Check(!control.EntryVisible,"目标关闭后入口未隐藏",errors);
                target=new FloatingTarget{bounds=new Rectangle(-31800,-31900,1100,800),workArea=new Rectangle(-32000,-32000,1920,1080),active=true};control.Refresh();control.SetEnabled(false);Check(!control.EntryVisible&&!FloatingPreferences.Read(root).enabled,"关闭入口未保存",errors);
                control.SetEnabled(true);control.ResetPosition();Check(FloatingPreferences.Read(root).offsetX==0,"重置位置失败",errors);
            }
            var persisted=FloatingPreferences.Read(root);persisted.offsetX=-100;persisted.offsetY=-40;persisted.Save(root);
            using(var reopened=new CodexFloatingController(root,client,null,()=>{},(a,b)=>target)){var saved=reopened.EntryBounds;Check(FloatingPreferences.Read(root).offsetX==-100,"悬浮位置没有持久化",errors);reopened.ResetPosition();Check(reopened.EntryBounds.X>saved.X&&reopened.EntryBounds.Y>saved.Y,"重新启动后没有恢复保存的位置",errors);}
            var actual=new CodexWindowTracker().Read(IntPtr.Zero,IntPtr.Zero);
            return new Result{ok=errors.Count==0,layoutChecks=layouts,controlChecks=15,actualCodexDetected=actual!=null,errors=errors};
        }
    }
}
