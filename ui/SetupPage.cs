using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed partial class MainForm {
        Label setupResources;
        internal void ApplyWelcomeChoice(string choice){
            if(choice=="setup")ShowPage("setup");
            else if(choice=="import"){ShowPage("configuration");importConfigurationButton.PerformClick();}
            else ShowPage("home");
        }
        void ReplayWelcome(){
            var state=new WelcomeState(Store);var preference=state.Read();
            using(var welcome=new WelcomeForm(preference.reduceMotion)){
                if(welcome.ShowDialog(this)!=DialogResult.OK)return;
                state.Choose(welcome.Choice,welcome.ReduceMotion);ApplyWelcomeChoice(welcome.Choice);
            }
        }
        void BuildSetup(){
            var stack=Stack(NewPage("setup"));var start=Card(stack,"按你的方式开始");
            Add(start,Hint("下面各项都可以跳过，也可以按任意顺序准备。打开详细页面后，点击侧边栏“配置中心”即可继续；已有声音和连接不会重置。"));
            setupResources=Hint("");setupResources.Name="setupResourceSummary";Add(start,setupResources);
            Add(start,Row(EntryBtn("继续上次配置","打开上次从引导进入的配置步骤。",()=>{string last=new WelcomeState(Store).Read().lastStep;if(last=="setup"||!pages.ContainsKey(last)){Status("选择下面任意步骤开始配置。");return;}ShowPage(last);}),EntryBtn("刷新资源状态","只检查文件是否齐全，不启动服务、不下载。",RefreshSetup),EntryBtn("直接进入首页","保存当前进度，之后可从侧边栏继续。",()=>ShowPage("home")),EntryBtn("重播开场","再次观看开场并调整减少动画选项。",ReplayWelcome)));
            SetupSection(stack,"01  环境与电脑检查","检查缺少的依赖、显卡兼容情况和音频设备。下载与修复由你主动执行，真实合成通过才算可用。","检查与补齐环境","environmentRepair");
            SetupSection(stack,"02  选择 TTS 引擎","当前已适配 CosyVoice、GPT-SoVITS v5 Turbo 和 ProPlus。可定位已有引擎，或从补齐环境页面按需下载。","管理本地引擎","engine");
            SetupSection(stack,"03  导入声音","导入带引擎后缀的音色包，或自行填写参考声音。轻量包需要匹配的基础模型，缺少资源时会明确报错。","导入音色包","configuration");
            var reference=Card(stack,"自行配置参考声音");Add(reference,Row(EntryBtn("参考声音与台词","选择推理录音并填写逐字一致的台词。",()=>ShowPage("cosyReference"))));
            SetupSection(stack,"04  试听与确认","准备完成后到首页选择声音、手动启动服务并试听。文件齐全只代表已配置，不代表已经生成或播放成功。","前往首页试听","home");
            SetupSection(stack,"05  连接 Harness（可选）","正式支持 Codex、DeepSeek Harness。目录识别后仍需随机回复、连接器回执和实际播放验证；DeepSeek 仅桌面登录时可能需要手动发送一次短句。","选择 Harness 并验证","harness");
            var developer=Card(stack,"其他 Harness");Add(developer,Hint("未实测支持的 Harness 可自行开发适配，程序已提供独立接口与验证流程。"));Add(developer,Row(EntryBtn("开发接入说明","查看适配模板、事件接口与验收标准。",()=>ServiceClient.Open(Store.Resolve("Harness二次开发指南.md")))));
            SetupSection(stack,"06  AIRI 桌宠（可选）","默认关闭，保持本机播放也可以使用所有朗读功能。启用后必须验证原生播放完成回执，心跳不算成功。","设置 AIRI 联动","airi");
        }
        void SetupSection(FlowLayoutPanel stack,string title,string note,string label,string target){
            var card=Card(stack,title);Add(card,Hint(note));Add(card,Row(Primary(EntryBtn(label,note,()=>{var state=new WelcomeState(Store);var p=state.Read();p.lastStep=target;state.Write(p);ShowPage(target);}))));
        }
        void RefreshSetup(){
            if(setupResources==null)return;var cfg=Config;bool core=File.Exists(Store.Resolve(cfg.engines.pythonService));
            bool runtime=File.Exists(Store.Resolve(cfg.voice.engine=="cosyvoice"?cfg.engines.pythonTts:cfg.engines.pythonGpt));
            string model=cfg.voice.engine=="cosyvoice"?(string.IsNullOrEmpty(cfg.voice.baseModel)?cfg.engines.cosyModel:cfg.voice.baseModel):(string.IsNullOrEmpty(cfg.voice.gptModel)?cfg.engines.gptModel:cfg.voice.gptModel);
            bool reference=File.Exists(Store.Resolve(cfg.voice.reference))&&!string.IsNullOrWhiteSpace(cfg.voice.referenceText);
            setupResources.Text="本地文件：主服务 "+(core?"已找到":"待补齐")+"  ·  当前 TTS 环境 "+(runtime?"已找到":"待补齐")+"\r\n声音资源：模型目录 "+(Directory.Exists(Store.Resolve(model))?"已找到":"待选择")+"  ·  参考声音 "+(reference?"已配置":"待配置")+"\r\n以上仅为文件检查；生成、播放及设备连接以各自的真实验证结果为准。";
        }
    }
}
