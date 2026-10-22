using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;

namespace AgentVoice {
    public static class EnvironmentBootstrap {
        static string Inside(string root,string path){string full=Path.GetFullPath(path);if(!full.StartsWith(Path.GetFullPath(root).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("环境文件必须位于软件目录内。");return full;}
        public static async Task EnsureCore(SettingsStore store,Action<string> status){
            var cfg=store.Read();if(File.Exists(store.Resolve(cfg.engines.pythonService)))return;
            string target=Inside(store.Root,Path.Combine(store.Root,"runtime","core"));
            if(File.Exists(Path.Combine(target,"python.exe"))){cfg.engines.pythonService=store.Portable(Path.Combine(target,"python.exe"));store.Write(cfg);return;}
            var catalog=Json.Object(File.ReadAllText(store.Resolve("service/environment-catalog.json")));var python=(Dictionary<string,object>)catalog["python"];
            string cache=Inside(store.Root,Path.Combine(store.Root,"data","environment-repair","downloads"));Directory.CreateDirectory(cache);string archive=Path.Combine(cache,"python-bootstrap.zip"),stage=Inside(store.Root,Path.Combine(store.Root,"runtime",".bootstrap-"+Guid.NewGuid().ToString("N")));
            status("主服务 Python 缺失，正在下载软件内的独立环境……");ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
            using(var client=new WebClient())await client.DownloadFileTaskAsync(new Uri(Json.Text(python,"url")),archive);
            string actual;using(var stream=File.OpenRead(archive))using(var sha=SHA256.Create())actual=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();if(actual!=Json.Text(python,"sha256"))throw new InvalidOperationException("Python 下载校验失败，请重试。");
            Directory.CreateDirectory(stage);
            try{
                using(var zip=ZipFile.OpenRead(archive)){
                    foreach(var entry in zip.Entries){string path=Inside(stage,Path.Combine(stage,entry.FullName));if(entry.Length>128L*1024*1024)throw new InvalidOperationException("环境压缩包内容异常。");}
                    foreach(var entry in zip.Entries){string path=Inside(stage,Path.Combine(stage,entry.FullName));if(entry.FullName.EndsWith("/")){Directory.CreateDirectory(path);continue;}Directory.CreateDirectory(Path.GetDirectoryName(path));entry.ExtractToFile(path);}
                }
                string[] version=Json.Text(python,"version").Split('.');string tag=version[0]+version[1];
                File.WriteAllText(Path.Combine(stage,"python"+tag+"._pth"),"python"+tag+".zip\r\n.\r\nLib/site-packages\r\nimport site\r\n",Encoding.ASCII);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if(Directory.Exists(target)){string backup=Inside(store.Root,Path.Combine(store.Root,"data","environment-repair","backups","core-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(Path.GetDirectoryName(backup));Directory.Move(target,backup);}
                Directory.Move(stage,target);cfg.engines.pythonService=store.Portable(Path.Combine(target,"python.exe"));store.Write(cfg);
            }finally{if(Directory.Exists(stage))Directory.Delete(Inside(store.Root,stage),true);}
        }
    }
    public sealed partial class MainForm {
        ComboBox repairScope;TextBox repairDetails;
        void BuildEnvironmentRepair(){
            var card=SettingsCard("environmentRepair","检查与补齐环境");
            Add(card,Hint("先检查缺少的运行环境、依赖、源码和基础模型，再只补缺项。补齐结束前会实际加载模型并生成验证音频，只有真实合成通过才会显示成功。下载、安装、缓存和报告都保存在软件内；不会向外部 Python 安装库，也不会替换已有音色权重。补齐前请关闭语音服务。"));
            repairScope=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Name="repairScope"};repairScope.Items.AddRange(new object[]{"当前引擎与主服务","仅主服务","CosyVoice 与主服务","GPT 两版与主服务","全部环境"});repairScope.SelectedIndex=0;Add(card,repairScope);
            Add(card,Row(Primary(AsyncBtn("检查缺少什么",async()=>await RepairEnvironment(false))),AsyncBtn("补齐缺失环境",async()=>await RepairEnvironment(true)),EntryBtn("打开修复报告","查看下载进度、详细安装日志和设置备份。",()=>{string p=Store.Resolve("data/environment-repair");Directory.CreateDirectory(p);ServiceClient.Open(p);}),EntryBtn("安装说明","查看目录、系统组件和失败恢复方法。",()=>ServiceClient.Open(Store.Resolve("环境补齐说明.md")))));
            Add(card,Row(AsyncBtn("运行换机语音链路验收",RunTargetAcceptance),EntryBtn("打开验收报告","查看三种 TTS、长文、停止恢复、Harness 与 AIRI 的本机结果。",()=>{string p=Store.Resolve("data/target-acceptance");Directory.CreateDirectory(p);ServiceClient.Open(p);})));
            repairDetails=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Height=280,Name="environmentRepairResult"};Add(card,repairDetails);
            Add(card,Hint("“换机语音链路验收”会依次切换三种 TTS、生成短句和长文、停止任务后再次生成，并在 AIRI 已启用且在线时验证桌宠真实播放；结束后恢复原声音和服务状态。Harness 仍必须从连接页面完成随机短句与实际播放验证，不会用环境检查冒充接入成功。"));
            Add(card,Hint("显卡驱动、系统 VC++／.NET 组件不能放在软件目录里代替系统安装；检测到这类问题会给出手动步骤。下载可能达到数 GB，等待期间显示当前阶段，失败后可查看日志并重试。环境检查通过后仍需实际生成试听。"));
        }
        async Task<Dictionary<string,object>> WaitAcceptanceJob(string id,bool cancelledAllowed=false){
            var watch=System.Diagnostics.Stopwatch.StartNew();
            while(watch.Elapsed.TotalMinutes<15){
                var job=await Service.Request("/api/jobs/"+id,null,5000);string state=Json.Text(job,"state");
                if(state=="completed"||(cancelledAllowed&&state=="cancelled"))return job;
                if(state=="failed"||state=="cancelled")throw new InvalidOperationException(Json.Text(job,"error").Length>0?Json.Text(job,"error"):"验收任务被取消");
                BusyStatus("换机验收："+Json.Text(job,"engine")+" · "+state+" · "+Json.Text(job,"segmentsCompleted")+" / "+Json.Text(job,"totalSegments"));await Task.Delay(500);
            }
            throw new InvalidOperationException("验收任务超过 15 分钟仍未完成，请查看服务日志。");
        }
        public async Task RunTargetAcceptance(){
            string directory=Store.Resolve("data/target-acceptance"),reportPath=Path.Combine(directory,"latest.json");Directory.CreateDirectory(directory);
            File.WriteAllText(reportPath,Json.Encode(new{createdAt=DateTimeOffset.Now.ToString("o"),status="running",ttsOk=(bool?)null,checks=new object[0]}),Encoding.UTF8);
            bool wasRunning=await Service.Running();var original=Json.Copy(Config);var checksResult=new List<object>();Exception failure=null;
            try{
                if(!wasRunning)await Service.Start();
                var voices=new[]{new{label="CosyVoice",engine="cosyvoice",variant=original.voice.gptVariant},new{label="GPT-SoVITS v5 Turbo",engine="gpt-sovits",variant="v5turbo"},new{label="GPT-SoVITS ProPlus",engine="gpt-sovits",variant="proplus"}};
                foreach(var target in voices){
                    BusyStatus("换机验收：正在加载 "+target.label+"……");var voice=original.voice.Clone();voice.engine=target.engine;voice.gptVariant=target.variant;
                    var response=await Service.Request("/api/config",new{voice=voice},90000);Config=Json.Decode<Settings>(Json.Encode(response["config"]));await WaitForVoice(voice);
                    var watch=System.Diagnostics.Stopwatch.StartNew();var submitted=await Service.Request("/speak",new{text="环境与声音引擎真实生成验证成功。",play=false,source="target-acceptance"});var job=await WaitAcceptanceJob(Json.Text(submitted,"jobId"));checksResult.Add(new{check="service-synthesis",engine=target.label,ok=true,seconds=Math.Round(watch.Elapsed.TotalSeconds,2),jobId=Json.Text(job,"id")});
                }
                var restored=await Service.Request("/api/config",new{voice=original.voice},90000);Config=Json.Decode<Settings>(Json.Encode(restored["config"]));await WaitForVoice(original.voice);
                string paragraph="清晨的风从窗边吹过，街道逐渐苏醒。为了确认长文本能够连续生成和播放，程序会按完整句子分段处理，并保持前后语意连贯。";
                string longText=string.Join("",Enumerable.Repeat(paragraph,18));var longWatch=System.Diagnostics.Stopwatch.StartNew();var longSubmit=await Service.Request("/speak",new{text=longText,play=false,source="target-acceptance-longtext"});var longJob=await WaitAcceptanceJob(Json.Text(longSubmit,"jobId"));checksResult.Add(new{check="long-text",engine=original.voice.engine,ok=true,characters=longText.Length,seconds=Math.Round(longWatch.Elapsed.TotalSeconds,2),segments=Json.Text(longJob,"segmentsCompleted")});
                string stopText=string.Join("",Enumerable.Repeat(paragraph,50));var stopSubmit=await Service.Request("/speak",new{text=stopText,play=true,source="target-acceptance-stop"});await Task.Delay(1200);await Service.Request("/stop",new{});var stopped=await WaitAcceptanceJob(Json.Text(stopSubmit,"jobId"),true);if(Json.Text(stopped,"state")!="cancelled")throw new InvalidOperationException("停止验收任务在停止指令前已经完成，未能确认取消链路；请重新运行验收。");var recoverySubmit=await Service.Request("/speak",new{text="停止恢复验证成功。",play=false,source="target-acceptance-recovery"});await WaitAcceptanceJob(Json.Text(recoverySubmit,"jobId"));checksResult.Add(new{check="stop-and-recover",ok=true,stoppedState=Json.Text(stopped,"state")});
                var health=await Service.Request("/health");bool airiEnabled=original.airi.enabled,airiOnline=Convert.ToBoolean(health["airiConnected"]);
                if(airiEnabled&&airiOnline){var verify=await Service.Request("/api/verify/airi",new{});verificationIds["airi"]=Json.Text(verify,"id");await WaitForAiriVerification();checksResult.Add(new{check="airi-real-playback",ok=true});}
                else checksResult.Add(new{check="airi-real-playback",ok=false,requiredAction=airiEnabled?"打开已接入的 AIRI 后重新运行验收":"在连接设备中手动启用并验证 AIRI；AIRI 是可选功能"});
                object rawVerification;var verification=health.TryGetValue("verification",out rawVerification)?rawVerification:null;checksResult.Add(new{check="harness-verification",automatic=false,states=verification,requiredAction="在连接设备中分别完成随机短句、连接器回执和实际播放验证"});
            }catch(Exception error){failure=error;checksResult.Add(new{check="failed",ok=false,error=error.Message});}
            Exception restoreFailure=null;bool runningNow=false;
            try{runningNow=await Service.Running();}catch(Exception error){restoreFailure=error;}
            if(runningNow){
                try{var restored=await Service.Request("/api/config",new{voice=original.voice},90000);Config=Json.Decode<Settings>(Json.Encode(restored["config"]));}catch(Exception error){restoreFailure=error;}
                if(!wasRunning)try{await Service.Stop();}catch(Exception error){restoreFailure=restoreFailure??error;}
            }else try{Store.Write(original);}catch(Exception error){restoreFailure=error;}
            if(restoreFailure!=null){if(failure==null)failure=restoreFailure;checksResult.Add(new{check="restore",ok=false,error=restoreFailure.Message});}
            Config=Store.Read();BindVoice();File.WriteAllText(reportPath,Json.Encode(new{createdAt=DateTimeOffset.Now.ToString("o"),status=failure==null?"completed":"failed",computer=Environment.MachineName,scope="tts-longtext-stop-recovery-and-optional-airi",ttsOk=failure==null,externalConnectionsRequireSeparateVerification=true,checks=checksResult}),Encoding.UTF8);
            repairDetails.Text="换机验收报告："+reportPath+"\r\n\r\n"+string.Join("\r\n",checksResult.Select(Json.Encode).ToArray());
            if(failure!=null)throw new InvalidOperationException("换机验收未通过，已把失败步骤写入报告："+failure.Message,failure);
            Status("三种 TTS、长文与停止恢复已经通过；Harness 必须在连接页面完成真实回复验证，AIRI 未启用时保持可选。");
        }
        async Task RepairEnvironment(bool repair){
            string scope=new[]{"current","core","cosyvoice","gpt-sovits","all"}[repairScope.SelectedIndex];
            bool offline=SetupPreference().offline;
            if(repair&&offline&&!File.Exists(Store.Resolve(Config.engines.pythonService)))throw new InvalidOperationException("严格离线模式缺少主服务 Python，请在配置中心导入环境补齐包。");
            if(repair&&await Service.Running())throw new InvalidOperationException("请先在首页关闭语音服务，再补齐环境，避免修改运行中的依赖。");
            if(!repair&&!File.Exists(Store.Resolve(Config.engines.pythonService))){repairDetails.Text="缺少主服务 Python。点击“补齐缺失环境”会先在 runtime/core 创建独立 Python，再继续检查和补齐。";return;}
            if(repair){await EnvironmentBootstrap.EnsureCore(Store,BusyStatus);Config=Store.Read();}
            var progress=new Timer{Interval=1000};progress.Tick+=(s,e)=>{try{string p=Store.Resolve("data/environment-repair/progress.json");if(File.Exists(p))BusyStatus(Json.Text(Json.Object(File.ReadAllText(p)),"message"));}catch(IOException){}catch(ArgumentException){}};
            try{
                progress.Start();string text=await Service.RunTool("environment_repair.py",(repair?"repair":"inspect")+" --root "+ServiceClient.Quote(Store.Root)+" --engine "+scope+(offline?" --offline":""));var report=Json.Object(text);bool ok=Convert.ToBoolean(report["ok"]),verified=report.ContainsKey("verifiedUsable")&&Convert.ToBoolean(report["verifiedUsable"]);repairDetails.Text=EnvironmentReport(report);Config=Store.Read();BindSettings();Status(!ok?"仍有未通过项目，请按页面提示处理后重试。":!repair?"环境检查通过；补齐时还会进行真实合成验证。":verified?"环境补齐与真实合成验证均已通过。":"主服务环境补齐通过；请选择声音引擎完成真实合成验证。");
            }finally{progress.Stop();progress.Dispose();}
        }
        string EnvironmentReport(Dictionary<string,object> report){
            var text=new StringBuilder();object items;string error=Json.Text(report,"error");if(error.Length>0)text.AppendLine(error);
            if(report.TryGetValue("items",out items)&&items is object[])foreach(var raw in (object[])items){
                var item=(Dictionary<string,object>)raw;text.AppendLine((Convert.ToBoolean(item["ok"])?"✓ ":"✗ ")+Json.Text(item,"id"));
                foreach(string field in new[]{"python","source","model"})if(Json.Text(item,field).Length>0)text.AppendLine("  "+new Dictionary<string,string>{{"python","运行环境"},{"source","引擎源码"},{"model","基础模型"}}[field]+"："+Json.Text(item,field));
                foreach(string field in new[]{"missingPackages","missingModelFiles"}){object values;if(item.TryGetValue(field,out values)&&values is object[])foreach(object value in (object[])values)text.AppendLine("  缺少："+Convert.ToString(value));}
                foreach(string field in new[]{"sourceMissing","matchaMissing","resourcesMissing","ffmpegMissing"}){object value;if(item.TryGetValue(field,out value)&&Convert.ToBoolean(value))text.AppendLine("  待补齐："+new Dictionary<string,string>{{"sourceMissing","引擎源码"},{"matchaMissing","Matcha 声学组件"},{"resourcesMissing","英文发音资源"},{"ffmpegMissing","FFmpeg 音频工具"}}[field]);}
                object probe;if(item.TryGetValue("probe",out probe)&&probe is Dictionary<string,object>){string detail=Json.Text((Dictionary<string,object>)probe,"error");if(detail.Length>0)text.AppendLine("  检查失败："+detail);}
            }
            if(report.TryGetValue("runtimeChecks",out items)&&items is object[])foreach(var raw in (object[])items){var check=(Dictionary<string,object>)raw;text.AppendLine((Convert.ToBoolean(check["ok"])?"✓ ":"✗ ")+Json.Text(check,"section")+" 推理环境："+Json.Text(check,"detail"));}
            if(report.TryGetValue("synthesisChecks",out items)&&items is object[])foreach(var raw in (object[])items){var check=(Dictionary<string,object>)raw;bool ok=Convert.ToBoolean(check["ok"]);text.AppendLine((ok?"✓ ":"✗ ")+Json.Text(check,"id")+" 真实合成"+(ok?"："+Json.Text(check,"seconds")+" 秒，验证音频 "+Json.Text(check,"audio"):"："+Json.Text(check,"error")));if(!ok&&Json.Text(check,"guidance").Length>0)text.AppendLine("  处理方法："+Json.Text(check,"guidance"));}
            text.AppendLine(Json.Text(report,"systemNotice"));text.AppendLine("日志："+Json.Text(report,"log"));return text.ToString();
        }
    }
}
