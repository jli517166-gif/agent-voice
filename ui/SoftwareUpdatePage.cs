using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed partial class MainForm {
        TextBox updateDetails;
        void BuildUpdates(){
            var card=SettingsCard("updates","软件更新");
            string file=Store.Resolve("app-version.json");
            var metadata=File.Exists(file)?Json.Object(File.ReadAllText(file)):new Dictionary<string,object>();
            Add(card,Hint("当前版本："+Json.Text(metadata,"version")+" · "+"公开版"));
            Add(card,Hint("程序更新、TTS 引擎和运行环境分别管理。更新前逐文件校验并备份，保留设置、引擎、功能模块与个人文件。仅接受与当前程序匹配的公开更新包。本地更新包包含程序代码，请只选择可信来源。"));
            Add(card,Row(Primary(AsyncBtn("检查更新",async()=>await RunUpdate("check"))),AsyncBtn("下载并校验",async()=>await RunUpdate("download")),AsyncBtn("选择本地更新包",async()=>{
                using(var dialog=new OpenFileDialog{Title="选择同版本类型的软件更新 ZIP",Filter="软件更新包|*.zip",CheckFileExists=true})if(dialog.ShowDialog(this)==DialogResult.OK)await RunUpdate("stage --package "+ServiceClient.Quote(dialog.FileName));
            })));
            Add(card,Row(AsyncBtn("退出并安装更新",InstallStagedUpdate),EntryBtn("打开更新记录","查看校验结果、更新日志与回退备份。",()=>{string path=Store.Resolve("data/updates");Directory.CreateDirectory(path);ServiceClient.Open(path);})));
            updateDetails=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Height=250,Name="softwareUpdateDetails"};Add(card,updateDetails);
            string previous=Store.Resolve("data/updates/latest.json");if(File.Exists(previous))updateDetails.Text=File.ReadAllText(previous);
            Add(card,Hint("GitHub 仓库私有或网络受限时，检查更新会明确提示原因；可用有权限的账号下载后选择本地更新包。旧版无更新清单的 ZIP 不会直接覆盖程序。安装失败查看更新记录，原文件会回滚。"));
        }
        async Task RunUpdate(string action){
            using(var download=action=="download"?Service.BeginDownload():null){
            var result=Json.Object(await Service.RunTool("software_update.py",action+" --root "+ServiceClient.Quote(Store.Root)));
            string message=Json.Text(result,"message");updateDetails.Text=message+"\r\n\r\n"+Json.Encode(result);Status(message);
        }
        }
        async Task InstallStagedUpdate(){
            if(!File.Exists(Store.Resolve("data/updates/pending.json")))throw new InvalidOperationException("请先下载并校验，或选择本地更新包。");
            if(await Service.Running())throw new InvalidOperationException("请先在控制台关闭语音服务，再安装更新。");
            string python=Store.Resolve(Config.engines.pythonService),script=Store.Resolve("tools/software_update.py");
            if(!File.Exists(python)||!File.Exists(script))throw new InvalidOperationException("缺少更新运行时或组件，请先补齐主服务环境。");
            var info=new ProcessStartInfo(python,"-I -X utf8 "+ServiceClient.Quote(script)+" apply --root "+ServiceClient.Quote(Store.Root)+" --wait-pid "+Process.GetCurrentProcess().Id+" --restart"){
                UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Store.Root};
            Process.Start(info);exiting=true;Application.Exit();
        }
    }
}
