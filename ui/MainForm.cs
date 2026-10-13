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
        Bitmap background,scaledBackground;
        readonly Dictionary<string,ScenePanel> pages=new Dictionary<string,ScenePanel>();
        readonly Dictionary<string,Button> navigationButtons=new Dictionary<string,Button>();
        readonly List<string> pageHistory=new List<string>();
        readonly Dictionary<string,List<Label>> harnessIndicators=new Dictionary<string,List<Label>>();
        Dictionary<string,object> harnessStates;
        bool harnessOnline;
        ScenePanel shell,content;
        Label footer,connection,voiceHint,harnessSummary,outputModeStatus,rvcModeStatus,pageTitle;
        NotifyIcon tray;
        readonly ToolTip tips=new ToolTip{AutomaticDelay=350,AutoPopDelay=10000,ReshowDelay=100,ShowAlways=true};
        readonly Timer poll=new Timer{Interval=1800};
        bool polling,operation,exiting;
        string page="home",lastJob="";
        Button serviceToggle,generate,saveVoiceButton,playButton,backButton;
        ComboBox phrases,profileKind;
        TextBox modelPath,indexPath,body,profileName;
        NumericUpDown pitch,formant;
        readonly Dictionary<string,TextBox> fields=new Dictionary<string,TextBox>();
        readonly Dictionary<string,NumericUpDown> numbers=new Dictionary<string,NumericUpDown>();
        readonly Dictionary<string,CheckBox> checks=new Dictionary<string,CheckBox>();
        ComboBox engineChoice,f0Choice,outputChoice,processingChoice,synthesisChoice;
        TextBox baseModelPath;
        ListBox profileList;
        SavedVoice deleted;
        public MainForm(SettingsStore store,bool testing=false){
            Store=store;Service=new ServiceClient(store);Config=store.Read();
            Text="agent语音";Font=new Font("Microsoft YaHei UI",10F);AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(1200,820);MinimumSize=new Size(1040,680);StartPosition=FormStartPosition.CenterScreen;
            SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);
            shell=new ScenePanel{Host=this,Dock=DockStyle.Fill};Controls.Add(shell);
            var outer=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,BackColor=Color.Transparent,Padding=new Padding(16)};outer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,224));outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));shell.Controls.Add(outer);
            var sidebar=new ScenePanel{Host=this,Card=true,Dock=DockStyle.Fill,Padding=new Padding(14),Margin=new Padding(0,0,12,0)};outer.Controls.Add(sidebar,0,0);
            var sideLayout=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,BackColor=Color.Transparent};sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,76));sideLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,82));sidebar.Controls.Add(sideLayout);
            var brand=new BufferedFlow{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,BackColor=Color.Transparent};brand.Controls.Add(new Label{Text="agent语音",Font=new Font(Font.FontFamily,14F,FontStyle.Bold),AutoSize=false,Width=184,Height=32,Margin=new Padding(2,2,0,0)});brand.Controls.Add(new Label{Text="本地声音工作台",AutoSize=true,Margin=new Padding(3,0,0,0)});sideLayout.Controls.Add(brand,0,0);
            var navigation=new BufferedFlow{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,BackColor=Color.Transparent,Padding=new Padding(0,4,0,4)};sideLayout.Controls.Add(navigation,0,1);
            AddNav(navigation,"首页","home","声音、服务和立即朗读。",true);
            navigation.Controls.Add(SideLabel("常用功能"));
            AddNav(navigation,"模型导入导出","configuration","导入、导出并管理本地 TTS 音色包。",true);
            AddNav(navigation,"保存声音配置","voices","保存、应用或删除声音配置快照。",true);
            AddNav(navigation,"连接与联动","connections","接入 Harness 或联动 AIRI 桌宠。",true);
            navigation.Controls.Add(SideLabel("声音工具"));
            AddNav(navigation,"RVC模型配置","profiles","配置可选的 RVC 二次变声。",false);
            AddNav(navigation,"训练音色","training","查看 CosyVoice 训练步骤。",false);
            AddNav(navigation,"设置","settings","外观、引擎、输出和维护设置。",false);
            var stateBox=new BufferedFlow{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,BackColor=Color.Transparent,Padding=new Padding(2,8,2,0)};stateBox.Controls.Add(new Label{Text="当前状态",Font=new Font(Font.FontFamily,9F,FontStyle.Bold),AutoSize=true});connection=new Label{Text=Config.voice.assetScope=="private"?"● 已加载私有声音":"● 声音待配置",AutoSize=true,MaximumSize=new Size(184,0),Margin=new Padding(0,4,0,0)};stateBox.Controls.Add(connection);sideLayout.Controls.Add(stateBox,0,2);
            var workspace=new BufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,BackColor=Color.Transparent};workspace.RowStyles.Add(new RowStyle(SizeType.Absolute,58));workspace.RowStyles.Add(new RowStyle(SizeType.Percent,100));workspace.RowStyles.Add(new RowStyle(SizeType.Absolute,42));outer.Controls.Add(workspace,1,0);
            var heading=new BufferedFlow{Dock=DockStyle.Fill,FlowDirection=FlowDirection.LeftToRight,WrapContents=false,BackColor=Color.Transparent};backButton=EntryBtn("← 返回","返回上一页，并保留尚未保存的输入。",GoBack);backButton.Name="navigationBack";backButton.Visible=false;backButton.Margin=new Padding(0,8,12,6);heading.Controls.Add(backButton);pageTitle=new Label{Text="首页",Font=new Font(Font.FontFamily,18F,FontStyle.Bold),AutoSize=true,Margin=new Padding(0,10,0,0)};heading.Controls.Add(pageTitle);workspace.Controls.Add(heading,0,0);
            content=new ScenePanel{Host=this,Dock=DockStyle.Fill,ComposeChildren=true};workspace.Controls.Add(content,0,1);
            footer=new Label{Dock=DockStyle.Fill,AutoEllipsis=true,Text=Config.voice.assetScope=="private"?"已载入认可版，私有声音资源单独保存。":"请选择本地 TTS 模型包与参考条件。",TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(10,0,0,0)};workspace.Controls.Add(footer,0,2);
            BuildProfiles();BuildHome();BuildSettings();EnsureProfileFolders();BindVoice();BindSettings();ApplyTheme();ShowPage("home");
            if(!testing){tray=new NotifyIcon{Text="agent语音",Icon=SystemIcons.Application,Visible=true};var menu=new ContextMenuStrip();menu.Items.Add("打开程序",null,(s,e)=>{Show();WindowState=FormWindowState.Normal;Activate();});menu.Items.Add("停止朗读",null,async(s,e)=>await StopSpeech());menu.Items.Add("退出",null,async(s,e)=>{exiting=true;try{if(await Service.Running())await Service.Stop();}catch(Exception x){Status(x.Message);}Close();});tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>{Show();Activate();};
                ApplyIcon();poll.Tick+=async(s,e)=>await Poll();poll.Start();Shown+=async(s,e)=>await Poll();}
        }
        Label SideLabel(string text){return new Label{Text=text.ToUpperInvariant(),AutoSize=false,Width=180,Height=28,TextAlign=ContentAlignment.BottomLeft,Font=new Font(Font.FontFamily,8.5F,FontStyle.Bold),Margin=new Padding(4,8,0,2)};}
        void AddNav(FlowLayoutPanel navigation,string text,string id,string note,bool important){var button=EntryBtn(text,note,()=>ShowPage(id));button.Tag=important?"nav-primary":"nav";button.Name="nav_"+id;button.AutoSize=false;button.Width=180;button.Height=42;button.TextAlign=ContentAlignment.MiddleLeft;button.Padding=new Padding(12,0,0,0);button.Margin=new Padding(0,2,0,2);navigationButtons[id]=button;navigation.Controls.Add(button);}
        Button Btn(string text,Action action){var b=new Button{Text=text,AutoSize=true,MinimumSize=new Size(108,38),FlatStyle=FlatStyle.Flat,Margin=new Padding(0,3,10,3),Padding=new Padding(8,0,8,0),Cursor=Cursors.Hand,Tag="secondary"};b.FlatAppearance.BorderSize=1;b.Click+=(s,e)=>{try{action();}catch(Exception exc){Status(exc.Message);}};return b;}
        Button Primary(Button button){button.Tag="primary";button.Font=new Font(Font.FontFamily,Font.Size,FontStyle.Bold);button.MinimumSize=new Size(Math.Max(128,button.MinimumSize.Width),42);return button;}
        Button Danger(Button button){button.Tag="danger";return button;}
        Button EntryBtn(string text,string note,Action action){var button=Btn(text,action);button.AccessibleDescription=note;tips.SetToolTip(button,note);return button;}
        Button AsyncBtn(string text,Func<Task> action){var b=Btn(text,()=>{});b.Click+=async(s,e)=>await Guard(action);return b;}
        void Status(string text){if(!IsDisposed)footer.Text=text.Replace("\r"," ").Replace("\n"," ");}
        async Task Guard(Func<Task> action){if(operation)return;operation=true;Cursor=Cursors.AppStarting;try{await action();}catch(Exception exc){Status(exc.Message);}finally{operation=false;Cursor=Cursors.Default;}}
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
            if(!pages.ContainsKey(id))return;if(page==id&&pages[id].Visible)return;
            if(id=="home")pageHistory.Clear();
            else if(remember){if(pageHistory.Count==64)pageHistory.RemoveAt(0);pageHistory.Add(page);}
            SendMessage(content.Handle,0x000B,IntPtr.Zero,IntPtr.Zero);content.SuspendLayout();
            try{foreach(var pair in pages)pair.Value.Visible=pair.Key==id;pages[id].BringToFront();page=id;backButton.Visible=id!="home";pageTitle.Text=PageName(id);foreach(var pair in navigationButtons)StyleButton(pair.Value,pair.Key==id);}
            finally{content.ResumeLayout(true);SendMessage(content.Handle,0x000B,new IntPtr(1),IntPtr.Zero);content.Invalidate(true);}
        }
        string PageName(string id){var names=new Dictionary<string,string>{{"home","首页"},{"profiles","RVC 模型配置"},{"voices","保存声音配置"},{"configuration","模型导入导出"},{"training","训练音色"},{"settings","设置"},{"appearance","外观与图标"},{"engine","本地引擎"},{"outputMode","音频输出模式"},{"connections","连接与联动"},{"harness","Harness 接入"},{"airi","AIRI 桌宠"},{"connectionGuide","接入说明"},{"advanced","高级声音"},{"cleanup","音频清理"},{"cosyReference","CosyVoice 参考条件"}};return names.ContainsKey(id)?names[id]:"agent语音";}
        ScenePanel NewPage(string id){var p=new ScenePanel{Host=this,Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(0,8,0,12),Visible=false};pages.Add(id,p);content.Controls.Add(p);return p;}
        FlowLayoutPanel Stack(ScenePanel p){var stack=new BufferedFlow{Dock=DockStyle.Top,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,BackColor=Color.Transparent,Padding=new Padding(0,0,16,10)};p.Controls.Add(stack);p.SizeChanged+=(s,e)=>{foreach(Control c in stack.Controls){int width=Math.Max(300,p.ClientSize.Width-22);c.MinimumSize=new Size(width,0);c.Width=width;}};return stack;}
        ScenePanel Card(FlowLayoutPanel stack,string title){int width=Math.Max(300,content.Width-22);var card=new ScenePanel{Host=this,Card=true,Padding=new Padding(20,16,20,18),Width=width,MinimumSize=new Size(width,0),AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Margin=new Padding(0,0,0,14)};stack.Controls.Add(card);var table=new BufferedTable{Dock=DockStyle.Top,ColumnCount=1,AutoSize=true,BackColor=Color.Transparent};table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));card.Controls.Add(table);var label=new Label{Text=title,AutoSize=true,Font=new Font(Font.FontFamily,13F,FontStyle.Bold),Margin=new Padding(0,0,0,12)};table.Controls.Add(label);return card;}
        TableLayoutPanel Inside(ScenePanel card){return (TableLayoutPanel)card.Controls[0];}
        void Add(ScenePanel card,Control control){control.Margin=new Padding(0,3,0,6);control.Dock=DockStyle.Top;Inside(card).Controls.Add(control);card.SizeChanged+=(s,e)=>{if(control is Label)((Label)control).MaximumSize=new Size(Math.Max(120,card.Width-40),0);};}
        FlowLayoutPanel Row(params Control[] controls){var row=new BufferedFlow{AutoSize=true,WrapContents=true,BackColor=Color.Transparent,Dock=DockStyle.Top};foreach(var c in controls)row.Controls.Add(c);return row;}
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
            using(var dialog=new OpenFileDialog{Title="从音色模型包库选择",Filter="CosyVoice模型包|*.cosyvoice.ttsmodel",CheckFileExists=true,InitialDirectory=VoicePackageDirectory(),RestoreDirectory=false}){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                path=dialog.FileName;
            }
            await ImportConfiguration(path);ShowPage("home");
            Status("已从音色模型包库导入并应用："+Path.GetFileName(path));
        }
        void BuildHome(){
            var home=NewPage("home");var grid=new BufferedTable{Dock=DockStyle.Top,Height=1000,ColumnCount=2,RowCount=1,BackColor=Color.Transparent};grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,55));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));home.Controls.Add(grid);var left=new ScenePanel{Host=this,Dock=DockStyle.Fill,Margin=new Padding(0,0,8,0)};var right=new ScenePanel{Host=this,Dock=DockStyle.Fill,Margin=new Padding(0)};grid.Controls.Add(left,0,0);grid.Controls.Add(right,1,0);var leftStack=Stack(left);var rightStack=Stack(right);leftStack.SizeChanged+=(s,e)=>grid.Height=Math.Max(leftStack.Height,rightStack.Height)+20;rightStack.SizeChanged+=(s,e)=>grid.Height=Math.Max(leftStack.Height,rightStack.Height)+20;
            var service=Card(rightStack,"语音服务");serviceToggle=Primary(AsyncBtn("启动服务",async()=>{if(await Service.Running()){await Service.Stop();Status("服务已关闭。");}else{await SaveDraft();await Service.Start();Status("服务已启动。资源齐全后可生成语音。");}await Poll();}));Add(service,Row(serviceToggle,Danger(Btn("停止朗读",async()=>await StopSpeech()))));Add(service,Hint("开启后在后台接收 Harness 请求；关闭窗口可继续在托盘运行。"));
            harnessSummary=Hint("Harness：服务关闭");harnessSummary.MaximumSize=new Size(330,0);Add(service,harnessSummary);Add(service,Row(Btn("查看接入状态",()=>ShowPage("harness"))));
            var voice=Card(leftStack,"本地 TTS");
            engineChoice=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=280};engineChoice.Items.AddRange(new object[]{"cosyvoice","sapi"});Add(voice,Hint("本地引擎（默认 CosyVoice）"));Add(voice,engineChoice);
            Add(voice,Hint("当前 CosyVoice 模型。轻量音色包会匹配已安装模型，完整包可自动安装缺少的模型。"));baseModelPath=Input();Add(voice,baseModelPath);Add(voice,Row(Primary(AsyncBtn("选择训练音色模型包",SelectVoiceModelPackage)),Btn("打开音色模型包文件夹",()=>ServiceClient.Open(VoicePackageDirectory())),Btn("打开本地 TTS 引擎文件夹",()=>ServiceClient.Open(TtsEngineDirectory())),Btn("使用默认模型",()=>baseModelPath.Text="")));
            Add(voice,Row(EntryBtn("CosyVoice 训练相关参考","设置零样本推理使用的一段参考音频和完全对应的逐字台词。",()=>ShowPage("cosyReference"))));Add(voice,Hint("参考音频与逐字台词已收进此入口；它们是推理条件，不是训练完成的模型权重。"));NumberField(voice,"speed","本地 TTS 语速（1 为原速）",.7M,1.3M,.01M,2);
            saveVoiceButton=Primary(AsyncBtn("应用当前设置的声音配置",async()=>{await SaveDraft();Status("已应用当前设置的声音配置：之后的新任务使用这里选择的模型包、参考音频、台词与语速；这一步不会训练或复制模型。");}));tips.SetToolTip(saveVoiceButton,"应用首页当前选择，之后的新语音任务按这套本地 TTS 设置生成；不会创建声音快照或开始训练。");Add(voice,Row(saveVoiceButton,Btn("训练音色指导",()=>ShowPage("training"))));voiceHint=Hint("默认本地 TTS 直出；需要二次变声时再到 RVC 页面切换。");Add(voice,voiceHint);
            var speech=Card(rightStack,"常用句与试听");phrases=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=330};Add(speech,phrases);Add(speech,Row(AsyncBtn("朗读常用句",async()=>{if(phrases.SelectedItem!=null)await Generate(Convert.ToString(phrases.SelectedItem),true);})));body=Input(true);body.Text="工作好久啦，起来动一动、喝口水吧～";Add(speech,body);
            generate=Primary(AsyncBtn("生成并朗读",async()=>await Generate(body.Text,true)));playButton=AsyncBtn("播放上次生成",PlayLast);Add(speech,Row(generate,AsyncBtn("仅生成",async()=>await Generate(body.Text,false)),playButton));
            Add(speech,Row(AsyncBtn("保存为常用句",async()=>{string value=body.Text.Trim();if(value.Length==0)return;if(!Config.phrases.Contains(value))Config.phrases.Add(value);await Persist(Config,new {phrases=Config.phrases});RefreshPhrases();}),AsyncBtn("删除选中常用句",async()=>{if(phrases.SelectedItem==null)return;Config.phrases.Remove(Convert.ToString(phrases.SelectedItem));await Persist(Config,new {phrases=Config.phrases});RefreshPhrases();})));
            right.SizeChanged+=(s,e)=>phrases.Width=Math.Max(160,right.ClientSize.Width-64);
            BuildCosyReference();
        }
        void BuildCosyReference(){var stack=Stack(NewPage("cosyReference"));var card=Card(stack,"CosyVoice 训练相关参考");Add(card,Hint("这里设置当前零样本推理条件。训练数据可以有很多段；这里一次只选择一段代表性参考音频，并填写与录音逐字一致的台词。真正训练得到的权重放在首页“本地训练音色模型包”。"));PathField(card,"reference","推理参考音频（当前支持一段，建议声音干净、单人、情绪稳定）","音频|*.wav;*.flac;*.mp3;*.m4a",false);Field(card,"referenceText","参考音频逐字台词（当前零样本克隆模式必填，须与录音一致）",true);Add(card,Row(Btn("导入台词文件",()=>{using(var dialog=new OpenFileDialog{Filter="台词文件|*.txt",CheckFileExists=true})if(dialog.ShowDialog(this)==DialogResult.OK)fields["referenceText"].Text=File.ReadAllText(dialog.FileName,System.Text.Encoding.UTF8);}),AsyncBtn("应用参考条件",async()=>{await SaveDraft();Status("CosyVoice 推理参考已应用；之后的新任务使用这段音频及其逐字台词。");}),Btn("返回本地 TTS",()=>ShowPage("home"))));Add(card,Hint("如果台词与录音不一致，可能出现漏字、错字、节奏异常或音色不稳定。应用参考条件不会训练模型。"));}
        void BindVoice(){var v=Config.voice;baseModelPath.Text=v.baseModel;modelPath.Text=v.model;indexPath.Text=v.index;pitch.Value=(decimal)v.pitch;formant.Value=(decimal)v.formant;voiceHint.Text=v.processing=="accepted"?"认可版兼容：保留原离线处理，声线粗细已禁用。切换处理模式请进入高级设置。":"标准 RVC：音调与声线粗细影响转换输出。底音与风格在设置中。";if(fields.ContainsKey("reference")){fields["reference"].Text=v.reference;fields["referenceText"].Text=v.referenceText;fields["instruction"].Text=v.instruction;checks["advancedEnabled"].Checked=v.advancedEnabled;synthesisChoice.SelectedIndex=v.synthesisMode=="base"?0:1;processingChoice.SelectedIndex=v.processing=="accepted"?1:0;engineChoice.SelectedItem=v.engine;f0Choice.SelectedItem=v.f0Method;numbers["speed"].Value=(decimal)v.speed;numbers["indexRate"].Value=(decimal)v.indexRate;numbers["rmsMix"].Value=(decimal)v.rmsMix;numbers["leadingSilence"].Value=(decimal)v.leadingSilence;numbers["volume"].Value=(decimal)v.volume;numbers["protect"].Value=(decimal)v.protect;}UpdateVoiceControls();RefreshPhrases();RefreshProfiles();}
        VoiceConfig ReadVoice(){var v=Config.voice.Clone();v.baseModel=baseModelPath.Text.Trim();v.synthesisMode=synthesisChoice.SelectedIndex==0?"base":"rvc";v.advancedEnabled=checks["advancedEnabled"].Checked;v.model=modelPath.Text.Trim();v.index=indexPath.Text.Trim();v.pitch=(double)pitch.Value;v.formant=(double)formant.Value;v.reference=fields["reference"].Text.Trim();v.referenceText=fields["referenceText"].Text;v.instruction=fields["instruction"].Text;v.engine=Convert.ToString(engineChoice.SelectedItem);v.f0Method=Convert.ToString(f0Choice.SelectedItem);v.speed=(double)numbers["speed"].Value;v.indexRate=(double)numbers["indexRate"].Value;v.rmsMix=(double)numbers["rmsMix"].Value;v.leadingSilence=(double)numbers["leadingSilence"].Value;v.volume=(double)numbers["volume"].Value;v.protect=(double)numbers["protect"].Value;v.processing=processingChoice.SelectedIndex==1?"accepted":"standard";return v;}
        void ValidatePaths(VoiceConfig v){if(v.engine=="cosyvoice"&&v.baseModel.Length>0){string folder=Store.Resolve(v.baseModel);if(!Directory.Exists(folder)||new[]{"cosyvoice3.yaml","llm.pt","flow.pt","hift.pt"}.Any(name=>!File.Exists(Path.Combine(folder,name))))throw new InvalidOperationException("请选择完整 CosyVoice 3 模型目录，包含 yaml、llm.pt、flow.pt、hift.pt。");}if(v.processing=="accepted"&&v.formant!=0)throw new InvalidOperationException("认可版兼容保持声线粗细为零。");foreach(var item in new[]{new[]{v.model,".pth"},new[]{v.index,".index"},new[]{v.reference,""}}){if(item[0].Length==0)continue;if(v.synthesisMode=="base"&&(item[1]==".pth"||item[1]==".index"))continue;string path=Store.Resolve(item[0]);if(!File.Exists(path)||(item[1].Length>0&&!path.EndsWith(item[1],StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("文件不存在或类型不匹配："+item[0]);}}
        async Task Persist(Settings next,object patch){if(await Service.Running()){var response=await Service.Request("/api/config",patch,90000);Config=Json.Decode<Settings>(Json.Encode(response["config"]));}else{Store.Write(next);Config=Json.Copy(next);}Status("配置已保存。");}
        async Task SaveDraft(){var next=Json.Copy(Config);next.voice=ReadVoice();ValidatePaths(next.voice);await Persist(next,new {voice=next.voice});}
        async Task Generate(string text,bool play){await SaveDraft();if(!await Service.Running())throw new InvalidOperationException("请先启动语音服务。");var result=await Service.Request("/speak",new {text=text,play=play,source="manual"});if(result.ContainsKey("skipped")){Status("这段只有链接或代码，已跳过。");return;}lastJob=Json.Text(result,"jobId");Status("已进入朗读队列。");await WaitJob(lastJob);}
        async Task WaitJob(string id){for(int i=0;i<2000;i++){await Task.Delay(300);if(IsDisposed)return;var job=await Service.Request("/api/jobs/"+id);string state=Json.Text(job,"state");if(state=="completed"){Status("已完成 · "+Store.Resolve("data/output")+"（生成的音频可手动清理）");return;}if(state=="failed"||state=="cancelled")throw new InvalidOperationException(Json.Text(job,"error"));}throw new InvalidOperationException("生成等待超时，请查看日志。");}
        async Task PlayLast(){if(lastJob.Length==0)throw new InvalidOperationException("请先生成一条语音。");var result=await Service.Request("/api/play",new {jobId=lastJob});await WaitJob(Json.Text(result,"jobId"));}
        async Task StopSpeech(){try{await Service.Request("/stop",new {});Status("已停止当前朗读和待播放任务。");}catch(Exception exc){Status(exc.Message);}}
        async Task Poll(){if(polling||IsDisposed)return;polling=true;try{var health=await Service.Request("/health",null,1000);if(Json.Text(health,"service")!="agent-voice-public")throw new InvalidOperationException("端口被其他服务占用");serviceToggle.Text="关闭服务";var missing=health["missing"] as object[];connection.Text=missing!=null&&missing.Length>0?"服务已开启 · 待配置声音":"服务已开启 · "+(Convert.ToBoolean(health["airiConnected"])?"桌宠已连接":"本地输出");object states;var connections=health.TryGetValue("harnessConnections",out states)?states as Dictionary<string,object>:null;object rawChecks;var verified=health.TryGetValue("verification",out rawChecks)?rawChecks as Dictionary<string,object>:null;
                if(verified!=null){foreach(var entry in verified){var item=entry.Value as Dictionary<string,object>;if(item!=null&&Json.Text(item,"state")=="verified"&&connections!=null&&connections.ContainsKey(entry.Key))((Dictionary<string,object>)connections[entry.Key])["state"]="verified";}UpdateVerification(verified);}UpdateHarnessIndicators(connections,true);await ResumeAiriVerification(Convert.ToBoolean(health["airiConnected"]));}catch{pendingAiriVerification=false;serviceToggle.Text="启动服务";connection.Text="服务已关闭";UpdateHarnessIndicators(null,false);if(airiIndicator!=null){airiIndicator.Text="AIRI · 服务关闭";airiIndicator.ForeColor=ForeColor;}}finally{if(Config.voice.assetScope=="private")connection.Text+=" · 私有声音";polling=false;}}
        void RefreshPhrases(){string selected=Convert.ToString(phrases.SelectedItem);phrases.Items.Clear();foreach(var text in Config.phrases)phrases.Items.Add(text);if(phrases.Items.Contains(selected))phrases.SelectedItem=selected;else if(phrases.Items.Count>0)phrases.SelectedIndex=0;}
        void RefreshProfiles(){if(profileList==null)return;string selected=(profileList.SelectedItem as SavedVoice)==null?"":((SavedVoice)profileList.SelectedItem).id;profileList.Items.Clear();foreach(var profile in Config.profiles)profileList.Items.Add(profile);for(int i=0;i<profileList.Items.Count;i++)if(((SavedVoice)profileList.Items[i]).id==selected)profileList.SelectedIndex=i;}
        void BuildProfiles(){
            var stack=Stack(NewPage("profiles"));
            var voice=Card(stack,"RVC模型配置");
            rvcModeStatus=Hint("当前输出模式：读取中");rvcModeStatus.Font=new Font(Font.FontFamily,11F,FontStyle.Bold);Add(voice,rvcModeStatus);
            modelPath=Input();indexPath=Input();Add(voice,Hint("模型 .pth"));Add(voice,modelPath);Add(voice,Row(Btn("选择 .pth",()=>SelectPath(modelPath,"RVC 模型|*.pth",false))));Add(voice,Hint("索引 .index（可选，空白表示不使用）"));Add(voice,indexPath);Add(voice,Row(Btn("选择 .index",()=>SelectPath(indexPath,"RVC 索引|*.index",false)),Btn("不用索引",()=>indexPath.Text="")));
            pitch=Number(-16,16,1,0);formant=Number(-2,2,.05M,2);Add(voice,Row(Hint("音调（半音）"),pitch,Hint("性别因子／声线粗细"),formant));

            Add(voice,Hint("RVC 是可选的第二步变声。点击“应用并启用 RVC”会同时保存这里的参数并切换到 RVC＋本地 TTS；需要恢复纯 TTS 时再进入输出模式切换。"));Add(voice,Row(Primary(AsyncBtn("应用并启用 RVC",ApplyRvcConfiguration)),Btn("切换输出模式",()=>OpenOutputMode())));
            BuildSavedVoices();
        }
        async Task ApplyRvcConfiguration(){
            int previous=synthesisChoice.SelectedIndex;
            synthesisChoice.SelectedIndex=1;
            try{await SaveDraft();UpdateVoiceControls();UpdateOutputModeStatus(true);Status("已应用 RVC 模型配置，当前输出模式：RVC＋本地 TTS。之后的新任务会经过 RVC 二次变声。");}
            catch{synthesisChoice.SelectedIndex=previous;UpdateVoiceControls();throw;}
        }
        void BuildSavedVoices(){var stack=Stack(NewPage("voices"));var card=Card(stack,"保存声音配置");Add(card,Hint("“应用当前设置的声音配置”只更新正在使用的设置；这里把一整套参数保存为有名字的快照，方便以后在不同声音之间一键切换。本地配置保存 TTS 模型、参考条件及表达参数；组合配置还保存 RVC 模型、索引与转换参数。"));
            profileKind=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=330};profileKind.Items.AddRange(new object[]{"本地 TTS 配置","RVC＋本地 TTS 配置"});profileKind.SelectedIndex=0;Add(card,profileKind);
            profileName=Input();profileName.Text="我的声音";Add(card,profileName);Add(card,Row(Primary(AsyncBtn("保存当前配置",SaveProfile))));profileList=new ListBox{Height=220,IntegralHeight=false};Add(card,profileList);
            Add(card,Row(Primary(AsyncBtn("应用选中的配置",async()=>{var item=profileList.SelectedItem as SavedVoice;if(item==null)return;var next=Json.Copy(Config);next.voice=item.voice.synthesisMode=="base"?MergeLocal(next.voice,item.voice):item.voice.Clone();ValidatePaths(next.voice);await Persist(next,new {voice=next.voice});BindVoice();Status("已应用声音快照："+item.name);})),Danger(AsyncBtn("删除配置",async()=>{var item=profileList.SelectedItem as SavedVoice;if(item==null)return;var next=Json.Copy(Config);next.profiles.RemoveAll(p=>p.id==item.id);await Persist(next,new {profiles=next.profiles});ArchiveProfileFolder(item);deleted=Json.Copy(item);RefreshProfiles();Status("声音快照已删除，模型文件保留；可撤销。");})),AsyncBtn("撤销删除",async()=>{if(deleted==null)return;var next=Json.Copy(Config);next.profiles.Add(deleted);await Persist(next,new {profiles=next.profiles});WriteProfileFolder(deleted);deleted=null;RefreshProfiles();}),Btn("打开配置储存位置",()=>{string directory=ProfileDirectory();ServiceClient.Open(directory);Status("每个声音快照有独立同名文件夹："+directory);})));Add(card,Hint("每次保存都会在 data/voice-configurations 下建立“配置名称 + ID”文件夹，profile.json 是可查看的参数副本；程序运行仍以 settings.json 为权威配置。"));
        }
        static readonly string[] LocalVoiceFields={"engine","baseModel","reference","referenceText","instruction","style","speed","leadingSilence","volume","advancedEnabled","assetScope"};
        static VoiceConfig MergeLocal(VoiceConfig current,VoiceConfig local){var result=current.Clone();foreach(string name in LocalVoiceFields){var field=typeof(VoiceConfig).GetField(name);field.SetValue(result,field.GetValue(local));}result.synthesisMode="base";return result;}
        async Task SaveProfile(){var next=Json.Copy(Config);var draft=ReadVoice();draft.synthesisMode=profileKind.SelectedIndex==1?"rvc":"base";if(draft.engine=="cosyvoice"&&draft.baseModel.Length==0)draft.baseModel=Config.engines.cosyModel;ValidatePaths(draft);string name=profileName.Text.Trim();if(name.Length==0)name="声音 "+DateTime.Now.ToString("MM-dd HH:mm");if(name.Length>80)throw new InvalidOperationException("配置名最长 80 字。");var snapshot=draft.synthesisMode=="base"?MergeLocal(new VoiceConfig(),draft):draft.Clone();var saved=new SavedVoice{name=name,voice=snapshot};next.profiles.Add(saved);await Persist(next,new {profiles=next.profiles});WriteProfileFolder(saved);RefreshProfiles();Status("声音快照已保存，并建立独立配置文件夹："+ProfileFolder(saved));}
        string ProfileDirectory(){string directory=Store.Resolve("data/voice-configurations");Directory.CreateDirectory(directory);return directory;}
        static string SafeProfileName(string value){var invalid=Path.GetInvalidFileNameChars();string safe=new string(value.Select(c=>invalid.Contains(c)||char.IsControl(c)?'_':c).ToArray()).Trim().TrimEnd('.');return safe.Length==0?"未命名配置":safe.Length>60?safe.Substring(0,60):safe;}
        string ProfileFolder(SavedVoice profile){return Path.Combine(ProfileDirectory(),SafeProfileName(profile.name)+"-"+profile.id.Substring(0,Math.Min(8,profile.id.Length)));}
        void WriteProfileFolder(SavedVoice profile){string folder=ProfileFolder(profile);Directory.CreateDirectory(folder);string target=Path.Combine(folder,"profile.json"),temp=target+".tmp";File.WriteAllText(temp,Json.Encode(profile),new System.Text.UTF8Encoding(false));if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);}
        void EnsureProfileFolders(){foreach(var profile in Config.profiles)WriteProfileFolder(profile);}
        void ArchiveProfileFolder(SavedVoice profile){string folder=ProfileFolder(profile);if(!Directory.Exists(folder))return;string deletedRoot=Path.Combine(ProfileDirectory(),".deleted");Directory.CreateDirectory(deletedRoot);string target=Path.Combine(deletedRoot,Path.GetFileName(folder)+"-"+DateTime.Now.ToString("yyyyMMddHHmmss"));Directory.Move(folder,target);}
        protected override bool ProcessCmdKey(ref Message msg,Keys key){if(key==Keys.Escape){ShowPage("home");return true;}return base.ProcessCmdKey(ref msg,key);}
        protected override void OnFormClosing(FormClosingEventArgs e){if(!exiting&&tray!=null){e.Cancel=true;Hide();return;}poll.Stop();tips.Dispose();if(tray!=null){tray.Visible=false;tray.Dispose();}if(background!=null)background.Dispose();if(scaledBackground!=null)scaledBackground.Dispose();base.OnFormClosing(e);}
        public void PaintScene(Graphics g,Control control){
            using(var brush=new SolidBrush(BackColor))g.FillRectangle(brush,control.ClientRectangle);
            if(background==null)return;
            if(scaledBackground==null||scaledBackground.Size!=ClientSize){
                if(scaledBackground!=null)scaledBackground.Dispose();
                scaledBackground=new Bitmap(Math.Max(1,ClientSize.Width),Math.Max(1,ClientSize.Height));
                using(var graphics=Graphics.FromImage(scaledBackground)){
                    graphics.Clear(BackColor);graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    float scale=Math.Max((float)ClientSize.Width/background.Width,(float)ClientSize.Height/background.Height);
                    float w=background.Width*scale,h=background.Height*scale;
                    graphics.DrawImage(background,(ClientSize.Width-w)/2,(ClientSize.Height-h)/2,w,h);
                }
            }
            Point origin=PointToClient(control.PointToScreen(Point.Empty));
            Rectangle source=Rectangle.Intersect(new Rectangle(origin,control.ClientSize),new Rectangle(Point.Empty,scaledBackground.Size));
            if(source.Width>0&&source.Height>0)g.DrawImage(scaledBackground,new Rectangle(source.X-origin.X,source.Y-origin.Y,source.Width,source.Height),source,GraphicsUnit.Pixel);
        }
        static Color Mix(Color one,Color two,int secondPercent){int first=100-secondPercent;return Color.FromArgb((one.R*first+two.R*secondPercent)/100,(one.G*first+two.G*secondPercent)/100,(one.B*first+two.B*secondPercent)/100);}
        void StyleButton(Button button,bool selected=false){string kind=Convert.ToString(button.Tag);button.FlatStyle=FlatStyle.Flat;if(kind=="primary"){button.BackColor=Accent;button.ForeColor=Accent.GetBrightness()<.58?Color.White:Color.Black;button.FlatAppearance.BorderSize=0;}else if(kind=="danger"){button.BackColor=Color.FromArgb(190,62,72);button.ForeColor=Color.White;button.FlatAppearance.BorderSize=0;}else if(kind=="nav"||kind=="nav-primary"){button.BackColor=selected?Accent:(kind=="nav-primary"?Mix(CardColor,Accent,12):CardColor);button.ForeColor=selected?(Accent.GetBrightness()<.58?Color.White:Color.Black):ForeColor;button.FlatAppearance.BorderSize=0;}else{button.BackColor=CardColor;button.ForeColor=BackColor.GetBrightness()<.4F?Mix(Color.White,Accent,20):Color.FromArgb(75,55,70);button.FlatAppearance.BorderColor=Mix(CardColor,Accent,65);button.FlatAppearance.BorderSize=1;}}
        void ApplyTheme(){var a=Config.appearance;Accent=ColorTranslator.FromHtml(a.accent);BackColor=ColorTranslator.FromHtml(a.backgroundColor);ForeColor=ColorTranslator.FromHtml(a.textColor);CardColor=BackColor.GetBrightness()<.4F?Color.FromArgb(43,38,50):Color.White;string image=Store.Resolve(a.background);if(background!=null){background.Dispose();background=null;}if(scaledBackground!=null){scaledBackground.Dispose();scaledBackground=null;}if(File.Exists(image))using(var source=Image.FromFile(image))background=new Bitmap(source);WalkTheme(shell);UpdateHarnessIndicators(harnessStates,harnessOnline);Invalidate(true);}
        void WalkTheme(Control parent){foreach(Control c in parent.Controls){c.ForeColor=ForeColor;if(c is Button)StyleButton((Button)c,navigationButtons.ContainsKey(page)&&navigationButtons[page]==c);else if(c is TextBox||c is ComboBox||c is ListBox||c is NumericUpDown){c.BackColor=CardColor;c.ForeColor=ForeColor;}WalkTheme(c);}}
        void ApplyIcon(){string path=Store.Resolve(Config.appearance.icon);if(!File.Exists(path))return;try{using(var icon=new Icon(path)){Icon=(Icon)icon.Clone();if(tray!=null)tray.Icon=Icon;}}catch(Exception exc){Status(exc.Message);}}
    }
}
