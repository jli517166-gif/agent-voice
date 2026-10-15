using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed partial class MainForm {
        readonly Dictionary<string,string> verificationIds=new Dictionary<string,string>();
        readonly Dictionary<string,string> connectionHints=new Dictionary<string,string>();
        Label verificationHint,airiIndicator;
        bool pendingAiriVerification;
        void UpdateVoiceControls(){
            bool rvc=synthesisChoice==null||synthesisChoice.SelectedIndex==1;
            pitch.Enabled=rvc;formant.Enabled=rvc&&(processingChoice==null||processingChoice.SelectedIndex!=1);
            bool advanced=checks.ContainsKey("advancedEnabled")&&checks["advancedEnabled"].Checked;
            foreach(string key in new[]{"protect","indexRate","rmsMix","volume","leadingSilence"})if(numbers.ContainsKey(key))numbers[key].Enabled=advanced;
            if(f0Choice!=null)f0Choice.Enabled=advanced;if(fields.ContainsKey("instruction"))fields["instruction"].Enabled=advanced;
            if(voiceHint!=null)voiceHint.Text=!rvc?"本地 TTS 直接输出：RVC 模型、音调、声线粗细与索引均不参与生成。":processingChoice!=null&&processingChoice.SelectedIndex==1?"认可版兼容：保留原离线处理，声线粗细已禁用。":"标准 RVC：音调与声线粗细影响第二次变声。输出模式在设置中。";
            if(rvcModeStatus!=null)rvcModeStatus.Text=Config.voice.synthesisMode=="rvc"?"● 当前已启用 RVC＋本地 TTS，生成内容会经过二次变声。":"○ 当前为本地 TTS 直出，下面的 RVC 参数处于备用状态。";
        }
        [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll",EntryPoint="ShowWindow")]static extern bool ShowAiriWindow(IntPtr window,int command);

        void BuildConnectionGuide(){
            var stack=Stack(NewPage("connectionGuide"));var card=Card(stack,"接入步骤与真实验证");
            Add(card,Hint("① 首页先试听声音并启动服务。② Harness 只需选择安装目录或用户配置目录，程序会识别类型；AIRI 先手动启用，再选择原安装目录。③ 程序自动安装连接器、启动真实验证、等待最终回复并确认播放。全部通过后显示绿色状态和礼炮；失败会停在具体步骤并给出处理方法。"));
            foreach(var entry in new[]{
                new[]{"Codex","程序定位 CODEX_HOME/sessions（默认当前用户 .codex/sessions），或你所选目录中的会话目录，重新加载监听后开始复验。在 Codex 发送验证提示；最终回复进入队列。若未收到，检查是否是同一 Windows 用户及会话目录。"},
                new[]{"DeepSeek Harness","自动定位 .dsh/profiles/desktop，同时为 headless 真实验证配置连接器，并同步桌面端当前模型账号与已配置的本机受信任 CA。程序备份配置并关闭旧 dsh-tts-autoread，防止重复朗读。验证会自动调用安装包内的 dsh headless；桌面端重启后，turn/end 才提交日常回复。"},
                new[]{"其他 Harness","正式版不提供未经真机验证的开关。请从 Harness 页面打开二次开发指南，新增独立适配器后执行同样的随机回复与真实播放验证。"},
                new[]{"AIRI","默认不启用。勾选并保存联动后，点击“选择目录并接入验证”，选择含 airi.exe 的安装目录。程序在该 AIRI 中安装桥接并保留 app.asar 单文件备份，不再生成完整副本。桥接连通后自动提交播放验证，收到桌宠原生播放完成回执才通过，心跳不算成功。AIRI 更新可能覆盖桥接，失效后重新连接验证即可自动修复。"}
            }){var section=Card(stack,entry[0]);Add(section,Hint(entry[1]));}
            var end=Card(stack,"结果与故障提示");verificationHint=Hint("尚未开始验证。监听就绪和连接器心跳只表示可用状态。换机、服务重启或超过 15 分钟需重新验证。");Add(end,verificationHint);
            Add(end,Hint("收不到正文：确认目标已启用、配置目录正确、客户端已重载、验证提示发给了对应 Harness。收到正文但失败：检查声音资源／输出设备与日志。Harness 配置备份在 data/connection-backups；AIRI 原 app.asar 备份在所选 AIRI 的 resources 目录。可打开服务日志排查。"));
            Add(end,Row(Btn("返回",()=>ShowPage("connections")),Btn("打开 Harness",()=>ShowPage("harness")),Btn("打开 AIRI",()=>ShowPage("airi")),EntryBtn("打开日志","查看连接与播放诊断记录。",()=>{Directory.CreateDirectory(Store.Resolve("data/logs"));ServiceClient.Open(Store.Resolve("data/logs"));})));
        }
        string HarnessManualHelp(string source){
            if(source=="codex")return "请确认 Codex CLI 已登录且可运行 codex exec；也可在 Codex 中手动发送页面显示的验证短句。";
            return "请确认 DeepSeek Harness 已登录，且安装目录中存在 resources/runtime/cli/bin/dsh.cmd；程序会使用 headless 配置执行真实验证。";
        }
        async Task InstallHarnessMcp(string source){
            BusyStatus("正在为 "+(source=="codex"?"Codex":"DeepSeek Harness")+" 安装聊天栏快捷控制……");
            var result=Json.Object(await Service.RunTool("install_mcp.py","--source "+source+" --root "+ServiceClient.Quote(Store.Root)));
            string message=Json.Text(result,"message"),usage=source=="codex"?"重启 Codex 后，点击聊天栏里的“语音控制”按钮；也可输入 $agent-voice-control。":"重启 DeepSeek Harness 后，点击聊天栏里的“语音”按钮；也可输入 /voice。";Status(message);MessageBox.Show(this,message+"\r\n\r\n"+usage+"\r\n底层工具名为 agent_voice。","聊天控制安装完成",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        async Task WaitForHarnessVerification(string source,string challenge){
            for(int i=0;i<240;i++){
                var item=await Service.Request("/api/verify/"+source,null,3000);string state=Json.Text(item,"state");
                if(state=="verified"){
                    bool first=verificationIds.Remove(source);verificationHint.Text=source+"：连接器、真实回复和语音播放全部验证通过。";BusyStatus("第 4/4 步："+verificationHint.Text);if(first)ShowCelebration(source);return;
                }
                if(state=="failed")throw new InvalidOperationException("播放验证失败："+Json.Text(item,"error"));
                BusyStatus(state=="playing"?"已收到真实回复，正在生成并播放验证语音……":"Harness 已执行，正在等待连接器返回验证短句……");
                await Task.Delay(500);
            }
            Clipboard.SetText("请只回复下面这段文字，不加解释和标点：\r\n"+challenge);
            throw new InvalidOperationException("程序没有在两分钟内收到完整播放回执。验证短句已复制。"+HarnessManualHelp(source));
        }
        async Task BeginAutoHarness(){
            if(!await Service.Running())throw new InvalidOperationException("请先启动语音服务。");
            using(var dialog=new FolderBrowserDialog{Description="选择任意已支持 Harness 的安装目录或用户配置目录（程序会自动识别）",ShowNewFolderButton=false}){
                if(dialog.ShowDialog(this)!=DialogResult.OK){Status("已取消定位，当前接入状态保留。");return;}
                BusyStatus("第 1/4 步：正在识别 Harness 并安装连接器……");
                var result=Json.Object(await Service.RunTool("connect_harness.py","--source auto --root "+ServiceClient.Quote(Store.Root)+" --location "+ServiceClient.Quote(dialog.SelectedPath)));
                string source=Json.Text(result,"source"),name=Json.Text(result,"name");
                if(!Config.harness.ContainsKey(source))throw new InvalidOperationException("识别结果未适配："+source);
                connectionHints[source]="已自动识别为 "+name+"，连接器安装完成。";
                verificationHint.Text=connectionHints[source]+" 正在检查真实验证命令。";
                BusyStatus("第 2/5 步：正在检查 "+name+" 命令行组件……");
                try{await Service.RunTool("verify_harness.py","--source "+source+" --location "+ServiceClient.Quote(dialog.SelectedPath)+" --probe");}
                catch(Exception error){verificationHint.Text=source+"：连接器已安装，但未启用；真实验证命令检查失败。修复后重新选择同一目录即可继续。"+HarnessManualHelp(source);ShowPage("connectionGuide");throw new InvalidOperationException(verificationHint.Text+" 原因："+error.Message);}
                var next=Json.Copy(Config);next.harness[source]=true;checks[source].Checked=true;await Persist(next,new {harness=next.harness});
                Exception verificationError=null;
                try{
                    if(source=="codex")await Service.Request("/api/connections/reload",new {});
                    verificationHint.Text=connectionHints[source]+" 命令行组件可用，开始真实回复验证。";
                    await VerifyHarness(source,dialog.SelectedPath);
                }catch(Exception error){verificationError=error;}
                if(verificationError!=null){
                    var rollback=Json.Copy(Config);rollback.harness[source]=false;checks[source].Checked=false;await Persist(rollback,new {harness=rollback.harness});
                    connectionHints[source]="连接器已安装，但真实验证未通过；当前未启用。修复提示中的问题后，重新选择同一目录即可自动检查并修复。";
                    throw verificationError;
                }
            }
        }
        async Task VerifyHarness(string source,string harnessLocation){
            var item=await Service.Request("/api/verify/"+source,new {});verificationIds[source]=Json.Text(item,"id");
            string challenge=Json.Text(item,"text");string prompt=Json.Text(item,"prompt");
            if(String.IsNullOrWhiteSpace(prompt))prompt="Reply with exactly this line and nothing else:\n"+challenge;
            BusyStatus("第 3/5 步：正在启动 "+source+" 的真实验证会话……");
            try{await Service.RunTool("verify_harness.py","--source "+source+" --location "+ServiceClient.Quote(harnessLocation)+" --prompt "+ServiceClient.Quote(prompt)+" --timeout 180");}
            catch(Exception error){Clipboard.SetText("请只回复下面这段文字，不加解释和标点：\r\n"+challenge);verificationHint.Text=source+"：自动验证停在“启动 Harness 并获取回复”。验证短句已复制。"+HarnessManualHelp(source);ShowPage("connectionGuide");throw new InvalidOperationException(verificationHint.Text+" 原因："+error.Message);}
            BusyStatus("第 4/5 步：Harness 已回复，正在确认连接器和播放回执……");
            await WaitForHarnessVerification(source,challenge);verificationHint.Text=source+"：自动识别、接入和真实播放验证成功。";ShowPage("connectionGuide");Status(verificationHint.Text);
        }
        async Task WaitForAiriVerification(){
            for(int i=0;i<240;i++){
                var item=await Service.Request("/api/verify/airi",null,3000);string state=Json.Text(item,"state");
                if(state=="verified"){bool first=verificationIds.Remove("airi");verificationHint.Text="AIRI：桥接、实际播放与完成回执全部验证通过。AIRI 更新可能覆盖桥接，失效后请重新连接并验证。";if(first){ShowCelebration("AIRI");MessageBox.Show(this,"AIRI 桥接已经安装并通过真实播放验证。\r\n\r\nAIRI 更新可能覆盖桥接组件；如果更新后桌宠无法发声，请重新执行“选择目录并接入验证”，程序会自动检查和修复。","AIRI 接入成功",MessageBoxButtons.OK,MessageBoxIcon.Information);}return;}
                if(state=="failed")throw new InvalidOperationException("AIRI 播放验证失败："+Json.Text(item,"error"));
                BusyStatus(state=="playing"?"AIRI 正在播放测试语音并同步嘴型……":"等待 AIRI 播放桥接返回状态……");await Task.Delay(500);
            }
            throw new InvalidOperationException("AIRI 未在两分钟内返回实际播放完成回执。请确认打开的是所选目录对应的已登录实例，再查看连接帮助。");
        }
        async Task BeginVerification(string source){
            if(!await Service.Running())throw new InvalidOperationException("请先启动语音服务。");
            if(source!="airi")throw new InvalidOperationException("Harness 请使用“选择目录并自动接入”。");
            if(!checks["airiEnabled"].Checked)throw new InvalidOperationException("AIRI 联动默认关闭。请先勾选“启用 AIRI 桌宠联动”并保存播放设置，再开始接入验证。");
            var enabled=Json.Copy(Config);enabled.airi.enabled=true;enabled.airi.output=outputChoice.SelectedIndex==1?"airi":"local";await Persist(enabled,new {airi=enabled.airi});
            using(var dialog=new FolderBrowserDialog{Description="请选择已经登录过、且包含 airi.exe 的 AIRI 原安装目录",ShowNewFolderButton=false}){
                if(fields["airiDirectory"].Text.Trim().Length>0)dialog.SelectedPath=Store.Resolve(fields["airiDirectory"].Text.Trim());
                if(dialog.ShowDialog(this)!=DialogResult.OK){Status("已取消定位，AIRI 当前设置保留。");return;}
                BusyStatus("第 1/3 步：正在安装 AIRI 联动桥接并打开已登录实例……");await InstallAiri(dialog.SelectedPath);
            }
            bool online=false;for(int i=0;i<120;i++){var state=await Service.Request("/health");if(Convert.ToBoolean(state["airiConnected"])){online=true;break;}BusyStatus("第 2/3 步：等待 AIRI 播放桥接连通……");await Task.Delay(500);}
            if(!online){pendingAiriVerification=true;verificationHint.Text="AIRI 联动已安装，但播放桥未在一分钟内连通。请退出其他 AIRI 实例，再点“打开当前 AIRI”；桥接上线后程序会继续验证。";ShowPage("connectionGuide");throw new InvalidOperationException(verificationHint.Text);}
            var item=await Service.Request("/api/verify/airi",new {});verificationIds["airi"]=Json.Text(item,"id");BusyStatus("第 3/3 步：正在验证 AIRI 实际播放与嘴型回执……");await WaitForAiriVerification();ShowPage("connectionGuide");Status(verificationHint.Text);
        }
        async Task ResumeAiriVerification(bool online){
            if(!Config.airi.enabled){pendingAiriVerification=false;return;}
            if(!pendingAiriVerification||!online||operation)return;
            pendingAiriVerification=false;
            try{var item=await Service.Request("/api/verify/airi",new {});verificationIds["airi"]=Json.Text(item,"id");verificationHint.Text="AIRI 已连通，正在自动复验实际播放。";Status(verificationHint.Text);}
            catch(Exception error){Status("AIRI 自动复验失败："+error.Message);}
        }
        async Task InstallAiri(string source){
            if(!File.Exists(Path.Combine(source,"airi.exe")))throw new InvalidOperationException("请选择包含 airi.exe 的原安装目录。");
            Status("正在检查并安装 AIRI 桥接；原 app.asar 会保留单文件备份……");
            try{await Service.RunTool("prepare_airi.py","--source "+ServiceClient.Quote(source)+" --root "+ServiceClient.Quote(Store.Root));}
            catch(Exception error){throw new InvalidOperationException("AIRI 桥接安装失败。若 AIRI 正在运行，请先正常退出后重试；安装失败时程序会恢复原文件。原因："+error.Message);}
            string registry=Store.Resolve("data/airi-installation.json"),temp=registry+".tmp";
            File.WriteAllText(temp,Json.Encode(new{directory=source}));if(File.Exists(registry))File.Replace(temp,registry,null);else File.Move(temp,registry);
            var next=Json.Copy(Config);next.airi.directory=Store.Portable(source);next.airi.enabled=true;await Persist(next,new {airi=next.airi});
            fields["airiDirectory"].Text=Config.airi.directory;checks["airiEnabled"].Checked=true;OpenLoggedAiri();
        }
        void UpdateVerification(Dictionary<string,object> states){
            object raw;var airi=states.TryGetValue("airi",out raw)?raw as Dictionary<string,object>:null;
            if(airiIndicator!=null){bool passed=Config.airi.enabled&&airi!=null&&Json.Text(airi,"state")=="verified";airiIndicator.Text="AIRI · "+(passed?"实际播放验证通过":!Config.airi.enabled?"未启用":"尚未通过播放验证");airiIndicator.ForeColor=passed?Color.FromArgb(25,128,72):ForeColor;}
            foreach(var entry in states){
                var item=entry.Value as Dictionary<string,object>;if(item==null)continue;string id;
                if(!verificationIds.TryGetValue(entry.Key,out id)||id!=Json.Text(item,"id"))continue;
                string state=Json.Text(item,"state");
                verificationHint.Text=entry.Key+"："+(state=="verified"?"验证正文与目标播放流程均已完成。":state=="waiting"?(connectionHints.ContainsKey(entry.Key)?connectionHints[entry.Key]+" ":"")+"等待 Harness 最终回复。":state=="playing"?"收到验证正文，正在生成／播放。":Json.Text(item,"error"));
                if(state=="verified"){verificationIds.Remove(entry.Key);ShowCelebration(entry.Key);}
            }
        }
        void OpenLoggedAiri(){
            string source=Store.Resolve(fields["airiDirectory"].Text.Trim());
            foreach(var process in Process.GetProcessesByName("airi"))try{
                string path=process.MainModule.FileName;
                if(path.Equals(Path.Combine(source,"airi.exe"),StringComparison.OrdinalIgnoreCase)&&process.MainWindowHandle!=IntPtr.Zero){ShowAiriWindow(process.MainWindowHandle,9);SetForegroundWindow(process.MainWindowHandle);Status("已唤醒正在使用的 AIRI；是否有播放桥接请运行验证。");return;}
            }catch(System.ComponentModel.Win32Exception){}
            string exe=Path.Combine(source,"airi.exe");
            if(!File.Exists(exe))throw new InvalidOperationException("请选择原 AIRI 安装目录。");
            Process.Start(new ProcessStartInfo(exe){WorkingDirectory=Path.GetDirectoryName(exe),UseShellExecute=true});
            Status("已打开 AIRI，沿用原用户登录资料；等待桌宠连接后验证。");
        }
        void ShowCelebration(string source){
            var snapshot=new Bitmap(Math.Max(1,shell.ClientSize.Width),Math.Max(1,shell.ClientSize.Height));shell.DrawToBitmap(snapshot,new Rectangle(Point.Empty,snapshot.Size));
            var banner=new Celebration(source+" · 恭喜接入成功",snapshot){Dock=DockStyle.Fill};shell.Controls.Add(banner);banner.BringToFront();
        }
    }
    sealed class Celebration:Control {
        readonly Timer timer=new Timer{Interval=30};readonly Random random=new Random();readonly string title;readonly Bitmap snapshot;int frame;
        readonly PointF[] pieces=new PointF[140];readonly Color[] colors={Color.DeepSkyBlue,Color.White,Color.MediumTurquoise,Color.DodgerBlue,Color.LightCyan,Color.Navy};
        public Celebration(string text,Bitmap background){title=text;snapshot=background;SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);for(int i=0;i<pieces.Length;i++)pieces[i]=new PointF((float)random.NextDouble(),(float)random.NextDouble()*-Height-random.Next(20,500));timer.Tick+=(s,e)=>{frame++;if(frame>110){Dispose();return;}Invalidate();};timer.Start();}
        protected override void OnPaintBackground(PaintEventArgs e){if(snapshot!=null)e.Graphics.DrawImage(snapshot,ClientRectangle);}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using(var shade=new SolidBrush(Color.FromArgb(42,3,18,30)))e.Graphics.FillRectangle(shade,ClientRectangle);for(int i=0;i<pieces.Length;i++){float x=pieces[i].X*Width+(float)Math.Sin(frame*.08+i)*22;float y=(pieces[i].Y+frame*(2.2f+i%5*.22f))%(Height+30);using(var b=new SolidBrush(colors[i%colors.Length]))e.Graphics.FillRectangle(b,x,y,6,i%2==0?16:7);}var box=new Rectangle(Math.Max(20,Width/2-260),Math.Max(30,Height/2-50),Math.Min(520,Width-40),100);using(var brush=new SolidBrush(Color.FromArgb(225,5,35,58)))e.Graphics.FillRectangle(brush,box);TextRenderer.DrawText(e.Graphics,title,new Font(Font.FontFamily,16F,FontStyle.Bold),box,Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
        protected override void Dispose(bool disposing){if(disposing){timer.Stop();timer.Dispose();if(snapshot!=null)snapshot.Dispose();}base.Dispose(disposing);}
    }
}
