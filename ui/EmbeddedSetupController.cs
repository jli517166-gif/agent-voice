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
        internal Action<string> Progress=delegate{};
        internal bool Finished,Deferred;
        internal bool Busy {get{return operation.CurrentCount==0;}}
        internal static readonly string[] EngineFields={"pythonService","pythonTts","cosyRepo","cosyModel","pythonGpt","gptRepo","gptModel","ffmpeg"};
        internal EmbeddedSetupController(SettingsStore store){Flow=new SetupWorkflow(store);Flow.Report=text=>Progress(text);Go(Flow.ResumeStep);}
        internal object Snapshot(){
            var c=Flow.Config;var p=Flow.State.Read();
            var paths=EngineFields.ToDictionary(key=>key,key=>Convert.ToString(typeof(Engines).GetField(key).GetValue(c.engines)));
            return new{step=Math.Max(0,Math.Min(9,p.onboardingStep)),stepLabel=SetupWorkflow.StepLabel(p.onboardingStep),legalAccepted=Flow.LegalAccepted,engine=c.voice.engine=="cosyvoice"?0:c.voice.gptVariant=="proplus"?2:1,
                installEngines=Flow.InstallEngines(),offline=p.offline,reduceMotion=p.reduceMotion,sound=p.interfaceSounds,resources=Flow.Resources(),paths=paths,
                airiEnabled=c.airi.enabled,airiDirectory=c.airi.directory,autoRead=c.harness,
                states=p.setupStates,finished=Finished,deferred=Deferred};
        }
        static string Text(Dictionary<string,object> args,string key){object value;if(!args.TryGetValue(key,out value))return "";if(!(value is string)||((string)value).Length>8192)throw new InvalidOperationException("输入格式或长度不正确："+key);return (string)value;}
        static int Number(Dictionary<string,object> args,string key,int maximum){object value;if(!args.TryGetValue(key,out value)||!(value is int)||((int)value)<0||((int)value)>maximum)throw new InvalidOperationException("请选择有效项目："+key);return (int)value;}
        static bool Flag(Dictionary<string,object> args,string key){object value;if(!args.TryGetValue(key,out value)||!(value is bool))throw new InvalidOperationException("开关格式不正确："+key);return (bool)value;}
        void Go(int step){if(step!=0&&step!=5)Flow.RequireLegal();var p=Flow.State.Read();p.onboardingVersion=2;p.onboardingStep=step;Flow.State.Write(p);}
        internal async Task<object> Execute(string action,Dictionary<string,object> args){
            if(action=="state")return new{message="",state=Snapshot()};
            if(!await operation.WaitAsync(0))throw new InvalidOperationException("当前操作仍在执行，请等待完成。");
            string message="";
            try{
                int step=Flow.State.Read().onboardingStep;
                switch(action){
                    case "go":Go(Number(args,"step",9));break;
                    case "back":Go(SetupWorkflow.Adjacent(step,-1));break;
                    case "next":
                        if(step!=0)Flow.RequireLegal();
                        if(step==2){
                            if(args.ContainsKey("engines")){
                                var values=args["engines"] as System.Collections.IEnumerable;
                                if(values==null||args["engines"] is string)throw new InvalidOperationException("安装引擎列表无效。");
                                var indices=new List<int>();foreach(object value in values){if(!(value is int)||indices.Count>=3)throw new InvalidOperationException("安装引擎列表无效。");indices.Add((int)value);}Flow.SelectInstallEngines(indices.ToArray());
                            }else await Flow.SelectEngine(Number(args,"engine",2));
                        }
                        if(step>=9){Finish(false);break;}Go(SetupWorkflow.Adjacent(step,1));break;
                    case "skip":if(step<1||step>8||step==5)throw new InvalidOperationException("当前页面不能跳过；使用须知需点击确认后继续。");Flow.RequireLegal();Flow.Mark("step"+step,"skipped");Go(SetupWorkflow.Adjacent(step,1));break;
                    case "defer":Finish(true);break;
                    case "preferences":
                        var p=Flow.State.Read();p.offline=Flag(args,"offline");p.reduceMotion=Flag(args,"reduceMotion");p.interfaceSounds=Flag(args,"sound");Flow.State.Write(p);break;
                    case "computer":message=await Flow.InspectComputer();break;
                    case "resources":message=Flow.Resources();break;
                    case "download-engine":message=await Flow.PrepareSelected(false,false);break;
                    case "switch-engine":await Flow.SelectEngine(Number(args,"engine",2));message="当前引擎已保存："+EngineSwitching.Label(EngineSwitching.Index(Flow.Config.voice))+"。声音可用性在控制台单独验证。";break;
                    case "recover":await Task.Run(()=>ModulePackages.Recover(Flow.Store));message="未完成安装已恢复。";break;
                    case "inspect-environment":message=await Flow.PrepareSelected(true,false);break;
                    case "repair-environment":if(Flow.State.Read().offline)throw new InvalidOperationException("严格离线已开启，请导入离线环境包或关闭离线开关。");message=await Flow.PrepareSelected(true,true);break;
                    case "stop-service":if(await Flow.Service.Running())await Flow.Service.Stop();message="服务已停止，可以准备环境。";break;
                    case "install-engine":case "install-environment":
                        string kind=action=="install-engine"?"engine":"environment",file=Text(args,"file");
                        if(ModulePackages.Inspect(file).kind!=kind)throw new InvalidOperationException("所选模块与当前步骤不匹配。");
                        await Flow.InstallModule(file,kind,text=>Progress(text));message=Flow.Resources()+"\n文件安装完成；不代表声音或法律授权验证通过。";break;
                    case "save-paths":
                        if(await Flow.Service.Running())throw new InvalidOperationException("请先停止服务，再修改引擎路径。");
                        var current=Flow.Config;
                        foreach(var field in EngineFields){string path=Text(args,field).Trim();if(path.Length==0)throw new InvalidOperationException("路径不能为空："+field);typeof(Engines).GetField(field).SetValue(current.engines,Flow.Store.Portable(path));}
                        Flow.Store.Write(current);Flow.InvalidateVoice();message=Flow.Resources();break;
                    case "legal-read":if(step!=5)throw new InvalidOperationException("请先打开使用须知页。");Flow.AcceptLegal();message="已确认阅读须知，可以继续下一步。";break;
                    case "save-autoread":
                        Flow.RequireLegal();var reading=Flow.Config;
                        foreach(string source in new[]{"codex","deepseek"})reading.harness[source]=Flag(args,source);
                        await Flow.Save(reading,new{harness=reading.harness});message="自动朗读开关已保存；MCP 安装自检仍不生成语音。";break;
                    case "check-mcp":
                        var check=await new ConnectionSetup(Flow).CheckMcp(Text(args,"source"));
                        return new{message=Json.Text(check,"message"),state=Snapshot(),celebrate=check.ContainsKey("passed")&&Convert.ToBoolean(check["passed"])};
                    case "save-airi":
                        var airi=Flow.Config;airi.airi.enabled=Flag(args,"enabled");if(!airi.airi.enabled)airi.airi.output="local";
                        await Flow.Save(airi,new{airi=airi.airi});message=airi.airi.enabled?"AIRI 已启用，请选择目录并验证。":"AIRI 已关闭，仍可本机播放。";break;
                    case "connect-harness":case "connect-airi":
                        bool useAiri=action=="connect-airi";if(useAiri&&!Flow.Config.airi.enabled)throw new InvalidOperationException("请先手动启用 AIRI 联动。");
                        var connector=new ConnectionSetup(Flow);connector.Report=text=>Progress(text);
                        bool passed;
                        if(useAiri){await Flow.EnsureService();await connector.Airi(Text(args,"directory"));message="AIRI 实际播放验证通过。";passed=true;}
                        else{var result=await connector.Harness(Text(args,"directory"));message=Json.Text(result,"message");passed=result.ContainsKey("passed")&&Convert.ToBoolean(result["passed"]);}
                        return new{message=message,state=Snapshot(),celebrate=passed};
                    case "summary":message=await Flow.Summary();break;
                    default:throw new InvalidOperationException("不支持的配置操作。");
                }
                return new{message=message,state=Snapshot()};
            }finally{operation.Release();}
        }
        void Finish(bool deferred){var p=Flow.State.Read();p.onboardingVersion=2;p.onboardingComplete=!deferred;p.onboardingDeferred=deferred;p.seen=true;Flow.State.Write(p);Finished=true;Deferred=deferred;}
    }
}
