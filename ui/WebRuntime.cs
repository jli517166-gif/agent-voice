using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace AgentVoice {
    internal static class WebRuntime {
        internal static string FixedFolder(SettingsStore store){string folder=store.Resolve("tools/system-prerequisites/webview2-fixed");return File.Exists(Path.Combine(folder,"msedgewebview2.exe"))?folder:null;}
        internal static string Version(SettingsStore store){return CoreWebView2Environment.GetAvailableBrowserVersionString(FixedFolder(store));}
        internal static async Task Install(SettingsStore store,string offlineInstaller){
            string folder=store.Resolve("tools/system-prerequisites/webview2");Directory.CreateDirectory(folder);
            string installer=Path.Combine(folder,offlineInstaller==null?"MicrosoftEdgeWebview2Setup.exe":"MicrosoftEdgeWebView2RuntimeInstallerX64.exe");
            if(offlineInstaller==null){
                if(new WelcomeState(store).Read().offline)throw new InvalidOperationException("严格离线已开启，请选择离线安装器或固定版运行时。");
                ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
                using(var client=new WebClient())await client.DownloadFileTaskAsync(new Uri("https://go.microsoft.com/fwlink/p/?LinkId=2124703"),installer+".partial");
                if(File.Exists(installer))File.Delete(installer);File.Move(installer+".partial",installer);
            }else if(!Path.GetFullPath(offlineInstaller).Equals(Path.GetFullPath(installer),StringComparison.OrdinalIgnoreCase))File.Copy(offlineInstaller,installer,true);
            await VerifyMicrosoft(installer);
            using(var process=Process.Start(new ProcessStartInfo(installer,"/silent /install"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){
                // Keep the caller's write guard until the installer really exits.
                await Task.Run(()=>process.WaitForExit());
                if(process.ExitCode!=0)throw new InvalidOperationException("WebView2 安装未通过，退出码 "+process.ExitCode+"。可使用固定版运行时或原生配置。");
            }
            Version(store);
        }
        internal static async Task ImportFixed(SettingsStore store,string source){
            string executable=Path.Combine(source,"msedgewebview2.exe");if(!File.Exists(executable))throw new InvalidOperationException("请选择解压后直接包含 msedgewebview2.exe 的固定版运行时目录。");
            await VerifyMicrosoft(executable);
            string target=store.Resolve("tools/system-prerequisites/webview2-fixed");
            if(Path.GetFullPath(target).TrimEnd('\\').Equals(Path.GetFullPath(source).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)){Version(store);return;}
            if(Directory.Exists(target))throw new InvalidOperationException("固定版目录已经存在，请保留原文件并检查完整性，或使用原生配置；不会覆盖已有运行时。");
            // Failed copies never become a selected runtime and can be retried.
            string stage=target+".staging-"+Guid.NewGuid().ToString("N");
            try{
                await Task.Run(()=>CopyTree(source,stage));
                CoreWebView2Environment.GetAvailableBrowserVersionString(stage);
                Directory.Move(stage,target);
            }finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
        }
        static void CopyTree(string source,string target){
            if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("运行时目录不能包含链接或联接点。");
            Directory.CreateDirectory(target);
            foreach(string file in Directory.GetFiles(source)){if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("运行时不能包含文件链接。");File.Copy(file,Path.Combine(target,Path.GetFileName(file)),false);}
            foreach(string folder in Directory.GetDirectories(source))CopyTree(folder,Path.Combine(target,Path.GetFileName(folder)));
        }
        static async Task VerifyMicrosoft(string file){
            string quoted=file.Replace("'","''");
            string script="$s=Get-AuthenticodeSignature -LiteralPath '"+quoted+"'; if($s.Status -eq 'Valid' -and $s.SignerCertificate.Subject -like '*O=Microsoft Corporation*'){exit 0}else{exit 1}";
            using(var p=Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script))){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){
                await Task.Run(()=>p.WaitForExit());if(p.ExitCode!=0)throw new InvalidOperationException("文件未通过微软 Authenticode 签名验证，未执行。请从微软官网下载运行时。");
            }
        }
    }
}
