using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace AgentVoice {
    public sealed partial class MainForm {
        TextBox gptModelPath;
        TextBox petEndpointPath,petVoiceId;
        readonly List<Control> gptOnlyControls=new List<Control>();
        readonly List<Control> petOnlyControls=new List<Control>();
        void BuildVariantControls(ScenePanel card){
            // Absolute twin of the CosyVoice model row: only one engine's rows
            // are visible, so both sets share the same card region.
            int start=card.Controls.Count;
            card.Controls.Add(WebText("GPT-SoVITS 权重目录（留空使用设置中的默认目录）",9.5F,FontStyle.Regular,16,42,700,25,ContentAlignment.MiddleLeft));
            gptModelPath=Input();gptModelPath.SetBounds(16,72,600,30);card.Controls.Add(gptModelPath);
            card.Controls.Add(WebButton("选择 GPT 模型目录","选择包含语义模型、声学模型与编码器的 GPT-SoVITS 权重目录。",()=>SelectPath(gptModelPath,"",true),16,108,190,38));
            card.Controls.Add(WebButton("使用默认 GPT 模型",null,()=>gptModelPath.Text="",214,108,200,38));
            gptOnlyControls.AddRange(card.Controls.Cast<Control>().Skip(start));
            start=card.Controls.Count;
            card.Controls.Add(WebText("dsh-pet 本地 Web 地址（只允许 127.0.0.1）",9.5F,FontStyle.Regular,16,42,700,25,ContentAlignment.MiddleLeft));
            petEndpointPath=Input();petEndpointPath.SetBounds(16,72,600,30);card.Controls.Add(petEndpointPath);
            card.Controls.Add(WebText("音色 ID（留空使用桌宠默认音色；可从桌宠的音色列表查看）",9F,FontStyle.Regular,16,108,460,25,ContentAlignment.MiddleLeft));
            petVoiceId=Input();petVoiceId.SetBounds(480,108,240,30);card.Controls.Add(petVoiceId);
            petOnlyControls.AddRange(card.Controls.Cast<Control>().Skip(start));
        }
        void UpdateVoiceControls(){
            string engine=SelectedEngine();
            foreach(var control in cosyOnlyControls)control.Visible=engine=="cosyvoice";
            foreach(var control in gptOnlyControls)control.Visible=engine=="gpt-sovits";
            foreach(var control in petOnlyControls)control.Visible=engine=="dsh-pet";
            if(engineDetails!=null)engineDetails.Text=engine=="dsh-pet"?"dsh-pet：使用桌宠自己的语音接口；需要先启用桌宠宿主插件与本机 Web 服务。":engine=="gpt-sovits"?"GPT-SoVITS · "+(SelectedVariant()=="v5turbo"?"v5 Turbo：推荐的参考音色版本。":"ProPlus：用于按角色保存另一套声音方案。") :"CosyVoice：保留已认可的本地模型与参考条件。";
            if(modelHint!=null)modelHint.Text=engine=="dsh-pet"?"这里连接桌宠已提供的语音；不会安装、复制或训练桌宠音色。":"模型目录留空时使用设置中的默认路径；切换引擎只改变显示，不会改动音量与句首空白。";
            if(referenceTitle!=null)referenceTitle.Text=engine=="dsh-pet"?"桌宠音色与语速":"参考声音与语速";
            if(referenceButton!=null)referenceButton.Visible=engine!="dsh-pet";
            bool advanced=checks.ContainsKey("advancedEnabled")&&checks["advancedEnabled"].Checked;
            foreach(string key in new[]{"volume","leadingSilence"})if(numbers.ContainsKey(key))numbers[key].Enabled=advanced&&(key=="volume"||Capability("leadingSilence"));
            if(fields.ContainsKey("instruction"))fields["instruction"].Enabled=advanced&&Capability("instructions");
            if(voiceHint!=null){bool applied=engine==Config.voice.engine&&(engine!="gpt-sovits"||SelectedVariant()==Config.voice.gptVariant);voiceHint.Text=(applied?"当前已保存：":"当前选择尚未应用；已保存：")+EngineLabel(Config.voice)+"。点击“使用当前声音”会停止旧任务，并等待新模型就绪。";}
        }
        string EngineLabel(VoiceConfig v){return v.engine=="dsh-pet"?"dsh-pet 桌宠语音":v.engine=="gpt-sovits"?"GPT-SoVITS · "+(v.gptVariant=="v5turbo"?"v5 Turbo":"ProPlus"):"CosyVoice";}
    }
}
