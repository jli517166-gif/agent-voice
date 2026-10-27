using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentVoice {
    internal sealed partial class SetupWizard {
        async Task InstallModule(string kind){
            using(var d=new OpenFileDialog{Title=kind=="engine"?"选择 TTS 引擎包":"选择离线环境包",Filter=kind=="engine"?"TTS 引擎包|*.ttsengine":"离线环境包|*.ttsenvironment",CheckFileExists=true}){
                if(d.ShowDialog(this)!=DialogResult.OK)return;
                var package=ModulePackages.Inspect(d.FileName);if(package.kind!=kind)throw new InvalidOperationException("模块类型与当前步骤不匹配。");
                if(MessageBox.Show(this,package.name+"\r\n平台："+package.platform+"\r\n运行时："+package.profile+"\r\n展开体积："+(package.files.Sum(f=>f.size)/1073741824d).ToString("0.00")+" GB\r\n安装位置："+Flow.Store.Root+"\r\n\r\n模块含可执行代码，请只使用可信来源。已有文件会保留回退副本。","安装所选模块",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)!=DialogResult.OK)return;
                string[] steps=kind=="engine"?new[]{"确认引擎包类型","展开源码与运行时","校验清单与模型"}:new[]{"确认环境包类型","展开依赖与工具","校验运行时"};
                await RunTask(kind=="engine"?"准备引擎文件":"补齐运行环境",steps,async p=>{await Flow.InstallModule(d.FileName,kind,p.Report);return Flow.Resources()+"\r\n模块已安装，可在控制台生成语音。";},text=>{(kind=="engine"?resourcesReport:environmentReport).Text=text;});
            }
        }
        async Task StopService(){await Work(async()=>{if(await Flow.Service.Running())await Flow.Service.Stop();environmentReport.Text=Flow.Resources()+"\r\n语音服务已停止，可以安装模块。";});}
        async Task EditEnginePaths(){
            using(var dialog=Dialog("指定已有引擎与环境")){
                var c=Flow.Config;var viewport=new Panel{Dock=DockStyle.Fill,AutoScroll=true,BackColor=BackColor};dialog.Controls.Add(viewport);var fields=new BufferedTable{Dock=DockStyle.Top,AutoSize=true,ColumnCount=3,BackColor=BackColor};fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,130));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,70));viewport.Controls.Add(fields);var boxes=new System.Collections.Generic.Dictionary<string,TextBox>();
                foreach(var entry in new[]{new[]{"pythonService","主服务 Python","file"},new[]{"pythonTts","CosyVoice Python","file"},new[]{"cosyRepo","CosyVoice 源码","folder"},new[]{"cosyModel","CosyVoice 模型","folder"},new[]{"pythonGpt","GPT-SoVITS Python","file"},new[]{"gptRepo","GPT 源码","folder"},new[]{"gptModel","GPT 模型","folder"},new[]{"ffmpeg","FFmpeg","file"}}){var item=entry;int row=fields.RowCount++;fields.RowStyles.Add(new RowStyle(SizeType.Absolute,58));var box=new TextBox{Text=Convert.ToString(typeof(Engines).GetField(item[0]).GetValue(c.engines)),Dock=DockStyle.Fill,BackColor=Color.FromArgb(30,31,40),ForeColor=ForeColor,Margin=new Padding(0,13,5,13)};boxes[item[0]]=box;fields.Controls.Add(new Label{Text=item[1],Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row);fields.Controls.Add(box,1,row);var browse=Button("选择",()=>{if(item[2]=="folder"){using(var d=new FolderBrowserDialog{ShowNewFolderButton=false,Description=item[1]})if(d.ShowDialog(dialog)==DialogResult.OK)box.Text=Flow.Store.Portable(d.SelectedPath);}else using(var d=new OpenFileDialog{Title=item[1],CheckFileExists=true})if(d.ShowDialog(dialog)==DialogResult.OK)box.Text=Flow.Store.Portable(d.FileName);},false);browse.Dock=DockStyle.Fill;browse.MinimumSize=Size.Empty;browse.Margin=new Padding(0,5,0,5);fields.Controls.Add(browse,2,row);}
                var save=Button("保存路径",()=>dialog.DialogResult=DialogResult.OK,true);save.Dock=DockStyle.Bottom;dialog.Controls.Add(save);if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                await Work(async()=>{if(await Flow.Service.Running())throw new InvalidOperationException("请先在环境步骤停止服务，再修改环境路径。");foreach(var field in boxes)typeof(Engines).GetField(field.Key).SetValue(c.engines,field.Value.Text.Trim());Flow.Store.Write(c);Flow.InvalidateVoice();resourcesReport.Text=Flow.Resources();});
            }
        }
        Form Dialog(string title){return new Form{Text=title,Size=new Size(820,630),MinimumSize=new Size(640,480),Font=Font,BackColor=BackColor,ForeColor=ForeColor,StartPosition=FormStartPosition.CenterParent,ShowInTaskbar=false,Padding=new Padding(18)};}
        async Task Connect(bool airi){
            using(var d=new FolderBrowserDialog{Description=airi?"选择已经登录、包含 airi.exe 的原 AIRI 安装目录":"选择 Codex 或 DeepSeek Harness 安装目录／用户配置目录",ShowNewFolderButton=false}){
                if(d.ShowDialog(this)!=DialogResult.OK)return;
                await Work(async()=>{if(airi)await Flow.EnsureService();var connector=new ConnectionSetup(Flow);connector.Report=text=>{SetStatus(text);if(airi)airiReport.Text=text;else connectionReport.Text=text;};if(airi){await connector.Airi(d.SelectedPath);Celebrate("AIRI");}else{var result=await connector.Harness(d.SelectedPath);connectionReport.Text=Json.Text(result,"message");if(result.ContainsKey("passed")&&Convert.ToBoolean(result["passed"]))Celebrate("MCP");}});
            }
        }
        async Task CheckMcp(string source){await Work(async()=>{var result=await new ConnectionSetup(Flow).CheckMcp(source);connectionReport.Text=Json.Text(result,"message");if(result.ContainsKey("passed")&&Convert.ToBoolean(result["passed"]))Celebrate(source+" MCP");});}
        void Celebrate(string source){using(var snapshot=CaptureFrame(this)){var celebration=new Celebration(source+" · 恭喜接入成功",new Bitmap(snapshot)){Dock=DockStyle.Fill};Controls.Add(celebration);celebration.BringToFront();}}
    }
}
