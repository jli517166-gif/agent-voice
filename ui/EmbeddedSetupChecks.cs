using System;
using System.Collections.Generic;
using System.IO;
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
            await CancelColdPreview(directory);
            await CleanupFailedPreview(directory);
            CheckPreferenceWrites(directory);
            var store=new SettingsStore(Path.Combine(directory,"fixture-"+Guid.NewGuid().ToString("N")));
            var c=store.Read();c.port=1;c.token="must-not-reach-web-content";store.Write(c);
            dynamic controller=Activator.CreateInstance(type,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new object[]{store},null);
            string state=Json.Encode(controller.Snapshot());
            if(state.Contains(c.token))throw new Exception("Setup state exposed service credentials");
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
        static void CheckPreferenceWrites(string directory){
            var state=new WelcomeState(new SettingsStore(Path.Combine(directory,"preferences-"+Guid.NewGuid().ToString("N"))));
            for(int i=0;i<100;i++){var value=state.Read();value.onboardingStep=i%10;state.Write(value);if(state.Read().onboardingStep!=i%10)throw new Exception("Repeated preference write lost state");}
            string original=File.ReadAllText(state.FilePath);bool rejected=false;
            using(var locked=new FileStream(state.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read)){
                try{state.Write(new WelcomePreferences{onboardingStep=9});}catch(IOException){rejected=true;}
            }
            if(!rejected||File.ReadAllText(state.FilePath)!=original)throw new Exception("Locked preferences were overwritten or reported saved");
        }
        static async Task CancelColdPreview(string directory){
            var store=new SettingsStore(Path.Combine(directory,"cancel-"+Guid.NewGuid().ToString("N")));
            using(var server=new SetupProbeServer()){
                var config=store.Read();config.port=server.Port;store.Write(config);
                var controller=new EmbeddedSetupController(store);var loading=new TaskCompletionSource<bool>();
                controller.Progress=text=>loading.TrySetResult(true);
                var preview=controller.Execute("preview",new Dictionary<string,object>());
                if(await Task.WhenAny(loading.Task,Task.Delay(5000))!=loading.Task)throw new Exception("Cold preview never reached loading");
                await controller.Execute("stop-preview",new Dictionary<string,object>());
                bool cancelled=false;try{await preview;}catch(OperationCanceledException){cancelled=true;}
                if(!cancelled||server.Speaks!=0||controller.Busy)throw new Exception("Cold preview still submitted speech after Stop");
                if(controller.Flow.State.Read().heardVoice)throw new Exception("Cancelled preview reported confirmation");
            }
        }
        static async Task CleanupFailedPreview(string directory){
            foreach(int failStops in new[]{0,1}){
            var store=new SettingsStore(Path.Combine(directory,"failed-preview-"+Guid.NewGuid().ToString("N")));
            using(var server=new SetupProbeServer()){
                server.ReadyModel=true;server.FailJobs=true;server.FailStops=failStops;var c=store.Read();c.port=server.Port;store.Write(c);
                var controller=new EmbeddedSetupController(store);bool failed=false;
                try{await controller.Execute("preview",new Dictionary<string,object>());}catch(InvalidOperationException){failed=true;}
                if(!failed||server.Speaks!=1||server.Stops!=1||controller.Flow.PreviewMayBePending!=(failStops==1))throw new Exception("Failed polling did not preserve cleanup state");
                if(failStops==1){
                    bool blocked=false;try{await controller.Execute("defer",new Dictionary<string,object>());}catch(InvalidOperationException){blocked=true;}
                    if(!blocked||controller.Finished)throw new Exception("Unstopped preview allowed setup to exit");
                    await controller.Flow.StopPreview(); // Same retry used by window closing.
                    if(controller.Flow.PreviewMayBePending)throw new Exception("Close retry left a pending preview");
                }
                await controller.Execute("stop-preview",new Dictionary<string,object>());
                if(server.Stops!=1+failStops)throw new Exception("Idle Stop affected an unrelated queue");
            }
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
