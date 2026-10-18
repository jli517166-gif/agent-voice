using Button = AgentVoice.AnimeButton;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed partial class MainForm {
        readonly string[] setupTitles={"检查电脑","选择 TTS","运行环境","选择音色","试听确认","Harness","AIRI 桌宠","配置结果"};
        readonly List<ScenePanel> setupPanels=new List<ScenePanel>();
        readonly string[] setupIds={"setup","setup-engine","setup-environment","setup-voice","setup-preview","setup-harness","setup-airi","setup-result"};
        readonly List<SetupAnimation> setupAnimations=new List<SetupAnimation>();
        readonly List<Button[]> setupNavigation=new List<Button[]>();
        readonly List<CheckBox> setupMotionChoices=new List<CheckBox>();
        readonly List<CheckBox> setupSoundChoices=new List<CheckBox>();
        Label setupResources,setupResult,setupSystem,setupPlayback;
        ComboBox setupEngineChoice;CheckBox setupOffline;Button setupBack,setupNext,setupSkip;
        internal int SetupStep {get;private set;}
        WelcomePreferences SetupPreference(){var p=new WelcomeState(Store).Read();if(p.setupStates==null)p.setupStates=new Dictionary<string,string>();return p;}
        void SetupStatus(string key,string value){var p=SetupPreference();p.setupStates[key]=value;new WelcomeState(Store).Write(p);RefreshSetup();}
        internal void ApplyWelcomeChoice(string choice){if(choice=="setup")ShowPage("setup");else if(choice=="import"){ShowPage("configuration");importConfigurationButton.PerformClick();}else ShowPage("home");}
        void ReplayWelcome(){var state=new WelcomeState(Store);var p=state.Read();string choice;using(var welcome=new WelcomeForm(p.reduceMotion,true,p.interfaceSounds)){if(welcome.ShowDialog(this)!=DialogResult.OK)return;choice=welcome.Choice;state.Choose(choice,welcome.ReduceMotion);}ApplyWelcomeChoice(choice);}
        bool IsSetupPage(string id){return Array.IndexOf(setupIds,id)>=0;}
        internal void GoSetup(int step){SetupStep=Math.Max(0,Math.Min(7,step));var p=SetupPreference();p.setupStep=SetupStep;p.lastStep="setup";new WelcomeState(Store).Write(p);Navigate(setupIds[SetupStep],false);RefreshSetup();}
        void BuildSetup(){
            SetupStep=Math.Max(0,Math.Min(7,SetupPreference().setupStep));
            var system=SetupCard(0,"从这台电脑开始","检查目录空间和显卡，确认安装条件。","电脑与资源");
            setupResources=Hint("");setupResources.Name="setupResourceSummary";Add(system,setupResources);
            setupSystem=Hint("点击检查系统、目录权限和显卡；不需要 Python，不会安装驱动。");Add(system,setupSystem);
            Add(system,Row(Primary(AsyncBtn("检查电脑",CheckSetupComputer)),AsyncBtn("恢复未完成安装",async()=>{await Task.Run(()=>ModulePackages.Recover(Store));Config=Store.Read();BindSettings();Status("未完成安装已恢复；原组件与配置保留。");})));
            var engine=SetupCard(1,"选一个声音引擎","选择适合你的 TTS，也可以稍后再选。","你想使用哪个 TTS？");Add(engine,Hint("CosyVoice、GPT-SoVITS v5 Turbo、ProPlus 已适配。选择后仍需安装引擎与运行环境。"));
            setupEngineChoice=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Name="setupEngineChoice",Width=480};setupEngineChoice.Items.AddRange(EngineChoices);setupEngineChoice.SelectedIndex=EngineIndex();Add(engine,setupEngineChoice);
            Add(engine,Hint("点击下一步会保存所选引擎；跳过则保留原设置。"));
            Add(engine,Row(AsyncBtn("导入 TTS 包",async()=>await ChooseModule("engine")),EntryBtn("定位已有引擎","保留自定义路径，再返回配置中心继续。",()=>ShowPage("engine")),EntryBtn("引擎说明","查看能力、许可和安装位置。",()=>ServiceClient.Open(Store.Resolve("TTS使用手册.md")))));
            var environment=SetupCard(2,"让环境就位","联网补齐，或导入为这台电脑准备的离线包。","运行环境");Add(environment,Hint("环境包是运行时与依赖；TTS 包是源码和基础模型；音色稍后导入。"));
            setupOffline=new CheckBox{Text="严格离线（不联网）",AutoSize=true,Checked=SetupPreference().offline,Name="setupOffline"};tips.SetToolTip(setupOffline,"只使用本地资源，缺少文件时明确提示，不会自动转为联网下载。");setupOffline.CheckedChanged+=(s,e)=>{var p=SetupPreference();p.offline=setupOffline.Checked;new WelcomeState(Store).Write(p);};Add(environment,setupOffline);
            Add(environment,Row(Primary(AsyncBtn("导入环境补齐包",async()=>await ChooseModule("environment"))),AsyncBtn("检查当前环境",async()=>await PrepareSetupEnvironment(false,false)),AsyncBtn("补齐当前引擎",async()=>await PrepareSetupEnvironment(true,false)),AsyncBtn("导出资源清单",ExportSetupRequirements),EntryBtn("定位已有环境","手动选择已有 Python 与引擎。",()=>ShowPage("engine"))));
            Add(environment,Hint("离线包按所含 GPU 运行时档位使用，不替代驱动。新显卡需要兼容档位或离线 wheel。安装备份保存在 data/module-backups，缺项会明确报错，不会静默联网。"));
            Add(environment,Row(EntryBtn("打开系统安装文件","如环境包附带官方 VC++ 安装器，可在此手动安装；程序不会自动提权。",()=>{string path=Store.Resolve("tools/system-prerequisites");Directory.CreateDirectory(path);ServiceClient.Open(path);})));
            var voice=SetupCard(3,"找到你的声音","导入音色包，或使用已经保存的声音。","声音来源");Add(voice,Hint("轻量包复用匹配的基础模型；完整包可安装模型权重，但仍需要环境。自行配置时，参考录音与逐字台词要一致。"));
            Add(voice,Row(Primary(AsyncBtn("选择并导入音色包",ImportSetupVoice)),EntryBtn("选择已有声音方案","应用以前保存的声音。",()=>ShowPage("voices")),EntryBtn("自行设置参考声音","选择录音并填写逐字台词。",()=>ShowPage("cosyReference")),EntryBtn("训练音色","查看当前引擎的训练指导。",()=>ShowPage("training"))));
            var preview=SetupCard(4,"听一听，再确认","实际播放和你的听感，是两项独立的确认。","声音测试");setupPlayback=Hint("尚未运行本次试听。完成实际播放后，请确认声音是否合适。");Add(preview,setupPlayback);
            Add(preview,Row(Primary(AsyncBtn("生成并播放测试",PreviewSetupVoice)),Danger(AsyncBtn("停止试听",StopSpeech)),AsyncBtn("修复兼容环境",async()=>await PrepareSetupEnvironment(true,true)),Btn("确认声音正常",ConfirmSetupVoice),EntryBtn("调整声音","在首页调整语速和当前声音。",()=>ShowPage("home"))));
            var harness=SetupCard(5,"把对话接过来","选择 Codex 或 DeepSeek Harness 的安装目录。","连接你的 Harness");Add(harness,Hint("随机回复、来源回执和实际播放完成后才通过。DeepSeek 仅桌面登录时，需要在聊天框发送一次复制的短句；API Key 模式可自动验证。"));
            Add(harness,Row(Primary(AsyncBtn("选择 Harness 并验证",async()=>{await EnsureSetupService();await BeginAutoHarness();ShowPage("setup");})),EntryBtn("管理连接与 MCP","查看真实状态、安装插件或修改来源。",()=>ShowPage("harness")),AsyncBtn("不接入 Harness",async()=>{var next=Json.Copy(Config);foreach(string key in next.harness.Keys.ToArray())next.harness[key]=false;await Persist(next,new{harness=next.harness});BindSettings();SetupStatus("harness","skipped");})));
            Add(harness,Row(EntryBtn("开发其他接入","查看其他 Harness 的模板、事件接口与验证方式。",()=>ServiceClient.Open(Store.Resolve("Harness二次开发指南.md"))),EntryBtn("失败原因与处理","查看凭据、目录、连接器和播放失败的处理。",()=>ShowPage("connectionGuide"))));
            var airi=SetupCard(6,"让桌宠接过声音","可选的一步。不启用也能在本机正常播放。","AIRI 桌宠联动");Add(airi,Hint("默认关闭。启用后选择原 AIRI 目录，原生播放回执通过才成功；不兼容版本会明确提示。"));
            Add(airi,Row(Primary(AsyncBtn("AIRI 接入验证",async()=>{await EnsureSetupService();checks["airiEnabled"].Checked=true;outputChoice.SelectedIndex=1;await BeginVerification("airi");ShowPage("setup");})),AsyncBtn("使用本机播放",async()=>{var next=Json.Copy(Config);next.airi.enabled=false;next.airi.output="local";await Persist(next,new{airi=next.airi});BindSettings();SetupStatus("airi","skipped");}),EntryBtn("AIRI 详细设置","选择原安装与输出并查看帮助。",()=>ShowPage("airi"))));
            var finish=SetupCard(7,"按你的节奏开始","看一眼配置结果，随时可以回来继续。","这次配置的结果");setupResult=Hint("");setupResult.Name="setupFinalResult";Add(finish,setupResult);Add(finish,Row(AsyncBtn("刷新真实连接状态",async()=>{await RefreshSetupConnections();RefreshSetup();}),EntryBtn("安装与故障说明","查看模块位置与离线重试步骤。",()=>ServiceClient.Open(Store.Resolve("模块安装与配置指南.md")))));
            RefreshSetup();
        }
        ScenePanel SetupCard(int step,string title,string note,string actionTitle){
            var route=NewPage(setupIds[step]);route.AutoScroll=false;route.Name="setupRoute_"+step;setupPanels.Add(route);
            var layout=new BufferedTable{Dock=DockStyle.Fill,RowCount=2,ColumnCount=2,BackColor=Color.Transparent,Margin=new Padding(0)};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,44));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,56));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,76));route.Controls.Add(layout);
            var banner=new SetupAnimation(step,title,note){Host=this,Dock=DockStyle.Fill,Name="setupAnimation_"+step};banner.Motion(SetupPreference().reduceMotion);setupAnimations.Add(banner);layout.Controls.Add(banner,0,0);
            var scroller=new ScenePanel{Host=this,Dock=DockStyle.Fill,AutoScroll=true,Name="setupScroller_"+step,Margin=new Padding(16,74,0,0)};layout.Controls.Add(scroller,1,0);var card=Card(Stack(scroller),actionTitle);
            var back=Btn("← 上一步",()=>GoSetup(step-1));back.Name="setupBack_"+step;back.Enabled=step>0;
            var next=Primary(step==1?AsyncBtn("下一步 →",async()=>{await SelectSetupEngine();GoSetup(2);}):Btn(step==7?"进入工作台":"下一步 →",()=>{if(step==7)ShowPage("home");else GoSetup(step+1);}));next.Name="setupNext_"+step;
            var skip=Btn("跳过本步",()=>{SetupStatus("step"+step,"skipped");GoSetup(step+1);});skip.Name="setupSkip_"+step;skip.Visible=step<7;
            var save=Btn("保存并稍后继续",()=>{var p=SetupPreference();p.setupStep=step;new WelcomeState(Store).Write(p);ShowPage("home");});save.Name="setupSave_"+step;
            var actions=new BufferedFlow{Dock=DockStyle.Fill,WrapContents=false,BackColor=Color.Transparent,Padding=new Padding(4,15,0,0)};
            actions.Controls.AddRange(new Control[]{back,next,skip,save});layout.Controls.Add(actions,0,1);layout.SetColumnSpan(actions,2);setupNavigation.Add(new[]{back,next,skip,save});
            var motion=new CheckBox{Text="减少动画",AutoSize=true,Name="setupMotion_"+step,Checked=SetupPreference().reduceMotion,Margin=new Padding(4,10,0,0)};
            motion.CheckedChanged+=(s,e)=>{var p=SetupPreference();if(p.reduceMotion==motion.Checked)return;p.reduceMotion=motion.Checked;new WelcomeState(Store).Write(p);foreach(var item in setupMotionChoices)item.Checked=p.reduceMotion;foreach(var animation in setupAnimations)animation.Motion(p.reduceMotion);if(homeHero!=null)homeHero.Motion(p.reduceMotion);};
            motion.Font=new Font(Font.FontFamily,8.5F);motion.Margin=new Padding(14,10,0,0);setupMotionChoices.Add(motion);actions.Controls.Add(motion);
            var sounds=new CheckBox{Text="电音音效",AutoSize=true,Checked=SetupPreference().interfaceSounds,Name="setupSounds_"+step,Font=new Font(Font.FontFamily,8.5F),Margin=new Padding(12,10,0,0)};
            tips.SetToolTip(sounds,"仅用于服务关闭时的页面切换。开启语音服务后暂停界面音效，避免干扰朗读。");
            sounds.CheckedChanged+=(s,e)=>SetInterfaceSounds(sounds.Checked);setupSoundChoices.Add(sounds);actions.Controls.Add(sounds);return card;
        }
        void SetInterfaceSounds(bool enabled){var p=SetupPreference();if(p.interfaceSounds==enabled)return;p.interfaceSounds=enabled;new WelcomeState(Store).Write(p);foreach(var item in setupSoundChoices)item.Checked=enabled;if(!enabled)interfaceSounds.Stop();}
        string VoiceEvidence(){
            string model=Config.voice.engine=="cosyvoice"?(string.IsNullOrEmpty(Config.voice.baseModel)?Config.engines.cosyModel:Config.voice.baseModel):(string.IsNullOrEmpty(Config.voice.gptModel)?Config.engines.gptModel:Config.voice.gptModel);
            string modelFile=Path.Combine(Store.Resolve(model),Config.voice.engine=="cosyvoice"?"llm.pt":Config.voice.gptVariant=="v5turbo"?"gsv-v5-pretrained/s2Gv5turbo.pth":"v2Pro/s2Gv2ProPlus.pth");
            Func<string,string> stamp=path=>{if(string.IsNullOrEmpty(path))return "missing";var f=new FileInfo(path);return f.Exists?f.Length+":"+f.LastWriteTimeUtc.Ticks:"missing";};
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Json.Encode(new{voice=Config.voice,engines=Config.engines,airi=Config.airi,reference=stamp(Store.Resolve(Config.voice.reference)),model=stamp(modelFile)})))).Replace("-","");
        }
        void RefreshSetup(){
            if(setupResources==null)return;var c=Config;bool core=File.Exists(Store.Resolve(c.engines.pythonService)),runtime=File.Exists(Store.Resolve(c.voice.engine=="cosyvoice"?c.engines.pythonTts:c.engines.pythonGpt));string model=c.voice.engine=="cosyvoice"?(string.IsNullOrEmpty(c.voice.baseModel)?c.engines.cosyModel:c.voice.baseModel):(string.IsNullOrEmpty(c.voice.gptModel)?c.engines.gptModel:c.voice.gptModel);bool reference=File.Exists(Store.Resolve(c.voice.reference))&&!string.IsNullOrWhiteSpace(c.voice.referenceText);
            setupResources.Text="主服务 "+(core?"已找到":"待补齐")+"  ·  TTS 环境 "+(runtime?"已找到":"待补齐")+"  ·  模型目录 "+(Directory.Exists(Store.Resolve(model))?"已找到":"待选择")+"  ·  参考声音 "+(reference?"已配置":"待配置")+"\r\n文件存在不代表生成或接入通过。";
            var nav=setupNavigation[SetupStep];setupBack=nav[0];setupNext=nav[1];setupSkip=nav[2];
            var p=SetupPreference();bool current=p.voiceEvidence==VoiceEvidence();setupResult.Text=setupResources.Text+"\r\n\r\n试听："+(current&&p.voiceJobId.Length>0?"上次实际播放完成（"+p.voiceVerifiedAt+"）":"尚未验证当前配置")+"\r\n听感确认："+(current&&p.heardVoice?"已确认":"待确认或已跳过")+"\r\nHarness："+setupHarnessSummary+"\r\nAIRI："+(c.airi.enabled?setupAiriSummary:"未启用，使用本机播放")+"\r\n\r\n可以进入首页；未完成项目会保留，可随时继续。";
        }
        string setupHarnessSummary="需刷新真实连接状态（启用不代表已验证）",setupAiriSummary="需刷新真实播放状态";
        async Task RefreshSetupConnections(){if(!await Service.Running()){SetSetupConnections(null);return;}SetSetupConnections(await Service.Request("/health"));}
        void SetSetupConnections(Dictionary<string,object> health){if(health==null){setupHarnessSummary="服务关闭，当前未验证";setupAiriSummary="服务关闭，当前未验证";}else{object raw;var states=health.TryGetValue("verification",out raw)?raw as Dictionary<string,object>:null;var enabled=Config.harness.Where(x=>x.Value).Select(x=>x.Key).ToArray();setupHarnessSummary=enabled.Length==0?"未启用":string.Join("；",enabled.Select(source=>source+"："+VerificationLabel(states,source)).ToArray());setupAiriSummary=VerificationLabel(states,"airi");}if(IsSetupPage(page)&&setupResult!=null)RefreshSetup();}
        static string VerificationLabel(Dictionary<string,object> states,string source){object raw;var item=states!=null&&states.TryGetValue(source,out raw)?raw as Dictionary<string,object>:null;return item!=null&&Json.Text(item,"state")=="verified"?"本次真实播放验证通过":"尚未通过本次真实验证";}
        async Task CheckSetupComputer(){
            setupSystem.Text=await Task.Run(()=>{var text=new StringBuilder();text.AppendLine(Environment.OSVersion+" · "+(Environment.Is64BitOperatingSystem?"64 位":"32 位"));text.AppendLine("剩余空间："+(new DriveInfo(Path.GetPathRoot(Store.Root)).AvailableFreeSpace/1024d/1024/1024).ToString("0.00")+" GB");string path=Store.Resolve("data/setup-write-test.tmp");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,"check");File.Delete(path);text.AppendLine("软件目录可写。");try{var info=new ProcessStartInfo("nvidia-smi","--query-gpu=name,driver_version,memory.total --format=csv,noheader"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};using(var process=Process.Start(info)){var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();if(!process.WaitForExit(15000)){process.Kill();throw new InvalidOperationException("显卡检测超时");}Task.WaitAll(stdout,stderr);text.AppendLine(process.ExitCode==0?stdout.Result:"未检测到可用 NVIDIA 驱动，请安装驱动后重试。");}}catch(Exception e){text.AppendLine("显卡检查未通过："+e.Message);}text.AppendLine("音频设备、CUDA 内核与实际声音在试听步骤继续验证。");return text.ToString();});SetupStatus("system","checked");
        }
        async Task SelectSetupEngine(){engineChoice.SelectedIndex=setupEngineChoice.SelectedIndex;var next=Json.Copy(Config);next.voice.engine=SelectedEngine();next.voice.gptVariant=SelectedVariant();await Persist(next,new{voice=next.voice});BindVoice();SetupStatus("engine","selected");Status("已选择 TTS；仍需安装与试听验证。");}
        async Task ChooseModule(string kind){
            if(await Service.Running())throw new InvalidOperationException("请先关闭语音服务，再安装模块；原配置未改动。");
            using(var dialog=new OpenFileDialog{Title=kind=="engine"?"选择 TTS 包":"选择离线环境包",Filter=kind=="engine"?"TTS 包|*.ttsengine":"环境补齐包|*.ttsenvironment",CheckFileExists=true}){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;var m=ModulePackages.Inspect(dialog.FileName);if(m.kind!=kind)throw new InvalidOperationException("模块类型不正确。");
                string plan=m.name+"\r\n平台："+m.platform+"\r\n运行时档位："+m.profile+"\r\n声明最低驱动："+(string.IsNullOrEmpty(m.minimumDriver)?"未声明，安装后检查":m.minimumDriver)+"\r\n展开体积："+(m.files.Sum(f=>f.size)/1024d/1024/1024).ToString("0.00")+" GB\r\n位置："+Store.Root+"\r\n\r\n同名模块会保留备份；不会更改音色或连接。请只安装可信来源的运行代码。";
                if(MessageBox.Show(this,plan,"确认模块安装",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)!=DialogResult.OK)return;
                using(var cancel=new CancellationTokenSource()){
                    if(busyOverlay!=null)busyOverlay.SetStopAction(()=>cancel.Cancel());var progress=new Progress<string>(text=>{if(busyOverlay!=null)busyOverlay.SetMessage(text);});var watch=Stopwatch.StartNew();long last=0;
                    var result=await Task.Run(()=>ModulePackages.Install(Store,dialog.FileName,text=>{long now=watch.ElapsedMilliseconds;if(now-last>200){last=now;((IProgress<string>)progress).Report(text);}},cancel.Token));
                    Config=Store.Read();BindSettings();BindVoice();var preferences=SetupPreference();preferences.voiceEvidence="";preferences.heardVoice=false;new WelcomeState(Store).Write(preferences);SetupStatus(kind,"installed");Status("模块已校验安装，尚未验证生成。备份位置可在模块安装报告中查看。");
                }
            }
        }
        async Task PrepareSetupEnvironment(bool repair,bool verify){
            if(repair&&await Service.Running())throw new InvalidOperationException("请先关闭服务，再准备运行环境。");
            if(!File.Exists(Store.Resolve(Config.engines.pythonService))){if(!repair||setupOffline.Checked)throw new InvalidOperationException("缺少主服务 Python，请导入环境包；或关闭严格离线后主动补齐。");await EnvironmentBootstrap.EnsureCore(Store,BusyStatus);Config=Store.Read();}
            var progress=new System.Windows.Forms.Timer{Interval=800};progress.Tick+=(s,e)=>{try{string file=Store.Resolve("data/environment-repair/progress.json");if(File.Exists(file)&&busyOverlay!=null)busyOverlay.SetMessage(Json.Text(Json.Object(File.ReadAllText(file)),"message"));}catch(IOException){}catch(ArgumentException){}};
            try{progress.Start();string args=(repair?"repair":"inspect")+" --root "+ServiceClient.Quote(Store.Root)+" --engine current"+(setupOffline.Checked?" --offline":"")+(verify?"":" --prepare-only");var r=Json.Object(await Service.RunTool("environment_repair.py",args));repairDetails.Text=EnvironmentReport(r);bool ok=Convert.ToBoolean(r["ok"]);SetupStatus("environment",ok?(verify?"generated":"prepared"):"failed");if(!ok){ShowPage("environmentRepair");throw new InvalidOperationException("环境未通过，详细失败原因与下一步已显示在报告。");}Config=Store.Read();BindSettings();Status(verify?"环境真实合成通过，仍需试听确认播放。":"环境文件检查通过，下一步导入音色并试听。");}finally{progress.Stop();progress.Dispose();}
        }
        async Task ExportSetupRequirements(){using(var dialog=new SaveFileDialog{Filter="资源清单|*.json",FileName="Agent语音-待补齐资源.json"}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;var catalog=Json.Object(File.ReadAllText(Store.Resolve("service/environment-catalog.json"),Encoding.UTF8));File.WriteAllText(dialog.FileName,Json.Encode(new{format="agent-voice-requirements",version=1,engine=Config.voice.engine,variant=Config.voice.gptVariant,platform="windows-x64",catalog=catalog,expectedModules=new[]{"环境 .ttsenvironment","TTS .ttsengine","音色 .ttsmodel"},instructions="在联网电脑准备相同引擎与兼容 GPU 档位的环境包，再带回导入；不含账号密钥或声音资产。"}),new UTF8Encoding(false));await Task.FromResult(0);Status("资源准备清单已导出，尚未联网或安装。");}}
        async Task ImportSetupVoice(){using(var dialog=new OpenFileDialog{Filter="TTS 音色包|*.cosyvoice.ttsmodel;*.gpt-sovits.ttsmodel",InitialDirectory=VoicePackageDirectory(),CheckFileExists=true}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;await ImportConfiguration(dialog.FileName);SetupStatus("voice","imported");setupEngineChoice.SelectedIndex=EngineIndex();}}
        async Task EnsureSetupService(){await SaveDraft();if(!await Service.Running())await Service.Start();await WaitForVoice(Config.voice);}
        async Task PreviewSetupVoice(){
            await EnsureSetupService();var watch=Stopwatch.StartNew();var item=await Service.Request("/speak",new{text="你好，声音已经准备好了。接下来，你可以按自己的方式开始。",source="setup-preview",play=true});lastJob=Json.Text(item,"jobId");await WaitJob(lastJob);var job=await Service.Request("/api/jobs/"+lastJob);var result=job["result"] as Dictionary<string,object>;if(result==null||!result.ContainsKey("played")||!Convert.ToBoolean(result["played"]))throw new InvalidOperationException("生成完成，但未收到播放完成结果。");
            var p=SetupPreference();p.voiceEvidence=VoiceEvidence();p.voiceJobId=lastJob;p.voiceVerifiedAt=DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");p.heardVoice=false;new WelcomeState(Store).Write(p);setupPlayback.Text="实际播放已完成，输出："+Json.Text(result,"output")+"，本次耗时 "+watch.Elapsed.TotalSeconds.ToString("0.00")+" 秒。\r\n请确认是否听到声音以及音色是否合适。";RefreshSetup();
        }
        void ConfirmSetupVoice(){var p=SetupPreference();if(p.voiceEvidence!=VoiceEvidence()||p.voiceJobId.Length==0)throw new InvalidOperationException("请先完成当前声音的生成与播放测试。");p.heardVoice=true;new WelcomeState(Store).Write(p);Status("已记录你对当前声音的听感确认。");RefreshSetup();}
        internal async Task VerifySetupPlayback(){
            bool running=await Service.Running();if(running)throw new InvalidOperationException("此验收只用于独立且服务关闭的目录。");
            Exception failure=null;try{ShowPage("setup");GoSetup(4);await PreviewSetupVoice();var p=SetupPreference();if(p.voiceJobId.Length==0||p.heardVoice)throw new InvalidOperationException("实际播放结果或人工确认隔离失败。");ConfirmSetupVoice();if(!SetupPreference().heardVoice)throw new InvalidOperationException("确认状态未保存。");}catch(Exception error){failure=error;}
            if(await Service.Running())await Service.Stop();if(failure!=null)throw failure;
        }
    }
}
