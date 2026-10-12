using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed partial class MainForm {
        Button exportConfigurationButton,importConfigurationButton;
        TextBox modelPackageName;
        string configurationTestPath;
        Dictionary<string,object> lastTransfer;
        void BuildConfiguration(){
            var card=SettingsCard("configuration","本地训练音色模型包 · 导入导出");
            Add(card,Hint("这里导入或导出的就是首页“本地训练音色模型包”：完整 CosyVoice 模型权重与配套资源，并附带当前参考音频／台词及本地声音参数。RVC、外观、Harness、AIRI 与运行环境均独立保留。"));
            Add(card,Hint("CosyVoice 包使用 .cosyvoice.ttsmodel 后缀；导入同时检查引擎、模型类型与逐文件校验，拒绝错误引擎或损坏的包。导入成功切换到本地直出，其他模块与 RVC 备用配置保留。"));
            Add(card,Hint("导出音色名称（文件最终保存为“名称.cosyvoice.ttsmodel”，名称和 TTS 引擎类型也会写进包内清单）"));modelPackageName=Input();modelPackageName.Text="我的声音";Add(card,modelPackageName);
            exportConfigurationButton=AsyncBtn("导出训练音色模型包",async()=>{
                string path=configurationTestPath;
                string name=SafeProfileName(modelPackageName.Text.Trim());if(name=="未命名配置")throw new InvalidOperationException("请先填写导出音色名称。");
                if(path==null)using(var dialog=new SaveFileDialog{Filter="CosyVoice模型包|*.cosyvoice.ttsmodel",DefaultExt="cosyvoice.ttsmodel",AddExtension=true,FileName=name+".cosyvoice.ttsmodel",InitialDirectory=VoicePackageDirectory()}){
                    if(dialog.ShowDialog(this)!=DialogResult.OK)return;path=dialog.FileName;
                }
                await ExportConfiguration(path);
            });
            importConfigurationButton=AsyncBtn("导入训练音色模型包",async()=>{
                string path=configurationTestPath;
                if(path==null)using(var dialog=new OpenFileDialog{Filter="CosyVoice模型包|*.cosyvoice.ttsmodel",CheckFileExists=true,InitialDirectory=VoicePackageDirectory()}){
                    if(dialog.ShowDialog(this)!=DialogResult.OK)return;path=dialog.FileName;
                }
                await ImportConfiguration(path);
            });
            Add(card,Row(exportConfigurationButton,importConfigurationButton,Btn("打开音色模型包文件夹",()=>ServiceClient.Open(VoicePackageDirectory()))));
            Add(card,Hint("导出首页已应用的模型目录；可以是训练后整理好的兼容模型。当前推理方式仍会使用包中的一段参考音频和对应逐字台词。模型包可能很大，请预留空间。正在生成或播放时等待完成再导入，服务空闲时会自动关闭并恢复。"));
        }
        async Task ExportConfiguration(string path){
            Status("正在导出本地模型文件与参考声音……");
            lastTransfer=Json.Object(await Service.RunTool("tts_bundle.py","export --root "+ServiceClient.Quote(Store.Root)+" --file "+ServiceClient.Quote(path)));
            string stored=configurationTestPath==null?await KeepModelPackage(path):path;Status("CosyVoice 模型已导出并登记到音色模型包文件夹 · "+stored);
        }
        async Task<string> KeepModelPackage(string path){string source=Path.GetFullPath(path),library=Path.GetFullPath(VoicePackageDirectory()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;if(source.StartsWith(library,StringComparison.OrdinalIgnoreCase))return source;string target=Path.Combine(VoicePackageDirectory(),Path.GetFileName(source));if(!source.Equals(target,StringComparison.OrdinalIgnoreCase)){Status("正在把模型包归档到程序专用文件夹……");await Task.Run(()=>File.Copy(source,target,true));}return target;}
        async Task ImportConfiguration(string path){
            bool running=await Service.Running();
            if(running){var health=await Service.Request("/health");if(Convert.ToBoolean(health["busy"])||Convert.ToInt32(health["queued"])>0)throw new InvalidOperationException("请等待当前朗读队列完成，再导入配置。");await Service.Stop();}
            Exception failure=null;
            try{
                Status("正在校验并导入本地模型文件……");
                lastTransfer=Json.Object(await Service.RunTool("tts_bundle.py","import --root "+ServiceClient.Quote(Store.Root)+" --file "+ServiceClient.Quote(path)));
                if(configurationTestPath==null)await KeepModelPackage(path);
                Config=Store.Read();BindVoice();BindSettings();ApplyTheme();ApplyIcon();
                lastJob="";deleted=null;
                Status("CosyVoice 音色“"+Json.Text(lastTransfer,"name")+"”已导入音色模型包库 · 原配置备份："+Json.Text(lastTransfer,"backup"));
            }catch(Exception error){failure=error;}
            try{if(running)await Service.Start();}catch(Exception error){if(failure!=null)throw new InvalidOperationException(failure.Message+"；服务恢复失败："+error.Message);throw;}
            await Poll();if(failure!=null)throw new InvalidOperationException(failure.Message,failure);
        }
    }
}
