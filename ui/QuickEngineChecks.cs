using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed partial class MainForm {
        internal async Task VerifyQuickEngine(QuickEngineServer server){
            if(quickEngine==null||quickEngine.Items.Count!=3)throw new Exception("Home engine selector missing");
            Config.voice.referenceText="cosy reference";Store.Write(Config);
            quickEngine.SelectedIndex=1;await QuickSwitchEngine();
            if(Config.voice.engine!="gpt-sovits"||Config.voice.gptVariant!="v5turbo"||ReadVoice().engine!="gpt-sovits")throw new Exception("Home switch did not update service/config/editor together");
            Config.voice.referenceText="turbo reference";Store.Write(Config);
            quickEngine.SelectedIndex=2;await QuickSwitchEngine();
            if(Config.voice.gptVariant!="proplus")throw new Exception("GPT variant switch failed");
            quickEngine.SelectedIndex=0;await QuickSwitchEngine();
            if(Config.voice.referenceText!="cosy reference")throw new Exception("Quick switch failed to restore CosyVoice reference");
            quickEngine.SelectedIndex=1;await QuickSwitchEngine();
            if(Config.voice.referenceText!="turbo reference")throw new Exception("Quick switch failed to restore GPT reference");
            server.WrongEngine=true;quickEngine.SelectedIndex=0;bool rejected=false;
            try{await QuickSwitchEngine();}catch(InvalidOperationException){rejected=true;}
            if(!rejected)throw new Exception("Quick switch falsely succeeded despite wrong health engine");
            rejected=false;try{await QuickSwitchEngine();}catch(InvalidOperationException){rejected=true;}
            if(!rejected)throw new Exception("Retry falsely succeeded based only on saved configuration");
            server.WrongEngine=false;
        }
    }
    internal sealed class QuickEngineServer:IDisposable {
        readonly HttpListener listener=new HttpListener();readonly SettingsStore store;readonly Task loop;
        internal int Port;internal bool WrongEngine;
        internal QuickEngineServer(SettingsStore target){
            store=target;var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();Port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();listener.Prefixes.Add("http://127.0.0.1:"+Port+"/");listener.Start();
            loop=Task.Run(async()=>{try{while(listener.IsListening){
                var context=await listener.GetContextAsync();string path=context.Request.Url.AbsolutePath;object result=new{};
                if(path=="/api/config"){
                    string body;using(var reader=new StreamReader(context.Request.InputStream,Encoding.UTF8))body=reader.ReadToEnd();
                    var patch=Json.Object(body);var c=store.Read();c.voice=Json.Decode<VoiceConfig>(Json.Encode(patch["voice"]));store.Write(c);result=new{config=c};
                }else if(path=="/health"){
                    var voice=store.Read().voice;result=new{service="agent-voice-public",ready=true,engine=WrongEngine?"wrong":voice.engine,variant=voice.gptVariant,serviceState="ready"};
                }
                byte[] bytes=Encoding.UTF8.GetBytes(Json.Encode(result));context.Response.ContentType="application/json";context.Response.ContentLength64=bytes.Length;await context.Response.OutputStream.WriteAsync(bytes,0,bytes.Length);context.Response.Close();
            }}catch(HttpListenerException){}catch(ObjectDisposedException){}});
        }
        public void Dispose(){listener.Stop();listener.Close();loop.Wait(5000);}
    }
    internal static class QuickEngineChecks {
        internal static int Run(string[] args){
            string directory=Path.GetFullPath(args[1]);Directory.CreateDirectory(directory);var store=new SettingsStore(Path.Combine(directory,"installation-"+Guid.NewGuid().ToString("N")));int exit=1;
            using(var server=new QuickEngineServer(store)){
                var c=store.Read();c.port=server.Port;store.Write(c);
                using(var form=new MainForm(store,true)){
                    form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);
                    form.Shown+=async(s,e)=>{try{await form.VerifyQuickEngine(server);File.WriteAllText(Path.Combine(directory,"quick-engine.json"),"{\"ok\":true,\"protocolFixture\":true}");exit=0;}catch(Exception error){File.WriteAllText(Path.Combine(directory,"quick-engine-error.txt"),error.ToString());}form.Dispose();};Application.Run(form);
                }
            }return exit;
        }
    }
}
