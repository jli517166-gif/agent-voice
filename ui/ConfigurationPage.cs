using Button = AgentVoice.AnimeButton;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed partial class MainForm {
        Button exportConfigurationButton,exportFullConfigurationButton,exportTrainedButton,exportTrainedFullButton,importConfigurationButton;
        TextBox modelPackageName;
        Label packageEngineHint;
        string configurationTestPath;
        Dictionary<string,object> lastTransfer;
        void BuildConfiguration(){
            var card=SettingsCard("configuration","音色包");
            packageEngineHint=Hint("CosyVoice 音色包导出适用于 CosyVoice 引擎。");Add(card,packageEngineHint);
            Add(card,Hint("轻量包：参考音频和参数；目标电脑必须已有完全一致的基础模型。参考完整包：额外携带当前基础模型，适合离线迁移。"));
            Add(card,Hint("高级训练包：只携带训练后变化的权重，体积较小，导入时需要匹配的基础模型。高级完整包：携带完整训练模型，不要求目标电脑已有基础模型。四种包都不包含 Python、RVC、外观或连接密钥。"));
            Add(card,Hint("音色名称"));modelPackageName=Input();modelPackageName.Text="我的声音";Add(card,modelPackageName);
            exportConfigurationButton=AsyncBtn("导出轻量包",async()=>await ChooseAndExport("lightweight"));
            exportFullConfigurationButton=AsyncBtn("导出参考完整包",async()=>await ChooseAndExport("full"));
            exportTrainedButton=AsyncBtn("导出高级训练包",async()=>await ChooseAndExport("trained"));
            exportTrainedFullButton=AsyncBtn("导出高级完整包",async()=>await ChooseAndExport("trained-full"));
            importConfigurationButton=Primary(AsyncBtn("导入音色包",async()=>{
                string path=configurationTestPath;
                if(path==null)using(var dialog=new OpenFileDialog{Filter="CosyVoice模型包|*.cosyvoice.ttsmodel",CheckFileExists=true,InitialDirectory=VoicePackageDirectory()}){
                    if(dialog.ShowDialog(this)!=DialogResult.OK)return;path=dialog.FileName;
                }
                await ImportConfiguration(path);
            }));
            tips.SetToolTip(importConfigurationButton,"导入四种标准的 .cosyvoice.ttsmodel 音色包。");
            tips.SetToolTip(exportConfigurationButton,"只导出参考声音和参数；目标电脑需要相同基础模型。");
            tips.SetToolTip(exportFullConfigurationButton,"导出参考声音、参数和当前基础模型。");
            tips.SetToolTip(exportTrainedButton,"只导出训练后变化的权重；目标电脑需要相同基础模型。");
            tips.SetToolTip(exportTrainedFullButton,"导出完整训练模型，适合跨电脑迁移。");
            Add(card,Row(importConfigurationButton,exportConfigurationButton,exportFullConfigurationButton,exportTrainedButton,exportTrainedFullButton));
            Add(card,Row(Btn("打开音色包目录",()=>ServiceClient.Open(VoicePackageDirectory())),Btn("打开引擎目录",()=>ServiceClient.Open(TtsEngineDirectory())),Btn("查看高级训练手册",()=>ShowPage("advancedTraining"))));
            Add(card,Hint("高级导出会先让你选择训练完成的完整 CosyVoice3 模型目录。导入时程序会校验模型、引擎和运行环境指纹；不兼容时不会覆盖当前声音。"));
        }
        async Task ChooseAndExport(string mode){
            string path=configurationTestPath;
            string name=SafeProfileName(PackageDisplayName(modelPackageName.Text));if(name=="未命名配置")throw new InvalidOperationException("请先填写导出音色名称。");
            string label=PackageModeLabel(mode);
            string trained="";
            if(mode=="trained"||mode=="trained-full")using(var folder=new FolderBrowserDialog{Description="选择训练完成的完整 CosyVoice3 模型目录",ShowNewFolderButton=false,SelectedPath=TrainingWorkspace()}){if(folder.ShowDialog(this)!=DialogResult.OK)return;trained=folder.SelectedPath;}
            string packageDirectory=Path.Combine(VoicePackageDirectory(),name);Directory.CreateDirectory(packageDirectory);
            if(path==null)using(var dialog=new SaveFileDialog{Filter="CosyVoice模型包|*.cosyvoice.ttsmodel",DefaultExt="cosyvoice.ttsmodel",AddExtension=false,FileName=name+"-"+label+".cosyvoice.ttsmodel",InitialDirectory=packageDirectory}){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;path=NormalizePackagePath(dialog.FileName);
            }
            await ExportConfiguration(path,mode,trained);
        }
        async Task ExportConfiguration(string path,string mode,string trained=""){
            Status("正在校验并导出"+PackageModeLabel(mode)+"……");
            string args="export --root "+ServiceClient.Quote(Store.Root)+" --file "+ServiceClient.Quote(path)+" --package-mode "+mode+(trained.Length>0?" --trained-model-dir "+ServiceClient.Quote(trained):"");
            lastTransfer=Json.Object(await Service.RunTool("tts_bundle.py",args));
            Status(PackageModeLabel(mode)+"已导出 · "+path);
        }
        string PackageModeLabel(string mode){if(mode=="lightweight")return "轻量包";if(mode=="full")return "参考完整包";if(mode=="trained")return "高级训练包";if(mode=="trained-full")return "高级完整包";return "音色包";}
        string PackageDisplayName(string value){const string suffix=".cosyvoice.ttsmodel";value=(value??"").Trim();while(value.EndsWith(suffix,StringComparison.OrdinalIgnoreCase))value=value.Substring(0,value.Length-suffix.Length).Trim();foreach(string label in new[]{"-高级完整包","-高级训练包","-参考完整包","-轻量包","-完整","-轻量"})if(value.EndsWith(label,StringComparison.OrdinalIgnoreCase))value=value.Substring(0,value.Length-label.Length).Trim();return value;}
        string NormalizePackagePath(string path){const string suffix=".cosyvoice.ttsmodel";string value=Path.GetFullPath(path);while(value.EndsWith(suffix,StringComparison.OrdinalIgnoreCase))value=value.Substring(0,value.Length-suffix.Length);return value+suffix;}
        async Task<string> KeepModelPackage(string path,string displayName){string source=Path.GetFullPath(path),library=Path.GetFullPath(VoicePackageDirectory()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;if(source.StartsWith(library,StringComparison.OrdinalIgnoreCase))return source;string folder=Path.Combine(VoicePackageDirectory(),SafeProfileName(PackageDisplayName(displayName)));Directory.CreateDirectory(folder);string target=Path.Combine(folder,Path.GetFileName(NormalizePackagePath(source)));if(!source.Equals(target,StringComparison.OrdinalIgnoreCase)){Status("正在把音色包登记到程序专用文件夹……");await Task.Run(()=>File.Copy(source,target,true));}return target;}
        async Task ImportConfiguration(string path){
            bool running=await Service.Running();
            if(running){var health=await Service.Request("/health");if(Convert.ToBoolean(health["busy"])||Convert.ToInt32(health["queued"])>0)throw new InvalidOperationException("请等待当前朗读队列完成，再导入配置。");await Service.Stop();}
            Exception failure=null;
            try{
                Status("正在校验音色包并匹配本地 TTS 引擎……");
                lastTransfer=Json.Object(await Service.RunTool("tts_bundle.py","import --root "+ServiceClient.Quote(Store.Root)+" --file "+ServiceClient.Quote(path)));
                if(configurationTestPath==null)await KeepModelPackage(path,Json.Text(lastTransfer,"name"));
                Config=Store.Read();BindVoice();BindSettings();ApplyTheme();ApplyIcon();lastJob="";deleted=null;
                string kind=PackageModeLabel(Json.Text(lastTransfer,"packageMode"));
                Status(kind+"“"+Json.Text(lastTransfer,"name")+"”已导入并匹配本地 TTS 引擎 · 原配置备份："+Json.Text(lastTransfer,"backup"));
            }catch(Exception error){failure=error;}
            try{if(running)await Service.Start();}catch(Exception error){if(failure!=null)throw new InvalidOperationException(failure.Message+"；服务恢复失败："+error.Message);throw;}
            await Poll();if(failure!=null)throw new InvalidOperationException(failure.Message,failure);
        }
    }
}
