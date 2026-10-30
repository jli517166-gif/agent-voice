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
        Label petEndpointLabel,petStandaloneHint;
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
            petEndpointLabel=WebText("dsh-pet 本地 Web 地址（只允许 127.0.0.1）",9.5F,FontStyle.Regular,16,42,700,25,ContentAlignment.MiddleLeft);card.Controls.Add(petEndpointLabel);
            petEndpointPath=Input();petEndpointPath.SetBounds(16,72,600,30);card.Controls.Add(petEndpointPath);
            petStandaloneHint=WebText("自动读取当前用户的独立桌宠语音设置。",9F,FontStyle.Regular,16,72,650,30,ContentAlignment.MiddleLeft);card.Controls.Add(petStandaloneHint);
            card.Controls.Add(WebButton("独立桌宠","使用已安装的 dsh-pet-indesktop Chat 版当前 Edge 音色。",()=>{petEndpointPath.Text="standalone://current";UpdateVoiceControls();},16,108,150,38));
            card.Controls.Add(WebButton("宿主插件","使用 dsh-pet 宿主插件提供的本地 HTTP 接口。",()=>{if(petEndpointPath.Text=="standalone://current")petEndpointPath.Text="http://127.0.0.1:3080/dsh-pet-7340";UpdateVoiceControls();},174,108,150,38));
            card.Controls.Add(WebText("音色 ID（可留空）",9F,FontStyle.Regular,338,108,155,30,ContentAlignment.MiddleLeft));
            petVoiceId=Input();petVoiceId.SetBounds(505,108,215,30);card.Controls.Add(petVoiceId);
            card.Controls.Add(WebAsyncButton("适配桌宠源码","给独立版桌宠源码安装 Agent Voice 语音提供者；需自行重建桌宠，当前 exe 不会变化。",AdaptStandalonePetSource,730,108,156,38));
            petOnlyControls.AddRange(card.Controls.Cast<Control>().Skip(start));
        }
        async System.Threading.Tasks.Task AdaptStandalonePetSource(){
            using(var dialog=new FolderBrowserDialog{Description="选择 dsh-pet-indesktop 的源码目录（不是已安装 exe 的目录）",ShowNewFolderButton=false}){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                string source=dialog.SelectedPath;
                if(!File.Exists(Path.Combine(source,"pet","tts","base.py")))throw new InvalidOperationException("这里不是 dsh-pet-indesktop 源码目录。请先获取上游源码。");
                await Service.RunTool("prepare_pet_source.py","--pet-source "+ServiceClient.Quote(source));
                Status("桌宠源码适配已写入；当前桌宠 exe 尚未改变。请按说明重建并在桌宠中选择 Agent Voice。");
                MessageBox.Show(this,"已在所选桌宠源码安装语音提供者，并保留原文件备份。当前桌宠程序尚未改变；请按桌宠上游构建说明重新构建，然后在新桌宠的语音设置中选择 Agent Voice 并试听。","源码适配已准备，尚未连接",MessageBoxButtons.OK,MessageBoxIcon.Information);
            }
        }
        void UpdateVoiceControls(){
            string engine=SelectedEngine();
            foreach(var control in cosyOnlyControls)control.Visible=engine=="cosyvoice";
            foreach(var control in gptOnlyControls)control.Visible=engine=="gpt-sovits";
            foreach(var control in petOnlyControls)control.Visible=engine=="dsh-pet";
            bool standalone=engine=="dsh-pet"&&petEndpointPath.Text=="standalone://current";
            if(petEndpointPath!=null)petEndpointPath.Visible=engine=="dsh-pet"&&!standalone;
            if(petStandaloneHint!=null)petStandaloneHint.Visible=standalone;
            if(petEndpointLabel!=null)petEndpointLabel.Text=standalone?"独立桌宠 · 跟随当前语音设置":"dsh-pet 本地 Web 地址（只允许 127.0.0.1）";
            if(engineDetails!=null)engineDetails.Text=engine=="dsh-pet"?(standalone?"独立桌宠：读取当前 Edge 音色；生成时需要网络。":"宿主插件：使用桌宠自己的本地语音接口；需启动插件与 Web 服务。") :engine=="gpt-sovits"?"GPT-SoVITS · "+(SelectedVariant()=="v5turbo"?"v5 Turbo：推荐的参考音色版本。":"ProPlus：用于按角色保存另一套声音方案。") :"CosyVoice：保留已认可的本地模型与参考条件。";
            if(modelHint!=null)modelHint.Text=engine=="dsh-pet"?(standalone?"跟随已安装桌宠的 Edge 音色、语速和音调；音色 ID 留空即可。":"这里连接宿主插件语音；不会安装、复制或训练桌宠音色。") :"模型目录留空时使用设置中的默认路径；切换引擎只改变显示，不会改动音量与句首空白。";
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
