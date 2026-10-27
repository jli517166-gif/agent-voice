using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentVoice {
    static class EmbeddedSetupChecks {
        internal static async Task Unit(string directory){
            var type=typeof(Program).Assembly.GetType("AgentVoice.EmbeddedSetupController");
            if(type==null)throw new InvalidOperationException("Embedded setup controller has not been implemented");
            await CheckRemovedVoiceActions(directory);
            await CheckAutomaticReading(directory);
            CheckPreferenceWrites(directory);
            for(int i=1;i<SetupWorkflow.PageOrder.Length;i++)if(!SetupWorkflow.StepLabel(SetupWorkflow.PageOrder[i]).StartsWith("第 "+i+" 步 / 共 9 步"))throw new Exception("Incorrect displayed step number");
            await CheckMultiEngine(directory);
            await CheckLocalEngines(directory);
            string python=Environment.GetEnvironmentVariable("AGENT_VOICE_TEST_PYTHON");if(!string.IsNullOrEmpty(python)){await CheckBatchPreparation(directory,python);await CheckDownloadPause(directory,python);}
            var store=new SettingsStore(Path.Combine(directory,"fixture-"+Guid.NewGuid().ToString("N")));
            var c=store.Read();c.port=1;c.token="must-not-reach-web-content";store.Write(c);
            dynamic controller=Activator.CreateInstance(type,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new object[]{store},null);
            string state=Json.Encode(controller.Snapshot());
            if(state.Contains(c.token))throw new Exception("Setup state exposed service credentials");
            await controller.Execute("next",new Dictionary<string,object>());
            if(controller.Flow.State.Read().onboardingStep!=5)throw new Exception("First-run bypasses legal notice before installation");
            foreach(string blocked in new[]{"next","skip","go"}){
                bool stopped=false;try{await controller.Execute(blocked,new Dictionary<string,object>{{"step",2}});}catch(InvalidOperationException){stopped=true;}
                if(!stopped||controller.Flow.State.Read().onboardingStep!=5)throw new Exception("Legal gate bypassed via "+blocked);
            }
            await controller.Execute("legal-read",new Dictionary<string,object>());
            await controller.Execute("next",new Dictionary<string,object>());
            if(controller.Flow.State.Read().onboardingStep!=6)throw new Exception("TTS license page absent before download");
            await controller.Execute("go",new Dictionary<string,object>{{"step",2}});
            await controller.Execute("next",new Dictionary<string,object>{{"engine",1}});
            if(store.Read().voice.engine!="gpt-sovits"||store.Read().voice.gptVariant!="v5turbo")throw new Exception("Next did not save selected engine");
            await controller.Execute("go",new Dictionary<string,object>{{"step",2}});
            await controller.Execute("skip",new Dictionary<string,object>{{"engine",2}});
            if(store.Read().voice.gptVariant!="v5turbo")throw new Exception("Skip applied unconfirmed engine");
            bool rejected=false;try{await controller.Execute("run-command",new Dictionary<string,object>());}catch(InvalidOperationException){rejected=true;}
            if(!rejected)throw new Exception("Unknown bridge command accepted");
            rejected=false;try{await controller.Execute("save-reference",new Dictionary<string,object>{{"reference","missing.wav"},{"transcript",""}});}catch(InvalidOperationException){rejected=true;}
            if(!rejected)throw new Exception("Invalid reference saved");
            await controller.Execute("preferences",new Dictionary<string,object>{{"offline",true},{"reduceMotion",true},{"sound",false}});
            var p=new WelcomeState(store).Read();if(!p.offline||!p.reduceMotion||p.interfaceSounds)throw new Exception("Preferences not persisted");
            await controller.Execute("defer",new Dictionary<string,object>());
            p=new WelcomeState(store).Read();if(!p.onboardingDeferred||p.onboardingComplete||p.heardVoice)throw new Exception("Defer reported verified configuration");
        }
        static async Task CheckAutomaticReading(string directory){
            var store=new SettingsStore(Path.Combine(directory,"privacy-setup-"+Guid.NewGuid().ToString("N")));var config=store.Read();config.port=1;store.Write(config);var controller=new EmbeddedSetupController(store);
            if(store.Read().harness.Values.Any(x=>x))throw new Exception("Fresh setup enabled automatic reading");
            bool blocked=false;try{await controller.Execute("save-autoread",new Dictionary<string,object>{{"codex",true},{"deepseek",false}});}catch(InvalidOperationException){blocked=true;}
            if(!blocked||store.Read().harness.Values.Any(x=>x))throw new Exception("Automatic reading bypassed legal confirmation");
            controller.Flow.AcceptLegal();await controller.Execute("save-autoread",new Dictionary<string,object>{{"codex",true},{"deepseek",false}});
            if(!store.Read().harness["codex"]||store.Read().harness["deepseek"])throw new Exception("Explicit automatic reading choice was not saved");
            var state=Json.Object(Json.Encode(controller.Snapshot()));if(!state.ContainsKey("autoRead")||state.ContainsKey("token"))throw new Exception("Setup did not expose safe automatic reading state");
        }
        static async Task CheckMultiEngine(string directory){
            var store=new SettingsStore(Path.Combine(directory,"multi-"+Guid.NewGuid().ToString("N")));var c=store.Read();c.port=1;c.voice.referenceText="cosy reference";c.voice.baseModel="cosy-trained";store.Write(c);
            var controller=new EmbeddedSetupController(store);controller.Flow.AcceptLegal();
            await controller.Execute("go",new Dictionary<string,object>{{"step",2}});
            await controller.Execute("next",new Dictionary<string,object>{{"engines",new object[]{0,1,2}}});
            if(!controller.Flow.InstallEngines().SequenceEqual(new[]{0,1,2})||store.Read().voice.engine!="cosyvoice")throw new Exception("Multi-selection changed active voice or lost targets");
            if(!EngineSwitching.Families(new[]{0,1,2}).SequenceEqual(new[]{"cosyvoice","gpt-sovits"}))throw new Exception("GPT variants downloaded twice");
            var reloaded=new SetupWorkflow(store);if(reloaded.InstallEngines().Length!=3)throw new Exception("Install selection did not survive reload");
            var p=reloaded.State.Read();p.offline=true;reloaded.State.Write(p);bool failed=false;
            try{await reloaded.PrepareSelected(false,false);}catch(InvalidOperationException error){failed=error.Message.Contains("cosyvoice")&&error.Message.Contains("gpt-sovits");}
            if(!failed||reloaded.State.Read().setupStates["engineFiles:cosyvoice"]!="failed"||reloaded.State.Read().setupStates["engineFiles:gpt-sovits"]!="failed")throw new Exception("Batch failure lost per-engine diagnostics");
            await reloaded.SelectEngine(1);if(store.Read().voice.gptModel!="")throw new Exception("Cross-engine trained model carried across");
            c=store.Read();c.voice.gptModel="turbo-trained";c.voice.referenceText="turbo reference";store.Write(c);
            await reloaded.SelectEngine(2);if(store.Read().voice.gptModel!="")throw new Exception("Incompatible GPT variant reused trained model");
            await reloaded.SelectEngine(0);if(store.Read().voice.baseModel!="cosy-trained"||store.Read().voice.referenceText!="cosy reference")throw new Exception("CosyVoice recall lost trained model or reference");
            await reloaded.SelectEngine(1);if(store.Read().voice.gptModel!="turbo-trained"||store.Read().voice.referenceText!="turbo reference")throw new Exception("GPT recall lost trained model or reference");
            bool rejected=false;try{await controller.Execute("go",new Dictionary<string,object>{{"step",2}});await controller.Execute("next",new Dictionary<string,object>{{"engines",new object[]{99}}});}catch(InvalidOperationException){rejected=true;}
            if(!rejected||controller.Flow.InstallEngines().Length!=3)throw new Exception("Invalid multi-selection accepted");
        }
        static async Task CheckBatchPreparation(string directory,string python){
            var store=new SettingsStore(Path.Combine(directory,"batch-"+Guid.NewGuid().ToString("N")));var c=store.Read();c.port=1;c.engines.pythonService=python;store.Write(c);
            Directory.CreateDirectory(store.Resolve("tools"));
            File.WriteAllText(store.Resolve("tools/environment_repair.py"),"import sys,json,pathlib\na=sys.argv;engine=a[a.index('--engine')+1];root=pathlib.Path(a[a.index('--root')+1]);log=root/'calls.txt'\nwith log.open('a') as f:f.write(engine+'\\n')\nprint(json.dumps({'ok':engine=='gpt-sovits','error':'fixture failure','message':'fixture ready'}))\n",Encoding.UTF8);
            var flow=new SetupWorkflow(store);flow.SelectInstallEngines(new[]{0,1,2});bool failed=false;
            try{await flow.PrepareSelected(false,false);}catch(InvalidOperationException){failed=true;}
            if(!failed||!File.ReadAllLines(store.Resolve("calls.txt")).SequenceEqual(new[]{"cosyvoice","gpt-sovits"}))throw new Exception("Batch did not continue after failure or duplicated a shared environment");
            var states=flow.State.Read().setupStates;if(states["engineFiles:cosyvoice"]!="failed"||states["engineFiles:gpt-sovits"]!="prepared"||store.Read().voice.engine!="cosyvoice")throw new Exception("Partial preparation lost status or switched active voice");
        }
        internal static void PreparePauseFixture(SettingsStore store,string python){
            var c=store.Read();c.port=1;c.engines.pythonService=python;store.Write(c);Directory.CreateDirectory(store.Resolve("tools"));
            string module=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"service");
            string script="import sys,time,json\nfrom pathlib import Path\nsys.path.insert(0,"+Json.Encode(module)+")\nfrom download_control import DownloadGate\nroot=Path(sys.argv[sys.argv.index('--root')+1]);gate=DownloadGate(root)\nfor i in range(2000):\n gate.check();(root/'counter.txt').write_text(str(i));time.sleep(.03)\nprint(json.dumps({'ok':True,'message':'fixture finished'}))\n";
            File.WriteAllText(store.Resolve("tools/environment_repair.py"),script);
        }
        static async Task CheckDownloadPause(string directory,string python){
            var store=new SettingsStore(Path.Combine(directory,"download-pause-"+Guid.NewGuid().ToString("N")));var c=store.Read();c.port=1;c.engines.pythonService=python;store.Write(c);
            var controller=new EmbeddedSetupController(store);controller.Flow.SelectInstallEngines(new[]{0});
            Directory.CreateDirectory(store.Resolve("tools"));
            string module=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"service");
            string script="import sys,time,json\nfrom pathlib import Path\nsys.path.insert(0,"+Json.Encode(module)+")\nfrom download_control import DownloadGate\nroot=Path(sys.argv[sys.argv.index('--root')+1]);gate=DownloadGate(root)\nfor i in range(200):\n gate.check();(root/'counter.txt').write_text(str(i));time.sleep(.03)\nprint(json.dumps({'ok':True,'message':'fixture finished'}))\n";
            File.WriteAllText(store.Resolve("tools/environment_repair.py"),script);
            var work=controller.Execute("download-engine",new Dictionary<string,object>());
            Func<Func<bool>,Task> wait=async condition=>{var end=DateTime.UtcNow.AddSeconds(8);while(!condition()){if(DateTime.UtcNow>end)throw new Exception("Download pause fixture timed out");await Task.Delay(25);}};
            await wait(()=>File.Exists(store.Resolve("counter.txt")));
            if(!controller.Busy)throw new Exception("Fixture must hold the normal operation gate");
            await controller.Execute("pause-download",new Dictionary<string,object>());
            await wait(()=>Json.Text(Json.Object(Json.Encode(controller.DownloadState)),"status")=="paused");
            string frozen=File.ReadAllText(store.Resolve("counter.txt"));await Task.Delay(180);
            if(File.ReadAllText(store.Resolve("counter.txt"))!=frozen)throw new Exception("Paused worker kept transferring");
            await controller.Execute("resume-download",new Dictionary<string,object>());
            await wait(()=>File.ReadAllText(store.Resolve("counter.txt"))!=frozen);
            await controller.Execute("pause-download",new Dictionary<string,object>());
            await wait(()=>Json.Text(Json.Object(Json.Encode(controller.DownloadState)),"status")=="paused");
            await controller.Execute("cancel-download",new Dictionary<string,object>());
            bool cancelled=false;try{await work;}catch(OperationCanceledException){cancelled=true;}
            if(!cancelled||controller.Busy||controller.Flow.Service.Download!=null)throw new Exception("Stopped download did not release the operation");
            File.WriteAllText(Path.Combine(directory,"download-pause.json"),"{\"ok\":true,\"busyPauseAllowed\":true,\"resumed\":true,\"cancelledWhilePaused\":true}");
        }
        static async Task CheckLocalEngines(string directory){
            var store=new SettingsStore(Path.Combine(directory,"local-"+Guid.NewGuid().ToString("N")));var c=store.Read();c.port=1;store.Write(c);var flow=new SetupWorkflow(store);flow.SelectInstallEngines(new int[0]);
            foreach(string engine in new[]{"cosyvoice","gpt-sovits"}){
                string source="tts-engines/"+engine+"/source",model="tts-engines/"+engine+"/models/fixture",prefix=engine=="cosyvoice"?"cosy":"gpt";
                string file=ModuleChecks.Package(directory,"local-"+Guid.NewGuid().ToString("N"),new Dictionary<string,string>{{source+"/fixture.py","source fixture"},{model+"/fixture.bin","model fixture"}},new[]{source,model},new Dictionary<string,string>{{prefix+"Repo",source},{prefix+"Model",model}},null,"engine",engine);
                await flow.InstallModule(file,"engine",text=>{});
            }
            if(!flow.InstallEngines().SequenceEqual(new[]{0,1})||!File.Exists(store.Resolve("tts-engines/cosyvoice/models/fixture/fixture.bin"))||!File.Exists(store.Resolve("tts-engines/gpt-sovits/models/fixture/fixture.bin"))||store.Read().voice.engine!="cosyvoice")throw new Exception("Multiple local imports replaced another engine, lost the install plan or changed active voice");
        }
        static void CheckPreferenceWrites(string directory){
            var state=new WelcomeState(new SettingsStore(Path.Combine(directory,"preferences-"+Guid.NewGuid().ToString("N"))));
            for(int i=0;i<100;i++){var value=state.Read();value.onboardingStep=i%10;state.Write(value);if(state.Read().onboardingStep!=i%10)throw new Exception("Repeated preference write lost state");}
            string original=File.ReadAllText(state.FilePath);bool rejected=false;
            using(var locked=new FileStream(state.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read)){
                try{state.Write(new WelcomePreferences{onboardingStep=9});}catch(IOException){rejected=true;}
            }
            if(!rejected||File.ReadAllText(state.FilePath)!=original)throw new Exception("Locked preferences were overwritten or reported saved");
        }
        static async Task CheckRemovedVoiceActions(string directory){
            var store=new SettingsStore(Path.Combine(directory,"no-setup-speech-"+Guid.NewGuid().ToString("N")));
            using(var server=new SetupProbeServer()){
                var c=store.Read();c.port=server.Port;c.voice.reference="private.wav";c.voice.referenceText="private transcript";store.Write(c);string before=File.ReadAllText(store.FilePath);
                var controller=new EmbeddedSetupController(store);
                foreach(string action in new[]{"preview","confirm","save-reference","apply-profile","stop-preview"}){
                    bool rejected=false;try{await controller.Execute(action,new Dictionary<string,object>());}catch(InvalidOperationException){rejected=true;}
                    if(!rejected)throw new Exception("Removed setup voice action accepted: "+action);
                }
                if(server.Speaks!=0||server.Stops!=0||File.ReadAllText(store.FilePath)!=before)throw new Exception("Legal setup altered voice or submitted audio");
                string snapshot=Json.Encode(controller.Snapshot());if(snapshot.Contains("private.wav")||snapshot.Contains("private transcript"))throw new Exception("Legal setup exposed reference assets");
                await controller.Execute("go",new Dictionary<string,object>{{"step",5}});
                await controller.Execute("legal-read",new Dictionary<string,object>());
                if(controller.Flow.State.Read().setupStates["legalNotice"]!=SetupWorkflow.LegalNoticeVersion)throw new Exception("Notice reading not persisted");
            }
        }
        internal static int Run(SettingsStore store,string[] args){
            string directory=args.Length>1?Path.GetFullPath(args[1]):store.Resolve("acceptance/web-setup");Directory.CreateDirectory(directory);int exit=1;
            using(var pump=new Form{ShowInTaskbar=false}){
                pump.Shown+=async(s,e)=>{try{await Unit(directory);File.WriteAllText(Path.Combine(directory,"controller.json"),"{\"ok\":true}");exit=0;}catch(Exception error){File.WriteAllText(Path.Combine(directory,"controller-error.txt"),error.ToString());}pump.Close();};
                pump.StartPosition=FormStartPosition.Manual;pump.Location=new System.Drawing.Point(-32000,-32000);Application.Run(pump);
            }return exit;
        }
    }
    // Isolated protocol fixture: never reads real Harness events or starts TTS.
    internal sealed class SetupProbeServer:IDisposable {
        readonly HttpListener listener=new HttpListener();
        readonly Task loop;
        internal readonly int Port;
        internal int Speaks,Stops,FailStops;
        internal bool WrongService,ReadyModel,FailJobs;
        internal SetupProbeServer(){
            var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();Port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();
            listener.Prefixes.Add("http://127.0.0.1:"+Port+"/");listener.Start();
            loop=Task.Run(async()=>{try{while(listener.IsListening){var context=await listener.GetContextAsync();string path=context.Request.Url.AbsolutePath;if(path=="/speak")Interlocked.Increment(ref Speaks);if(path=="/stop")Interlocked.Increment(ref Stops);string body=path=="/health"?Json.Encode(new{service=WrongService?"unrelated":"agent-voice-public",engine="cosyvoice",ready=ReadyModel,serviceState="loading"}):path=="/speak"?"{\"jobId\":\"fixture-job\"}":"{}";if(FailJobs&&path.StartsWith("/api/jobs/")){context.Response.StatusCode=500;body="{\"error\":\"fixture poll failure\"}";}if(path=="/stop"&&FailStops>0){FailStops--;context.Response.StatusCode=500;body="{\"error\":\"fixture stop failure\"}";}byte[] bytes=Encoding.UTF8.GetBytes(body);context.Response.ContentType="application/json";context.Response.ContentLength64=bytes.Length;await context.Response.OutputStream.WriteAsync(bytes,0,bytes.Length);context.Response.Close();}}catch(HttpListenerException){}catch(ObjectDisposedException){}});
        }
        public void Dispose(){listener.Close();loop.Wait(3000);}
    }
}
