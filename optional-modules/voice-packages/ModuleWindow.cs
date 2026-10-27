using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using AgentVoice;

namespace VoicePackages {
    static class Program {
        [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
        [STAThread]static int Main(string[] args){
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling",false);AppContext.SetSwitch("Switch.System.IO.BlockLongPaths",false);SetProcessDPIAware();
            int index=Array.IndexOf(args,"--root");if(index<0||index+1>=args.Length)return 2;
            string root=Path.GetFullPath(args[index+1]);string core=Path.Combine(root,"agent语音.exe");if(!File.Exists(core))return 2;
            AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>new AssemblyName(e.Name).Name=="agent-voice-build"?Assembly.LoadFrom(core):null;
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            try{return Run(root,args);}catch(Exception error){int test=Array.IndexOf(args,"--selftest");if(test>=0&&test+1<args.Length){File.WriteAllText(args[test+1]+".error.txt",error.ToString(),Encoding.UTF8);return 1;}MessageBox.Show(error.Message,"音色包模块无法打开");return 1;}
        }
        [MethodImpl(MethodImplOptions.NoInlining)]static int Run(string root,string[] args){
            var store=new SettingsStore(root);ModulePackages.InstalledFeature(store,"voice-packages");
            if(!Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\').Equals(store.Resolve("modules/voice-packages").TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("请先从主程序安装该模块，再从模块页打开。");
            using(var form=new ModuleWindow(root)){
                int test=Array.IndexOf(args,"--selftest");
                if(test>=0&&test+1<args.Length){form.Show();Application.DoEvents();string report=args[test+1];Directory.CreateDirectory(Path.GetDirectoryName(report));File.WriteAllText(report,Json.Encode(form.Verify()),Encoding.UTF8);using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.ChangeExtension(report,".png"));}form.Close();return 0;}
                Application.Run(form);return 0;
            }
        }
    }
    sealed class ModuleWindow:Form {
        readonly SettingsStore store;readonly ServiceClient service;
        readonly TextBox name;readonly ComboBox mode;readonly Label current,explanation,status;
        readonly FlowLayoutPanel actions;readonly Timer motion=new Timer{Interval=100};readonly Panel waveform;
        readonly ListBox voices;readonly TableLayoutPanel content;
        readonly string backend=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"backend/module_cli.py");
        bool busy;int frame;
        static readonly string[] Modes={"lightweight","full","trained","trained-full"};
        static readonly string[] Labels={"轻量包","模型完整包","高级训练包","高级完整包"};
        static readonly string[] Details={"参考录音和参数；目标电脑必须已有完全匹配的基础模型。","参考录音、参数和当前模型权重；不包含 Python 或 TTS 引擎源码。","参考录音、参数和训练后改变的权重；目标电脑需要匹配的基础模型。","参考录音、参数和完整训练模型；不包含 Python 或 TTS 引擎源码。"};
        internal ModuleWindow(string root){
            SuspendLayout();store=new SettingsStore(root);service=new ServiceClient(store);Text="音色包 · 导入与导出";Font=new Font("Microsoft YaHei UI",11F);
            ClientSize=new Size(920,650);MinimumSize=new Size(760,640);StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(16,17,22);ForeColor=Color.FromArgb(235,237,245);Padding=new Padding(28);DoubleBuffered=true;
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,74));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));Controls.Add(layout);
            var heading=new Panel{Dock=DockStyle.Fill};heading.Controls.Add(new Label{Text="让喜欢的声音，留在这里。",AutoSize=true,Font=new Font(Font.FontFamily,23F,FontStyle.Bold),Location=new Point(0,0)});heading.Controls.Add(new Label{Text="导入后自动应用，并加入主程序的声音方案。",AutoSize=true,Location=new Point(1,51),ForeColor=Color.FromArgb(178,181,197)});layout.Controls.Add(heading,0,0);
            content=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Margin=Padding.Empty};content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(float height in new[]{32,46,0,46,165,46})content.RowStyles.Add(new RowStyle(height==0?SizeType.Percent:SizeType.Absolute,height==0?100:height));layout.Controls.Add(content,0,1);
            current=new Label{Dock=DockStyle.Fill,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft};content.Controls.Add(current,0,0);
            actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};content.Controls.Add(actions,0,1);
            AddAction("导入音色包",Import,true);AddAction("打开包文件夹",()=>{ServiceClient.Open(Library());return Task.FromResult(0);});AddAction("刷新列表",()=>{RefreshVoice();return Task.FromResult(0);});
            voices=new ListBox{Name="installedVoices",Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,BackColor=Color.FromArgb(28,29,37),ForeColor=ForeColor,IntegralHeight=false,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=48,Margin=new Padding(0,8,0,8)};
            voices.DrawItem+=(s,e)=>{if(e.Index<0)return;var item=voices.Items[e.Index] as SavedVoice;bool selected=(e.State&DrawItemState.Selected)!=0;using(var brush=new SolidBrush(selected?Color.FromArgb(57,50,78):voices.BackColor))e.Graphics.FillRectangle(brush,e.Bounds);TextRenderer.DrawText(e.Graphics,item.name+"   ·   "+EngineName(item.voice),Font,new Rectangle(e.Bounds.X+14,e.Bounds.Y,e.Bounds.Width-28,e.Bounds.Height),ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};content.Controls.Add(voices,0,2);
            var selection=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};content.Controls.Add(selection,0,3);AddAction("应用选中声音",async()=>{var item=voices.SelectedItem as SavedVoice;if(item==null){status.Text="还没有选择声音。先导入音色包，或保存当前已应用的声音。";return;}await Transfer("apply",null,null,null," --profile-id "+ServiceClient.Quote(item.id));RefreshVoice();status.Text="已应用："+item.name+"。返回控制台即可生成并播放。";},false,selection);
            AddAction("保存当前声音",async()=>{await Transfer("register-current",null,null,null," --name "+ServiceClient.Quote(CleanName(name.Text)));RefreshVoice();status.Text="当前声音已保存到列表，也可在主程序快捷切换。";},false,selection);
            var export=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=4,BackColor=Color.FromArgb(25,26,33),Padding=new Padding(12),Margin=new Padding(0,12,0,6)};export.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));export.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));foreach(int h in new[]{26,34,34,44})export.RowStyles.Add(new RowStyle(SizeType.Absolute,h));content.Controls.Add(export,0,4);
            export.Controls.Add(new Label{Text="导出名称",Dock=DockStyle.Fill},0,0);export.Controls.Add(new Label{Text="导出类型",Dock=DockStyle.Fill},1,0);
            name=new TextBox{Name="packageName",AccessibleName="导出名称",Text="我的声音",Dock=DockStyle.Top,BackColor=Color.FromArgb(34,35,44),ForeColor=ForeColor,BorderStyle=BorderStyle.FixedSingle,Margin=new Padding(0,0,16,0)};export.Controls.Add(name,0,1);
            mode=new ComboBox{Name="packageMode",DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top,BackColor=Color.FromArgb(34,35,44),ForeColor=ForeColor,DrawMode=DrawMode.OwnerDrawFixed,FlatStyle=FlatStyle.Flat};mode.DrawItem+=(s,e)=>{using(var brush=new SolidBrush((e.State&DrawItemState.Selected)!=0?Color.FromArgb(57,50,78):mode.BackColor))e.Graphics.FillRectangle(brush,e.Bounds);if(e.Index>=0)TextRenderer.DrawText(e.Graphics,Convert.ToString(mode.Items[e.Index]),Font,new Rectangle(e.Bounds.X+8,e.Bounds.Y,e.Bounds.Width-12,e.Bounds.Height),mode.ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};export.Controls.Add(mode,1,1);
            explanation=new Label{Dock=DockStyle.Fill,ForeColor=Color.FromArgb(178,181,197),Font=new Font(Font.FontFamily,9.5F)};export.Controls.Add(explanation,0,2);export.SetColumnSpan(explanation,2);mode.SelectedIndexChanged+=(s,e)=>explanation.Text=Details[Math.Max(0,mode.SelectedIndex)];
            var exports=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty};export.Controls.Add(exports,0,3);export.SetColumnSpan(exports,2);AddAction("导出当前声音",Export,false,exports);
            var bottom=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty};content.Controls.Add(bottom,0,5);AddAction("返回控制台",()=>{Close();return Task.FromResult(0);},false,bottom);
            var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,96));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.Controls.Add(footer,0,2);
            waveform=new Panel{Dock=DockStyle.Fill};waveform.Paint+=(s,e)=>{if(!busy)return;using(var brush=new SolidBrush(Color.FromArgb(180,154,255)))for(int i=0;i<7;i++){int h=5+(int)(18*(.5+.5*Math.Sin(frame*.3+i*.7)));e.Graphics.FillRectangle(brush,6+i*12,35-h,6,h);}};footer.Controls.Add(waveform,0,0);
            status=new Label{Dock=DockStyle.Fill,Text="先导入音色包，或导出主程序已经应用的声音。",Padding=new Padding(0,12,0,0),AutoEllipsis=true};footer.Controls.Add(status,1,0);motion.Tick+=(s,e)=>{frame++;waveform.Invalidate();};
            FormClosing+=(s,e)=>{if(busy){e.Cancel=true;status.Text="文件操作尚未完成，请等待校验和恢复服务结束。";}};
            RefreshVoice();AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;ResumeLayout(true);
        }
        void AddAction(string text,Func<Task> action,bool primary=false,FlowLayoutPanel target=null){
            var button=new AnimeButton{Text=text,AutoSize=true,MinimumSize=new Size(130,38),BackColor=primary?Color.FromArgb(180,154,255):Color.FromArgb(43,42,55),ForeColor=primary?Color.FromArgb(20,17,31):ForeColor,BorderSize=0,Padding=new Padding(12,0,12,0),Margin=new Padding(0,4,12,4),Cursor=Cursors.Hand};
            button.Click+=async(s,e)=>{if(busy)return;if(text=="返回控制台"){Close();return;}busy=true;content.Enabled=false;motion.Start();try{await action();}catch(Exception error){status.Text="未完成："+error.Message;MessageBox.Show(this,error.Message,"操作未完成");}finally{busy=false;motion.Stop();waveform.Invalidate();content.Enabled=true;}};(target??actions).Controls.Add(button);
        }
        static string EngineName(VoiceConfig voice){return voice.engine=="cosyvoice"?"CosyVoice":"GPT-SoVITS · "+voice.gptVariant;}
        void RefreshVoice(){var config=store.Read();var voice=config.voice;var applied=config.profiles.FirstOrDefault(p=>Json.Canonical(p.voice)==Json.Canonical(voice));current.Text="当前声音  ·  "+(applied==null?"尚未保存为方案":applied.name)+"   /   "+EngineName(voice);voices.Items.Clear();foreach(var item in config.profiles)voices.Items.Add(item);if(applied!=null){voices.SelectedItem=applied;name.Text=applied.name;}int selected=Math.Max(0,mode.SelectedIndex);mode.Items.Clear();mode.Items.AddRange(voice.engine=="cosyvoice"?Labels:Labels.Take(2).ToArray());mode.SelectedIndex=Math.Min(selected,mode.Items.Count-1);}
        string Library(){string path=store.Resolve("voice-model-packages");Directory.CreateDirectory(path);return path;}
        static string CleanName(string value){value=(value??"").Trim();foreach(char c in Path.GetInvalidFileNameChars())value=value.Replace(c,'_');if(value.Length==0||value.Length>80)throw new InvalidOperationException("请填写 1～80 字的音色名称。");return value;}
        static string DisplayName(string value){value=(value??"").Trim();string suffix;while((suffix=new[]{".cosyvoice.ttsmodel",".gpt-sovits.ttsmodel"}.FirstOrDefault(s=>value.EndsWith(s,StringComparison.OrdinalIgnoreCase)))!=null)value=value.Substring(0,value.Length-suffix.Length).Trim();foreach(string label in new[]{"-高级完整包","-高级训练包","-模型完整包","-轻量包"})if(value.EndsWith(label,StringComparison.OrdinalIgnoreCase))value=value.Substring(0,value.Length-label.Length).Trim();return value;}
        async Task Import(){
            string file;using(var d=new OpenFileDialog{Title="导入音色包",Filter="TTS 音色包|*.cosyvoice.ttsmodel;*.gpt-sovits.ttsmodel",InitialDirectory=Library(),CheckFileExists=true}){if(d.ShowDialog(this)!=DialogResult.OK)return;file=d.FileName;}
            status.Text="正在校验并导入，请勿关闭窗口……";var result=await Transfer("import",file,null,null);
            string display=CleanName(DisplayName(Json.Text(result,"name")));string parent=Path.Combine(Library(),display);Directory.CreateDirectory(parent);string copy=Path.Combine(parent,Path.GetFileName(file));if(!Path.GetFullPath(copy).Equals(Path.GetFullPath(file),StringComparison.OrdinalIgnoreCase))File.Copy(file,copy,true);
            RefreshVoice();status.Text="已导入并应用："+display+"。"+"已恢复模型与参考录音"+"。";
            object warnings;if(result.TryGetValue("warnings",out warnings)&&warnings is object[]&&((object[])warnings).Length>0)MessageBox.Show(this,Json.Encode(warnings),"运行环境兼容提醒");
        }
        async Task Export(){
            string title=CleanName(DisplayName(name.Text)),kind=Modes[mode.SelectedIndex],trained=null;
            if(kind.StartsWith("trained"))using(var d=new FolderBrowserDialog{Description="选择训练完成的完整 CosyVoice3 模型目录",ShowNewFolderButton=false}){if(d.ShowDialog(this)!=DialogResult.OK)return;trained=d.SelectedPath;}
            string suffix=store.Read().voice.engine=="gpt-sovits"?".gpt-sovits.ttsmodel":".cosyvoice.ttsmodel",parent=Path.Combine(Library(),title);Directory.CreateDirectory(parent);
            string file;using(var d=new SaveFileDialog{Title="导出"+Labels[mode.SelectedIndex],Filter="当前引擎音色包|*"+suffix,FileName=title+"-"+Labels[mode.SelectedIndex]+suffix,AddExtension=false,InitialDirectory=parent}){if(d.ShowDialog(this)!=DialogResult.OK)return;file=d.FileName;}
            string known;while((known=new[]{".cosyvoice.ttsmodel",".gpt-sovits.ttsmodel"}.FirstOrDefault(s=>file.EndsWith(s,StringComparison.OrdinalIgnoreCase)))!=null)file=file.Substring(0,file.Length-known.Length);file+=suffix;
            status.Text="正在校验并导出，请勿关闭窗口……";await Transfer("export",file,kind,trained);status.Text="已导出："+file;
        }
        async Task<Dictionary<string,object>> Transfer(string action,string file,string kind,string trained,string extra=""){
            bool running=await service.Running();if(running){var h=await service.Request("/health");if(Convert.ToBoolean(h["busy"])||Convert.ToInt32(h["queued"])>0)throw new InvalidOperationException("请先停止当前朗读或等待队列完成。");await service.Stop();}
            Dictionary<string,object> result=null;Exception failure=null;
            try{string args=action+" --root "+ServiceClient.Quote(store.Root)+(file==null?"":" --file "+ServiceClient.Quote(file))+(kind==null?"":" --package-mode "+kind)+(trained==null?"":" --trained-model-dir "+ServiceClient.Quote(trained))+extra;result=Json.Object(await RunBackend(args));}catch(Exception error){failure=error;}
            try{if(running)await service.Start();}catch(Exception error){failure=new InvalidOperationException((failure==null?"文件操作已完成，但服务恢复失败：":failure.Message+"；服务恢复失败：")+error.Message,error);}
            if(failure!=null)throw failure;return result;
        }
        Task<string> RunBackend(string args){return Task.Run(()=>{
            string python=store.Resolve(store.Read().engines.pythonService);if(!File.Exists(python))throw new InvalidOperationException("请先在主程序补齐主服务 Python 环境。");
            var start=new ProcessStartInfo(python,"-I -X utf8 "+ServiceClient.Quote(backend)+" "+args){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=store.Root,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};start.EnvironmentVariables.Remove("PYTHONHOME");start.EnvironmentVariables.Remove("PYTHONPATH");start.EnvironmentVariables["PYTHONNOUSERSITE"]="1";
            using(var process=Process.Start(start)){var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();process.WaitForExit();Task.WaitAll(output,error);if(process.ExitCode!=0)throw new InvalidOperationException(error.Result.Length>0?error.Result:output.Result);return output.Result;}
        });}
        internal object Verify(){var initial=Size;Size=MinimumSize;PerformLayout();Application.DoEvents();int minimumList=voices.Height;Size=initial;PerformLayout();Application.DoEvents();if(mode.Items.Count!=(store.Read().voice.engine=="cosyvoice"?4:2)||!File.Exists(backend)||DisplayName("声音-轻量包.cosyvoice.ttsmodel.gpt-sovits.ttsmodel")!="声音"||status.Height<40||voices.Height<50||minimumList<voices.ItemHeight||voices.Items.Count!=store.Read().profiles.Count)throw new InvalidOperationException("模块页面、命名、声音列表或后端缺失；最小窗口列表高度："+minimumList);float dpi;using(var graphics=CreateGraphics())dpi=graphics.DpiX;return new{ok=true,uiOnly=true,modes=mode.Items.Count,savedVoices=voices.Items.Count,statusHeight=status.Height,listHeight=voices.Height,minimumListHeight=minimumList,dpi=dpi,backendPresent=true};}
        protected override void Dispose(bool disposing){if(disposing)motion.Dispose();base.Dispose(disposing);}
    }
}
