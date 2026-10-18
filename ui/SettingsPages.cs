using Button = AgentVoice.AnimeButton;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed partial class MainForm {
        void BuildSettings(){
            var hub=Stack(NewPage("settings"));
            var common=Card(hub,"常用设置");Add(common,Hint("最常用的输出、连接和界面选项。"));
            Add(common,Row(EntryBtn("配置中心","继续准备环境、声音与连接。",()=>ShowPage("setup")),EntryBtn("重播开场","重播开场并重新选择欢迎页入口；不重置声音设置。",ReplayWelcome)));
            Add(common,HubRow(HubEntry("连接设备","连接 Harness 或 AIRI，并做真实播放验证。","connections",true),HubEntry("外观","设置背景、配色和程序图标。","appearance",false)));
            var system=Card(hub,"系统与维护");Add(system,Hint("运行环境和低频高级选项集中在这里，日常使用通常无需修改。"));
            Add(system,HubRow(HubEntry("补齐环境","检查缺少的依赖、源码与基础模型，在软件内下载并补齐。","environmentRepair",true)));
            Add(system,HubRow(HubEntry("本地引擎","设置 CosyVoice、GPT-SoVITS v5 Turbo 和 ProPlus 的运行位置。","engine",false),HubEntry("声音细调","调整表达指令、句首空白和播放音量。","advanced",false),HubEntry("音频清理","设置生成音频的保留时间、数量和空间上限。","cleanup",false)));
            BuildAppearance();BuildEngines();BuildEnvironmentRepair();BuildHarness();BuildAiri();BuildConnectionHub();BuildAdvanced();BuildCleanup();BuildConfiguration();BuildTraining();BuildAdvancedTraining();
        }
        Button HubEntry(string text,string note,string pageId,bool important){var button=EntryBtn(text,note,()=>ShowPage(pageId));button.AutoSize=false;button.Width=250;button.Height=52;button.TextAlign=ContentAlignment.MiddleLeft;button.Padding=new Padding(14,0,8,0);return important?Primary(button):button;}
        ScenePanel SettingsCard(string pageId,string title){var stack=Stack(NewPage(pageId));return Card(stack,title);}
        void BuildAppearance(){var card=SettingsCard("appearance","外观与图标");Field(card,"accent","主色（推荐青蓝 #19C7F2）");Field(card,"backgroundColor","背景颜色（推荐深海军蓝 #05101D）");Field(card,"textColor","文字颜色（推荐雾白 #EAF6FF）");
            var uiSound=new CheckBox{Text="界面电音音效（语音服务开启时暂停）",AutoSize=true,Checked=SetupPreference().interfaceSounds};uiSound.CheckedChanged+=(s,e)=>SetInterfaceSounds(uiSound.Checked);setupSoundChoices.Add(uiSound);Add(card,uiSound);Add(card,Row(Btn("试听电音音效",()=>{PlayInterfaceSound(8);Status("语音服务关闭时可试听界面电音；朗读期间暂停界面音效。");})));
            foreach(var key in new[]{"accent","backgroundColor","textColor"}){string field=key;Add(card,Row(Btn("选择颜色",()=>{using(var dialog=new ColorDialog{FullOpen=true})if(dialog.ShowDialog(this)==DialogResult.OK)fields[field].Text=ColorTranslator.ToHtml(dialog.Color).StartsWith("#")?ColorTranslator.ToHtml(dialog.Color):"#"+dialog.Color.R.ToString("X2")+dialog.Color.G.ToString("X2")+dialog.Color.B.ToString("X2");})));}
            PathField(card,"background","整窗背景图片","图片|*.png;*.jpg;*.jpeg;*.bmp",false);Add(card,Row(Btn("清除背景",()=>fields["background"].Text="")));
            PathField(card,"iconSource","自定义图标图片（自动圆角，窗口／托盘／快捷方式同步）","图片|*.png;*.jpg;*.jpeg;*.bmp",false);
            PathField(card,"loadingAnimation","等待动画（支持 PNG、JPG、BMP、GIF；留空使用默认动画）","图片或 GIF|*.png;*.jpg;*.jpeg;*.bmp;*.gif",false);Add(card,Row(Btn("使用默认动画",()=>fields["loadingAnimation"].Text="")));
            Add(card,Row(Primary(AsyncBtn("保存外观",async()=>{var next=Json.Copy(Config);foreach(var value in new[]{"accent","backgroundColor","textColor"}){string text=fields[value].Text.Trim();if(!System.Text.RegularExpressions.Regex.IsMatch(text,"^#[0-9A-Fa-f]{6}$"))throw new InvalidOperationException("颜色请填写 # 加六位十六进制值。");ColorTranslator.FromHtml(text);}next.appearance.accent=fields["accent"].Text.Trim();next.appearance.backgroundColor=fields["backgroundColor"].Text.Trim();next.appearance.textColor=fields["textColor"].Text.Trim();next.appearance.background=ImportImage(fields["background"].Text,"background");next.appearance.loadingAnimation=ImportImage(fields["loadingAnimation"].Text,"loading-animation");string image=fields["iconSource"].Text.Trim();if(image.Length>0){string target=Icons.Make(Store.Resolve(image),Path.Combine(Store.Root,"data","appearance"));next.appearance.icon=Store.Portable(target);}await Persist(next,new {appearance=next.appearance});ApplyTheme();ApplyIcon();if(Config.appearance.icon.Length>0)Icons.Shortcut(Application.ExecutablePath,Store.Resolve(Config.appearance.icon));Status("外观与等待动画已保存。");}))));
            Add(card,Row(AsyncBtn("创建桌面快捷方式",async()=>{await Task.Run(()=>{});Icons.Shortcut(Application.ExecutablePath,Config.appearance.icon.Length>0?Store.Resolve(Config.appearance.icon):Application.ExecutablePath);Status("快捷方式已创建。");})));
        }
        string ImportImage(string value,string name){if(string.IsNullOrWhiteSpace(value))return "";string source=Store.Resolve(value);using(var image=Image.FromFile(source)){}string dir=Path.Combine(Store.Root,"data","appearance");Directory.CreateDirectory(dir);string target=Path.Combine(dir,name+Path.GetExtension(source));if(!source.Equals(target,StringComparison.OrdinalIgnoreCase))File.Copy(source,target,true);return Store.Portable(target);}
        void BuildEngines(){var card=SettingsCard("engine","本地 TTS 引擎");Add(card,Row(Primary(AsyncBtn("环境自检",Diagnose)),AsyncBtn("恢复声音",RecoverVoice),EntryBtn("故障帮助","查看检查范围、依赖位置和恢复步骤。",()=>ServiceClient.Open(Store.Resolve("稳定性与故障排查.md")))));Add(card,Hint("只保留 CosyVoice、GPT-SoVITS v5 Turbo 和 ProPlus，全部直接输出。各引擎使用独立环境；CosyVoice 的认可音色保持原参数。"));Add(card,Row(EntryBtn("打开引擎目录","查看独立的源码、运行环境与模型。",()=>ServiceClient.Open(TtsEngineDirectory())),Btn("CosyVoice 官方页面",()=>ServiceClient.Open("https://github.com/FunAudioLLM/CosyVoice")),Btn("GPT-SoVITS 官方页面",()=>ServiceClient.Open("https://github.com/RVC-Boss/GPT-SoVITS"))));
            foreach(var item in new[]{new[]{"pythonService","主服务 Python", "file"},new[]{"pythonTts","CosyVoice Python","file"},new[]{"cosyRepo","CosyVoice 源码","folder"},new[]{"cosyModel","CosyVoice 默认模型","folder"},new[]{"pythonGpt","GPT-SoVITS Python","file"},new[]{"gptRepo","GPT-SoVITS 源码","folder"},new[]{"gptModel","GPT-SoVITS 默认权重目录","folder"},new[]{"ffmpeg","FFmpeg 编码器","file"}})PathField(card,item[0],item[1],"文件|*.*",item[2]=="folder");
            Add(card,Row(Primary(AsyncBtn("保存运行环境",async()=>{if(await Service.Running())throw new InvalidOperationException("请先关闭服务，再修改运行环境。");var next=Json.Copy(Config);foreach(var field in typeof(Engines).GetFields())field.SetValue(next.engines,fields[field.Name].Text.Trim());Store.Write(next);Config=next;Status("独立 TTS 环境已保存，启动服务后生效。");}))));
        }
        void BuildAdvanced(){var card=SettingsCard("advanced","声音细调");Add(card,Hint("仅调整 TTS 表达和播放。CosyVoice 表达指令与句首空白控制只作用于 CosyVoice；播放音量适用于所有引擎。"));var advanced=new CheckBox{Text="启用声音细调（默认关闭）",AutoSize=true};checks["advancedEnabled"]=advanced;advanced.CheckedChanged+=(s,e)=>UpdateVoiceControls();Add(card,Row(advanced));Add(card,Hint("关闭后使用参考录音的表达方式和默认播放音量；模型、参考录音、语速以及 GPT 版本仍生效。"));NumberField(card,"volume","播放音量",0,1.5M,.05M,2);NumberField(card,"leadingSilence","CosyVoice 句首空白保留上限（秒）",.2M,1,.05M,2);Field(card,"instruction","CosyVoice 表达指令（可选）",true);Add(card,Row(AsyncBtn("保存声音细调",SaveDraft)));}
        void BuildHarness(){
            var stack=Stack(NewPage("harness"));var intro=Card(stack,"Harness");
            Add(intro,Hint("正式支持范围只保留已经完成真机端到端测试的 Codex 与 DeepSeek Harness。选择安装目录或用户配置目录后，程序会自动识别、安装连接器并验证真实回复和播放。"));
            Add(intro,Row(Primary(AsyncBtn("选择目录并自动接入",BeginAutoHarness)),EntryBtn("连接帮助","查看识别依据、重载方法和真实验证标准。",()=>ShowPage("connectionGuide")),Btn("返回",()=>ShowPage("connections"))));
            foreach(var item in new[]{new[]{"codex","Codex"},new[]{"deepseek","DeepSeek Harness"}}){string source=item[0],name=item[1];var check=new CheckBox{Text="启用",AutoSize=true,Height=30,Margin=new Padding(0,5,10,5)};checks[source]=check;Add(intro,Row(Hint(name),check,HarnessIndicator(source,name)));}
            Add(intro,Row(AsyncBtn("保存开关",async()=>{var next=Json.Copy(Config);foreach(var key in Config.harness.Keys)next.harness[key]=checks[key].Checked;await Persist(next,new {harness=next.harness});await Poll();})));
            Add(intro,Hint("只有“指定 Harness 返回本次随机短句，并完成实际语音播放”才变绿和播放礼炮。监听就绪、文件已安装、心跳在线、近期收到普通回复都不算验证通过。"));
            var mcp=Card(stack,"聊天栏快捷控制");Add(mcp,Hint("DeepSeek 安装后在聊天栏显示“语音”按钮，使用独有的 /agent-voice 命令。Codex 原生入口为技能选择器中的“语音控制”或 $agent-voice-control。Codex 使用 MCP 语言命令，不添加自定义面板或浮窗。MCP 可控制服务、朗读和声音方案。"));
            Add(mcp,Row(Primary(AsyncBtn("安装到 Codex",async()=>await InstallHarnessMcp("codex"))),Primary(AsyncBtn("安装到 DeepSeek",async()=>await InstallHarnessMcp("deepseek")))));
            var developer=Card(stack,"其他 Harness · 二次开发");Add(developer,Hint("未真机验证的 Harness 不进入正式支持列表。开发者可以按适配器契约接入最终回复，并复用相同的随机挑战、播放回执和 MCP 工具。"));
            Add(developer,Row(EntryBtn("打开 Harness 二次开发指南","新增识别器、连接器、验证命令和测试。",()=>ServiceClient.Open(Store.Resolve("Harness二次开发指南.md"))),EntryBtn("打开 MCP 开发说明","复用或扩展本地 MCP 控制服务。",()=>ServiceClient.Open(Store.Resolve("MCP控制插件开发说明.md")))));
            BuildConnectionGuide();
        }
        void BuildAiri(){
            var stack=Stack(NewPage("airi"));var playback=Card(stack,"AIRI 输出选择");
            Add(playback,Hint("AIRI 联动完全可选。选择本机时，不启动 AIRI 也能正常播放；选择桌宠时，Harness 回复与 AIRI 自己的聊天语音都走桌宠原生播放与嘴型。"));
            outputChoice=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=360};outputChoice.Items.AddRange(new object[]{"本机音频设备（local）","AIRI 桌宠播放与嘴型（airi）"});Add(playback,Row(Hint("音频最终从哪里播放"),outputChoice));Add(playback,Hint("本机音频设备（local）：由这台电脑当前选择的扬声器或耳机直接播放，不需要启动 AIRI。local 是程序内部保存的模式名称。"));var enabled=new CheckBox{Text="启用 AIRI 桌宠联动",AutoSize=true};checks["airiEnabled"]=enabled;Add(playback,Row(enabled));
            Add(playback,Row(AsyncBtn("保存播放设置",async()=>{var next=Json.Copy(Config);next.airi.enabled=checks["airiEnabled"].Checked;next.airi.directory=fields["airiDirectory"].Text.Trim();next.airi.output=outputChoice.SelectedIndex==1?"airi":"local";await Persist(next,new {airi=next.airi});Status(next.airi.enabled?"AIRI 联动设置已保存。":"AIRI 联动已关闭，本机音频设备仍可正常播放。");})));
            var connect=Card(stack,"连接验证");
            Add(connect,Hint("AIRI 默认关闭。先勾选并保存“启用 AIRI 桌宠联动”，再选择你已经登录过、且目录中含 airi.exe 的安装目录。程序会在这一个 AIRI 中安装桥接、保留 app.asar 单文件备份并完成真实播放验证，不再生成完整联动副本。AIRI 更新可能覆盖桥接，失效后重新接入验证即可修复。"));PathField(connect,"airiDirectory","已选择的 AIRI 安装目录","",true);fields["airiDirectory"].Text="";airiIndicator=Hint("AIRI · 尚未通过播放验证");Add(connect,airiIndicator);
            Add(connect,Row(Primary(AsyncBtn("选择目录并接入验证",async()=>await BeginVerification("airi"))),EntryBtn("打开当前 AIRI","打开已经登录的 AIRI。",OpenLoggedAiri),EntryBtn("连接帮助","查看 AIRI 的配置和验证步骤。",()=>ShowPage("connectionGuide")),Btn("返回",()=>ShowPage("connections"))));
            Add(connect,Hint("验证测试音频必须经桌宠播放完成并返回回执后才变绿和播放礼炮。仅连接心跳、打开窗口或降级到本机播放都不会误报成功；启动较慢时，桥接连通后会自动继续验证。"));
            var manual=Card(stack,"高级 · 手动连接信息");Add(manual,Hint("仅在自动联动不适用时使用。AIRI 的 TTS 提供方填入下面的本机地址；账号仍由 AIRI 自己管理。"));Add(manual,Row(Btn("复制 AIRI 连接信息",()=>{Clipboard.SetText("Base URL: http://127.0.0.1:"+Config.port+"/v1/\r\nAPI Key: "+Config.token+"\r\nModel: agent-voice-tts\r\nVoice: current");Status("AIRI 手动连接信息已复制。");})));
        }
        void BuildConnectionHub(){var stack=Stack(NewPage("connections"));var start=Card(stack,"选择连接对象");Add(start,Hint("Harness 把进度说明和最终回复交给语音队列；AIRI 可以朗读聊天，也可以接管播放和嘴型。两者可以独立使用。"));var harness=HubEntry("Harness","连接已实测支持的 Codex 或 DeepSeek Harness，并可安装 MCP 快捷控制。","harness",true);var airi=HubEntry("AIRI 桌宠","手动启用后，选择已登录 AIRI 的目录并自动验证播放与嘴型。","airi",true);var guide=HubEntry("连接帮助","查看重载方法、验证标准和故障排查。","connectionGuide",false);Add(start,HubRow(harness,airi,guide));var flow=Card(stack,"连接步骤");Add(flow,Hint("1. 在首页试听声音。\r\n2. 启动语音服务。\r\n3. Harness 直接选择目录；AIRI 先手动启用，再选择目录。\r\n4. 程序自动识别、配置并完成真实播放验证。\r\n5. 看到绿色状态和礼炮才算完成。"));Add(flow,Hint("换电脑或移动程序后需要重新定位并验证。其他 Harness 请从二次开发入口新增适配器。"));}
        void BuildCleanup(){var card=SettingsCard("cleanup","音频清理");Add(card,Hint("音频目录："+Store.Resolve("data/output")));Add(card,Row(Btn("打开音频目录",()=>{Directory.CreateDirectory(Store.Resolve("data/output"));ServiceClient.Open(Store.Resolve("data/output"));})));NumberField(card,"maxAgeMinutes","删除超过（分钟）",1,43200,1,0);NumberField(card,"keepLatest","保留最新文件数",0,10000,1,0);NumberField(card,"maxTotalMb","总占用上限（MB）",1,100000,10,0);Add(card,Row(Primary(AsyncBtn("保存并清理",async()=>{var next=Json.Copy(Config);next.cleanup.maxAgeMinutes=(int)numbers["maxAgeMinutes"].Value;next.cleanup.keepLatest=(int)numbers["keepLatest"].Value;next.cleanup.maxTotalMb=(int)numbers["maxTotalMb"].Value;await Persist(next,new {cleanup=next.cleanup});var result=await Service.Request("/api/cleanup",new {});Status("已清理 "+result["removed"]+" 个音频。");})),Danger(AsyncBtn("清空已完成音频",async()=>{var result=await Service.Request("/api/cleanup",new {all=true});Status("已清理 "+result["removed"]+" 个音频，生成或播放中的文件受到保护。");}))));
        }
        void BindSettings(){foreach(var field in typeof(Engines).GetFields())fields[field.Name].Text=Convert.ToString(field.GetValue(Config.engines));fields["accent"].Text=Config.appearance.accent;fields["backgroundColor"].Text=Config.appearance.backgroundColor;fields["textColor"].Text=Config.appearance.textColor;fields["background"].Text=Config.appearance.background;fields["loadingAnimation"].Text=Config.appearance.loadingAnimation;fields["airiDirectory"].Text=Config.airi.directory;outputChoice.SelectedIndex=Config.airi.output=="airi"?1:0;checks["airiEnabled"].Checked=Config.airi.enabled;foreach(var key in Config.harness.Keys)checks[key].Checked=Config.harness[key];numbers["maxAgeMinutes"].Value=Config.cleanup.maxAgeMinutes;numbers["keepLatest"].Value=Config.cleanup.keepLatest;numbers["maxTotalMb"].Value=Config.cleanup.maxTotalMb;}

    }
}
