using Button = AgentVoice.AnimeButton;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed partial class MainForm {
        Button exportConfigurationButton,exportFullConfigurationButton,exportTrainedButton,exportTrainedFullButton,importConfigurationButton;
        ToolStripMenuItem exportTrainedMenuItem,exportTrainedFullMenuItem;
        TextBox modelPackageName;
        Label packageEngineHint;

        Dictionary<string,object> lastTransfer;
        void BuildConfiguration(){
            var page=WebPage("configuration");
            WebTitle(page,"音色包","声音文件放在这里，参数组合放在“声音方案”。");
            importConfigurationButton=WebAsyncButton("导入音色包","导入 CosyVoice 或 GPT-SoVITS 音色包，自动匹配模型和版本。",async()=>{
                string path=null;
                using(var dialog=new OpenFileDialog{Filter="TTS 音色包|*.cosyvoice.ttsmodel;*.gpt-sovits.ttsmodel",CheckFileExists=true,InitialDirectory=VoicePackageDirectory()}){
                    if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                    path=dialog.FileName;
                }
                await ImportConfiguration(path);
            },676,46,128,39,true);
            page.Controls.Add(importConfigurationButton);
            var exportButton=WebButton("导出音色包","先在这里选择导出类型，或在下方按类型直接导出。",()=>{},812,46,128,39);
            page.Controls.Add(exportButton);
            var menu=new ContextMenuStrip();
            foreach(var pair in new string[][]{
                new[]{"导出轻量包","lightweight"},
                new[]{"导出模型完整包","full"},
                new[]{"导出高级训练包","trained"},
                new[]{"导出高级完整包","trained-full"}
            }){
                var item=new ToolStripMenuItem(pair[0]);string mode=pair[1];
                item.Click+=(s,e)=>ExportMode(mode);
                if(mode=="trained")exportTrainedMenuItem=item;
                if(mode=="trained-full")exportTrainedFullMenuItem=item;
                menu.Items.Add(item);
            }
            exportButton.Click+=(s,e)=>menu.Show(exportButton,new Point(0,exportButton.Height));
            exportButton.ContextMenuStrip=menu;

            var list=WebCard(page,38,126,902,275);
            PackageIcon(list,0);
            list.Controls.Add(WebText("轻量包",13F,FontStyle.Bold,70,18,430,26,ContentAlignment.MiddleLeft));
            list.Controls.Add(WebText("参考声音与参数；目标电脑需要相同的基础模型。",9F,FontStyle.Regular,70,48,560,20,ContentAlignment.MiddleLeft));
            exportConfigurationButton=WebAsyncButton("导出轻量包","只导出参考声音和参数；目标电脑需要相同基础模型。",async()=>await ChooseAndExport("lightweight"),700,26,180,39,false);
            list.Controls.Add(exportConfigurationButton);
            WebDivider(list,16,91,870);

            PackageIcon(list,91);
            list.Controls.Add(WebText("模型完整包",13F,FontStyle.Bold,70,109,430,26,ContentAlignment.MiddleLeft));
            list.Controls.Add(WebText("额外携带当前基础模型，适合离线迁移。",9F,FontStyle.Regular,70,139,560,20,ContentAlignment.MiddleLeft));
            exportFullConfigurationButton=WebAsyncButton("导出模型完整包","导出参考声音、参数和当前基础模型。",async()=>await ChooseAndExport("full"),700,117,180,39,false);
            list.Controls.Add(exportFullConfigurationButton);
            WebDivider(list,16,182,870);

            PackageIcon(list,182);
            list.Controls.Add(WebText("高级训练包 / 高级完整包",13F,FontStyle.Bold,70,200,430,26,ContentAlignment.MiddleLeft));
            list.Controls.Add(WebText("训练后权重或完整训练模型；导入时匹配。",9F,FontStyle.Regular,70,230,408,20,ContentAlignment.MiddleLeft));
            exportTrainedButton=WebAsyncButton("导出高级训练包","只导出训练后变化的权重；目标电脑需要相同基础模型。",async()=>await ChooseAndExport("trained"),478,208,180,39,false);
            list.Controls.Add(exportTrainedButton);
            exportTrainedFullButton=WebAsyncButton("导出高级完整包","导出完整训练模型，适合跨电脑迁移。",async()=>await ChooseAndExport("trained-full"),666,208,180,39,false);
            list.Controls.Add(exportTrainedFullButton);

            page.Controls.Add(WebText("音色名称",9.5F,FontStyle.Bold,38,408,90,24,ContentAlignment.MiddleLeft));
            modelPackageName=Input();modelPackageName.Text="我的声音";modelPackageName.SetBounds(132,406,260,30);page.Controls.Add(modelPackageName);
            page.Controls.Add(WebText("导出文件名会使用此名称；高级导出会再选模型目录。",8.8F,FontStyle.Regular,410,406,530,34,ContentAlignment.TopLeft));
            packageEngineHint=WebText("CosyVoice 与 GPT-SoVITS 使用各自后缀的音色包，导入时自动选择引擎和版本。",9F,FontStyle.Regular,38,446,902,20,ContentAlignment.MiddleLeft);
            page.Controls.Add(packageEngineHint);

            page.Controls.Add(WebButton("训练自己的音色","准备数据并校验训练模型。",()=>ShowPage("training"),38,484,170,39));
            page.Controls.Add(WebButton("查看高级训练手册","查看高配置电脑训练专属权重的完整流程。",()=>ShowPage("advancedTraining"),216,484,190,39));
            page.Controls.Add(WebButton("打开引擎目录","查看独立的源码、运行环境与模型。",()=>ServiceClient.Open(TtsEngineDirectory()),414,484,150,39));
            page.Controls.Add(WebButton("打开音色包文件夹","打开程序专用的音色包库。",()=>ServiceClient.Open(VoicePackageDirectory()),760,484,180,39));
            page.Controls.Add(WebText("高级导出会先选择训练模型目录；导入会校验模型与环境指纹，不兼容时不覆盖当前声音。",8.8F,FontStyle.Regular,38,536,902,40,ContentAlignment.TopLeft));
        }
        void PackageIcon(ScenePanel card,int y){
            var icon=new Panel{BackColor=Mix(CardColor,ForeColor,8),Bounds=new Rectangle(16,y+24,42,42),Margin=new Padding(0)};
            icon.Controls.Add(WebText("♪",16F,FontStyle.Bold,0,0,42,42,ContentAlignment.MiddleCenter));
            card.Controls.Add(icon);
        }
        async void ExportMode(string mode){try{await ChooseAndExport(mode);}catch(Exception error){StatusError(error.Message);}}
        async Task ChooseAndExport(string mode){
            string path=null;
            string name=SafeProfileName(PackageDisplayName(modelPackageName.Text));if(name=="未命名配置")throw new InvalidOperationException("请先填写导出音色名称。");
            string label=PackageModeLabel(mode);
            string trained="";
            if(mode=="trained"||mode=="trained-full")using(var folder=new FolderBrowserDialog{Description="选择训练完成的完整 CosyVoice3 模型目录",ShowNewFolderButton=false,SelectedPath=TrainingWorkspace()}){if(folder.ShowDialog(this)!=DialogResult.OK)return;trained=folder.SelectedPath;}
            string packageDirectory=Path.Combine(VoicePackageDirectory(),name);Directory.CreateDirectory(packageDirectory);
            string suffix=SelectedEngine()=="gpt-sovits"?".gpt-sovits.ttsmodel":".cosyvoice.ttsmodel";
            if(path==null)using(var dialog=new SaveFileDialog{Filter="当前引擎音色包|*"+suffix,DefaultExt=suffix.Substring(1),AddExtension=false,FileName=name+"-"+label+suffix,InitialDirectory=packageDirectory}){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;path=NormalizePackagePath(dialog.FileName);
            }
            await ExportConfiguration(path,mode,trained);
        }
        async Task ExportConfiguration(string path,string mode,string trained=""){
            await SaveDraft();
            Status("正在校验并导出"+PackageModeLabel(mode)+"……");
            string args="export --root "+ServiceClient.Quote(Store.Root)+" --file "+ServiceClient.Quote(path)+" --package-mode "+mode+(trained.Length>0?" --trained-model-dir "+ServiceClient.Quote(trained):"");
            lastTransfer=Json.Object(await Service.RunTool("tts_bundle.py",args));
            Status(PackageModeLabel(mode)+"已导出 · "+path);
        }
        string PackageModeLabel(string mode){if(mode=="lightweight")return "轻量包";if(mode=="full")return "模型完整包";if(mode=="trained")return "高级训练包";if(mode=="trained-full")return "高级完整包";return "音色包";}
        string PackageDisplayName(string value){value=(value??"").Trim();foreach(string suffix in new[]{".cosyvoice.ttsmodel",".gpt-sovits.ttsmodel"})while(value.EndsWith(suffix,StringComparison.OrdinalIgnoreCase))value=value.Substring(0,value.Length-suffix.Length).Trim();foreach(string label in new[]{"-高级完整包","-高级训练包","-模型完整包","-参考完整包","-轻量包","-完整","-轻量"})if(value.EndsWith(label,StringComparison.OrdinalIgnoreCase))value=value.Substring(0,value.Length-label.Length).Trim();return value;}
        string NormalizePackagePath(string path){string suffix=SelectedEngine()=="gpt-sovits"?".gpt-sovits.ttsmodel":".cosyvoice.ttsmodel";string value=Path.GetFullPath(path);foreach(string known in new[]{".cosyvoice.ttsmodel",".gpt-sovits.ttsmodel"})while(value.EndsWith(known,StringComparison.OrdinalIgnoreCase))value=value.Substring(0,value.Length-known.Length);return value+suffix;}
        async Task<string> KeepModelPackage(string path,string displayName){string source=Path.GetFullPath(path),library=Path.GetFullPath(VoicePackageDirectory()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;if(source.StartsWith(library,StringComparison.OrdinalIgnoreCase))return source;string folder=Path.Combine(VoicePackageDirectory(),SafeProfileName(PackageDisplayName(displayName)));Directory.CreateDirectory(folder);string target=Path.Combine(folder,Path.GetFileName(source));if(!source.Equals(target,StringComparison.OrdinalIgnoreCase)){Status("正在把音色包登记到程序专用文件夹……");await Task.Run(()=>File.Copy(source,target,true));}return target;}
        async Task ImportConfiguration(string path){
            bool running=await Service.Running();
            if(running){var health=await Service.Request("/health");if(Convert.ToBoolean(health["busy"])||Convert.ToInt32(health["queued"])>0)throw new InvalidOperationException("请等待当前朗读队列完成，再导入配置。");await Service.Stop();}
            Exception failure=null;
            try{
                Status("正在校验音色包并匹配本地 TTS 引擎……");
                lastTransfer=Json.Object(await Service.RunTool("tts_bundle.py","import --root "+ServiceClient.Quote(Store.Root)+" --file "+ServiceClient.Quote(path)));
                await KeepModelPackage(path,Json.Text(lastTransfer,"name"));
                Config=Store.Read();BindVoice();BindSettings();ApplyTheme();ApplyIcon();lastJob="";deleted=null;
                string kind=PackageModeLabel(Json.Text(lastTransfer,"packageMode"));
                string success=kind+"“"+Json.Text(lastTransfer,"name")+"”已导入并匹配本地 TTS 引擎 · 原配置备份："+Json.Text(lastTransfer,"backup");
                object rawWarnings;var warningItems=lastTransfer.TryGetValue("warnings",out rawWarnings)?rawWarnings as object[]:null;
                if(warningItems!=null&&warningItems.Length>0){
                    string warning=string.Join("\r\n",Array.ConvertAll(warningItems,Convert.ToString));
                    Status(success+" · 有兼容性提醒，请查看弹窗。");
                    MessageBox.Show(this,"音色包已经导入，但运行环境发生了兼容调整：\r\n\r\n"+warning+"\r\n\r\n建议立即运行“环境自检”并试听。","音色包兼容性提醒",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                }else Status(success);
            }catch(Exception error){failure=error;}
            try{if(running)await Service.Start();}catch(Exception error){if(failure!=null)throw new InvalidOperationException(failure.Message+"；服务恢复失败："+error.Message);throw;}
            await Poll();if(failure!=null)throw new InvalidOperationException(failure.Message,failure);
        }
    }
}
