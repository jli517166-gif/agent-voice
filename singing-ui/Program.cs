using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AgentSinging {
    static class Program {
        [STAThread] static void Main(){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new SingingForm());}
    }

    sealed class SingingForm:Form {
        readonly string root=ResolveRoot();
        readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=16*1024*1024};
        readonly ComboBox engines=new ComboBox();readonly Label engineState=new Label(),output=new Label(),status=new Label();
        readonly TextBox prompt=new TextBox(),lyrics=new TextBox();readonly NumericUpDown duration=new NumericUpDown();readonly Button generate=new Button();
        readonly Timer poll=new Timer{Interval=1000};string currentJob="";
        static readonly Color Back=Color.FromArgb(13,15,20),Panel=Color.FromArgb(24,27,35),Ink=Color.FromArgb(245,246,250),Muted=Color.FromArgb(171,175,190),Accent=Color.FromArgb(169,137,255);
        static string ResolveRoot(){
            var args=Environment.GetCommandLineArgs();
            for(int i=1;i+1<args.Length;i++)if(args[i]=="--root"&&File.Exists(Path.Combine(args[i+1],"data","settings.json")))return Path.GetFullPath(args[i+1]);
            string current=AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            for(int i=0;i<4;i++){if(File.Exists(Path.Combine(current,"data","settings.json")))return current;current=Path.GetDirectoryName(current);if(string.IsNullOrEmpty(current))break;}
            return AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        }

        internal SingingForm(){
            Text="Agent Singing · 独立唱歌工作台";BackColor=Back;ForeColor=Ink;MinimumSize=new Size(920,680);Size=new Size(1060,760);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",10F);
            var title=TextLabel("AGENT SINGING",26,FontStyle.Bold);title.Location=new Point(38,28);title.Size=new Size(500,48);Controls.Add(title);
            var subtitle=TextLabel("歌词、风格与引擎，各自独立。歌曲只保存到你选择的位置。",10,FontStyle.Regular);subtitle.ForeColor=Muted;subtitle.Location=new Point(42,78);subtitle.Size=new Size(760,28);Controls.Add(subtitle);
            var top=Card(new Rectangle(38,120,984,126));Controls.Add(top);
            top.Controls.Add(TextLabelAt("唱歌引擎",18,18,94,28));engines.DropDownStyle=ComboBoxStyle.DropDownList;engines.SetBounds(18,52,280,38);StyleInput(engines);top.Controls.Add(engines);
            top.Controls.Add(AsyncButtonAt("刷新",314,52,86,38,async()=>await RefreshEngines()));top.Controls.Add(AsyncButtonAt("启动 / 打开引擎",414,52,168,38,async()=>await OpenEngine()));
            engineState.SetBounds(604,18,350,76);engineState.ForeColor=Muted;engineState.TextAlign=ContentAlignment.MiddleLeft;top.Controls.Add(engineState);
            engines.SelectedIndexChanged+=async(s,e)=>{var item=engines.SelectedItem as EngineChoice;if(item!=null)await RunSafe(async()=>{await Call("select",new Dictionary<string,object>{{"engine",item.Id}});await RefreshEngines();});};
            var left=Card(new Rectangle(38,266,614,392));Controls.Add(left);left.Controls.Add(TextLabelAt("写下这首歌",22,18,260,34));
            left.Controls.Add(TextLabelAt("风格描述",18,68,100,24));prompt.Multiline=true;prompt.SetBounds(18,96,578,72);prompt.Text="清亮女声，梦幻电子流行，咬字清楚，旋律完整";StyleInput(prompt);left.Controls.Add(prompt);
            left.Controls.Add(TextLabelAt("歌词",18,184,100,24));lyrics.Multiline=true;lyrics.ScrollBars=ScrollBars.Vertical;lyrics.SetBounds(18,212,578,158);lyrics.Text="[Verse]\r\n晚风穿过屏幕的光\r\n让沉默也学会歌唱";StyleInput(lyrics);left.Controls.Add(lyrics);
            var right=Card(new Rectangle(674,266,348,392));Controls.Add(right);right.Controls.Add(TextLabelAt("生成设置",22,18,220,34));
            right.Controls.Add(TextLabelAt("目标时长（秒）",18,74,150,24));duration.SetBounds(18,104,120,36);duration.Minimum=5;duration.Maximum=300;duration.Value=30;StyleInput(duration);right.Controls.Add(duration);
            right.Controls.Add(TextLabelAt("歌曲保存位置",18,160,180,24));output.SetBounds(18,190,312,66);output.ForeColor=Muted;output.AutoEllipsis=true;right.Controls.Add(output);
            right.Controls.Add(ButtonAt("选择文件夹",18,266,130,38,ChooseOutput));right.Controls.Add(ButtonAt("打开文件夹",160,266,130,38,OpenOutput));
            generate=AsyncButtonAt("生成歌曲",18,322,272,48,async()=>await Generate());generate.BackColor=Accent;generate.ForeColor=Color.FromArgb(15,13,20);generate.Font=new Font(Font.FontFamily,11F,FontStyle.Bold);right.Controls.Add(generate);
            status.SetBounds(42,676,970,42);status.ForeColor=Muted;status.Text="正在读取唱歌引擎…";Controls.Add(status);
            poll.Tick+=async(s,e)=>await Poll();Shown+=async(s,e)=>await RefreshEngines();Resize+=delegate{LayoutCards();};
        }
        void LayoutCards(){int w=ClientSize.Width;Controls[2].Width=w-76;Controls[3].Width=(int)((w-98)*.63);Controls[4].Left=Controls[3].Right+22;Controls[4].Width=w-Controls[4].Left-38;status.Top=ClientSize.Height-62;}
        Panel Card(Rectangle bounds){return new Panel{Bounds=bounds,BackColor=Panel,Padding=new Padding(18)};}
        Label TextLabel(string text,float size,FontStyle style){return new Label{Text=text,ForeColor=Ink,Font=new Font(Font.FontFamily,size,style),AutoSize=false};}
        Label TextLabelAt(string text,int x,int y,int w,int h){var l=TextLabel(text,10F,FontStyle.Bold);l.SetBounds(x,y,w,h);return l;}
        void StyleInput(Control c){c.BackColor=Color.FromArgb(17,19,25);c.ForeColor=Ink;c.Font=new Font(Font.FontFamily,10F);}
        Button ButtonAt(string text,int x,int y,int w,int h,Action action){var b=new Button{Text=text,Bounds=new Rectangle(x,y,w,h),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(39,42,52),ForeColor=Ink,Cursor=Cursors.Hand};b.FlatAppearance.BorderSize=0;b.Click+=(s,e)=>action();return b;}
        Button AsyncButtonAt(string text,int x,int y,int w,int h,Func<Task> action){return ButtonAt(text,x,y,w,h,()=>{var ignored=action();});}
        async Task<Dictionary<string,object>> Call(string action,Dictionary<string,object> payload=null){
            string python=Path.Combine(root,"runtime","core","python.exe"),tool=Path.Combine(root,"tools","singing_control.py");
            if(!File.Exists(python))throw new InvalidOperationException("缺少主服务 Python，请先在 Agent Voice 补齐环境。\r\n"+python);
            if(!File.Exists(tool))throw new InvalidOperationException("缺少唱歌控制组件："+tool);
            var info=new ProcessStartInfo(python,"-I -X utf8 \""+tool+"\" "+action+" --root \""+root+"\" --stdin"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            return await Task.Run(()=>{using(var p=Process.Start(info)){p.StandardInput.Write(json.Serialize(payload??new Dictionary<string,object>()));p.StandardInput.Close();string text=p.StandardOutput.ReadToEnd(),error=p.StandardError.ReadToEnd();p.WaitForExit();if(p.ExitCode!=0)throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)?text:error);return json.Deserialize<Dictionary<string,object>>(text);}});
        }
        async Task RunSafe(Func<Task> work){try{UseWait(true);await work();}catch(Exception e){MessageBox.Show(this,e.Message,"操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);status.Text=e.Message;}finally{UseWait(false);}}
        void UseWait(bool value){UseWaitCursor=value;generate.Enabled=!value;}
        async Task RefreshEngines(){await RunSafe(async()=>{var result=await Call("list");output.Text=Convert.ToString(result["outputDirectory"]);var rows=result["engines"] as System.Collections.ArrayList;string selected=Convert.ToString(result["activeEngine"]);engines.Items.Clear();if(rows!=null)foreach(Dictionary<string,object> row in rows){var item=new EngineChoice(row);engines.Items.Add(item);if(item.Id==selected)engines.SelectedItem=item;}var active=engines.SelectedItem as EngineChoice;engineState.Text=active==null?"没有可用引擎":active.Status;status.Text="独立唱歌软件已就绪。";});}
        async Task OpenEngine(){await RunSafe(async()=>{var result=await Call("open");status.Text=Convert.ToString(result["message"]);});}
        void ChooseOutput(){using(var dialog=new FolderBrowserDialog{Description="选择歌曲保存位置",SelectedPath=output.Text})if(dialog.ShowDialog(this)==DialogResult.OK){var ignored=RunSafe(async()=>{var result=await Call("output",new Dictionary<string,object>{{"path",dialog.SelectedPath}});output.Text=Convert.ToString(result["outputDirectory"]);status.Text="歌曲保存位置已更新。";});}}
        void OpenOutput(){if(Directory.Exists(output.Text))Process.Start("explorer.exe","\""+output.Text+"\"");}
        async Task Generate(){await RunSafe(async()=>{var result=await Call("generate",new Dictionary<string,object>{{"lyrics",lyrics.Text},{"prompt",prompt.Text},{"duration",Convert.ToInt32(duration.Value)}});currentJob=Convert.ToString(result["jobId"]);status.Text="任务已提交 · "+currentJob.Substring(0,8)+" · 正在生成";poll.Start();});}
        async Task Poll(){if(string.IsNullOrEmpty(currentJob))return;try{var result=await Call("job",new Dictionary<string,object>{{"job",currentJob}});string state=Convert.ToString(result["state"]);status.Text="歌曲任务 · "+state;if(state=="completed"){poll.Stop();status.Text="生成完成："+Convert.ToString(result["output"]);}else if(state=="failed"){poll.Stop();status.Text="生成失败："+Convert.ToString(result["error"]);}}catch(Exception e){poll.Stop();status.Text=e.Message;}}
        sealed class EngineChoice {internal readonly string Id,Status,Name;internal EngineChoice(Dictionary<string,object> row){Id=Convert.ToString(row["id"]);Name=Convert.ToString(row["name"]);bool installed=Convert.ToBoolean(row["installed"]),online=Convert.ToBoolean(row["online"]);Status=Name+"\r\n"+(installed?(online?"引擎在线，可生成":"已安装，尚未启动"):"未安装")+"\r\n"+Convert.ToString(row["root"]);}public override string ToString(){return Name;}}
    }
}
