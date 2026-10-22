using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgentVoice {
    // First-run work has no dependency on the daily workspace or its controls.
    internal sealed class SetupWorkflow {
        internal readonly SettingsStore Store;
        internal readonly ServiceClient Service;
        internal readonly WelcomeState State;
        internal Action<string> Report=delegate{};
        internal bool PreviewMayBePending;
        internal async Task StopPreview(){if(PreviewMayBePending){await Service.Request("/stop",new{});PreviewMayBePending=false;}InvalidateVoice();}
        internal Settings Config {get{return Store.Read();}}
        internal SetupWorkflow(SettingsStore store){Store=store;Service=new ServiceClient(store);State=new WelcomeState(store);}
        internal void Mark(string key,string status){var p=State.Read();p.setupStates[key]=status;State.Write(p);}
        internal async Task Save(Settings next,object patch){if(await Service.Running())await Service.Request("/api/config",patch,90000);else Store.Write(next);}
        internal async Task SelectEngine(int index){var c=Config;var old=c.voice.Clone();c.voice=EngineSwitching.Target(Store,c,index);EngineSwitching.Remember(Store,old);await Save(c,new{voice=c.voice});InvalidateVoice();Mark("engine","selected");}
        internal int[] InstallEngines(){return State.Read().installEngines??new[]{EngineSwitching.Index(Config.voice)};}
        internal void SelectInstallEngines(int[] indices){if(indices.Length>3||indices.Any(i=>i<0||i>2))throw new InvalidOperationException("安装引擎选择无效。");var p=State.Read();p.installEngines=indices.Distinct().OrderBy(i=>i).ToArray();State.Write(p);}
        internal async Task<string> PrepareSelected(bool environment,bool repair){
            string[] families=EngineSwitching.Families(InstallEngines());if(families.Length==0)throw new InvalidOperationException("尚未选择安装引擎。请返回选择，或使用本地导入。");
            var results=new List<string>();bool failed=false;
            foreach(string family in families){
                string key=(environment?"environment:":"engineFiles:")+family;Report("正在处理 "+family+"（"+(results.Count+1)+"/"+families.Length+"）");
                try{string result=environment?await PrepareEnvironment(repair,family):await PrepareEngine(family);Mark(key,"prepared");results.Add(family+"："+result);}
                catch(Exception error){failed=true;Mark(key,"failed");results.Add(family+"：未完成 — "+error.Message);}
            }
            string summary=string.Join("\r\n",results);if(failed)throw new InvalidOperationException(summary+"\r\n已完成的引擎会保留，可修复失败项后重试。");return summary;
        }
        internal string Resources(){var c=Config;string model=c.voice.engine=="cosyvoice"?(c.voice.baseModel.Length>0?c.voice.baseModel:c.engines.cosyModel):(c.voice.gptModel.Length>0?c.voice.gptModel:c.engines.gptModel);return "主服务："+(File.Exists(Store.Resolve(c.engines.pythonService))?"已找到":"待安装")+"   ·   TTS 环境："+(File.Exists(Store.Resolve(c.voice.engine=="cosyvoice"?c.engines.pythonTts:c.engines.pythonGpt))?"已找到":"待安装")+"\r\n基础模型："+(Directory.Exists(Store.Resolve(model))?"已找到":"待安装")+"   ·   参考声音："+(File.Exists(Store.Resolve(c.voice.reference))&&!string.IsNullOrWhiteSpace(c.voice.referenceText)?"已配置":"待选择");}
        internal async Task<string> InspectComputer(){
            string result=await Task.Run(()=>{var b=new StringBuilder();b.AppendLine(Environment.OSVersion+" · "+(Environment.Is64BitOperatingSystem?"64 位":"32 位"));b.AppendLine("可用空间："+(new DriveInfo(Path.GetPathRoot(Store.Root)).AvailableFreeSpace/1073741824d).ToString("0.0")+" GB");string probe=Store.Resolve("data/setup-write-"+Guid.NewGuid().ToString("N")+".tmp");Directory.CreateDirectory(Path.GetDirectoryName(probe));try{File.WriteAllText(probe,"check");b.AppendLine("安装目录可写。");}finally{if(File.Exists(probe))File.Delete(probe);}try{using(var p=Process.Start(new ProcessStartInfo("nvidia-smi","--query-gpu=name,driver_version,memory.total --format=csv,noheader"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();if(!p.WaitForExit(15000)){p.Kill();throw new InvalidOperationException("显卡检测超时");}Task.WaitAll(output,error);b.AppendLine(p.ExitCode==0?output.Result:"未检测到 NVIDIA 驱动。请安装驱动后重试。");}}catch(Exception e){b.AppendLine("显卡检查："+e.Message);}b.AppendLine("文件存在不代表声音可用；稍后还会测试真实生成和播放。");return b.ToString();});Mark("computer","checked");return result;
        }
        internal async Task InstallModule(string file,string kind,Action<string> progress){
            if(await Service.Running())throw new InvalidOperationException("请先停止语音服务，再安装模块。当前配置保留。");
            var manifest=ModulePackages.Inspect(file);
            var dispatcher=new Progress<string>(progress);await Task.Run(()=>ModulePackages.Install(Store,file,s=>((IProgress<string>)dispatcher).Report(s),CancellationToken.None));
            if(kind=="engine"){
                int index=manifest.engine=="cosyvoice"?0:(manifest.profile??"").Contains("proplus")?2:1;
                SelectInstallEngines(InstallEngines().Concat(new[]{index}).Distinct().ToArray());Mark("engineFiles:"+manifest.engine,"installed");
            }
            Mark(kind,"installed");InvalidateVoice();
        }
        internal async Task<string> PrepareEnvironment(bool repair,string engine="current"){
            if(repair&&await Service.Running())throw new InvalidOperationException("请先停止语音服务，再补齐环境。");
            var c=Config;bool offline=State.Read().offline;
            if(!File.Exists(Store.Resolve(c.engines.pythonService))){if(!repair||offline)throw new InvalidOperationException("缺少主服务运行环境。请导入环境包；或关闭严格离线，再选择联网补齐。");await EnvironmentBootstrap.EnsureCore(Store,Report);}
            var result=Json.Object(await RunPreparationTool((repair?"repair":"inspect")+" --root "+ServiceClient.Quote(Store.Root)+" --engine "+engine+" --prepare-only"+(repair?" --dependencies-only":"")+(offline?" --offline":"")));
            Mark("environment",Convert.ToBoolean(result["ok"])?"prepared":"failed");
            // Keep the detailed diagnostic, including failed paths, available in this step.
            string text=Json.Encode(result);Directory.CreateDirectory(Store.Resolve("data/setup"));File.WriteAllText(Store.Resolve("data/setup/environment-report.json"),text,Encoding.UTF8);
            if(!Convert.ToBoolean(result["ok"]))throw new InvalidOperationException("环境检查未通过："+Json.Text(result,"error")+"\r\n详细报告："+Store.Resolve("data/setup/environment-report.json"));
            return "运行文件检查通过。接下来选择自己的参考录音，再进行真实试听。\r\n详细报告："+Store.Resolve("data/setup/environment-report.json");
        }
        internal async Task<string> PrepareEngine(string engine="current"){
            if(State.Read().offline)throw new InvalidOperationException("严格离线已开启，请展开本地安装，导入引擎包或定位已有引擎。");
            if(await Service.Running())throw new InvalidOperationException("请先停止语音服务，再准备引擎文件。");
            await EnvironmentBootstrap.EnsureCore(Store,Report);
            var result=Json.Object(await RunPreparationTool("resources --root "+ServiceClient.Quote(Store.Root)+" --engine "+engine));
            if(!Convert.ToBoolean(result["ok"]))throw new InvalidOperationException(Json.Text(result,"error"));
            Mark("engineFiles","prepared");InvalidateVoice();return Json.Text(result,"message");
        }
        async Task<string> RunPreparationTool(string args){
            var task=Service.RunTool("environment_repair.py",args);string previous="";
            while(!task.IsCompleted){
                try{string file=Store.Resolve("data/environment-repair/progress.json");if(File.Exists(file)){string text=Json.Text(Json.Object(File.ReadAllText(file)),"message");if(text!=previous){previous=text;Report(text);}}}catch(IOException){}catch(ArgumentException){}
                await Task.WhenAny(task,Task.Delay(350));
            }
            return await task;
        }
        internal void InvalidateVoice(){var p=State.Read();p.voiceEvidence="";p.heardVoice=false;State.Write(p);}
        internal string Evidence(){var c=Config;Func<string,string> stamp=path=>{if(string.IsNullOrEmpty(path))return "missing";var f=new FileInfo(Store.Resolve(path));return f.Exists?f.Length+":"+f.LastWriteTimeUtc.Ticks:"missing";};string model=c.voice.engine=="cosyvoice"?(c.voice.baseModel.Length>0?c.voice.baseModel:c.engines.cosyModel):(c.voice.gptModel.Length>0?c.voice.gptModel:c.engines.gptModel);string weight=Path.Combine(model,c.voice.engine=="cosyvoice"?"llm.pt":c.voice.gptVariant=="v5turbo"?"gsv-v5-pretrained/s2Gv5turbo.pth":"v2Pro/s2Gv2ProPlus.pth");using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Json.Encode(new{voice=c.voice,engines=c.engines,airi=c.airi,reference=stamp(c.voice.reference),model=stamp(weight)}))));}
        internal async Task EnsureService(CancellationToken cancellation=default(CancellationToken)){cancellation.ThrowIfCancellationRequested();if(!await Service.Running()){cancellation.ThrowIfCancellationRequested();Report("正在启动语音服务……");await Service.Start(cancellation);}var expected=Config.voice;var watch=Stopwatch.StartNew();while(watch.Elapsed.TotalMinutes<4){cancellation.ThrowIfCancellationRequested();var h=await Service.Request("/health");if(Json.Text(h,"engine")!=expected.engine||(expected.engine=="gpt-sovits"&&Json.Text(h,"variant")!=expected.gptVariant))throw new InvalidOperationException("另一项操作改变了当前引擎，请重新选择声音。");cancellation.ThrowIfCancellationRequested();if(Convert.ToBoolean(h["ready"]))return;string state=Json.Text(h,"serviceState");if(state=="error"||state=="unconfigured")throw new InvalidOperationException("声音未就绪："+Json.Text(h,"error")+"。请返回环境或参考声音步骤修复。");Report("正在加载当前声音模型……");await Task.Delay(300,cancellation);}throw new InvalidOperationException("模型加载超过四分钟。请查看日志并检查显存。");}
        internal async Task<string> Preview(CancellationToken cancellation=default(CancellationToken)){
            bool submitted=false;Exception failure=null;string result=null;
            Func<Task<string>> run=async()=>{
            await EnsureService(cancellation);cancellation.ThrowIfCancellationRequested();string evidence=Evidence();var watch=Stopwatch.StartNew();submitted=true;PreviewMayBePending=true;var item=await Service.Request("/speak",new{text="你好，这里是你的语音工作台。接下来，让每一句话，都用你喜欢的声音说出来。",source="setup-preview",play=true});cancellation.ThrowIfCancellationRequested();string id=Json.Text(item,"jobId");if(id.Length==0)throw new InvalidOperationException("服务没有返回试听任务。");
            while(watch.Elapsed.TotalMinutes<5){cancellation.ThrowIfCancellationRequested();var job=await Service.Request("/api/jobs/"+id);cancellation.ThrowIfCancellationRequested();string state=Json.Text(job,"state");if(state=="failed"||state=="cancelled"){PreviewMayBePending=false;throw new InvalidOperationException(state=="cancelled"?"试听已停止，尚未确认声音。":Json.Text(job,"error"));}if(state=="completed"){PreviewMayBePending=false;var r=job.ContainsKey("result")?job["result"] as Dictionary<string,object>:null;if(r==null||!r.ContainsKey("played")||!Convert.ToBoolean(r["played"]))throw new InvalidOperationException("生成完成，但没有播放完成回执。");if(evidence!=Evidence())throw new InvalidOperationException("试听期间声音配置已变化，请重新试听。");var p=State.Read();p.voiceEvidence=evidence;p.voiceJobId=id;p.voiceVerifiedAt=DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");p.heardVoice=false;State.Write(p);PreviewMayBePending=false;return "实际播放完成，用时 "+watch.Elapsed.TotalSeconds.ToString("0.0")+" 秒。请根据你听到的声音点击确认。";}Report(state=="playing"?"正在播放测试声音……":"正在生成测试声音……");await Task.Delay(300,cancellation);}throw new InvalidOperationException("试听超过五分钟，请停止后检查日志。");
            };
            try{result=await run();}catch(Exception error){failure=error;}
            if(failure!=null&&submitted){InvalidateVoice();await StopPreview();}
            if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();return result;
        }
        internal void Confirm(){var p=State.Read();if(p.voiceJobId.Length==0||p.voiceEvidence!=Evidence())throw new InvalidOperationException("请先完成当前声音的生成和实际播放。");p.heardVoice=true;State.Write(p);Mark("preview","confirmed");}
        internal async Task<string> Summary(){var p=State.Read();bool current=p.voiceEvidence==Evidence();string connections="服务未运行，尚未验证连接。";if(await Service.Running()){var h=await Service.Request("/health");object raw;var states=h.TryGetValue("verification",out raw)?raw as Dictionary<string,object>:null;connections=string.Join("\r\n",new[]{"codex","deepseek","airi"}.Select(key=>{object v;var item=states!=null&&states.TryGetValue(key,out v)?v as Dictionary<string,object>:null;return key+"："+(item!=null&&Json.Text(item,"state")=="verified"?"本次真实播放验证通过":"尚未验证或未启用");}));}return Resources()+"\r\n\r\n试听："+(current&&p.voiceJobId.Length>0?"播放已完成":"未验证当前声音")+"  ·  听感："+(current&&p.heardVoice?"已确认":"未确认")+"\r\n\r\n"+connections+"\r\n\r\n未完成的项目可以稍后在设置中继续；进入软件不代表上述验证通过。";}
    }
}
