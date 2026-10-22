using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AgentVoice {
    internal sealed class EmbeddedSetupForm:Form {
        internal const string Origin="https://setup.agent-voice.local";
        internal readonly EmbeddedSetupController Controller;
        internal WebView2 Browser;
        internal bool Ready;
        readonly bool testing,forceMissing;
        readonly InterfaceSounds sounds=new InterfaceSounds();
        readonly Panel fallback=new Panel{Dock=DockStyle.Fill};
        readonly Label detail=new Label{Dock=DockStyle.Top,Height=140,AutoSize=false};
        string activeRequest="";
        bool repairing,initializing,serviceWasRunning=true,closing,allowClose;
        internal EmbeddedSetupForm(SettingsStore store,bool test=false,bool missing=false){
            testing=test;forceMissing=missing;Controller=new EmbeddedSetupController(store);
            Text="Agent Voice · 首次配置";Font=new Font("Microsoft YaHei UI",11F);BackColor=Color.FromArgb(16,17,22);ForeColor=Color.FromArgb(243,245,248);
            AutoScaleMode=AutoScaleMode.Dpi;using(var g=CreateGraphics())AutoScaleDimensions=new SizeF(g.DpiX,g.DpiY);
            var area=Screen.FromControl(this).WorkingArea;float dpi=AutoScaleDimensions.Width/96F;
            ClientSize=new Size(Math.Min((int)(1180*dpi),area.Width-40),Math.Min((int)(780*dpi),area.Height-60));MinimumSize=new Size(900,620);StartPosition=FormStartPosition.CenterScreen;
            fallback.BackColor=BackColor;fallback.Padding=new Padding(42);Controls.Add(fallback);detail.Text="正在准备本地配置界面……";fallback.Controls.Add(detail);
            Controller.Progress=text=>Send(new{type="progress",id=activeRequest,message=text});
            Shown+=async(s,e)=>{
                // Loading configuration must not depend on an already healthy service.
                await Initialize();
                try{serviceWasRunning=await Controller.Flow.Service.Running();}catch{serviceWasRunning=true;}
            };
            FormClosing+=OnClosing;
        }
        internal static bool Trusted(string source){Uri uri;return Uri.TryCreate(source,UriKind.Absolute,out uri)&&uri.Scheme=="https"&&uri.Host=="setup.agent-voice.local"&&uri.IsDefaultPort;}
        internal async Task Initialize(){
            if(initializing||IsDisposed)return;initializing=true;
            Ready=false;
            try{
                if(Browser!=null){Browser.Dispose();Browser=null;}
                if(forceMissing)throw new InvalidOperationException("模拟缺少 WebView2 运行时。");
                WebRuntime.Version(Controller.Flow.Store);
                string assets=Controller.Flow.Store.Resolve("assets/setup");if(!File.Exists(Path.Combine(assets,"index.html")))throw new FileNotFoundException("配置网页文件缺失。请保留整个软件目录，不要只复制 exe。");
                Browser=new WebView2{Dock=DockStyle.Fill,DefaultBackgroundColor=BackColor};Controls.Add(Browser);Browser.BringToFront();
                var environment=await CoreWebView2Environment.CreateAsync(WebRuntime.FixedFolder(Controller.Flow.Store),Controller.Flow.Store.Resolve("data/ui-webview2"));
                await Browser.EnsureCoreWebView2Async(environment);
                var core=Browser.CoreWebView2;core.Settings.AreDevToolsEnabled=testing;core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.IsStatusBarEnabled=false;core.Settings.IsZoomControlEnabled=false;core.Settings.AreBrowserAcceleratorKeysEnabled=false;
                core.SetVirtualHostNameToFolderMapping("setup.agent-voice.local",assets,CoreWebView2HostResourceAccessKind.DenyCors);
                core.NavigationStarting+=(s,e)=>{if(!Trusted(e.Uri))e.Cancel=true;};core.NewWindowRequested+=(s,e)=>e.Handled=true;
                core.WebMessageReceived+=Message;
                core.NavigationCompleted+=(s,e)=>{if(!e.IsSuccess){ShowFallback("配置网页加载失败："+e.WebErrorStatus);return;}Ready=true;fallback.Visible=false;};
                core.Navigate(Origin+"/index.html");
            }catch(Exception error){if(!IsDisposed)ShowFallback(error.Message);}
            finally{initializing=false;}
        }
        void ShowFallback(string reason){
            Ready=false;if(Browser!=null){Browser.Dispose();Browser=null;}
            fallback.Visible=true;fallback.BringToFront();
            foreach(Control control in fallback.Controls.Cast<Control>().Where(c=>c!=detail).ToArray())control.Dispose();
            detail.Text="配置界面组件未就绪\r\n\r\n"+reason+"\r\n可以补齐组件，或继续使用简洁配置。声音配置和模型不会被删除。";
            var actions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true};fallback.Controls.Add(actions);actions.BringToFront();
            AddFallback(actions,"联网安装 WebView2",async()=>await Repair(null,false));
            AddFallback(actions,"选择离线安装器",async()=>{using(var d=new OpenFileDialog{Filter="微软 WebView2 安装器|*.exe",CheckFileExists=true})if(d.ShowDialog(this)==DialogResult.OK)await Repair(d.FileName,false);});
            AddFallback(actions,"导入固定版运行时",async()=>{using(var d=new FolderBrowserDialog{Description="选择直接包含 msedgewebview2.exe 的固定版运行时目录",ShowNewFolderButton=false})if(d.ShowDialog(this)==DialogResult.OK)await Repair(d.SelectedPath,true);});
            AddFallback(actions,"重试网页配置",Initialize);
            AddFallback(actions,"使用简洁配置",async()=>{using(var wizard=new SetupWizard(Controller.Flow.Store,testing)){Hide();var result=wizard.ShowDialog();Show();if(result==DialogResult.OK){allowClose=true;DialogResult=DialogResult.OK;Close();}}await Task.FromResult(0);});
            AddFallback(actions,"稍后配置",async()=>{await Controller.Execute("defer",new Dictionary<string,object>());DialogResult=DialogResult.OK;Close();});
        }
        void AddFallback(FlowLayoutPanel panel,string text,Func<Task> action){var button=new System.Windows.Forms.Button{Text=text,AutoSize=true,MinimumSize=new Size(190,44),Margin=new Padding(0,8,12,0)};button.Click+=async(s,e)=>{if(repairing||initializing)return;try{await action();}catch(Exception error){detail.Text=error.Message;}};panel.Controls.Add(button);}
        async Task Repair(string path,bool fixedVersion){
            if(repairing)return;repairing=true;detail.Text="正在准备 WebView2；请等待完成。微软安装器使用系统共享位置，便携使用请选固定版运行时。";
            try{if(fixedVersion)await WebRuntime.ImportFixed(Controller.Flow.Store,path);else await WebRuntime.Install(Controller.Flow.Store,path);await Initialize();}finally{repairing=false;}
        }
        async void Message(object sender,CoreWebView2WebMessageReceivedEventArgs e){
            if(!Trusted(e.Source)||e.WebMessageAsJson.Length>65536)return;
            string id="";
            try{
                var request=Json.Object(e.WebMessageAsJson);id=Json.Text(request,"id");if(!Regex.IsMatch(id,"^[a-zA-Z0-9_-]{1,64}$"))throw new InvalidOperationException("无效请求编号。");
                string action=Json.Text(request,"action");object raw;var args=request.TryGetValue("args",out raw)?raw as Dictionary<string,object>:null;args=args??new Dictionary<string,object>();
                object result;
                if(action=="browse")result=Browse(args);
                else if(action=="open-doc")result=OpenDoc(args);
                else{if(action!="stop-preview"&&!Controller.Busy)activeRequest=id;result=await Controller.Execute(action,args);}
                Send(new{type="result",id=id,ok=true,result=result});
                if(new[]{"next","back","skip"}.Contains(action))PlayCue();
                if(Controller.Finished){DialogResult=DialogResult.OK;Close();}
            }catch(Exception error){Send(new{type="result",id=id,ok=false,error=error.Message,state=Controller.Snapshot()});}
        }
        object Browse(Dictionary<string,object> args){
            if(Controller.Busy)throw new InvalidOperationException("当前任务仍在执行。");string kind=Json.Text(args,"kind"),path="";
            if(new[]{"folder","harness","airi","model"}.Contains(kind))using(var d=new FolderBrowserDialog{Description=kind=="airi"?"选择已登录的原 AIRI 安装目录":"选择对应的已有目录",ShowNewFolderButton=false}){if(d.ShowDialog(this)==DialogResult.OK)path=d.SelectedPath;}
            else{
                var filters=new Dictionary<string,string>{{"engine","引擎包|*.ttsengine"},{"environment","环境包|*.ttsenvironment"},{"reference","参考音频|*.wav;*.flac;*.mp3;*.m4a"},{"executable","运行程序|*.exe"}};
                if(!filters.ContainsKey(kind))throw new InvalidOperationException("不支持的文件选择类型。");
                using(var d=new OpenFileDialog{Filter=filters[kind],CheckFileExists=true,InitialDirectory=Controller.Flow.Store.Root})if(d.ShowDialog(this)==DialogResult.OK)path=d.FileName;
                if((kind=="engine"||kind=="environment")&&path.Length>0){var package=ModulePackages.Inspect(path);if(package.kind!=kind)throw new InvalidOperationException("模块类型不匹配。");if(MessageBox.Show(this,package.name+"\r\n安装目录："+Controller.Flow.Store.Root+"\r\n模块含可执行代码。确认来源可信后安装；已有文件保留回退副本。","安装所选模块",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)!=DialogResult.OK)path="";}
            }
            return new{path=path};
        }
        object OpenDoc(Dictionary<string,object> args){
            string name=Json.Text(args,"name");if(!new[]{"模块安装与配置指南.md","TTS使用手册.md","环境补齐说明.md","接入说明.md","音色训练从零开始.md","稳定性与故障排查.md","docs/使用须知与TTS许可.md","docs/licenses/CosyVoice-Apache-2.0.txt","docs/licenses/GPT-SoVITS-MIT.txt"}.Contains(name))throw new InvalidOperationException("不支持的文档。");ServiceClient.Open(Controller.Flow.Store.Resolve(name));return new{message="已打开说明。"};
        }
        void Send(object value){if(IsDisposed||Browser==null||Browser.IsDisposed||Browser.CoreWebView2==null)return;Browser.CoreWebView2.PostWebMessageAsJson(Json.Encode(value));}
        async void PlayCue(){try{var p=Controller.Flow.State.Read();bool running=await Controller.Flow.Service.Running();if(!IsDisposed&&InterfaceSounds.Allowed(p.interfaceSounds,testing,running))sounds.Play(Controller.Flow.Store.Root,Math.Max(0,p.onboardingStep-1));}catch{/* Sound is optional and must never block setup. */}}
        async void OnClosing(object sender,FormClosingEventArgs e){
            if(Controller.Busy||repairing||initializing){e.Cancel=true;Send(new{type="notice",message="任务仍在执行。请等待当前操作完成后关闭。"});return;}
            if(allowClose||Controller.Finished)return;e.Cancel=true;if(closing)return;closing=true;
            if(Controller.Flow.PreviewMayBePending){
                Exception stopError=null;try{await Controller.Flow.StopPreview();}catch(Exception error){stopError=error;}
                if(stopError!=null){closing=false;detail.Text=stopError.Message;Send(new{type="notice",message="试听停止尚未确认，请重试停止后再关闭。"+stopError.Message});return;}
            }
            Exception failure=null;
            try{if(!serviceWasRunning||Controller.Flow.Service.Owned!=null){if(await Controller.Flow.Service.Running())await Controller.Flow.Service.Stop();}}catch(Exception error){failure=error;}
            try{await Controller.Flow.Service.StopOwnedStartup();}catch(Exception error){closing=false;detail.Text=error.Message;Send(new{type="notice",message=error.Message});return;}
            if(failure!=null)detail.Text=failure.Message;allowClose=true;Close();
        }
        protected override void Dispose(bool disposing){if(disposing){sounds.Dispose();if(Browser!=null)Browser.Dispose();}base.Dispose(disposing);}
    }
}
