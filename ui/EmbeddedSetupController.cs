using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AgentVoice {
    // The webpage receives a view model, never the service token or raw Settings.
    internal sealed class EmbeddedSetupController {
        internal readonly SetupWorkflow Flow;
        readonly SemaphoreSlim operation=new SemaphoreSlim(1,1);
        CancellationTokenSource previewCancellation;
        Task<string> previewTask;
        internal Action<string> Progress=delegate{};
        internal string ManualPrompt="";
        internal bool Finished,Deferred;
        internal bool Busy {get{return operation.CurrentCount==0;}}
        internal static readonly string[] EngineFields={"pythonService","pythonTts","cosyRepo","cosyModel","pythonGpt","gptRepo","gptModel","ffmpeg"};
        internal EmbeddedSetupController(SettingsStore store){Flow=new SetupWorkflow(store);Flow.Report=text=>Progress(text);}
        internal object Snapshot(){
            var c=Flow.Config;var p=Flow.State.Read();
            var paths=EngineFields.ToDictionary(key=>key,key=>Convert.ToString(typeof(Engines).GetField(key).GetValue(c.engines)));
            return new{step=Math.Max(0,Math.Min(9,p.onboardingStep)),engine=c.voice.engine=="cosyvoice"?0:c.voice.gptVariant=="proplus"?2:1,
                installEngines=Flow.InstallEngines(),offline=p.offline,reduceMotion=p.reduceMotion,sound=p.interfaceSounds,resources=Flow.Resources(),paths=paths,
                reference=c.voice.reference,transcript=c.voice.referenceText,baseModel=c.voice.baseModel,gptModel=c.voice.gptModel,
                profiles=c.profiles.Select(v=>new{id=v.id,name=v.name}).ToArray(),airiEnabled=c.airi.enabled,airiDirectory=c.airi.directory,
                heardVoice=p.heardVoice&&p.voiceEvidence==Flow.Evidence(),states=p.setupStates,manualPrompt=ManualPrompt,finished=Finished,deferred=Deferred};
        }
        static string Text(Dictionary<string,object> args,string key){object value;if(!args.TryGetValue(key,out value))return "";if(!(value is string)||((string)value).Length>8192)throw new InvalidOperationException("输入格式或长度不正确："+key);return (string)value;}
        static int Number(Dictionary<string,object> args,string key,int maximum){object value;if(!args.TryGetValue(key,out value)||!(value is int)||((int)value)<0||((int)value)>maximum)throw new InvalidOperationException("请选择有效项目："+key);return (int)value;}
        static bool Flag(Dictionary<string,object> args,string key){object value;if(!args.TryGetValue(key,out value)||!(value is bool))throw new InvalidOperationException("开关格式不正确："+key);return (bool)value;}
        void Go(int step){var p=Flow.State.Read();p.onboardingVersion=2;p.onboardingStep=step;Flow.State.Write(p);}
        internal async Task<object> Execute(string action,Dictionary<string,object> args){
            if(action=="state")return new{message="",state=Snapshot()};
            if(action=="stop-preview"){
                var cancellation=previewCancellation;var task=previewTask;
                if(cancellation!=null)cancellation.Cancel();
                if(task!=null)try{await task;}catch(OperationCanceledException){}
                await Flow.StopPreview();return new{message="已停止试听；需要重新播放后确认。",state=Snapshot()};
            }
            if(!await operation.WaitAsync(0))throw new InvalidOperationException("当前操作仍在执行，请等待完成。试听可使用停止按钮。");
            string message="";
            try{
                if(Flow.PreviewMayBePending)throw new InvalidOperationException("上次试听尚未确认停止，请先点击停止试听，再继续配置。");
                int step=Flow.State.Read().onboardingStep;
                switch(action){
                    case "go":Go(Number(args,"step",9));break;
                    case "back":Go(Math.Max(0,step-1));break;
                    case "next":
                        if(step==2){
                            if(args.ContainsKey("engines")){
                                var values=args["engines"] as System.Collections.IEnumerable;
                                if(values==null||args["engines"] is string)throw new InvalidOperationException("安装引擎列表无效。");
                                var indices=new List<int>();foreach(object value in values){if(!(value is int)||indices.Count>=3)throw new InvalidOperationException("安装引擎列表无效。");indices.Add((int)value);}Flow.SelectInstallEngines(indices.ToArray());
                            }else await Flow.SelectEngine(Number(args,"engine",2));
                        }
                        if(step>=9){Finish(false);break;}Go(step+1);break;
                    case "skip":if(step<1||step>8)throw new InvalidOperationException("当前页面不能跳过。");Flow.Mark("step"+step,"skipped");Go(step+1);break;
                    case "defer":Finish(true);break;
                    case "preferences":
                        var p=Flow.State.Read();p.offline=Flag(args,"offline");p.reduceMotion=Flag(args,"reduceMotion");p.interfaceSounds=Flag(args,"sound");Flow.State.Write(p);break;
                    case "computer":message=await Flow.InspectComputer();break;
                    case "resources":message=Flow.Resources();break;
                    case "download-engine":message=await Flow.PrepareSelected(false,false);break;
                    case "switch-engine":await Flow.SelectEngine(Number(args,"engine",2));message="当前引擎已保存："+EngineSwitching.Label(EngineSwitching.Index(Flow.Config.voice))+"。请重新试听确认。";break;
                    case "recover":await Task.Run(()=>ModulePackages.Recover(Flow.Store));message="未完成安装已恢复。";break;
                    case "inspect-environment":message=await Flow.PrepareSelected(true,false);break;
                    case "repair-environment":if(Flow.State.Read().offline)throw new InvalidOperationException("严格离线已开启，请导入离线环境包或关闭离线开关。");message=await Flow.PrepareSelected(true,true);break;
                    case "stop-service":if(await Flow.Service.Running())await Flow.Service.Stop();message="服务已停止，可以准备环境。";break;
                    case "install-engine":case "install-environment":
                        string kind=action=="install-engine"?"engine":"environment",file=Text(args,"file");
                        if(ModulePackages.Inspect(file).kind!=kind)throw new InvalidOperationException("所选模块与当前步骤不匹配。");
                        await Flow.InstallModule(file,kind,text=>Progress(text));message=Flow.Resources()+"\n文件安装完成，仍需真实试听验证。";break;
                    case "apply-profile":
                        var saved=Flow.Config.profiles.FirstOrDefault(v=>v.id==Text(args,"id"));if(saved==null)throw new InvalidOperationException("请选择已保存的声音。");
                        var config=Flow.Config;config.voice=saved.voice.Clone();await Flow.Save(config,new{voice=config.voice});Flow.InvalidateVoice();message="已应用："+saved.name+"。请继续试听。";break;
                    case "save-reference":
                        string reference=Text(args,"reference").Trim(),transcript=Text(args,"transcript").Trim();
                        if(!File.Exists(Flow.Store.Resolve(reference))||transcript.Length==0)throw new InvalidOperationException("请选择存在的参考音频，并填写与录音一致的台词。");
                        var voice=Flow.Config;voice.voice.reference=Flow.Store.Portable(reference);voice.voice.referenceText=transcript;
                        await Flow.Save(voice,new{voice=voice.voice});Flow.InvalidateVoice();message="参考声音已保存。";break;
                    case "save-paths":
                        if(await Flow.Service.Running())throw new InvalidOperationException("请先停止服务，再修改引擎路径。");
                        var current=Flow.Config;
                        foreach(var field in EngineFields){string path=Text(args,field).Trim();if(path.Length==0)throw new InvalidOperationException("路径不能为空："+field);typeof(Engines).GetField(field).SetValue(current.engines,Flow.Store.Portable(path));}
                        current.voice.baseModel=Text(args,"baseModel").Trim();current.voice.gptModel=Text(args,"voiceGptModel").Trim();
                        Flow.Store.Write(current);Flow.InvalidateVoice();message=Flow.Resources();break;
                    case "preview":
                        using(var cancellation=new CancellationTokenSource()){
                            previewCancellation=cancellation;
                            try{previewTask=Flow.Preview(cancellation.Token);message=await previewTask;}
                            finally{previewTask=null;previewCancellation=null;}
                        }break;
                    case "confirm":Flow.Confirm();message="已记录你的听感确认。";break;
                    case "save-airi":
                        var airi=Flow.Config;airi.airi.enabled=Flag(args,"enabled");if(!airi.airi.enabled)airi.airi.output="local";
                        await Flow.Save(airi,new{airi=airi.airi});message=airi.airi.enabled?"AIRI 已启用，请选择目录并验证。":"AIRI 已关闭，仍可本机播放。";break;
                    case "connect-harness":case "connect-airi":
                        bool useAiri=action=="connect-airi";if(useAiri&&!Flow.Config.airi.enabled)throw new InvalidOperationException("请先手动启用 AIRI 联动。");
                        await Flow.EnsureService();var connector=new ConnectionSetup(Flow);connector.Report=text=>Progress(text);connector.ManualPrompt=text=>{ManualPrompt=text;Progress(text);};
                        if(useAiri)await connector.Airi(Text(args,"directory"));else await connector.Harness(Text(args,"directory"));
                        message=(useAiri?"AIRI":"Harness")+" 真实回复与播放验证通过。";return new{message=message,state=Snapshot(),celebrate=true};
                    case "summary":message=await Flow.Summary();break;
                    default:throw new InvalidOperationException("不支持的配置操作。");
                }
                return new{message=message,state=Snapshot()};
            }finally{operation.Release();}
        }
        void Finish(bool deferred){var p=Flow.State.Read();p.onboardingVersion=2;p.onboardingComplete=!deferred;p.onboardingDeferred=deferred;p.seen=true;Flow.State.Write(p);Finished=true;Deferred=deferred;}
    }
}
