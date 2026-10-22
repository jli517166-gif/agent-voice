using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Net.NetworkInformation;
using System.Linq;
using System.Threading;

namespace AgentVoice {
    public sealed class ServiceClient {
        public readonly SettingsStore Store;
        public Process Owned;
        public ServiceClient(SettingsStore store){Store=store;}
        public Task<Dictionary<string,object>> Request(string path,object data=null,int timeout=5000) {
            return Task.Run(()=> {
                Settings settings=Store.Read();
                var request=(HttpWebRequest)WebRequest.Create("http://127.0.0.1:"+settings.port+path);
                request.Proxy=null;request.Timeout=timeout;request.ReadWriteTimeout=timeout;
                request.Headers[HttpRequestHeader.Authorization]="Bearer "+settings.token;
                if(data!=null){request.Method="POST";request.ContentType="application/json; charset=utf-8";byte[] body=Encoding.UTF8.GetBytes(Json.Encode(data));request.ContentLength=body.Length;using(var output=request.GetRequestStream())output.Write(body,0,body.Length);}
                try {using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8))return Json.Object(reader.ReadToEnd());}
                catch(WebException exc){if(exc.Response!=null){using(var reader=new StreamReader(exc.Response.GetResponseStream(),Encoding.UTF8)){string text=reader.ReadToEnd();try {var obj=Json.Object(text);object error;if(obj.TryGetValue("error",out error)){var details=error as Dictionary<string,object>;throw new InvalidOperationException(details!=null?Json.Text(details,"message"):Convert.ToString(error));}}catch(ArgumentException){} }}throw;}
            });
        }
        public async Task<bool> Running() {
            try {var status=await Request("/health",null,1500);string expected;using(var sha=SHA256.Create())expected=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Store.Root.ToLowerInvariant()))).Replace("-","").ToLowerInvariant().Substring(0,24);if(Json.Text(status,"service")!="agent-voice-public"||(status.ContainsKey("instanceId")&&Json.Text(status,"instanceId")!=expected))throw new InvalidOperationException("端口被另一安装目录的程序占用，请先关闭另一套语音服务。");if(!status.ContainsKey("instanceId"))await Request("/api/state",null,1500);return true;}
            catch(WebException error){if(error.Status==WebExceptionStatus.ConnectFailure)return false;if(error.Status==WebExceptionStatus.Timeout){try{int port=Store.Read().port;if(!IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p=>p.Port==port))return false;}catch(NetworkInformationException){}}throw new InvalidOperationException("语音服务暂时未响应，未按离线方式改写配置。请稍后重试或恢复服务。",error);}
        }
        public async Task Start(CancellationToken cancellation=default(CancellationToken)) {
            cancellation.ThrowIfCancellationRequested();
            if(File.Exists(ModulePackages.Pending(Store)))throw new InvalidOperationException("上次模块安装尚未恢复，请先打开配置中心恢复安装。");
            bool running=await Running();cancellation.ThrowIfCancellationRequested();if(running)return;
            Settings cfg=Store.Read();string python=Store.Resolve(cfg.engines.pythonService);
            if(!File.Exists(python))throw new InvalidOperationException("请在本地引擎设置中选择主服务 Python 运行时。");
            Directory.CreateDirectory(Path.Combine(Store.Root,"data","logs"));
            var info=new ProcessStartInfo(python,"-I -X utf8 -u "+Quote(Path.Combine(Store.Root,"service","launcher.py"))+" --root "+Quote(Store.Root));
            info.WorkingDirectory=Store.Root;info.UseShellExecute=false;info.CreateNoWindow=true;info.WindowStyle=ProcessWindowStyle.Hidden;
            info.EnvironmentVariables.Remove("PYTHONHOME");info.EnvironmentVariables.Remove("PYTHONPATH");
            info.EnvironmentVariables["PYTHONDONTWRITEBYTECODE"]="1";info.EnvironmentVariables["PYTHONUTF8"]="1";info.EnvironmentVariables["PYTHONNOUSERSITE"]="1";
            cancellation.ThrowIfCancellationRequested();Owned=Process.Start(info);
            for(int i=0;i<80;i++){await Task.Delay(150,cancellation);if(Owned.HasExited)throw new InvalidOperationException("服务启动失败，请查看 data\\logs\\service.log。");if(await Running()){cancellation.ThrowIfCancellationRequested();return;}}
            throw new InvalidOperationException("服务启动超时，请查看日志。");
        }
        public async Task Stop() {
            await Request("/api/shutdown",new {},3000);
            // After shutdown, the socket can remain bound during worker cleanup
            // without accepting HTTP. Do not health-poll this intentional state.
            if(Owned!=null&&!Owned.HasExited){await Task.Run(()=>Owned.WaitForExit(5000));if(!Owned.HasExited)await Task.Run(()=>{using(var kill=Process.Start(new ProcessStartInfo("taskkill.exe","/PID "+Owned.Id+" /T /F"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true})){var output=kill.StandardOutput.ReadToEndAsync();var error=kill.StandardError.ReadToEndAsync();kill.WaitForExit(5000);Task.WaitAll(output,error);}});}
            // The listening socket closes before worker cleanup finishes. Wait for
            // the service's file lock too, including a service started by MCP.
            for(int i=0;i<100;i++){
                try{using(var file=new FileStream(Path.Combine(Store.Root,"data","engine-operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite)){file.Lock(0,1);file.Unlock(0,1);}return;}
                catch(IOException){}
                await Task.Delay(100);
            }
            throw new InvalidOperationException("服务正在清理后台任务，请稍后再保存或导入配置。");
        }
        public async Task StopOwnedStartup(){
            var owned=Owned;if(owned==null||owned.HasExited)return;
            await Task.Run(()=>{using(var kill=Process.Start(new ProcessStartInfo("taskkill.exe","/PID "+owned.Id+" /T /F"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true})){
                var output=kill.StandardOutput.ReadToEndAsync();var error=kill.StandardError.ReadToEndAsync();kill.WaitForExit(10000);Task.WaitAll(output,error);
            }});
            if(!owned.HasExited)throw new InvalidOperationException("本窗口启动的服务尚未退出，请稍后重试关闭。");
        }
        public static string Quote(string text) {
            var quoted=new StringBuilder("\"");int slashes=0;
            foreach(char value in text){
                if(value=='\\'){slashes++;continue;}
                if(value=='"'){quoted.Append('\\',slashes*2+1);quoted.Append('"');}
                else{quoted.Append('\\',slashes);quoted.Append(value);}
                slashes=0;
            }
            quoted.Append('\\',slashes*2);quoted.Append('"');return quoted.ToString();
        }
        public static void Open(string path) {
            if(string.IsNullOrWhiteSpace(path))throw new InvalidOperationException("尚未设置要打开的位置。");
            Uri address;if(Uri.TryCreate(path,UriKind.Absolute,out address)&&(address.Scheme=="https"||address.Scheme=="http")){Process.Start(new ProcessStartInfo(address.AbsoluteUri){UseShellExecute=true});return;}
            string full=Path.GetFullPath(path);
            if(Directory.Exists(full)){Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),Quote(full)){UseShellExecute=false});return;}
            if(!File.Exists(full))throw new InvalidOperationException("找不到文件或目录："+full+"。请检查安装是否完整，或重新选择已有位置。");
            if(TextDocumentForm.Supported(full)){TextDocumentForm.Open(full);return;}
            try{Process.Start(new ProcessStartInfo(full){UseShellExecute=true});}
            catch(System.ComponentModel.Win32Exception error){throw new InvalidOperationException("无法打开文件："+full+"。请为该文件类型设置默认应用。",error);}
        }
        public Task<string> RunTool(string tool,string arguments) {
            return Task.Run(()=> {
                var cfg=Store.Read();string python=Store.Resolve(cfg.engines.pythonService);
                if(!File.Exists(python))throw new InvalidOperationException("尚未选择 Python 运行时。");
                var info=new ProcessStartInfo(python,"-I -X utf8 "+Quote(Path.Combine(Store.Root,"tools",tool))+" "+arguments);
                info.WorkingDirectory=Store.Root;info.UseShellExecute=false;info.CreateNoWindow=true;info.WindowStyle=ProcessWindowStyle.Hidden;
                info.RedirectStandardError=true;info.RedirectStandardOutput=true;info.StandardOutputEncoding=Encoding.UTF8;info.StandardErrorEncoding=Encoding.UTF8;
                using(var process=Process.Start(info)){var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();process.WaitForExit();Task.WaitAll(output,error);if(process.ExitCode!=0)throw new InvalidOperationException(String.IsNullOrWhiteSpace(error.Result)?output.Result:error.Result);return output.Result;}
            });
        }
        public Task<byte[]> Audio(string path){return Task.Run(()=>{
            if(!System.Text.RegularExpressions.Regex.IsMatch(path??"",@"^/audio/[a-f0-9]{32}\.wav$"))throw new InvalidOperationException("无效音频地址。");
            var config=Store.Read();var request=(HttpWebRequest)WebRequest.Create("http://127.0.0.1:"+config.port+path);request.Proxy=null;request.Timeout=30000;request.ReadWriteTimeout=30000;request.Headers[HttpRequestHeader.Authorization]="Bearer "+config.token;
            using(var response=request.GetResponse())using(var source=response.GetResponseStream())using(var buffer=new MemoryStream()){source.CopyTo(buffer);return buffer.ToArray();}
        });}
    }
}
