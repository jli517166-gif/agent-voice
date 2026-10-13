using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed partial class MainForm {
        Button exportConfigurationButton,exportFullConfigurationButton,importConfigurationButton;
        TextBox modelPackageName;
        string configurationTestPath;
        Dictionary<string,object> lastTransfer;
        void BuildConfiguration(){
            var card=SettingsCard("configuration","本地训练音色模型包 · 导入导出");
            Add(card,Hint("轻量音色包：只包含参考音频、逐字台词、声音参数，以及本地 TTS 基础模型的完整指纹；通常只有几百 KB 到几十 MB。导入时必须在 tts-engines 中找到完全相同的基础模型，否则明确拒绝。"));
            Add(card,Hint("完整便携包：包含轻量包的全部内容，并额外携带完整基础模型权重；通常约 5 GB。目标电脑没有对应模型时，程序会校验并安装到 tts-engines/<引擎>/models。"));
            Add(card,Hint("两种包使用同一 .cosyvoice.ttsmodel 后缀，以包内 packageMode 为准；都不包含 RVC、外观、Harness、AIRI、连接密钥或程序运行环境。相同模型指纹下，两种包的音色结果一致。"));
            Add(card,Hint("导出音色名称（建议文件名保留“轻量”或“完整”字样，包内同时记录类型、引擎、模型类型和 SHA-256 指纹）"));modelPackageName=Input();modelPackageName.Text="我的声音";Add(card,modelPackageName);
            exportConfigurationButton=AsyncBtn("导出轻量音色包",async()=>await ChooseAndExport("lightweight"));
            exportFullConfigurationButton=AsyncBtn("导出完整便携包",async()=>await ChooseAndExport("full"));
            importConfigurationButton=Primary(AsyncBtn("导入音色包",async()=>{
                string path=configurationTestPath;
                if(path==null)using(var dialog=new OpenFileDialog{Filter="CosyVoice模型包|*.cosyvoice.ttsmodel",CheckFileExists=true,InitialDirectory=VoicePackageDirectory()}){
                    if(dialog.ShowDialog(this)!=DialogResult.OK)return;path=dialog.FileName;
                }
                await ImportConfiguration(path);
            }));
            Add(card,Row(exportConfigurationButton,exportFullConfigurationButton,importConfigurationButton,Btn("打开音色模型包文件夹",()=>ServiceClient.Open(VoicePackageDirectory())),Btn("打开本地 TTS 引擎文件夹",()=>ServiceClient.Open(TtsEngineDirectory()))));
            Add(card,Hint("轻量包是默认选择。完整包适合离线迁移；导出会读取并哈希完整模型，处理时间取决于硬盘速度。正在生成或播放时等待队列完成再导入，服务空闲时会自动关闭并恢复。"));
        }
        async Task ChooseAndExport(string mode){
            string path=configurationTestPath;
            string name=SafeProfileName(PackageDisplayName(modelPackageName.Text));if(name=="未命名配置")throw new InvalidOperationException("请先填写导出音色名称。");
            string label=mode=="lightweight"?"轻量":"完整";
            string packageDirectory=Path.Combine(VoicePackageDirectory(),name);Directory.CreateDirectory(packageDirectory);
            if(path==null)using(var dialog=new SaveFileDialog{Filter="CosyVoice模型包|*.cosyvoice.ttsmodel",DefaultExt="cosyvoice.ttsmodel",AddExtension=false,FileName=name+"-"+label+".cosyvoice.ttsmodel",InitialDirectory=packageDirectory}){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;path=NormalizePackagePath(dialog.FileName);
            }
            await ExportConfiguration(path,mode);
        }
        async Task ExportConfiguration(string path,string mode){
            Status(mode=="lightweight"?"正在生成模型指纹并导出轻量音色包……":"正在导出完整基础模型与参考声音……");
            lastTransfer=Json.Object(await Service.RunTool("tts_bundle.py","export --root "+ServiceClient.Quote(Store.Root)+" --file "+ServiceClient.Quote(path)+" --package-mode "+mode));
            Status((mode=="lightweight"?"轻量音色包":"完整便携包")+"已导出 · "+path);
        }
        string PackageDisplayName(string value){const string suffix=".cosyvoice.ttsmodel";value=(value??"").Trim();while(value.EndsWith(suffix,StringComparison.OrdinalIgnoreCase))value=value.Substring(0,value.Length-suffix.Length).Trim();foreach(string label in new[]{"-轻量","-完整"})if(value.EndsWith(label,StringComparison.OrdinalIgnoreCase))value=value.Substring(0,value.Length-label.Length).Trim();return value;}
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
                string kind=Json.Text(lastTransfer,"packageMode")=="lightweight"?"轻量音色包":"完整便携包";
                Status(kind+"“"+Json.Text(lastTransfer,"name")+"”已导入并匹配本地 TTS 引擎 · 原配置备份："+Json.Text(lastTransfer,"backup"));
            }catch(Exception error){failure=error;}
            try{if(running)await Service.Start();}catch(Exception error){if(failure!=null)throw new InvalidOperationException(failure.Message+"；服务恢复失败："+error.Message);throw;}
            await Poll();if(failure!=null)throw new InvalidOperationException(failure.Message,failure);
        }
    }
}
