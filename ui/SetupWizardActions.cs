using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentVoice {
    internal sealed partial class SetupWizard {
        void BuildReference(){
            var group=new BufferedFlow{FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,BackColor=BackColor,Visible=false,Padding=new Padding(0,8,12,0)};choices[4].Controls.Add(group);
            group.Controls.Add(new Label{Text="参考录音与逐字一致的台词",AutoSize=true,Margin=new Padding(0,0,0,8)});
            reference=new TextBox{Text=Flow.Config.voice.reference,Width=650,Height=30,BackColor=Color.FromArgb(30,31,40),ForeColor=ForeColor,AccessibleName="推理参考录音路径"};transcript=new TextBox{Text=Flow.Config.voice.referenceText,Width=650,Height=90,Multiline=true,ScrollBars=ScrollBars.Vertical,BackColor=reference.BackColor,ForeColor=ForeColor,AccessibleName="参考录音逐字台词"};group.Controls.Add(reference);
            var browse=Button("选择参考音频",()=>{using(var d=new OpenFileDialog{Title="选择参考音频",Filter="音频|*.wav;*.flac;*.mp3;*.m4a",CheckFileExists=true})if(d.ShowDialog(this)==DialogResult.OK)reference.Text=d.FileName;},false);group.Controls.Add(browse);group.Controls.Add(transcript);
            group.Controls.Add(Button("保存参考声音",async()=>await Work(async()=>{if(!File.Exists(Flow.Store.Resolve(reference.Text.Trim())))throw new InvalidOperationException("参考音频不存在，请重新选择。");if(string.IsNullOrWhiteSpace(transcript.Text))throw new InvalidOperationException("请填写与录音逐字一致的台词。");var c=Flow.Config;c.voice.reference=Flow.Store.Portable(reference.Text.Trim());c.voice.referenceText=transcript.Text;await Flow.Save(c,new{voice=c.voice});Flow.InvalidateVoice();voiceReport.Text=VoiceDescription();}),true));
            group.SizeChanged+=(s,e)=>{reference.Width=transcript.Width=Math.Max(200,group.Width-28);};
        }
        async Task InstallModule(string kind){
            using(var d=new OpenFileDialog{Title=kind=="engine"?"选择 TTS 引擎包":"选择离线环境包",Filter=kind=="engine"?"TTS 引擎包|*.ttsengine":"离线环境包|*.ttsenvironment",CheckFileExists=true}){
                if(d.ShowDialog(this)!=DialogResult.OK)return;
                var package=ModulePackages.Inspect(d.FileName);if(package.kind!=kind)throw new InvalidOperationException("模块类型与当前步骤不匹配。");
                if(MessageBox.Show(this,package.name+"\r\n平台："+package.platform+"\r\n运行时："+package.profile+"\r\n展开体积："+(package.files.Sum(f=>f.size)/1073741824d).ToString("0.00")+" GB\r\n安装位置："+Flow.Store.Root+"\r\n\r\n模块含可执行代码，请只使用可信来源。已有文件会保留回退副本。","安装所选模块",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)!=DialogResult.OK)return;
                await Work(async()=>{await Flow.InstallModule(d.FileName,kind,SetStatus);environmentReport.Text=Flow.Resources()+"\r\n模块已安装，尚需实际生成与播放验证。";voiceReport.Text=VoiceDescription();});
            }
        }
        async Task StopService(){await Work(async()=>{if(await Flow.Service.Running())await Flow.Service.Stop();environmentReport.Text=Flow.Resources()+"\r\n语音服务已停止，可以安装模块。";});}
        async Task ImportVoice(){using(var d=new OpenFileDialog{Title="导入训练音色包",Filter="音色包|*.cosyvoice.ttsmodel;*.gpt-sovits.ttsmodel",InitialDirectory=Flow.Store.Resolve("voice-model-packages"),CheckFileExists=true}){if(d.ShowDialog(this)!=DialogResult.OK)return;await Work(async()=>{voiceReport.Text=await Flow.ImportVoice(d.FileName);SyncEngine();});}}
        async Task ChooseSavedVoice(){
            var c=Flow.Config;if(c.profiles.Count==0){voiceReport.Text="尚无已保存的声音。可导入音色包，或展开参考声音配置。";return;}
            using(var dialog=Dialog("选择已保存的声音")){
                var list=new ListBox{Dock=DockStyle.Fill,DataSource=c.profiles,BackColor=Color.FromArgb(30,31,40),ForeColor=ForeColor,Font=Font};dialog.Controls.Add(list);var apply=Button("使用选中声音",()=>dialog.DialogResult=DialogResult.OK,true);apply.Dock=DockStyle.Bottom;dialog.Controls.Add(apply);if(dialog.ShowDialog(this)!=DialogResult.OK||list.SelectedItem==null)return;var selected=list.SelectedItem as SavedVoice;
                await Work(async()=>{if(await Flow.Service.Running())await Flow.Service.Request("/api/profiles/apply",new{id=selected.id},300000);else{c=Flow.Config;c.voice=selected.voice.Clone();Flow.Store.Write(c);}Flow.InvalidateVoice();voiceReport.Text="已选择："+selected.name+"。请在下一步试听。";SyncEngine();});
            }
        }
        async Task EditEnginePaths(){
            using(var dialog=Dialog("指定已有引擎与环境")){
                var c=Flow.Config;var viewport=new Panel{Dock=DockStyle.Fill,AutoScroll=true,BackColor=BackColor};dialog.Controls.Add(viewport);var fields=new BufferedTable{Dock=DockStyle.Top,AutoSize=true,ColumnCount=3,BackColor=BackColor};fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,130));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,70));viewport.Controls.Add(fields);var boxes=new System.Collections.Generic.Dictionary<string,TextBox>();
                foreach(var entry in new[]{new[]{"pythonService","主服务 Python","file"},new[]{"pythonTts","CosyVoice Python","file"},new[]{"cosyRepo","CosyVoice 源码","folder"},new[]{"cosyModel","CosyVoice 模型","folder"},new[]{"pythonGpt","GPT-SoVITS Python","file"},new[]{"gptRepo","GPT 源码","folder"},new[]{"gptModel","GPT 模型","folder"},new[]{"ffmpeg","FFmpeg","file"}}){var item=entry;int row=fields.RowCount++;fields.RowStyles.Add(new RowStyle(SizeType.Absolute,58));var box=new TextBox{Text=Convert.ToString(typeof(Engines).GetField(item[0]).GetValue(c.engines)),Dock=DockStyle.Fill,BackColor=Color.FromArgb(30,31,40),ForeColor=ForeColor,Margin=new Padding(0,13,5,13)};boxes[item[0]]=box;fields.Controls.Add(new Label{Text=item[1],Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row);fields.Controls.Add(box,1,row);var browse=Button("选择",()=>{if(item[2]=="folder"){using(var d=new FolderBrowserDialog{ShowNewFolderButton=false,Description=item[1]})if(d.ShowDialog(dialog)==DialogResult.OK)box.Text=Flow.Store.Portable(d.SelectedPath);}else using(var d=new OpenFileDialog{Title=item[1],CheckFileExists=true})if(d.ShowDialog(dialog)==DialogResult.OK)box.Text=Flow.Store.Portable(d.FileName);},false);browse.Dock=DockStyle.Fill;browse.MinimumSize=Size.Empty;browse.Margin=new Padding(0,5,0,5);fields.Controls.Add(browse,2,row);}
                var save=Button("保存路径",()=>dialog.DialogResult=DialogResult.OK,true);save.Dock=DockStyle.Bottom;dialog.Controls.Add(save);if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                await Work(async()=>{if(await Flow.Service.Running())throw new InvalidOperationException("请先在环境步骤停止服务，再修改环境路径。");foreach(var field in boxes)typeof(Engines).GetField(field.Key).SetValue(c.engines,field.Value.Text.Trim());Flow.Store.Write(c);Flow.InvalidateVoice();environmentReport.Text=Flow.Resources();});
            }
        }
        Form Dialog(string title){return new Form{Text=title,Size=new Size(820,630),MinimumSize=new Size(640,480),Font=Font,BackColor=BackColor,ForeColor=ForeColor,StartPosition=FormStartPosition.CenterParent,ShowInTaskbar=false,Padding=new Padding(18)};}
        async Task Connect(bool airi){
            using(var d=new FolderBrowserDialog{Description=airi?"选择已经登录、包含 airi.exe 的原 AIRI 安装目录":"选择 Codex 或 DeepSeek Harness 安装目录／用户配置目录",ShowNewFolderButton=false}){
                if(d.ShowDialog(this)!=DialogResult.OK)return;
                await Work(async()=>{await Flow.EnsureService();var connector=new ConnectionSetup(Flow);connector.Report=text=>{SetStatus(text);if(airi)airiReport.Text=text;else connectionReport.Text=text;};connector.ManualPrompt=text=>{manualPrompt.Text=text;manualPrompt.Visible=true;Clipboard.SetText(text);};if(airi)await connector.Airi(d.SelectedPath);else await connector.Harness(d.SelectedPath);Celebrate(airi?"AIRI":"Harness");});
            }
        }
        void Celebrate(string source){using(var snapshot=CaptureFrame(this)){var celebration=new Celebration(source+" · 恭喜接入成功",new Bitmap(snapshot)){Dock=DockStyle.Fill};Controls.Add(celebration);celebration.BringToFront();}}
    }
}
