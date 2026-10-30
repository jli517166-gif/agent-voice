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
        Label verificationHint,airiIndicator,airiConnectionsStatus,mcpConnectionHint,mcpHubHint;
        CheckBox autoReadToggle;
        bool autoReadSyncing;
        bool pendingAiriVerification;
        bool mcpChecking;DateTime lastMcpCheck=DateTime.MinValue;
        [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll",EntryPoint="ShowWindow")]static extern bool ShowAiriWindow(IntPtr window,int command);

        void BuildConnectionGuide(){
            var page=WebPage("connectionGuide");
            WebTitle(page,"连接帮助","MCP 接入标准、适用范围和故障处理。");
            var intro=WebCard(page,38,126,902,392);
            WebCardTitle(intro,"接入步骤与 MCP 自检");
            intro.Controls.Add(WebText("① 选择 Harness 目录，推荐安装 MCP。② 核对路径、启用状态、协议握手与只读状态调用。③ 客户端重载后自检，收到完整工具列表才庆祝。无需先启动 TTS。AIRI 另行验证。",7.5F,FontStyle.Regular,16,44,870,40,ContentAlignment.TopLeft));
            intro.Controls.Add(WebText("Codex",9F,FontStyle.Bold,16,86,300,24,ContentAlignment.MiddleLeft));
            intro.Controls.Add(WebText("选择 Codex 安装目录或 CODEX_HOME；安装后重载插件／会话，再点击 MCP 自检。通过语言命令调用工具；不提供常驻自定义按钮，也不接管内置朗读。",7.5F,FontStyle.Regular,16,110,870,40,ContentAlignment.TopLeft));
            intro.Controls.Add(WebText("DeepSeek Harness",9F,FontStyle.Bold,16,154,300,24,ContentAlignment.MiddleLeft));
            intro.Controls.Add(WebText("定位 .dsh/profiles/desktop，安装 MCP 客户端插件与当前程序路径配置，写后回读。重启 Harness 后自检。独立探针通过但客户端未加载，显示等待客户端。",7.5F,FontStyle.Regular,16,178,870,40,ContentAlignment.TopLeft));
            intro.Controls.Add(WebText("其他 Harness",9F,FontStyle.Bold,16,222,300,24,ContentAlignment.MiddleLeft));
            intro.Controls.Add(WebText("仅正式支持实测的 Codex 和 DeepSeek Harness；其他宿主按二次开发指南补适配器、MCP 配置与协议自检。",7.5F,FontStyle.Regular,16,246,870,20,ContentAlignment.MiddleLeft));
            intro.Controls.Add(WebText("AIRI",9F,FontStyle.Bold,16,270,300,24,ContentAlignment.MiddleLeft));
            intro.Controls.Add(WebText("默认不启用。勾选并保存联动后选择含 airi.exe 的原安装目录；程序安装桥接并保留 app.asar 备份。收到桌宠实际播放回执才算通过，心跳不算成功。",7.5F,FontStyle.Regular,16,294,870,40,ContentAlignment.TopLeft));
            intro.Controls.Add(WebText("MCP 失败会指出路径、插件、启用状态或协议错误。客户端未读取工具请重载后再自检。通过 MCP 自检不等于声音可用或自动朗读开启。备份在 data/connection-backups。",7.5F,FontStyle.Regular,16,340,870,40,ContentAlignment.TopLeft));

            var end=WebCard(page,38,526,902,133);
            verificationHint=WebText("推荐先安装 MCP，再执行自检。配置正确但客户端未重载时仍显示等待；此结果不测试语音。",8F,FontStyle.Regular,16,14,870,63,ContentAlignment.TopLeft);
            end.Controls.Add(verificationHint);
            end.Controls.Add(WebButton("返回",null,()=>ShowPage("connections"),16,83,70,38));
            end.Controls.Add(WebButton("打开 Harness",null,()=>ShowPage("harness"),94,83,170,38));
            end.Controls.Add(WebButton("打开 AIRI",null,()=>ShowPage("airi"),272,83,150,38));
            end.Controls.Add(WebButton("打开日志","查看连接与播放诊断记录。",()=>{Directory.CreateDirectory(Store.Resolve("data/logs"));ServiceClient.Open(Store.Resolve("data/logs"));},430,83,110,38));
        }
        static bool HarnessNeedsDesktop(Dictionary<string,object> launch){object value;return launch!=null&&launch.TryGetValue("manualRequired",out value)&&value!=null&&Convert.ToBoolean(value);}
        async Task InstallHarnessMcp(string source){
            BusyStatus("正在为 "+(source=="codex"?"Codex":"DeepSeek Harness")+" 安装聊天栏快捷控制……");
            var result=Json.Object(await Service.RunTool("install_mcp.py","--source "+source+" --root "+ServiceClient.Quote(Store.Root)));
            lastMcpCheck=DateTime.MinValue;await RefreshMcpConnection(true,true);
            string message=Json.Text(result,"message"),usage=source=="codex"?"在聊天栏技能选择器选择“语音控制”，或输入 $agent-voice-control；安装完成不表示客户端支持常驻自定义按钮。":"重启 DeepSeek Harness 后点击 MCP 自检，检查完整工具列表与只读状态调用。不需要朗读验证。聊天栏“语音”按钮与 MCP 工具是两条独立通道。";Status(message);MessageBox.Show(this,message+"\r\n\r\n"+usage+"\r\n底层工具名为 agent_voice。","聊天控制安装完成",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        readonly Dictionary<string,bool> mcpPassed=new Dictionary<string,bool>();
        readonly Dictionary<string,string> mcpMessages=new Dictionary<string,string>();
        async Task RefreshMcpConnection(bool verify,bool celebrate=false){
            if(mcpChecking||(!verify&&(DateTime.UtcNow-lastMcpCheck).TotalSeconds<10))return;
            mcpChecking=true;lastMcpCheck=DateTime.UtcNow;
            var messages=new List<string>();var passedNames=new List<string>();
            try{foreach(string source in new[]{"codex","deepseek"}){
                try{
                    if(verify&&celebrate&&source=="deepseek")await Service.RunTool("install_mcp.py","--source deepseek --root "+ServiceClient.Quote(Store.Root)+" --repair");
                    var result=Json.Object(await Service.RunTool("install_mcp.py","--source "+source+" --root "+ServiceClient.Quote(Store.Root)+(verify?" --self-test":" --check")));
                    bool loaded=result.ContainsKey("clientLoaded")&&Convert.ToBoolean(result["clientLoaded"]);
                    // A normal refresh must recover after restart, rather than leave a
                    // working client labelled failed until the user clicks self-test.
                    if(!verify&&loaded&&(!mcpPassed.ContainsKey(source)||!mcpPassed[source])){
                        result=Json.Object(await Service.RunTool("install_mcp.py","--source "+source+" --root "+ServiceClient.Quote(Store.Root)+" --self-test"));
                        mcpPassed[source]=result.ContainsKey("passed")&&Convert.ToBoolean(result["passed"]);
                    }
                    if(verify)mcpPassed[source]=result.ContainsKey("passed")&&Convert.ToBoolean(result["passed"]);
                    else if(!loaded)mcpPassed[source]=false;
                    mcpMessages[source]=McpStatusText(result,mcpPassed.ContainsKey(source)&&mcpPassed[source]);
                    messages.Add(source+"："+mcpMessages[source]);
                    if(verify&&mcpPassed[source])passedNames.Add(source);
                    if(verify&&verificationHint!=null)verificationHint.Text=Json.Text(result,"message");
                }catch(Exception error){mcpPassed[source]=false;mcpMessages[source]="检查未完成："+error.Message;messages.Add(source+"："+mcpMessages[source]);}
            }}finally{mcpChecking=false;}
            string message=string.Join("\r\n",messages);
            foreach(var label in new[]{mcpConnectionHint,mcpHubHint})if(label!=null&&!label.IsDisposed){label.Text=message;tips.SetToolTip(label,message);}
            UpdateHarnessIndicators(harnessStates,harnessOnline);
            if(celebrate&&passedNames.Count>0)ShowCelebration(string.Join("、",passedNames)+" MCP");
        }
        internal static string McpStatusText(Dictionary<string,object> result,bool passed){
            if(passed)return "MCP 自检通过 · 完整工具已加载";
            string state=Json.Text(result,"state");
            if(state=="awaiting-client")return "已安装并启用 · 等待客户端加载（重载插件后再检查）";
            if(state=="tools-read")return "客户端已加载 · 正在核对协议";
            if(state=="not-installed"||state=="not-configured")return "尚未安装 MCP";
            return Json.Text(result,"message");
        }
        async Task BeginAutoHarness(){
            using(var dialog=new FolderBrowserDialog{Description="选择 Codex 或 DeepSeek Harness 安装目录／用户配置目录，安装推荐的 MCP 控制",ShowNewFolderButton=false}){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                var connector=new ConnectionSetup(new SetupWorkflow(Store));connector.Report=BusyStatus;
                var result=await connector.Harness(dialog.SelectedPath);string source=Json.Text(result,"source");
                bool passed=result.ContainsKey("passed")&&Convert.ToBoolean(result["passed"]);
                if(source.Length>0)mcpPassed[source]=passed;
                verificationHint.Text=Json.Text(result,"message");Status(verificationHint.Text);
                if(passed)ShowCelebration(source+" MCP");
                lastMcpCheck=DateTime.MinValue;await RefreshMcpConnection(false);ShowPage("connectionGuide");
            }
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
            if(airiIndicator!=null||airiConnectionsStatus!=null){bool passed=Config.airi.enabled&&airi!=null&&Json.Text(airi,"state")=="verified";UpdateAiriStatus("AIRI · "+(passed?"实际播放验证通过":!Config.airi.enabled?"未启用":"尚未通过播放验证"),passed?Color.FromArgb(25,128,72):ForeColor);}
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
        void BuildConnectionHub(){
            var page=WebPage("connections");
            WebTitle(page,"连接","推荐安装 MCP；自检安装、启用与工具调用，不生成语音。");
            page.Controls.Add(WebButton("接入验证说明","查看识别依据、真实验证标准和故障排查。",()=>ShowPage("connectionGuide"),784,46,156,39));
            var clients=WebCard(page,38,126,902,350);
            ClientIcon(clients,0,"Cx");
            clients.Controls.Add(WebText("Codex",13F,FontStyle.Bold,70,18,600,26,ContentAlignment.MiddleLeft));
            clients.Controls.Add(WebText("MCP 语言控制与生成过程自动朗读。",9F,FontStyle.Regular,70,47,650,20,ContentAlignment.MiddleLeft));
            clients.Controls.Add(WebHarnessStatus("codex","Codex",70,73,640));
            clients.Controls.Add(WebAsyncButton("安装 MCP","选择目录，自动识别并安装 MCP，再核对启用状态、协议与客户端工具回执。",BeginAutoHarness,762,39,132,39));
            WebDivider(clients,16,117,870);

            ClientIcon(clients,117,"DS");
            clients.Controls.Add(WebText("DeepSeek Harness",13F,FontStyle.Bold,70,135,600,26,ContentAlignment.MiddleLeft));
            clients.Controls.Add(WebText("聊天栏控制语音与唱歌；可把当前声音注册给 dsh-pet。",9F,FontStyle.Regular,70,164,650,20,ContentAlignment.MiddleLeft));
            clients.Controls.Add(WebHarnessStatus("deepseek","DeepSeek Harness",70,190,640));
            clients.Controls.Add(WebAsyncButton("安装 MCP","选择目录，安装 MCP；客户端重载后再次自检，无需发送验证消息。",BeginAutoHarness,762,156,132,39));
            WebDivider(clients,16,234,870);

            ClientIcon(clients,234,"AI");
            clients.Controls.Add(WebText("AIRI",13F,FontStyle.Bold,70,252,520,26,ContentAlignment.MiddleLeft));
            clients.Controls.Add(WebText("使用你已经登录的原 AIRI，不另建副本。",9F,FontStyle.Regular,70,281,520,20,ContentAlignment.MiddleLeft));
            airiConnectionsStatus=WebText("AIRI · 未启用",9F,FontStyle.Regular,70,307,540,20,ContentAlignment.MiddleLeft);
            clients.Controls.Add(airiConnectionsStatus);
            var airiToggle=WebCheck("启用联动",636,275,120,32,Config.airi.enabled);airiToggle.Name="airiLinkToggle";airiToggle.AccessibleName="启用 AIRI 联动";
            airiToggle.Checked=Config.airi.enabled;
            airiToggle.CheckedChanged+=async(s,e)=>{
                try{
                    var next=Json.Copy(Config);next.airi.enabled=airiToggle.Checked;await Persist(next,new{airi=next.airi});
                    if(checks.ContainsKey("airiEnabled"))checks["airiEnabled"].Checked=airiToggle.Checked;
                    Status(airiToggle.Checked?"AIRI 联动已启用。":"AIRI 联动已关闭。");
                }catch(Exception error){StatusError(error.Message);}
            };
            clients.Controls.Add(airiToggle);
            clients.Controls.Add(WebButton("AIRI 设置","打开 AIRI 页面，选择目录并做真实播放验证。",()=>ShowPage("airi"),768,273,126,39));

            var mcp=WebCard(page,38,484,902,96);WebCardTitle(mcp,"MCP 控制状态");
            mcpHubHint=WebText("MCP 安装与客户端加载分开检查，不测试语音播放。",11F,FontStyle.Regular,16,42,660,42);mcpHubHint.AutoEllipsis=true;mcp.Controls.Add(mcpHubHint);
            mcp.Controls.Add(WebAsyncButton("检查 MCP","检查当前程序路径和客户端读取工具的回执。",async()=>{lastMcpCheck=DateTime.MinValue;await RefreshMcpConnection(true,true);},742,40,144,38));
            var auto=WebCard(page,38,588,902,80);
            auto.Controls.Add(WebText("自动朗读",10F,FontStyle.Bold,16,12,300,24,ContentAlignment.MiddleLeft));
            auto.Controls.Add(WebText("任务执行过程中的正文与最终回复都可朗读；完整句子按顺序播放。",9F,FontStyle.Regular,16,38,620,34,ContentAlignment.TopLeft));
            auto.Controls.Add(WebButton("Harness 设置","分别启用 Codex 或 DeepSeek Harness。",()=>ShowPage("harness"),656,22,144,39));
            autoReadToggle=new CheckBox{Text="启用",AutoSize=false,Bounds=new Rectangle(808,26,92,28),BackColor=Color.Transparent};
            autoReadToggle.Checked=AnyHarnessEnabled();
            autoReadToggle.CheckedChanged+=async(s,e)=>{
                if(autoReadSyncing)return;
                try{
                    var next=Json.Copy(Config);
                    foreach(var key in next.harness.Keys.ToArray())next.harness[key]=autoReadToggle.Checked;
                    await Persist(next,new{harness=next.harness});
                    if(checks.ContainsKey("codex"))checks["codex"].Checked=next.harness["codex"];
                    if(checks.ContainsKey("deepseek"))checks["deepseek"].Checked=next.harness["deepseek"];
                    Status(autoReadToggle.Checked?"自动朗读已开启。":"自动朗读已关闭；仍可手动生成和播放。");
                }catch(Exception error){StatusError(error.Message);}
            };
            auto.Controls.Add(autoReadToggle);

            var footer=WebCard(page,38,678,902,74);
            footer.Controls.Add(WebButton("开发其他客户端连接器","新增适配器前先阅读 Harness 二次开发指南。",()=>ServiceClient.Open(Store.Resolve("docs/guides/Harness二次开发指南.md")),16,16,236,39));
            footer.Controls.Add(WebText("当前实测支持范围：Codex、DeepSeek Harness。",9F,FontStyle.Regular,270,16,610,20,ContentAlignment.MiddleLeft));
            footer.Controls.Add(WebText("未真机验证的 Harness 不进入正式支持列表。",8.8F,FontStyle.Regular,270,40,610,20,ContentAlignment.MiddleLeft));
        }
        void ClientIcon(ScenePanel card,int y,string text){
            var panel=new Panel{BackColor=Mix(CardColor,ForeColor,8),Bounds=new Rectangle(16,y+37,42,42),Margin=new Padding(0)};
            panel.Controls.Add(WebText(text,9F,FontStyle.Bold,0,0,42,42,ContentAlignment.MiddleCenter));
            card.Controls.Add(panel);
        }
        bool AnyHarnessEnabled(){foreach(var value in Config.harness.Values)if(value)return true;return false;}
        void SyncAutoRead(){
            if(autoReadToggle==null||autoReadSyncing)return;
            autoReadSyncing=true;
            try{autoReadToggle.Checked=AnyHarnessEnabled();}finally{autoReadSyncing=false;}
        }
        void UpdateAiriStatus(string text,Color color){
            if(airiIndicator!=null){airiIndicator.Text=text;airiIndicator.ForeColor=color;}
            if(airiConnectionsStatus!=null){airiConnectionsStatus.Text=text;airiConnectionsStatus.ForeColor=color;}
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
