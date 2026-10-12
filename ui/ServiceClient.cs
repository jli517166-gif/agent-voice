using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

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
            try {var status=await Request("/health",null,900);return Json.Text(status,"service")=="agent-voice-public";}
            catch {return false;}
        }
        public async Task Start() {
            if(await Running())return;
            Settings cfg=Store.Read();string python=Store.Resolve(cfg.engines.pythonRvc);
            if(!File.Exists(python))throw new InvalidOperationException("请在本地引擎设置中选择 RVC 的 Python 运行时。");
            Directory.CreateDirectory(Path.Combine(Store.Root,"data","logs"));
            var info=new ProcessStartInfo(python,"-X utf8 -u "+Quote(Path.Combine(Store.Root,"service","launcher.py"))+" --root "+Quote(Store.Root));
            info.WorkingDirectory=Store.Root;info.UseShellExecute=false;info.CreateNoWindow=true;info.WindowStyle=ProcessWindowStyle.Hidden;
            info.EnvironmentVariables.Remove("PYTHONHOME");info.EnvironmentVariables.Remove("PYTHONPATH");
            info.EnvironmentVariables["PYTHONDONTWRITEBYTECODE"]="1";info.EnvironmentVariables["PYTHONUTF8"]="1";
            Owned=Process.Start(info);
            for(int i=0;i<80;i++){await Task.Delay(150);if(Owned.HasExited)throw new InvalidOperationException("服务启动失败，请查看 data\\logs\\service.log。");if(await Running())return;}
            throw new InvalidOperationException("服务启动超时，请查看日志。");
        }
        public async Task Stop() {
            await Request("/api/shutdown",new {},3000);
            for(int i=0;i<30;i++){await Task.Delay(100);if(!await Running())break;}
            if(Owned!=null&&!Owned.HasExited){await Task.Run(()=>Owned.WaitForExit(3000));if(!Owned.HasExited)Owned.Kill();}
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
        public static void Open(string path) {Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
        public Task<string> RunTool(string tool,string arguments) {
            return Task.Run(()=> {
                var cfg=Store.Read();string python=Store.Resolve(cfg.engines.pythonTts);
                if(!File.Exists(python))python=Store.Resolve(cfg.engines.pythonRvc);
                if(!File.Exists(python))throw new InvalidOperationException("尚未选择 Python 运行时。");
                var info=new ProcessStartInfo(python,"-X utf8 "+Quote(Path.Combine(Store.Root,"tools",tool))+" "+arguments);
                info.WorkingDirectory=Store.Root;info.UseShellExecute=false;info.CreateNoWindow=true;info.WindowStyle=ProcessWindowStyle.Hidden;
                info.RedirectStandardError=true;info.RedirectStandardOutput=true;info.StandardOutputEncoding=Encoding.UTF8;info.StandardErrorEncoding=Encoding.UTF8;
                using(var process=Process.Start(info)){var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();process.WaitForExit();Task.WaitAll(output,error);if(process.ExitCode!=0)throw new InvalidOperationException(error.Result);return output.Result;}
            });
        }
    }
}
