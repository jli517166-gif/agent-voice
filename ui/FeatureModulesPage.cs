using System;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed partial class MainForm {
        FlowLayoutPanel installedModules;
        void BuildModules(){
            var stack=Stack(NewPage("modules"));var card=Card(stack,"模块");
            Add(card,Hint("功能扩展、TTS、唱歌引擎和运行环境可按需安装。"));
            Add(card,Row(AsyncBtn("导入模块",ImportOptionalModule),Btn("刷新列表",RefreshOptionalModules)));
            Add(card,Hint("功能模块为 .avmodule，TTS 为 .ttsengine，唱歌引擎为 .singengine，环境为 .ttsenvironment。模块会运行本地代码，请确认来源可信。"));
            var list=Card(stack,"已安装的功能模块");installedModules=new BufferedFlow{AutoSize=true,Dock=DockStyle.Top,FlowDirection=FlowDirection.TopDown,WrapContents=false};Add(list,installedModules);RefreshOptionalModules();
        }
        void RefreshOptionalModules(){
            if(installedModules==null)return;while(installedModules.Controls.Count>0){var old=installedModules.Controls[0];installedModules.Controls.RemoveAt(0);old.Dispose();}
            var ids=ModulePackages.FeatureIds(Store);if(ids.Length==0)installedModules.Controls.Add(Hint("尚未安装功能模块。核心朗读、参考声音设置和声音方案均可独立使用。"));
            foreach(string id in ids){string key=id,name=id;bool valid=true;try{name=ModulePackages.InstalledFeature(Store,key).name;}catch(Exception error){valid=false;name=id+" · 需要重新安装："+error.Message;}
                installedModules.Controls.Add(Hint(name));var open=AsyncBtn("打开模块",async()=>await OpenOptionalModule(key));open.Enabled=valid;
                installedModules.Controls.Add(Row(open,AsyncBtn("移除模块",async()=>{ModulePackages.RemoveFeature(Store,key);RefreshOptionalModules();Status("模块已移除，保留恢复副本；模型和参考录音未删除。");await Task.FromResult(0);})));
            }
            WalkTheme(installedModules);
        }
        async Task ImportOptionalModule(){
            string file;using(var d=new OpenFileDialog{Title="导入模块",Filter="模块包|*.avmodule;*.ttsengine;*.singengine;*.ttsenvironment",CheckFileExists=true}){if(d.ShowDialog(this)!=DialogResult.OK)return;file=d.FileName;}
            var manifest=ModulePackages.Inspect(file);
            if(MessageBox.Show(this,manifest.name+"\r\n\r\n模块会运行本地代码，仅安装来源可信的文件。安装范围：软件内的独立模块目录。","安装所选模块",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)!=DialogResult.OK)return;
            if(await Service.Running())throw new InvalidOperationException("请先关闭语音服务，再安装模块。");
            var progress=new Progress<string>(BusyStatus);await Task.Run(()=>ModulePackages.Install(Store,file,text=>((IProgress<string>)progress).Report(text),CancellationToken.None));
            Config=Store.Read();BindVoice();BindSettings();RefreshOptionalModules();Status("模块已安装："+manifest.name+"。功能模块请在列表中打开；引擎与环境仍需真实生成验证。");
        }
        async Task OpenOptionalModule(string id){
            // Importers must also open on an installation without a voice.
            // Exports deliberately read the last applied settings, not a draft.
            var manifest=ModulePackages.InstalledFeature(Store,id);
            var start=new ProcessStartInfo(Store.Resolve(manifest.entrypoint),"--root "+ServiceClient.Quote(Store.Root)){UseShellExecute=false,WorkingDirectory=Store.Root};
            using(var process=Process.Start(start))await Task.Run(()=>process.WaitForExit());
            Config=Store.Read();observedVoiceRevision=-1;BindVoice();BindSettings();RefreshOptionalModules();await Poll();
        }
    }
}
