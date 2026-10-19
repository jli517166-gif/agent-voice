using Button = AgentVoice.AnimeButton;
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
    public sealed partial class MainForm:Form {
        [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
        public readonly SettingsStore Store;
        public readonly ServiceClient Service;
        public Settings Config;
        public Color Accent,CardColor;
        public bool HasBackground {get{return background!=null;}}
        Bitmap background,scaledBackground,defaultBackdrop,pageBackdrop,scaledPageBackdrop;
        readonly Dictionary<string,ScenePanel> pages=new Dictionary<string,ScenePanel>();
        readonly Dictionary<string,Button> navigationButtons=new Dictionary<string,Button>();
        readonly List<string> pageHistory=new List<string>();
        readonly Dictionary<string,List<Label>> harnessIndicators=new Dictionary<string,List<Label>>();
        Dictionary<string,object> harnessStates;
        bool harnessOnline;
        string modelStatus="not_loaded";
        ScenePanel shell,content,statusBar,sidebar;
        BufferedTable workspace,outer;
        readonly InterfaceSounds interfaceSounds=new InterfaceSounds();readonly bool testingMode;
        Label footer,connection,voiceHint,harnessSummary,pageTitle,statusSystem,statusEngine,statusVoice,statusOutput;
        NotifyIcon tray;
        readonly ToolTip tips=new ToolTip{AutomaticDelay=350,AutoPopDelay=10000,ReshowDelay=100,ShowAlways=true};
        readonly Timer poll=new Timer{Interval=1800};
        bool polling,operation,exiting;
        BusyOverlay busyOverlay;
        string page="home",lastJob="";
        Button serviceToggle,generate,saveVoiceButton,playButton,backButton;
        ComboBox phrases;
        TextBox body,profileName;
        readonly Dictionary<string,TextBox> fields=new Dictionary<string,TextBox>();
        readonly Dictionary<string,NumericUpDown> numbers=new Dictionary<string,NumericUpDown>();
        readonly Dictionary<string,CheckBox> checks=new Dictionary<string,CheckBox>();
        ComboBox engineChoice,outputChoice;
        TextBox baseModelPath;
        Label engineDetails;
        readonly List<Control> cosyOnlyControls=new List<Control>();
        ListBox profileList;
        Label profileEmpty;
        SavedVoice deleted;
        public MainForm(SettingsStore store,bool testing=false){
            Store=store;Service=new ServiceClient(store);Config=store.Read();testingMode=testing;
            Text="Agent Voice · 本地语音工作台";Font=new Font("Microsoft YaHei UI",10F);AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(1180,800);MinimumSize=new Size(900,620);StartPosition=FormStartPosition.CenterScreen;
            SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);
            shell=new ScenePanel{Host=this,Dock=DockStyle.Fill};Controls.Add(shell);
            outer=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,BackColor=Color.Transparent,Padding=new Padding(0)};outer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));shell.Controls.Add(outer);
            sidebar=new ScenePanel{Host=this,Dock=DockStyle.Fill,Padding=new Padding(8),Margin=new Padding(0)};outer.Controls.Add(sidebar,0,0);
            var sideLayout=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,BackColor=Color.Transparent};sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,70));sideLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,210));sidebar.Controls.Add(sideLayout);
            var brand=new BufferedFlow{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,BackColor=Color.Transparent,Padding=new Padding(5,8,0,0)};brand.Controls.Add(new Label{Text="AGENT VOICE",Font=new Font("Segoe UI",9F,FontStyle.Bold),AutoSize=true,Margin=new Padding(0,0,0,3)});brand.Controls.Add(new Label{Text="本地语音工作台",Font=new Font(Font.FontFamily,8.5F),AutoSize=true,Margin=new Padding(0)});sideLayout.Controls.Add(brand,0,0);
            var navigation=new BufferedFlow{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,BackColor=Color.Transparent,Padding=new Padding(0,4,0,4)};sideLayout.Controls.Add(navigation,0,1);
            AddNav(navigation,"朗读","home","输入文字、常用句与播放控制。",true);
            
            AddNav(navigation,"音色包","configuration","导入或导出声音模型。",true);
            AddNav(navigation,"声音方案","voices","保存和切换整套声音设置。",true);
            AddNav(navigation,"连接","connections","连接 Harness 或 AIRI。",true);
            AddNav(navigation,"训练","training","准备数据并校验训练模型。",false);
            AddNav(navigation,"设置","settings","调整输出、引擎、外观和维护选项。",false);
            var stateBox=new BufferedTable{Name="sidebarTransport",Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,BackColor=Color.Transparent,Padding=new Padding(2,8,2,0),Margin=new Padding(0)};stateBox.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));stateBox.RowStyles.Add(new RowStyle(SizeType.Absolute,30));stateBox.RowStyles.Add(new RowStyle(SizeType.Percent,100));stateBox.RowStyles.Add(new RowStyle(SizeType.Absolute,48));stateBox.RowStyles.Add(new RowStyle(SizeType.Absolute,48));stateBox.Controls.Add(new Label{Text="当前状态",Font=new Font(Font.FontFamily,9F,FontStyle.Bold),Dock=DockStyle.Fill,Margin=new Padding(0),TextAlign=ContentAlignment.MiddleLeft},0,0);connection=new Label{Text="服务：检查中\r\n声音："+(Config.voice.assetScope=="private"?"私有声音":"当前配置"),AutoSize=false,AutoEllipsis=true,Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,9F),TextAlign=ContentAlignment.TopLeft,Margin=new Padding(0,4,0,0)};stateBox.Controls.Add(connection,0,1);sideLayout.Controls.Add(stateBox,0,2);
            workspace=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,BackColor=Color.Transparent};workspace.RowStyles.Add(new RowStyle(SizeType.Absolute,76));workspace.RowStyles.Add(new RowStyle(SizeType.Absolute,0));workspace.RowStyles.Add(new RowStyle(SizeType.Percent,100));workspace.RowStyles.Add(new RowStyle(SizeType.Absolute,48));outer.Controls.Add(workspace,1,0);
            var heading=new BufferedFlow{Dock=DockStyle.Fill,FlowDirection=FlowDirection.LeftToRight,WrapContents=false,BackColor=Color.Transparent,Padding=new Padding(0,5,0,5)};backButton=EntryBtn("← 返回","返回上一页，并保留尚未保存的输入。",GoBack);backButton.Name="navigationBack";backButton.Visible=false;backButton.Margin=new Padding(0,8,12,8);heading.Controls.Add(backButton);pageTitle=new Label{Text="首页",Font=new Font(Font.FontFamily,26F,FontStyle.Bold),AutoSize=true,Margin=new Padding(0,10,0,10),Padding=new Padding(0,0,0,3)};heading.Controls.Add(pageTitle);workspace.Controls.Add(heading,0,0);
            statusBar=new ScenePanel{Host=this,Card=true,Dock=DockStyle.Fill,Margin=new Padding(0),Padding=new Padding(6,4,6,4),Visible=false};var statusGrid=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,BackColor=Color.Transparent};for(int i=0;i<4;i++)statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));statusSystem=TopStatus("● 系统 · 检查中");statusEngine=TopStatus("● 引擎 · "+Config.voice.engine);statusVoice=TopStatus("● 声音 · "+(Config.voice.baseModel.Length>0?"已配置":"待配置"));statusOutput=TopStatus("● 输出 · TTS 直出");statusGrid.Controls.Add(statusSystem,0,0);statusGrid.Controls.Add(statusEngine,1,0);statusGrid.Controls.Add(statusVoice,2,0);statusGrid.Controls.Add(statusOutput,3,0);statusBar.Controls.Add(statusGrid);workspace.Controls.Add(statusBar,0,1);
            content=new ScenePanel{Host=this,Dock=DockStyle.Fill,ComposeChildren=true};workspace.Controls.Add(content,0,2);
            footer=new Label{Dock=DockStyle.Fill,AutoEllipsis=false,Font=new Font(Font.FontFamily,9.5F,FontStyle.Bold),Text=Config.voice.assetScope=="private"?"状态：已载入认可版，私有声音资源单独保存。":"状态：请选择本地 TTS 模型包与参考条件。",TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(14,5,14,5),AccessibleName="操作状态提示"};workspace.Controls.Add(footer,0,3);
            BuildSavedVoices();BuildHomeWeb();BuildSettings();EnsureProfileFolders();BindVoice();BindSettings();ApplyTheme();ShowPage("home");
            if(!testing){tray=new NotifyIcon{Text="agent语音",Icon=SystemIcons.Application,Visible=true};var menu=new ContextMenuStrip();menu.Items.Add("打开程序",null,(s,e)=>OpenMain());menu.Items.Add("停止朗读",null,async(s,e)=>await StopSpeech());
                menu.Items.Add("退出",null,async(s,e)=>{exiting=true;try{if(await Service.Running())await Service.Stop();}catch(Exception x){Status(x.Message);}Close();});tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>OpenMain();
                ApplyIcon();poll.Tick+=async(s,e)=>await Poll();poll.Start();Shown+=async(s,e)=>await Poll();}
        }
        void OpenMain(){Show();WindowState=FormWindowState.Normal;Activate();}
        Label SideLabel(string text){return new Label{Text=text,AutoSize=false,Width=150,Height=18,TextAlign=ContentAlignment.BottomLeft,Font=new Font(Font.FontFamily,8F),Margin=new Padding(10,6,0,4)};}
        Label TopStatus(string text){return new Label{Text=text,AutoSize=false,AutoEllipsis=true,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=new Font(Font.FontFamily,8.5F),Margin=new Padding(2,0,2,0),Padding=new Padding(4,0,4,0)};}
        void AddNav(FlowLayoutPanel navigation,string text,string id,string note,bool important){var button=EntryBtn(text,note,()=>ShowPage(id));button.Tag=important?"nav-primary":"nav";button.Name="nav_"+id;button.AutoSize=false;button.Width=126;button.Height=44;button.TextAlign=ContentAlignment.MiddleLeft;button.Padding=new Padding(12,0,0,0);button.Margin=new Padding(0,1,0,1);navigationButtons[id]=button;navigation.Controls.Add(button);}
        Button Btn(string text,Action action){var b=new AnimeButton{Text=text,AutoSize=true,MinimumSize=new Size(108,40),Margin=new Padding(0,3,10,3),Padding=new Padding(10,0,10,0),Cursor=Cursors.Hand,Tag="secondary"};b.BorderSize=1;b.Click+=(s,e)=>{try{action();}catch(Exception exc){Status(exc.Message);}};return b;}
        Button Primary(Button button){button.Tag="primary";button.Font=new Font(Font.FontFamily,Font.Size,FontStyle.Bold);button.MinimumSize=new Size(Math.Max(128,button.MinimumSize.Width),42);return button;}
        Button Danger(Button button){button.Tag="danger";return button;}
        Button EntryBtn(string text,string note,Action action){var button=Btn(text,action);button.AccessibleDescription=note;tips.SetToolTip(button,note);return button;}
        Button AsyncBtn(string text,Func<Task> action){var b=Btn(text,()=>{});b.Click+=async(s,e)=>await Guard(action,b.Text);return b;}
        void StyleStatus(bool error){if(footer==null)return;footer.BackColor=error?Color.FromArgb(255,226,230):Mix(CardColor,Accent,14);footer.ForeColor=error?Color.FromArgb(142,20,42):(BackColor.GetBrightness()<.4F?Color.FromArgb(225,245,255):Color.FromArgb(16,24,32));}
        void Status(string text){if(!IsDisposed){footer.Text="状态："+text.Replace("\r"," ").Replace("\n"," ");footer.AccessibleDescription=text;StyleStatus(false);}}
        void StatusError(string text){if(!IsDisposed){footer.Text="操作失败："+text.Replace("\r"," ").Replace("\n"," ");footer.AccessibleDescription=text;StyleStatus(true);}}
        void BusyStatus(string text){Status(text);if(busyOverlay!=null)busyOverlay.SetMessage(text);Application.DoEvents();}
        void ShowBusy(string text){
            interfaceSounds.Stop();
            if(busyOverlay!=null){busyOverlay.SetMessage(text);return;}
            var snapshot=new Bitmap(Math.Max(1,shell.ClientSize.Width),Math.Max(1,shell.ClientSize.Height));shell.DrawToBitmap(snapshot,new Rectangle(Point.Empty,snapshot.Size));
            string animation=Store.Resolve(Config.appearance.loadingAnimation);if(!File.Exists(animation))animation=DefaultMascotPath();busyOverlay=new BusyOverlay(text,snapshot,animation);shell.Controls.Add(busyOverlay);busyOverlay.BringToFront();busyOverlay.Focus();
        }
        void HideBusy(){if(busyOverlay==null)return;var overlay=busyOverlay;busyOverlay=null;overlay.Dispose();shell.Invalidate(true);}
        async Task Guard(Func<Task> action,string label){if(operation)return;operation=true;Cursor=Cursors.AppStarting;ShowBusy(label+"…");try{await action();}catch(Exception exc){StatusError(exc.Message);}finally{HideBusy();operation=false;Cursor=Cursors.Default;}}
        public void ShowPage(string id){
            Navigate(id,true);
        }
        void GoBack(){
            if(page=="home")return;
            string target="home";
            if(pageHistory.Count>0){int last=pageHistory.Count-1;target=pageHistory[last];pageHistory.RemoveAt(last);}
            Navigate(target,false);
        }
        void Navigate(string id,bool remember){
            if(id=="setup"){RunConfiguration();return;}
            if(!pages.ContainsKey(id))return;if(page==id&&pages[id].Visible)return;
            if(id=="home")pageHistory.Clear();
            else if(remember){if(pageHistory.Count==64)pageHistory.RemoveAt(0);pageHistory.Add(page);}
            SendMessage(content.Handle,0x000B,IntPtr.Zero,IntPtr.Zero);content.SuspendLayout();
            try{foreach(var pair in pages)pair.Value.Visible=pair.Key==id;pages[id].BringToFront();page=id;if(id=="home"||IsWebLayoutPage(id))WebFit.Attach(pages[id]);sidebar.Visible=true;outer.ColumnStyles[0].Width=150;workspace.RowStyles[0].Height=76;backButton.Visible=id!="home";pageTitle.Text=PageName(id);pageTitle.Visible=id!="home"&&!IsWebLayoutPage(id);statusBar.Visible=false;workspace.RowStyles[1].Height=0;foreach(var pair in navigationButtons)StyleButton(pair.Value,pair.Key==id);}
            finally{content.ResumeLayout(true);SendMessage(content.Handle,0x000B,new IntPtr(1),IntPtr.Zero);content.Invalidate(true);}

        }
        void PlayInterfaceSound(int step){bool listening=true;try{listening=System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p=>p.Port==Config.port);}catch(System.Net.NetworkInformation.NetworkInformationException){}if(InterfaceSounds.Allowed(SetupPreference().interfaceSounds,testingMode,listening))interfaceSounds.Play(Store.Root,step);}
        string PageName(string id){var names=new Dictionary<string,string>{{"home","文字，有了声音。"},{"voiceEditor","声音设置"},{"voices","声音方案"},{"configuration","音色包"},{"training","基础训练"},{"advancedTraining","高级训练"},{"settings","设置"},{"appearance","外观"},{"engine","本地引擎"},{"connections","连接设备"},{"harness","Harness"},{"airi","AIRI 桌宠"},{"connectionGuide","连接帮助"},{"advanced","声音细调"},{"cleanup","音频清理"},{"cosyReference","参考声音"}};return names.ContainsKey(id)?names[id]:"agent语音";}
        ScenePanel NewPage(string id){var p=new ScenePanel{Host=this,Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(0,8,0,12),Visible=false};pages.Add(id,p);content.Controls.Add(p);return p;}
        FlowLayoutPanel Stack(ScenePanel p){var stack=new BufferedFlow{Dock=DockStyle.Top,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,BackColor=Color.Transparent,Padding=new Padding(0,0,16,10)};p.Controls.Add(stack);p.SizeChanged+=(s,e)=>{foreach(Control c in stack.Controls){int width=Math.Max(300,p.ClientSize.Width-22);c.MinimumSize=new Size(width,0);c.Width=width;}};return stack;}
        ScenePanel Card(FlowLayoutPanel stack,string title){int width=Math.Max(300,content.Width-22);var card=new ScenePanel{Host=this,Card=true,Padding=new Padding(20,16,20,18),Width=width,MinimumSize=new Size(width,0),AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Margin=new Padding(0,0,0,14)};stack.Controls.Add(card);var table=new BufferedTable{Dock=DockStyle.Top,ColumnCount=1,AutoSize=true,BackColor=Color.Transparent};table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));card.Controls.Add(table);var label=new Label{Text=title,AutoSize=true,Font=new Font(Font.FontFamily,13F,FontStyle.Bold),Margin=new Padding(0,0,0,12)};table.Controls.Add(label);return card;}
        TableLayoutPanel Inside(ScenePanel card){return (TableLayoutPanel)card.Controls[0];}
        void Add(ScenePanel card,Control control){control.Margin=new Padding(0,3,0,6);control.Dock=DockStyle.Top;Inside(card).Controls.Add(control);card.SizeChanged+=(s,e)=>{if(control is Label)((Label)control).MaximumSize=new Size(Math.Max(120,card.Width-40),0);};}
        FlowLayoutPanel Row(params Control[] controls){var row=new BufferedFlow{AutoSize=true,WrapContents=true,BackColor=Color.Transparent,Dock=DockStyle.Top,Margin=new Padding(0)};bool actions=controls.Length>0&&controls.All(c=>c is Button);int actionWidth=actions?controls.Cast<Button>().Max(b=>Math.Max(148,Math.Min(210,b.PreferredSize.Width+18))):0;foreach(var c in controls){if(actions){c.AutoSize=false;c.Width=actionWidth;c.Height=Math.Max(42,c.Height);c.Margin=new Padding(0,3,10,5);}row.Controls.Add(c);}return row;}
        TableLayoutPanel HubRow(params Button[] buttons){var grid=new BufferedTable{Dock=DockStyle.Top,Height=60,ColumnCount=buttons.Length,RowCount=1,BackColor=Color.Transparent,Margin=new Padding(0)};for(int i=0;i<buttons.Length;i++){grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F/buttons.Length));buttons[i].AutoSize=false;buttons[i].Dock=DockStyle.Fill;buttons[i].Margin=new Padding(i==0?0:6,3,i==buttons.Length-1?0:6,5);grid.Controls.Add(buttons[i],i,0);}return grid;}
        TableLayoutPanel ActionGrid(int columns,params Button[] buttons){int rows=(buttons.Length+columns-1)/columns;var grid=new BufferedTable{Dock=DockStyle.Top,Height=54*rows,ColumnCount=columns,RowCount=rows,BackColor=Color.Transparent,Margin=new Padding(0)};for(int column=0;column<columns;column++)grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F/columns));for(int row=0;row<rows;row++)grid.RowStyles.Add(new RowStyle(SizeType.Absolute,54));for(int i=0;i<buttons.Length;i++){int column=i%columns,row=i/columns;buttons[i].AutoSize=false;buttons[i].Dock=DockStyle.Fill;buttons[i].Margin=new Padding(column==0?0:6,3,column==columns-1?0:6,5);grid.Controls.Add(buttons[i],column,row);}return grid;}
        Label Hint(string text){return new Label{Text=text,AutoSize=true,MaximumSize=new Size(850,0),Margin=new Padding(0,4,0,6)};}
        Label HarnessIndicator(string source,string name){
            var label=Hint(name+" · 服务关闭");label.Name="harnessStatus_"+source;label.Tag=name;
            if(!harnessIndicators.ContainsKey(source))harnessIndicators[source]=new List<Label>();harnessIndicators[source].Add(label);return label;
        }
        void UpdateHarnessIndicators(Dictionary<string,object> states,bool online){
            harnessStates=states;harnessOnline=online;
            var active=new List<string>();
            foreach(var pair in harnessIndicators){
                object raw=null;var item=states!=null&&states.TryGetValue(pair.Key,out raw)?raw as Dictionary<string,object>:null;
                bool enabled=Config.harness.ContainsKey(pair.Key)&&Config.harness[pair.Key];
                string state=!enabled?"disabled":!online?"offline":item==null?"waiting":Json.Text(item,"state");
                string text=state=="verified"?"实际朗读验证通过":state=="connected"?"连接器在线":state=="recent_event"?"近期收到回复":state=="monitoring"?"监听就绪":state=="disabled"?"未启用":state=="offline"?"服务关闭":"等待接入";
                bool green=state=="verified";
                foreach(var label in pair.Value){label.Text=Convert.ToString(label.Tag)+" · "+text;label.ForeColor=green?(BackColor.GetBrightness()<.4F?Color.LightGreen:Color.FromArgb(25,128,72)):ForeColor;label.AccessibleDescription=text;}
                if(green)active.Add(Convert.ToString(pair.Value[0].Tag)+"（"+text+"）");
            }
            if(harnessSummary!=null){harnessSummary.Text=!online?"Harness：服务关闭":active.Count==0?"Harness：等待接入":"已验证接入："+string.Join("、",active);harnessSummary.ForeColor=online&&active.Count>0?(BackColor.GetBrightness()<.4F?Color.LightGreen:Color.FromArgb(25,128,72)):ForeColor;}
            SyncAutoRead();
        }
        TextBox Input(bool multiline=false){return new TextBox{BorderStyle=BorderStyle.FixedSingle,Multiline=multiline,Height=multiline?78:30,MinimumSize=new Size(100,multiline?78:28),ScrollBars=multiline?ScrollBars.Vertical:ScrollBars.None};}
        NumericUpDown Number(decimal min,decimal max,decimal step,int digits){return new NumericUpDown{Minimum=min,Maximum=max,Increment=step,DecimalPlaces=digits,Width=110,Height=30};}
        void Field(ScenePanel card,string key,string label,bool multiline=false){Add(card,Hint(label));var box=Input(multiline);fields[key]=box;Add(card,box);}
        void NumberField(ScenePanel card,string key,string label,decimal min,decimal max,decimal step,int digits){var box=Number(min,max,step,digits);numbers[key]=box;Add(card,Row(Hint(label),box));}
        void PathField(ScenePanel card,string key,string label,string filter,bool folder){Field(card,key,label);Add(card,Row(Btn("选择"+(folder?"目录":"文件"),()=>SelectPath(fields[key],filter,folder))));}
        void SelectPath(TextBox box,string filter,bool folder){if(folder){using(var dialog=new FolderBrowserDialog{Description="选择目录",ShowNewFolderButton=true})if(dialog.ShowDialog(this)==DialogResult.OK)box.Text=Store.Portable(dialog.SelectedPath);}else{using(var dialog=new OpenFileDialog{Filter=filter,CheckFileExists=true})if(dialog.ShowDialog(this)==DialogResult.OK)box.Text=Store.Portable(dialog.FileName);}}
        string VoicePackageDirectory(){string directory=Store.Resolve("voice-model-packages");Directory.CreateDirectory(directory);return directory;}
        string TtsEngineDirectory(){string directory=Store.Resolve("tts-engines");Directory.CreateDirectory(directory);return directory;}
        async Task SelectVoiceModelPackage(){
            string path;
            using(var dialog=new OpenFileDialog{Title="从音色模型包库选择",Filter="TTS 音色包|*.cosyvoice.ttsmodel;*.gpt-sovits.ttsmodel",CheckFileExists=true,InitialDirectory=VoicePackageDirectory(),RestoreDirectory=false}){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                path=dialog.FileName;
            }
            await ImportConfiguration(path);ShowPage("home");
            Status("已从音色模型包库导入并应用："+Path.GetFileName(path));
        }
        Label homeVoiceSummary;
        ComboBox quickVoice;
        bool speechOperation;
        void BuildHomeWeb(){
            var home=NewPage("home");home.AutoScroll=true;
            var title=new Label{Text="文字，有了声音。",Font=new Font(Font.FontFamily,26F,FontStyle.Bold),AutoSize=true,BackColor=Color.Transparent,ForeColor=ForeColor};title.SetBounds(38,12,620,70);home.Controls.Add(title);
            var lead=Hint("手动朗读与自动朗读，在这里统一控制。");lead.SetBounds(40,72,520,24);home.Controls.Add(lead);
            serviceToggle=Primary(AsyncBtn("开启服务",async()=>{if(await Service.Running()){await Service.Stop();Status("服务已关闭。");}else{await SaveDraft();await Service.Start();Status("服务已启动。");}await Poll();}));serviceToggle.AutoSize=false;serviceToggle.SetBounds(868,46,96,34);home.Controls.Add(serviceToggle);
            var voice=new ScenePanel{Host=this,Card=true,Location=new Point(38,120),Size=new Size(658,104),Padding=new Padding(16,10,16,10)};home.Controls.Add(voice);
            homeVoiceSummary=Hint("");homeVoiceSummary.Font=new Font(Font.FontFamily,12F,FontStyle.Bold);homeVoiceSummary.SetBounds(0,0,620,24);voice.Controls.Add(homeVoiceSummary);
            quickVoice=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=330,AccessibleName="快速选择已保存的声音方案"};quickVoice.SetBounds(0,30,330,28);voice.Controls.Add(quickVoice);
            var switchVoice=Btn("切换声音",async()=>{var item=quickVoice.SelectedItem as SavedVoice;if(item==null)throw new InvalidOperationException("请先在声音方案中保存或选择一个声音。");await ProfileOperation("apply",item.id,null);BindVoice();Status("声音已切换："+item.name);});switchVoice.SetBounds(342,30,96,30);voice.Controls.Add(switchVoice);
            var adjust=EntryBtn("调整声音","选择引擎、模型和语速。",()=>ShowPage("voiceEditor"));adjust.SetBounds(446,30,96,30);voice.Controls.Add(adjust);
            harnessSummary=Hint("Harness：服务关闭");harnessSummary.Font=new Font(Font.FontFamily,9F);harnessSummary.SetBounds(0,66,620,20);voice.Controls.Add(harnessSummary);
            var speech=new ScenePanel{Host=this,Card=true,Location=new Point(38,240),Size=new Size(658,176),Padding=new Padding(16,10,16,10)};home.Controls.Add(speech);
            var speechTitle=new Label{Text="想说什么？",Font=new Font(Font.FontFamily,13F,FontStyle.Bold),AutoSize=true,BackColor=Color.Transparent};speechTitle.SetBounds(0,0,300,26);speech.Controls.Add(speechTitle);
            body=Input(true);body.SetBounds(0,32,626,106);body.Text="工作好久啦，起来动一动、喝口水吧～";body.AccessibleName="朗读文本";speech.Controls.Add(body);
            var phraseCard=new ScenePanel{Host=this,Card=true,Location=new Point(38,430),Size=new Size(658,72),Padding=new Padding(16,8,16,8)};home.Controls.Add(phraseCard);
            var phraseTitle=new Label{Text="常用句",Font=new Font(Font.FontFamily,10F,FontStyle.Bold),AutoSize=true,BackColor=Color.Transparent};phraseTitle.SetBounds(0,0,160,20);phraseCard.Controls.Add(phraseTitle);
            phrases=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=420,AccessibleName="常用句选择"};phrases.SetBounds(0,26,420,26);phraseCard.Controls.Add(phrases);
            var manage=Btn("管理",()=>ShowPage("home"));manage.SetBounds(590,26,50,26);phraseCard.Controls.Add(manage);
            var queue=new ScenePanel{Host=this,Card=true,Location=new Point(725,120),Size=new Size(215,355),Padding=new Padding(16,14,16,14)};home.Controls.Add(queue);
            var queueTitle=new Label{Text="朗读队列",Font=new Font(Font.FontFamily,10F,FontStyle.Bold),AutoSize=true,BackColor=Color.Transparent};queueTitle.SetBounds(0,0,150,24);queue.Controls.Add(queueTitle);
            var queueEmpty=Hint("暂时没有朗读任务。\r\n来自客户端的完整句子\r\n会按顺序出现在这里。");queueEmpty.SetBounds(0,46,185,120);queue.Controls.Add(queueEmpty);
            var deck=new ScenePanel{Host=this,Card=true,Location=new Point(0,516),Size=new Size(978,88),Padding=new Padding(16,10,16,10)};home.Controls.Add(deck);
            var stop=Btn("停止",async()=>await StopSpeech());stop.SetBounds(0,20,96,38);deck.Controls.Add(stop);
            playButton=LiveSpeechButton("重播上一条",PlayLast);playButton.AutoSize=false;playButton.SetBounds(112,20,142,38);deck.Controls.Add(playButton);
            generate=Primary(LiveSpeechButton("生成并播放",async()=>await Generate(body.Text,true)));generate.AutoSize=false;generate.SetBounds(788,20,150,38);deck.Controls.Add(generate);BuildVoiceEditor();BuildCosyReference();
        }
        void BuildHome(){
            var home=NewPage("home");var stack=Stack(home);
            serviceToggle=Primary(AsyncBtn("启动服务",async()=>{if(await Service.Running()){await Service.Stop();Status("服务已关闭。");}else{await SaveDraft();await Service.Start();Status("服务已启动。");}await Poll();}));
            var transport=Controls.Find("sidebarTransport",true).First() as TableLayoutPanel;
            transport.ColumnStyles[0].SizeType=SizeType.Absolute;transport.SizeChanged+=(s,e)=>{transport.ColumnStyles[0].Width=Math.Max(1,transport.ClientSize.Width-transport.Padding.Horizontal);};transport.ColumnStyles[0].Width=Math.Max(1,transport.ClientSize.Width-transport.Padding.Horizontal);serviceToggle.MinimumSize=new Size(0,42);serviceToggle.AutoSize=false;serviceToggle.Dock=DockStyle.Fill;serviceToggle.Margin=new Padding(0,3,0,3);transport.Controls.Add(serviceToggle,0,2);
            var stop=Danger(Btn("停止朗读",async()=>await StopSpeech()));stop.MinimumSize=new Size(0,42);stop.AutoSize=false;stop.Dock=DockStyle.Fill;stop.Margin=new Padding(0,3,0,3);transport.Controls.Add(stop,0,3);
            var voice=Card(stack,"正在使用");homeVoiceSummary=Hint("");homeVoiceSummary.Font=new Font(Font.FontFamily,12,FontStyle.Bold);Add(voice,homeVoiceSummary);
            quickVoice=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=330,AccessibleName="快速选择已保存的声音方案"};
            Add(voice,Row(quickVoice,AsyncBtn("切换声音",async()=>{var item=quickVoice.SelectedItem as SavedVoice;if(item==null)throw new InvalidOperationException("请先在声音方案中保存或选择一个声音。");await ProfileOperation("apply",item.id,null);BindVoice();Status("声音已切换："+item.name);}),EntryBtn("调整声音","选择引擎、模型和语速。",()=>ShowPage("voiceEditor"))));
            harnessSummary=Hint("Harness：服务关闭");harnessSummary.Font=new Font(Font.FontFamily,9);Add(voice,harnessSummary);voice.AutoSize=false;Inside(voice).SizeChanged+=(s,e)=>voice.Height=Inside(voice).Height+voice.Padding.Vertical;voice.Height=Inside(voice).PreferredSize.Height+voice.Padding.Vertical;
            var speech=Card(stack,"想说什么？");body=Input(true);body.Height=174;body.Text="工作好久啦，起来动一动、喝口水吧～";body.AccessibleName="朗读文本";Add(speech,body);
            generate=Primary(LiveSpeechButton("生成并播放",async()=>await Generate(body.Text,true)));playButton=LiveSpeechButton("重播上一条",PlayLast);
            Add(speech,Row(generate,LiveSpeechButton("只生成音频",async()=>await Generate(body.Text,false)),playButton));
            var phraseCard=Card(stack,"常用句");phrases=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=520,AccessibleName="常用句选择"};Add(phraseCard,phrases);
            Add(phraseCard,Row(LiveSpeechButton("朗读选中句",async()=>{if(phrases.SelectedItem!=null)await Generate(Convert.ToString(phrases.SelectedItem),true);}),Btn("放入输入框",()=>{if(phrases.SelectedItem!=null)body.Text=Convert.ToString(phrases.SelectedItem);}),AsyncBtn("保存输入为常用句",async()=>{string value=body.Text.Trim();if(value.Length==0)return;if(!Config.phrases.Contains(value))Config.phrases.Add(value);await Persist(Config,new{phrases=Config.phrases});RefreshPhrases();}),AsyncBtn("删除选中句",async()=>{if(phrases.SelectedItem==null)return;Config.phrases.Remove(Convert.ToString(phrases.SelectedItem));await Persist(Config,new{phrases=Config.phrases});RefreshPhrases();})));
            phraseCard.SizeChanged+=(s,e)=>phrases.Width=Math.Max(180,phraseCard.ClientSize.Width-44);
            BuildVoiceEditor();BuildCosyReference();
        }
        Button LiveSpeechButton(string text,Func<Task> action){return Btn(text,async()=>{if(speechOperation){Status("已有手动朗读任务进行中。可随时点击左侧停止朗读。");return;}speechOperation=true;try{await action();}catch(Exception error){StatusError(error.Message);}finally{speechOperation=false;}});}
        void BuildVoiceEditor(){
            var voice=Card(Stack(NewPage("voiceEditor")),"引擎、模型与参考声音");
            engineChoice=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=280};engineChoice.Items.AddRange(EngineChoices);Add(voice,Hint("TTS 引擎"));Add(voice,engineChoice);engineChoice.SelectedIndexChanged+=(s,e)=>UpdateVoiceControls();engineDetails=Hint("选择引擎后显示对应的模型设置。");Add(voice,engineDetails);int cosyStart=Inside(voice).Controls.Count;
            Add(voice,Hint("CosyVoice 模型目录（留空使用默认模型）"));baseModelPath=Input();Add(voice,baseModelPath);Add(voice,ActionGrid(2,EntryBtn("选择模型目录","选择已经训练完成的完整 CosyVoice3 模型目录。",()=>SelectPath(baseModelPath,"",true)),Btn("恢复默认模型",()=>baseModelPath.Text="")));
            cosyOnlyControls.AddRange(Inside(voice).Controls.Cast<Control>().Skip(cosyStart));BuildVariantControls(voice);
            Add(voice,Row(Primary(AsyncBtn("选择音色包",SelectVoiceModelPackage)),EntryBtn("参考声音","为当前引擎选择参考录音和逐字一致的台词。",()=>ShowPage("cosyReference"))));NumberField(voice,"speed","语速（1 为原速）",.7M,1.3M,.01M,2);
            saveVoiceButton=Primary(AsyncBtn("使用当前声音",async()=>{await SaveDraft();Status("已使用当前声音：之后的新任务会采用当前模型、参考声音和语速。");}));tips.SetToolTip(saveVoiceButton,"保存声音页当前选择，并用于之后的新语音；不会训练或复制模型。");Add(voice,Row(saveVoiceButton,EntryBtn("保存或切换声音方案","把当前声音保存为可复用方案。",()=>ShowPage("voices"))));voiceHint=Hint("当前引擎直接生成最终语音。");Add(voice,voiceHint);
        }
        void BuildCosyReference(){var stack=Stack(NewPage("cosyReference"));var card=Card(stack,"参考声音");Add(card,Hint("选择一段代表性的参考音频，并填写与录音逐字一致的台词。训练数据可以有很多段，但这里一次使用一段推理参考。"));PathField(card,"reference","参考音频（建议干净、单人、情绪稳定）","音频|*.wav;*.flac;*.mp3;*.m4a",false);Field(card,"referenceText","逐字台词（必须与录音一致）",true);Add(card,Row(Btn("导入台词",()=>{using(var dialog=new OpenFileDialog{Filter="台词文件|*.txt",CheckFileExists=true})if(dialog.ShowDialog(this)==DialogResult.OK)fields["referenceText"].Text=File.ReadAllText(dialog.FileName,System.Text.Encoding.UTF8);}),AsyncBtn("使用参考声音",async()=>{await SaveDraft();Status("参考声音已应用；之后的新任务会使用这段音频和台词。");}),Btn("返回首页",()=>ShowPage("home"))));Add(card,Hint("台词不一致可能造成漏字、错字、节奏异常或音色不稳定。"));}
        void BindVoice(){var v=Config.voice;if(quickVoice!=null){quickVoice.DataSource=null;quickVoice.DataSource=Config.profiles.ToList();}if(homeVoiceSummary!=null)homeVoiceSummary.Text=EngineLabel(v)+"   ·   语速 "+v.speed.ToString("0.00")+"×   ·   "+(v.reference.Length>0?Path.GetFileNameWithoutExtension(v.reference):"待选择参考声音");baseModelPath.Text=v.baseModel;gptModelPath.Text=v.gptModel;if(fields.ContainsKey("reference")){fields["reference"].Text=v.reference;fields["referenceText"].Text=v.referenceText;fields["instruction"].Text=v.instruction;checks["advancedEnabled"].Checked=v.advancedEnabled;SelectEngineVoice(v);numbers["speed"].Value=(decimal)v.speed;numbers["leadingSilence"].Value=(decimal)v.leadingSilence;numbers["volume"].Value=(decimal)v.volume;}UpdateVoiceControls();UpdateTopStatus(harnessOnline);RefreshPhrases();RefreshProfiles();}
        VoiceConfig ReadVoice(){var v=Config.voice.Clone();v.baseModel=baseModelPath.Text.Trim();v.gptModel=gptModelPath.Text.Trim();v.gptVariant=SelectedVariant();v.advancedEnabled=checks["advancedEnabled"].Checked;v.reference=fields["reference"].Text.Trim();v.referenceText=fields["referenceText"].Text;v.instruction=fields["instruction"].Text;v.engine=SelectedEngine();v.speed=(double)numbers["speed"].Value;v.leadingSilence=(double)numbers["leadingSilence"].Value;v.volume=(double)numbers["volume"].Value;return v;}
        void ValidatePaths(VoiceConfig v){if(!new[]{"cosyvoice","gpt-sovits"}.Contains(v.engine))throw new InvalidOperationException("请选择支持的 TTS 引擎。");if(v.engine=="cosyvoice"&&v.baseModel.Length>0){string folder=Store.Resolve(v.baseModel);if(!Directory.Exists(folder)||new[]{"cosyvoice3.yaml","llm.pt","flow.pt","hift.pt"}.Any(name=>!File.Exists(Path.Combine(folder,name))))throw new InvalidOperationException("请选择完整 CosyVoice 3 模型目录。");}if(v.engine=="gpt-sovits"&&v.gptModel.Length>0&&!Directory.Exists(Store.Resolve(v.gptModel)))throw new InvalidOperationException("GPT-SoVITS 模型目录不存在。");if(v.reference.Length>0&&!File.Exists(Store.Resolve(v.reference)))throw new InvalidOperationException("参考录音不存在。");}
        async Task Persist(Settings next,object patch){if(await Service.Running()){var response=await Service.Request("/api/config",patch,90000);Config=Json.Decode<Settings>(Json.Encode(response["config"]));}else{Store.Write(next);Config=Json.Copy(next);}Status("配置已保存。");}
        async Task SaveDraft(){var next=Json.Copy(Config);next.voice=ReadVoice();ValidatePaths(next.voice);await Persist(next,new {voice=next.voice});if(await Service.Running())await WaitForVoice(next.voice);UpdateVoiceControls();}
        async Task WaitForVoice(VoiceConfig expected){
            Status("正在切换为 "+EngineLabel(expected)+"；旧声音和排队任务已停止，等待新模型就绪……");
            var loadWatch=System.Diagnostics.Stopwatch.StartNew();while(loadWatch.Elapsed.TotalMinutes<4){
                if(IsDisposed)return;
                var health=await Service.Request("/health",null,5000);
                if(health.ContainsKey("engine")&&(Json.Text(health,"engine")!=expected.engine||(expected.engine=="gpt-sovits"&&Json.Text(health,"variant")!=expected.gptVariant)))throw new InvalidOperationException("声音已被另一项操作切换，请重新确认当前选择。");
                if(Convert.ToBoolean(health["ready"]))return;
                string state=Json.Text(health,"serviceState");
                if(state=="error"||state=="unconfigured")throw new InvalidOperationException("配置已保存，但新声音尚不可用："+Json.Text(health,"error")+"。请运行环境自检；尚未确认切换成功。");
                await Task.Delay(250);
            }
            throw new InvalidOperationException("配置已保存，但模型加载超过 4 分钟；尚未确认切换成功，请查看日志。");
        }
        async Task Generate(string text,bool play){await SaveDraft();if(!await Service.Running())throw new InvalidOperationException("请先启动语音服务。");var result=await Service.Request("/speak",new {text=text,play=play,source="manual"});if(result.ContainsKey("skipped")){Status("这段只有链接或代码，已跳过。");return;}lastJob=Json.Text(result,"jobId");Status("已进入朗读队列。");await WaitJob(lastJob);}
        async Task WaitJob(string id){
            if(busyOverlay!=null)busyOverlay.SetStopAction(async()=>await StopSpeech());
            var watch=System.Diagnostics.Stopwatch.StartNew();
            while(watch.Elapsed.TotalSeconds<3600){
                await Task.Delay(300);if(IsDisposed)return;var job=await Service.Request("/api/jobs/"+id);string state=Json.Text(job,"state");
                if(state=="completed"){Status("已完成 · "+Store.Resolve("data/output")+"（生成的音频可手动清理）");return;}
                if(state=="cancelled"){Status("已停止朗读。");return;}
                if(state=="failed")throw new InvalidOperationException(Json.Text(job,"error"));
                if(Json.Text(job,"totalSegments").Length>0)BusyStatus("已生成 "+Json.Text(job,"segmentsCompleted")+" / "+Json.Text(job,"totalSegments")+" 段 · "+(state=="playing"?"正在播放":"正在生成"));
            }
            throw new InvalidOperationException("生成等待超时，请查看日志。");
        }
        async Task PlayLast(){if(lastJob.Length==0)throw new InvalidOperationException("请先生成一条语音。");var result=await Service.Request("/api/play",new {jobId=lastJob});await WaitJob(Json.Text(result,"jobId"));}
        async Task StopSpeech(){try{await Service.Request("/stop",new {});Status("已停止当前朗读和待播放任务。");}catch(Exception exc){Status(exc.Message);}}
        int observedVoiceRevision=-1;
        async Task SyncRemoteVoice(Dictionary<string,object> health){
            object revision;if(operation||!health.TryGetValue("voiceRevision",out revision))return;
            int current=Convert.ToInt32(revision);if(current==observedVoiceRevision)return;
            bool draftChanged=Json.Encode(ReadVoice())!=Json.Encode(Config.voice);
            var state=await Service.Request("/api/state");Config=Json.Decode<Settings>(Json.Encode(state["config"]));observedVoiceRevision=current;
            if(!draftChanged)BindVoice();else UpdateVoiceControls();
        }
        async Task Poll(){if(polling||IsDisposed)return;polling=true;try{var health=await Service.Request("/health",null,1000);if(Json.Text(health,"service")!="agent-voice-public")throw new InvalidOperationException("端口被其他服务占用");await SyncRemoteVoice(health);serviceToggle.Text="关闭服务";var missing=health["missing"] as object[];connection.Text="服务：已开启\r\n输出："+(missing!=null&&missing.Length>0?"声音待配置":(Convert.ToBoolean(health["airiConnected"])?"AIRI 桌宠":"本地播放"));object states;var connections=health.TryGetValue("harnessConnections",out states)?states as Dictionary<string,object>:null;object rawChecks;var verified=health.TryGetValue("verification",out rawChecks)?rawChecks as Dictionary<string,object>:null;
                if(verified!=null){foreach(var entry in verified){var item=entry.Value as Dictionary<string,object>;if(item!=null&&Json.Text(item,"state")=="verified"&&connections!=null&&connections.ContainsKey(entry.Key))((Dictionary<string,object>)connections[entry.Key])["state"]="verified";}UpdateVerification(verified);}modelStatus=Json.Text(health,"serviceState");tips.SetToolTip(statusSystem,Json.Text(health,"error"));UpdateHarnessIndicators(connections,true);UpdateTopStatus(true);await ResumeAiriVerification(Convert.ToBoolean(health["airiConnected"]));}catch{pendingAiriVerification=false;modelStatus="offline";serviceToggle.Text="启动服务";connection.Text="服务：关闭或未响应\r\n输出：待命";UpdateHarnessIndicators(null,false);UpdateTopStatus(false);UpdateAiriStatus("AIRI · 服务关闭",ForeColor);}finally{if(Config.voice.assetScope=="private")connection.Text+="\r\n声音：私有";polling=false;}}
        void UpdateTopStatus(bool online){if(statusSystem==null)return;string label="未就绪";switch(modelStatus){case "ready":label="声音已就绪";break;case "loading":label="模型加载中";break;case "unconfigured":label="声音待配置";break;case "error":label="声音故障 · 请自检";break;case "generating":label="语音生成中";break;case "playing":label="正在播放";break;case "queued":label="等待播放";break;case "not_loaded":label="模型尚未加载";break;}statusSystem.Text="● 系统 · "+(online?label:"服务关闭或未响应");statusSystem.ForeColor=online?(modelStatus=="error"?Color.Salmon:modelStatus=="ready"?Color.FromArgb(80,235,190):Color.Gold):ForeColor;statusEngine.Text="● 引擎 · "+(Config.voice.engine=="gpt-sovits"?"GPT-SoVITS":"CosyVoice");statusVoice.Text=Config.voice.engine=="gpt-sovits"?"● 版本 · "+(Config.voice.gptVariant=="v5turbo"?"v5 Turbo":"ProPlus"):"● 声音 · CosyVoice";statusOutput.Text="● 输出 · TTS 直出";}
        void RefreshPhrases(){string selected=Convert.ToString(phrases.SelectedItem);phrases.Items.Clear();foreach(var text in Config.phrases)phrases.Items.Add(text);if(phrases.Items.Contains(selected))phrases.SelectedItem=selected;else if(phrases.Items.Count>0)phrases.SelectedIndex=0;}
        void RefreshProfiles(){if(profileList==null)return;string selected=(profileList.SelectedItem as SavedVoice)==null?"":((SavedVoice)profileList.SelectedItem).id;profileList.Items.Clear();foreach(var profile in Config.profiles)profileList.Items.Add(profile);for(int i=0;i<profileList.Items.Count;i++)if(((SavedVoice)profileList.Items[i]).id==selected)profileList.SelectedIndex=i;if(profileEmpty!=null)profileEmpty.Visible=profileList.Items.Count==0;}
        void BuildSavedVoices(){
            var page=WebPage("voices");
            WebTitle(page,"声音方案","保存当前声音和参数，随时切回熟悉的听感。");
            page.Controls.Add(WebAsyncButton("保存当前设置","把当前声音、参考条件和语速保存为可复用方案。",SaveProfile,632,46,140,39,true));
            page.Controls.Add(WebButton("打开方案文件夹","查看每个声音方案保存的参数副本。",()=>{string directory=ProfileDirectory();ServiceClient.Open(directory);Status("每个声音方案有独立同名文件夹："+directory);},780,46,160,39));
            var listCard=WebCard(page,38,126,902,111);
            profileList=new ListBox{IntegralHeight=false,BorderStyle=BorderStyle.FixedSingle,ItemHeight=40,DrawMode=DrawMode.OwnerDrawFixed};
            profileList.SetBounds(16,14,720,82);
            profileList.DrawItem+=(s,e)=>{
                if(e.Index<0||e.Index>=profileList.Items.Count)return;
                var item=profileList.Items[e.Index] as SavedVoice;if(item==null)return;
                bool selected=(e.State&DrawItemState.Selected)!=0;
                using(var brush=new SolidBrush(selected?Mix(CardColor,Accent,18):CardColor))e.Graphics.FillRectangle(brush,e.Bounds);
                Color color=selected?Accent:ForeColor;
                using(var nameFont=new Font(Font.FontFamily,10F,FontStyle.Bold))using(var detailFont=new Font(Font.FontFamily,8F)){
                    TextRenderer.DrawText(e.Graphics,item.name,nameFont,new Rectangle(e.Bounds.X+10,e.Bounds.Y+3,e.Bounds.Width-20,19),color,TextFormatFlags.Left|TextFormatFlags.EndEllipsis);
                    string detail=EngineLabel(item.voice)+" · 语速 "+item.voice.speed.ToString("0.00")+"× · 音量 "+(item.voice.volume*100).ToString("0")+"%";
                    TextRenderer.DrawText(e.Graphics,detail,detailFont,new Rectangle(e.Bounds.X+10,e.Bounds.Y+21,e.Bounds.Width-20,16),color,TextFormatFlags.Left|TextFormatFlags.EndEllipsis);
                }
                if(selected)e.DrawFocusRectangle();
            };
            listCard.Controls.Add(profileList);
            profileEmpty=WebText("还没有保存的声音方案。填写方案名称后，点右上角“保存当前设置”。",9F,FontStyle.Regular,16,14,720,82,ContentAlignment.MiddleCenter);
            profileEmpty.Visible=false;
            listCard.Controls.Add(profileEmpty);
            var apply=WebAsyncButton("应用",null,async()=>{var item=profileList.SelectedItem as SavedVoice;if(item==null)return;await ProfileOperation("apply",item.id,null);BindVoice();Status("已应用声音方案："+item.name);},772,38,60,34,false);
            listCard.Controls.Add(apply);
            var remove=WebAsyncButton("删除",null,async()=>{var item=profileList.SelectedItem as SavedVoice;if(item==null)return;var next=Json.Copy(Config);next.profiles.RemoveAll(p=>p.id==item.id);await Persist(next,new {profiles=next.profiles});ArchiveProfileFolder(item);deleted=Json.Copy(item);RefreshProfiles();Status("声音方案已删除，模型文件保留；可撤销。");},840,38,60,34,false);
            Danger(remove);listCard.Controls.Add(remove);
            page.Controls.Add(WebText("方案记录设置；音色包保存声音文件。两者分开管理，避免重复导出模型。",8.8F,FontStyle.Regular,38,240,902,18,ContentAlignment.MiddleLeft));
            var maintain=WebCard(page,38,260,902,168);
            maintain.Controls.Add(WebText("方案名称",9.5F,FontStyle.Bold,16,18,90,24,ContentAlignment.MiddleLeft));
            profileName=Input();profileName.Text="我的声音";profileName.SetBounds(110,15,250,28);maintain.Controls.Add(profileName);
            maintain.Controls.Add(WebAsyncButton("固定旧方案参考","把旧方案用到的参考录音固定到方案文件夹，并记录模型校验信息。",async()=>{var item=profileList.SelectedItem as SavedVoice;if(item==null)return;var result=await ProfileOperation("seal",item.id,null);WriteProfileFolder(Json.Decode<SavedVoice>(Json.Encode(result["profile"])));RefreshProfiles();Status("已固定旧方案的参考录音并记录模型校验信息。");},368,13,160,36,false));
            maintain.Controls.Add(WebAsyncButton("撤销删除",null,async()=>{if(deleted==null)return;var next=Json.Copy(Config);next.profiles.Add(deleted);await Persist(next,new {profiles=next.profiles});WriteProfileFolder(deleted);deleted=null;RefreshProfiles();},536,13,110,36,false));
            maintain.Controls.Add(WebButton("打开方案目录","查看每个声音方案保存的参数副本。",()=>{string directory=ProfileDirectory();ServiceClient.Open(directory);Status("每个声音方案有独立同名文件夹："+directory);},654,13,150,36));
            maintain.Controls.Add(WebText("保存时会把参考录音复制到 data/voice-configurations/方案名称-ID。",9F,FontStyle.Regular,16,60,860,20,ContentAlignment.MiddleLeft));
            maintain.Controls.Add(WebText("模型只记录校验信息，不重复复制大权重；跨电脑请导出音色包。",9F,FontStyle.Regular,16,84,860,20,ContentAlignment.MiddleLeft));
            page.Controls.Add(WebText("每次保存都会在 data/voice-configurations 下建立“方案名称 + ID”文件夹。",9F,FontStyle.Regular,38,440,902,20,ContentAlignment.MiddleLeft));
            page.Controls.Add(WebText("删除方案只归档配置文件夹并保留模型文件；可撤销最近一次删除。",8.8F,FontStyle.Regular,38,466,902,20,ContentAlignment.MiddleLeft));
        }

        async Task<Dictionary<string,object>> ProfileOperation(string mode,string id,object payload){
            Dictionary<string,object> result;
            if(await Service.Running()){result=await Service.Request("/api/profiles/"+mode,payload??new {id=id},300000);if(mode=="apply"){Config=Json.Decode<Settings>(Json.Encode(result["config"]));await WaitForVoice(Config.voice);}}
            else {string file=Path.Combine(Store.Root,"data","profile-request-"+Guid.NewGuid().ToString("N")+".json");try{string arguments=mode+" --root "+ServiceClient.Quote(Store.Root);if(payload!=null){Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,Json.Encode(payload),new System.Text.UTF8Encoding(false));arguments+=" --file "+ServiceClient.Quote(file);}else arguments+=" --id "+ServiceClient.Quote(id);result=Json.Object(await Service.RunTool("voice_profiles.py",arguments));}finally{if(File.Exists(file))File.Delete(file);}}
            Config=Json.Decode<Settings>(Json.Encode(result["config"]));return result;
        }
        async Task SaveProfile(){var draft=ReadVoice();ValidatePaths(draft);string name=profileName.Text.Trim();if(name.Length==0)name="声音 "+DateTime.Now.ToString("MM-dd HH:mm");var result=await ProfileOperation("save",null,new {name=name,voice=draft});var saved=Json.Decode<SavedVoice>(Json.Encode(result["profile"]));WriteProfileFolder(saved);RefreshProfiles();Status("已保存方案与固定参考录音，模型记录校验信息："+ProfileFolder(saved));}
        async Task Diagnose(){var text=await Service.RunTool("diagnose.py","--root "+ServiceClient.Quote(Store.Root)+" --deep");var result=Json.Object(text);var checks=result["checks"] as object[];string details=string.Join("\r\n",checks.Select(o=>{var c=(Dictionary<string,object>)o;return (Convert.ToBoolean(c["ok"])?"✓ ":"✗ ")+Json.Text(c,"name")+"："+Json.Text(c,"detail");}).ToArray());Status("自检已完成："+Json.Text(result,"report"));MessageBox.Show(this,details+"\r\n\r\n"+Json.Text(result,"scope"),Convert.ToBoolean(result["ok"])?"自检通过 · 请试听确认":"自检发现问题",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        async Task RecoverVoice(){if(!await Service.Running())await Service.Start();var r=await Service.Request("/api/recover",new {},30000);Status(Json.Text(r,"message"));await Poll();}
        string ProfileDirectory(){string directory=Store.Resolve("data/voice-configurations");Directory.CreateDirectory(directory);return directory;}
        static string SafeProfileName(string value){var invalid=Path.GetInvalidFileNameChars();string safe=new string(value.Select(c=>invalid.Contains(c)||char.IsControl(c)?'_':c).ToArray()).Trim().TrimEnd('.');return safe.Length==0?"未命名配置":safe.Length>60?safe.Substring(0,60):safe;}
        string ProfileFolder(SavedVoice profile){return Path.Combine(ProfileDirectory(),SafeProfileName(profile.name)+"-"+profile.id.Substring(0,Math.Min(8,profile.id.Length)));}
        void WriteProfileFolder(SavedVoice profile){string folder=ProfileFolder(profile);Directory.CreateDirectory(folder);string target=Path.Combine(folder,"profile.json"),temp=target+".tmp";File.WriteAllText(temp,Json.Encode(profile),new System.Text.UTF8Encoding(false));if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);}
        void EnsureProfileFolders(){foreach(var profile in Config.profiles)WriteProfileFolder(profile);}
        void ArchiveProfileFolder(SavedVoice profile){string folder=ProfileFolder(profile);if(!Directory.Exists(folder))return;string deletedRoot=Path.Combine(ProfileDirectory(),".deleted");Directory.CreateDirectory(deletedRoot);string target=Path.Combine(deletedRoot,Path.GetFileName(folder)+"-"+DateTime.Now.ToString("yyyyMMddHHmmss"));Directory.Move(folder,target);}
        protected override bool ProcessCmdKey(ref Message msg,Keys key){if(key==Keys.Escape){ShowPage("home");return true;}return base.ProcessCmdKey(ref msg,key);}
        protected override void OnFormClosing(FormClosingEventArgs e){if(!exiting&&tray!=null){e.Cancel=true;Hide();return;}poll.Stop();tips.Dispose();if(tray!=null){tray.Visible=false;tray.Dispose();}if(background!=null)background.Dispose();if(scaledBackground!=null)scaledBackground.Dispose();if(defaultBackdrop!=null)defaultBackdrop.Dispose();if(pageBackdrop!=null)pageBackdrop.Dispose();if(scaledPageBackdrop!=null)scaledPageBackdrop.Dispose();base.OnFormClosing(e);}
        string DefaultMascotPath(){return Path.Combine(Store.Root,".agents","plugins","agent-voice-control","assets","voice-core.png");}
        void LoadPageBackdrop(string id){if(pageBackdrop!=null){pageBackdrop.Dispose();pageBackdrop=null;}if(scaledPageBackdrop!=null){scaledPageBackdrop.Dispose();scaledPageBackdrop=null;}if(background!=null)return;string name=id=="home"?"speech":id=="configuration"?"voices":id=="voices"?"schemes":id;string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","workspace",name+".png");if(File.Exists(path))using(var source=Image.FromFile(path))pageBackdrop=new Bitmap(source);}
        public void PaintScene(Graphics g,Control control){
            Bitmap active=background??pageBackdrop;Bitmap scaled=background!=null?scaledBackground:scaledPageBackdrop;
            if(active!=null&&(scaled==null||scaled.Size!=ClientSize)){
                if(scaled!=null)scaled.Dispose();
                scaled=new Bitmap(Math.Max(1,ClientSize.Width),Math.Max(1,ClientSize.Height));
                using(var graphics=Graphics.FromImage(scaled)){
                    graphics.Clear(BackColor);graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    float scale=Math.Max((float)ClientSize.Width/active.Width,(float)ClientSize.Height/active.Height);
                    float w=active.Width*scale,h=active.Height*scale;
                    graphics.DrawImage(active,(ClientSize.Width-w)/2,(ClientSize.Height-h)/2,w,h);
                }
                if(background!=null)scaledBackground=scaled;else scaledPageBackdrop=scaled;
            }
            Bitmap scene=scaled??DefaultScene();
            if(scene==null){using(var brush=new SolidBrush(BackColor))g.FillRectangle(brush,control.ClientRectangle);return;}
            Point origin=PointToClient(control.PointToScreen(Point.Empty));
            Rectangle source=Rectangle.Intersect(new Rectangle(origin,control.ClientSize),new Rectangle(Point.Empty,scene.Size));
            if(source.Width>0&&source.Height>0)g.DrawImage(scene,new Rectangle(source.X-origin.X,source.Y-origin.Y,source.Width,source.Height),source,GraphicsUnit.Pixel);
        }
        Bitmap DefaultScene(){if(defaultBackdrop!=null&&defaultBackdrop.Size==ClientSize)return defaultBackdrop;if(defaultBackdrop!=null)defaultBackdrop.Dispose();defaultBackdrop=new Bitmap(Math.Max(1,ClientSize.Width),Math.Max(1,ClientSize.Height));using(var g=Graphics.FromImage(defaultBackdrop))g.Clear(BackColor);return defaultBackdrop;}
        static Color Mix(Color one,Color two,int secondPercent){int first=100-secondPercent;return Color.FromArgb((one.R*first+two.R*secondPercent)/100,(one.G*first+two.G*secondPercent)/100,(one.B*first+two.B*secondPercent)/100);}
        void StyleButton(Button button,bool selected=false){
            string kind=Convert.ToString(button.Tag);button.BorderSize=0;
            if(kind=="primary"){button.BackColor=Accent;button.ForeColor=Accent.GetBrightness()>.48F?Color.FromArgb(3,20,31):Color.White;}
            else if(kind=="danger"){button.BackColor=Color.FromArgb(166,42,62);button.ForeColor=Color.White;}
            else if(kind=="nav"||kind=="nav-primary"){button.BackColor=selected?Mix(CardColor,Accent,18):CardColor;button.ForeColor=selected?Accent:ForeColor;}
            else{button.BackColor=Mix(CardColor,ForeColor,7);button.ForeColor=ForeColor;}
            button.BorderColor=Accent;
        }
        void ApplyTheme(){var a=Config.appearance;Accent=ColorTranslator.FromHtml(a.accent=="#19C7F2"?"#B49AFF":a.accent);BackColor=ColorTranslator.FromHtml(a.backgroundColor=="#05101D"?"#101116":a.backgroundColor);ForeColor=ColorTranslator.FromHtml(a.textColor);CardColor=BackColor.GetBrightness()<.4F?Mix(BackColor,Color.White,4):Mix(BackColor,Color.White,55);string image=Store.Resolve(a.background);if(background!=null){background.Dispose();background=null;}if(scaledBackground!=null){scaledBackground.Dispose();scaledBackground=null;}if(defaultBackdrop!=null){defaultBackdrop.Dispose();defaultBackdrop=null;}if(File.Exists(image))using(var source=Image.FromFile(image))background=new Bitmap(source);WalkTheme(shell);StyleStatus(false);UpdateHarnessIndicators(harnessStates,harnessOnline);UpdateTopStatus(harnessOnline);Invalidate(true);}
        void WalkTheme(Control parent){foreach(Control c in parent.Controls){c.ForeColor=ForeColor;if(c is Button)StyleButton((Button)c,(navigationButtons.ContainsKey(page)&&navigationButtons[page]==c));else if(c is TextBox||c is ComboBox||c is ListBox||c is NumericUpDown){c.BackColor=CardColor;c.ForeColor=ForeColor;}if(c is ComboBox)ChoiceStyle.Apply((ComboBox)c);WalkTheme(c);}}
        void ApplyIcon(){string path=Store.Resolve(Config.appearance.icon);try{if(!File.Exists(path)){string mascot=DefaultMascotPath();if(File.Exists(mascot))path=Icons.Make(mascot,Path.Combine(Store.Root,"data","appearance"));}if(!File.Exists(path))return;using(var icon=new Icon(path)){Icon=(Icon)icon.Clone();if(tray!=null)tray.Icon=Icon;}}catch(Exception exc){Status(exc.Message);}}
        protected override void Dispose(bool disposing){if(disposing){interfaceSounds.Dispose();}base.Dispose(disposing);}
    }
}
