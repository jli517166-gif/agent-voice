using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using AgentVoice;

namespace VoicePackages {
    static class Program {
        [STAThread]static int Main(string[] args){
            int index=Array.IndexOf(args,"--root");if(index<0||index+1>=args.Length)return 2;
            string root=Path.GetFullPath(args[index+1]);string core=Path.Combine(root,"agent语音.exe");if(!File.Exists(core))return 2;
            AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>new AssemblyName(e.Name).Name=="agent-voice-build"?Assembly.LoadFrom(core):null;
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            try{return Run(root,args);}catch(Exception error){MessageBox.Show(error.Message,"音色包模块无法打开");return 1;}
        }
        [MethodImpl(MethodImplOptions.NoInlining)]static int Run(string root,string[] args){
            var store=new SettingsStore(root);ModulePackages.InstalledFeature(store,"voice-packages");
            if(!Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\').Equals(store.Resolve("modules/voice-packages").TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("请先从主程序安装该模块，再从模块页打开。");
            using(var form=new ModuleWindow(root)){
                int test=Array.IndexOf(args,"--selftest");
                if(test>=0&&test+1<args.Length){form.Show();Application.DoEvents();string report=args[test+1];Directory.CreateDirectory(Path.GetDirectoryName(report));File.WriteAllText(report,Json.Encode(form.Verify()),Encoding.UTF8);form.Close();return 0;}
                Application.Run(form);return 0;
            }
        }
    }
    sealed class ModuleWindow:Form {
        readonly SettingsStore store;readonly ServiceClient service;
        readonly TextBox name;readonly ComboBox mode;readonly Label current,explanation,status;
        readonly FlowLayoutPanel actions;readonly Timer motion=new Timer{Interval=100};readonly Panel waveform;
        readonly string backend=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"backend/module_cli.py");
        bool busy;int frame;
        static readonly string[] Modes={"lightweight","full","trained","trained-full"};
        static readonly string[] Labels={"轻量包","模型完整包","高级训练包","高级完整包"};
        static readonly string[] Details={"参考录音和参数；目标电脑必须已有完全匹配的基础模型。","参考录音、参数和当前模型权重；不包含 Python 或 TTS 引擎源码。","参考录音、参数和训练后改变的权重；目标电脑需要匹配的基础模型。","参考录音、参数和完整训练模型；不包含 Python 或 TTS 引擎源码。"};
        internal ModuleWindow(string root){
            store=new SettingsStore(root);service=new ServiceClient(store);Text="音色包 · 可选模块";AutoScaleMode=AutoScaleMode.Dpi;Font=new Font("Microsoft YaHei UI",11F);
            ClientSize=new Size(790,560);MinimumSize=new Size(680,500);StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(16,17,22);ForeColor=Color.FromArgb(235,237,245);Padding=new Padding(28);
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=9};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(int height in new[]{52,52,58,38,38,64,64,38})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(layout);
            layout.Controls.Add(new Label{Text="音色包",Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,24F,FontStyle.Bold)},0,0);
            layout.Controls.Add(new Label{Text="独立安装的可选功能。只使用你有权使用和分享的模型及录音。",Dock=DockStyle.Fill},0,1);
            current=new Label{Dock=DockStyle.Fill,AutoEllipsis=true};layout.Controls.Add(current,0,2);
            name=new TextBox{Name="packageName",Text="我的声音",Dock=DockStyle.Fill,BackColor=Color.FromArgb(34,35,44),ForeColor=ForeColor};layout.Controls.Add(name,0,3);
            mode=new ComboBox{Name="packageMode",DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill,BackColor=Color.FromArgb(34,35,44),ForeColor=ForeColor};layout.Controls.Add(mode,0,4);
            explanation=new Label{Dock=DockStyle.Fill,Padding=new Padding(0,10,0,0)};layout.Controls.Add(explanation,0,5);mode.SelectedIndexChanged+=(s,e)=>explanation.Text=Details[Math.Max(0,mode.SelectedIndex)];
            actions=new FlowLayoutPanel{Dock=DockStyle.Fill};layout.Controls.Add(actions,0,6);
            AddAction("导入音色包",Import);AddAction("导出音色包",Export);AddAction("打开包文件夹",()=>{ServiceClient.Open(Library());return Task.FromResult(0);});AddAction("刷新当前声音",()=>{RefreshVoice();return Task.FromResult(0);});
            waveform=new Panel{Dock=DockStyle.Fill};waveform.Paint+=(s,e)=>{if(!busy)return;using(var brush=new SolidBrush(Color.FromArgb(180,154,255)))for(int i=0;i<7;i++){int h=5+(int)(18*(.5+.5*Math.Sin(frame*.3+i*.7)));e.Graphics.FillRectangle(brush,6+i*12,28-h,6,h);}};layout.Controls.Add(waveform,0,7);
            status=new Label{Dock=DockStyle.Fill,Text="请先在主程序应用当前模型与参考录音，再导出。"};layout.Controls.Add(status,0,8);motion.Tick+=(s,e)=>{frame++;waveform.Invalidate();};
            FormClosing+=(s,e)=>{if(busy){e.Cancel=true;status.Text="文件操作尚未完成，请等待校验和恢复服务结束。";}};
            RefreshVoice();
        }
        void AddAction(string text,Func<Task> action){
            var button=new System.Windows.Forms.Button{Text=text,AutoSize=true,Height=40,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(43,42,55),ForeColor=ForeColor,Padding=new Padding(8,4,8,4),Margin=new Padding(0,4,8,4)};button.FlatAppearance.BorderSize=0;
            button.Click+=async(s,e)=>{if(busy)return;busy=true;actions.Enabled=name.Enabled=mode.Enabled=false;motion.Start();try{await action();}catch(Exception error){status.Text="未完成："+error.Message;MessageBox.Show(this,error.Message,"操作未完成");}finally{busy=false;motion.Stop();waveform.Invalidate();actions.Enabled=name.Enabled=mode.Enabled=true;}};actions.Controls.Add(button);
        }
        void RefreshVoice(){var voice=store.Read().voice;current.Text="当前已应用："+(voice.engine=="cosyvoice"?"CosyVoice":"GPT-SoVITS · "+voice.gptVariant)+"\r\n模型与参考录音来自主程序当前配置。";int selected=Math.Max(0,mode.SelectedIndex);mode.Items.Clear();mode.Items.AddRange(voice.engine=="cosyvoice"?Labels:Labels.Take(2).ToArray());mode.SelectedIndex=Math.Min(selected,mode.Items.Count-1);}
        string Library(){string path=store.Resolve("voice-model-packages");Directory.CreateDirectory(path);return path;}
        static string CleanName(string value){value=(value??"").Trim();foreach(char c in Path.GetInvalidFileNameChars())value=value.Replace(c,'_');if(value.Length==0||value.Length>80)throw new InvalidOperationException("请填写 1～80 字的音色名称。");return value;}
        static string DisplayName(string value){value=(value??"").Trim();string suffix;while((suffix=new[]{".cosyvoice.ttsmodel",".gpt-sovits.ttsmodel"}.FirstOrDefault(s=>value.EndsWith(s,StringComparison.OrdinalIgnoreCase)))!=null)value=value.Substring(0,value.Length-suffix.Length).Trim();foreach(string label in new[]{"-高级完整包","-高级训练包","-模型完整包","-轻量包"})if(value.EndsWith(label,StringComparison.OrdinalIgnoreCase))value=value.Substring(0,value.Length-label.Length).Trim();return value;}
        async Task Import(){
            string file;using(var d=new OpenFileDialog{Title="导入音色包",Filter="TTS 音色包|*.cosyvoice.ttsmodel;*.gpt-sovits.ttsmodel",InitialDirectory=Library(),CheckFileExists=true}){if(d.ShowDialog(this)!=DialogResult.OK)return;file=d.FileName;}
            status.Text="正在校验并导入，请勿关闭窗口……";var result=await Transfer("import",file,null,null);
            string display=CleanName(DisplayName(Json.Text(result,"name")));string parent=Path.Combine(Library(),display);Directory.CreateDirectory(parent);string copy=Path.Combine(parent,Path.GetFileName(file));if(!Path.GetFullPath(copy).Equals(Path.GetFullPath(file),StringComparison.OrdinalIgnoreCase))File.Copy(file,copy,true);
            RefreshVoice();status.Text="已导入："+display+"。返回控制台后可试听。";
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
        async Task<Dictionary<string,object>> Transfer(string action,string file,string kind,string trained){
            bool running=await service.Running();if(running){var h=await service.Request("/health");if(Convert.ToBoolean(h["busy"])||Convert.ToInt32(h["queued"])>0)throw new InvalidOperationException("请先停止当前朗读或等待队列完成。");await service.Stop();}
            Dictionary<string,object> result=null;Exception failure=null;
            try{string args=action+" --root "+ServiceClient.Quote(store.Root)+" --file "+ServiceClient.Quote(file)+(kind==null?"":" --package-mode "+kind)+(trained==null?"":" --trained-model-dir "+ServiceClient.Quote(trained));result=Json.Object(await RunBackend(args));}catch(Exception error){failure=error;}
            try{if(running)await service.Start();}catch(Exception error){failure=new InvalidOperationException((failure==null?"文件操作已完成，但服务恢复失败：":failure.Message+"；服务恢复失败：")+error.Message,error);}
            if(failure!=null)throw failure;return result;
        }
        Task<string> RunBackend(string args){return Task.Run(()=>{
            string python=store.Resolve(store.Read().engines.pythonService);if(!File.Exists(python))throw new InvalidOperationException("请先在主程序补齐主服务 Python 环境。");
            var start=new ProcessStartInfo(python,"-I -X utf8 "+ServiceClient.Quote(backend)+" "+args){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=store.Root,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};start.EnvironmentVariables.Remove("PYTHONHOME");start.EnvironmentVariables.Remove("PYTHONPATH");start.EnvironmentVariables["PYTHONNOUSERSITE"]="1";
            using(var process=Process.Start(start)){var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();process.WaitForExit();Task.WaitAll(output,error);if(process.ExitCode!=0)throw new InvalidOperationException(error.Result.Length>0?error.Result:output.Result);return output.Result;}
        });}
        internal object Verify(){if(mode.Items.Count!=(store.Read().voice.engine=="cosyvoice"?4:2)||!File.Exists(backend)||DisplayName("声音-轻量包.cosyvoice.ttsmodel.gpt-sovits.ttsmodel")!="声音")throw new InvalidOperationException("模块页面、命名或后端缺失");return new{ok=true,uiOnly=true,modes=mode.Items.Count,backendPresent=true};}
        protected override void Dispose(bool disposing){if(disposing)motion.Dispose();base.Dispose(disposing);}
    }
}
