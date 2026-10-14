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
            Add(card,Hint("① 首页先试听声音并启动服务。② 点击目标的“全自动接入”，选择安装目录或用户配置目录。③ 程序安装连接器、启动真实 Harness 验证会话、等待最终回复并确认语音播放。全部通过后显示绿色状态和礼炮。某一步无法自动完成时，会保留已完成设置并给出具体的手动处理方法。"));
            foreach(var entry in new[]{
                new[]{"Codex","程序定位 CODEX_HOME/sessions（默认当前用户 .codex/sessions），或你所选目录中的会话目录，重新加载监听后开始复验。在 Codex 发送验证提示；最终回复进入队列。若未收到，检查是否是同一 Windows 用户及会话目录。"},
                new[]{"Claude Code","验证时自动合并当前用户 .claude/settings.json 的 Stop hook（尊重 CLAUDE_CONFIG_DIR），并备份旧文件。重新打开 Claude Code，检查 /hooks 中 Stop 命令是否加载、是否获准运行，再发送验证提示。"},
                new[]{"pi","验证时安装当前用户 .pi/agent/extensions/agent-voice.ts（尊重 PI_CODING_AGENT_DIR）。执行 /reload 或重启 pi，确认扩展加载无错误，再发送验证提示。移动语音服务目录后重新定位接入。"},
                new[]{"OpenCode","验证时安装当前用户 .config/opencode/plugins/agent-voice.js（尊重 XDG_CONFIG_HOME）。退出并重启 OpenCode，检查插件日志，再发送验证提示；session.idle 后最新完成的助手回复才会朗读。"},
                new[]{"DeepSeek Harness","验证时自动定位所选目录或当前用户 .dsh/profiles/desktop，安装并启用 dsh-agent-voice-public。程序备份配置并关闭已识别的旧 dsh-tts-autoread，防止重复朗读；其他插件保持。重新加载 Harness，再发送验证提示；turn/end 才提交最终正文。"},
                new[]{"AIRI","点击验证，未接入时选择含 airi.exe 的原安装目录。程序自动准备联动副本、启用并打开，沿用原 ai.moeru.airi 用户资料及账号。原实例没有桥接时需退出后从本入口重开。桥接连通后自动提交播放验证，收到桌宠原生播放完成回执才通过，心跳不算成功。"}
            }){var section=Card(stack,entry[0]);Add(section,Hint(entry[1]));}
            var end=Card(stack,"结果与故障提示");verificationHint=Hint("尚未开始验证。监听就绪和连接器心跳只表示可用状态。换机、服务重启或超过 15 分钟需重新验证。");Add(end,verificationHint);
            Add(end,Hint("收不到正文：确认目标已启用、配置目录正确、客户端已重载、验证提示发给了对应 Harness。收到正文但失败：检查声音资源／输出设备与日志。AIRI 失败：确认正确实例已打开及播放桥已加载。安装备份在 data/connection-backups，可打开服务日志排查。"));
            Add(end,Row(Btn("返回",()=>ShowPage("connections")),Btn("打开 Harness",()=>ShowPage("harness")),Btn("打开 AIRI",()=>ShowPage("airi")),EntryBtn("打开日志","查看连接与播放诊断记录。",()=>{Directory.CreateDirectory(Store.Resolve("data/logs"));ServiceClient.Open(Store.Resolve("data/logs"));})));
        }
        string HarnessManualHelp(string source){
            if(source=="codex")return "请确认 Codex CLI 已登录且可运行 codex exec；也可在 Codex 中手动发送页面显示的验证短句。";
            if(source=="claude")return "请确认 Claude Code CLI 已登录，重新打开 Claude Code 后手动发送验证短句，并检查 /hooks。";
            if(source=="pi")return "请执行 /reload 或重启 pi，再手动发送验证短句，检查扩展加载日志。";
            if(source=="opencode")return "请重启 OpenCode，再手动发送验证短句，检查插件日志。";
            return "DeepSeek Harness 暂无统一的无界面命令；请重新加载桌面端，再手动发送验证短句。";
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
        async Task BeginVerification(string source){
            if(!await Service.Running())throw new InvalidOperationException("请先启动语音服务。");
            if(source=="airi"&&!Config.airi.enabled){var enabled=Json.Copy(Config);enabled.airi.enabled=true;checks["airiEnabled"].Checked=true;await Persist(enabled,new {airi=enabled.airi});}
            else if(source!="airi"&&!Config.harness[source]){var enabled=Json.Copy(Config);enabled.harness[source]=true;checks[source].Checked=true;await Persist(enabled,new {harness=enabled.harness});}
            var health=await Service.Request("/health");bool connected=false;
            if(source=="airi")connected=Convert.ToBoolean(health["airiConnected"]);
            else{object raw;var states=health.TryGetValue("harnessConnections",out raw)?raw as Dictionary<string,object>:null;var presence=states!=null&&states.TryGetValue(source,out raw)?raw as Dictionary<string,object>:null;string state=presence==null?"":Json.Text(presence,"state");connected=state=="connected"||state=="recent_event";}
            string harnessLocation="";
            if(source!="airi"||!connected){
                using(var dialog=new FolderBrowserDialog{Description="请选择 "+(source=="airi"?"AIRI 原安装目录":source+" 安装目录（也可选择其用户配置目录）"),ShowNewFolderButton=false}){
                    if(source=="airi"&&fields["airiDirectory"].Text.Trim().Length>0)dialog.SelectedPath=Store.Resolve(fields["airiDirectory"].Text.Trim());
                    if(dialog.ShowDialog(this)!=DialogResult.OK){Status("已取消定位，当前接入状态保留。");return;}
                    if(source=="airi")await InstallAiri(dialog.SelectedPath);
                    else{
                        harnessLocation=dialog.SelectedPath;BusyStatus("第 1/4 步：正在安装并校验 "+source+" 连接器……");
                        var result=Json.Object(await Service.RunTool("connect_harness.py","--source "+source+" --root "+ServiceClient.Quote(Store.Root)+" --location "+ServiceClient.Quote(dialog.SelectedPath)));
                        var next=Json.Copy(Config);next.harness[source]=true;checks[source].Checked=true;await Persist(next,new {harness=next.harness});
                        if(source=="codex")await Service.Request("/api/connections/reload",new {});
                        connectionHints[source]="连接器安装完成，正在执行真实验证。";
                    }
                }
            }
            if(source=="airi"){
                bool online=false;for(int i=0;i<30;i++){var state=await Service.Request("/health");if(Convert.ToBoolean(state["airiConnected"])){online=true;break;}await Task.Delay(500);}
                if(!online){pendingAiriVerification=true;verificationHint.Text="AIRI 已配置，等待播放桥连通后自动复验。若原实例未加载组件，请退出它，再从本页打开已登录的 AIRI。";ShowPage("connectionGuide");Status(verificationHint.Text);return;}
            }
            var item=await Service.Request("/api/verify/"+source,new {});
            verificationIds[source]=Json.Text(item,"id");
            if(source!="airi"){
                string challenge=Json.Text(item,"text");string prompt="这是 agent语音 的自动接入验证。请只回复下面这段文字，不加解释和额外标点：\n"+challenge;
                BusyStatus("第 2/4 步：正在启动 "+source+" 的真实验证会话……");
                try{await Service.RunTool("verify_harness.py","--source "+source+" --location "+ServiceClient.Quote(harnessLocation)+" --prompt "+ServiceClient.Quote(prompt)+" --timeout 180");}
                catch(Exception error){Clipboard.SetText("请只回复下面这段文字，不加解释和标点：\r\n"+challenge);verificationHint.Text=source+"：自动验证停在“启动 Harness 并获取回复”。验证短句已复制。"+HarnessManualHelp(source);ShowPage("connectionGuide");throw new InvalidOperationException(verificationHint.Text+" 原因："+error.Message);}
                BusyStatus("第 3/4 步：Harness 已回复，正在确认连接器回执……");
                await WaitForHarnessVerification(source,challenge);
                verificationHint.Text=source+"：全自动接入验证成功。";
            }else verificationHint.Text="AIRI：测试语音已排队，等待桌宠播放完成回执。";
            ShowPage("connectionGuide");Status(verificationHint.Text);
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
            string sibling=source.TrimEnd('\\')+"-agent语音联动版";
            string target=Directory.Exists(sibling)?sibling:Path.Combine(Store.Root,"data","airi-companion");
            bool targetRunning=false;
            foreach(var process in Process.GetProcessesByName("airi"))try{if(process.MainModule.FileName.Equals(Path.Combine(target,"airi.exe"),StringComparison.OrdinalIgnoreCase))targetRunning=true;}catch(System.ComponentModel.Win32Exception){}
            if(targetRunning){string marker=Path.Combine(target,"agent-voice-integration.json");if(!File.Exists(marker)||!Store.Resolve(Json.Text(Json.Object(File.ReadAllText(marker)),"voiceRoot")).Equals(Store.Root,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("当前联动 AIRI 正在运行且连接位置不同，请退出它后再更新，登录资料会保留。");}
            else{Status("正在自动准备 AIRI 联动组件……");await Service.RunTool("prepare_airi.py","--source "+ServiceClient.Quote(source)+" --target "+ServiceClient.Quote(target)+" --root "+ServiceClient.Quote(Store.Root)+(Directory.Exists(target)?" --refresh":""));}
            string registry=Store.Resolve("data/airi-installation.json"),temp=registry+".tmp";
            File.WriteAllText(temp,Json.Encode(new{original=source,companion=target}));if(File.Exists(registry))File.Replace(temp,registry,null);else File.Move(temp,registry);
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
            string companion=source.TrimEnd('\\')+"-agent语音联动版";
            string registry=Store.Resolve("data/airi-installation.json");
            if(File.Exists(registry)){var installed=Json.Object(File.ReadAllText(registry));if(Store.Resolve(Json.Text(installed,"original")).Equals(source,StringComparison.OrdinalIgnoreCase))companion=Store.Resolve(Json.Text(installed,"companion"));}
            foreach(var process in Process.GetProcessesByName("airi"))try{
                string path=process.MainModule.FileName;
                if((path.Equals(Path.Combine(source,"airi.exe"),StringComparison.OrdinalIgnoreCase)||path.Equals(Path.Combine(companion,"airi.exe"),StringComparison.OrdinalIgnoreCase))&&process.MainWindowHandle!=IntPtr.Zero){ShowAiriWindow(process.MainWindowHandle,9);SetForegroundWindow(process.MainWindowHandle);Status("已唤醒正在使用的 AIRI；是否有播放桥接请运行验证。");return;}
            }catch(System.ComponentModel.Win32Exception){}
            string exe=Path.Combine(companion,"airi.exe");
            if(!File.Exists(exe))exe=Path.Combine(source,"airi.exe");
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
        readonly PointF[] pieces=new PointF[140];readonly Color[] colors={Color.HotPink,Color.Gold,Color.MediumTurquoise,Color.MediumPurple,Color.Coral,Color.LightGreen};
        public Celebration(string text,Bitmap background){title=text;snapshot=background;SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);for(int i=0;i<pieces.Length;i++)pieces[i]=new PointF((float)random.NextDouble(),(float)random.NextDouble()*-Height-random.Next(20,500));timer.Tick+=(s,e)=>{frame++;if(frame>110){Dispose();return;}Invalidate();};timer.Start();}
        protected override void OnPaintBackground(PaintEventArgs e){if(snapshot!=null)e.Graphics.DrawImage(snapshot,ClientRectangle);}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using(var shade=new SolidBrush(Color.FromArgb(36,24,18,34)))e.Graphics.FillRectangle(shade,ClientRectangle);for(int i=0;i<pieces.Length;i++){float x=pieces[i].X*Width+(float)Math.Sin(frame*.08+i)*22;float y=(pieces[i].Y+frame*(2.2f+i%5*.22f))%(Height+30);using(var b=new SolidBrush(colors[i%colors.Length]))e.Graphics.FillRectangle(b,x,y,6,i%2==0?16:7);}var box=new Rectangle(Math.Max(20,Width/2-260),Math.Max(30,Height/2-50),Math.Min(520,Width-40),100);using(var brush=new SolidBrush(Color.FromArgb(220,40,34,54)))e.Graphics.FillRectangle(brush,box);TextRenderer.DrawText(e.Graphics,title,new Font(Font.FontFamily,16F,FontStyle.Bold),box,Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
        protected override void Dispose(bool disposing){if(disposing){timer.Stop();timer.Dispose();if(snapshot!=null)snapshot.Dispose();}base.Dispose(disposing);}
    }
}
