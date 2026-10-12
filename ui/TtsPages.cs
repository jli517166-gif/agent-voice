using System;
using System.Windows.Forms;
namespace AgentVoice {
    public sealed partial class MainForm {
        void BuildOutputMode(){
            var card=SettingsCard("outputMode","音频输出模式");
            Add(card,Hint("默认由本地 TTS 直接输出。需要第二次音色转换时，切换到 RVC＋本地 TTS；本机／AIRI 播放位置仍在桌宠联动中设置。"));
            synthesisChoice=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=340};
            synthesisChoice.Items.AddRange(new object[]{"本地 TTS 直接输出（默认）","RVC＋本地 TTS 混合输出"});
            synthesisChoice.SelectedIndexChanged+=(s,e)=>{UpdateVoiceControls();UpdateOutputModeStatus(false);};Add(card,synthesisChoice);
            outputModeStatus=Hint("当前输出模式：读取中");outputModeStatus.Font=new System.Drawing.Font(Font.FontFamily,11F,System.Drawing.FontStyle.Bold);Add(card,outputModeStatus);
            Add(card,Hint("本地 TTS 直接输出会跳过 RVC 模型、索引、音调和声线粗细。混合输出会先由本地 TTS 生成，再使用 RVC模型配置页做第二次变声。模式切换保留两套选择，语音任务使用提交时的模式。"));
            Add(card,Row(AsyncBtn("应用输出模式",async()=>{await SaveDraft();UpdateOutputModeStatus(true);}),Btn("本地 TTS",()=>ShowPage("home")),Btn("RVC模型配置",()=>ShowPage("profiles"))));
        }
        void OpenOutputMode(){ShowPage("outputMode");UpdateOutputModeStatus(false);Status("当前输出模式："+(synthesisChoice.SelectedIndex==0?"本地 TTS 直出":"RVC＋本地 TTS 混合输出")+"。选择后请点击“应用输出模式”。");}
        void UpdateOutputModeStatus(bool applied){if(outputModeStatus==null||synthesisChoice==null)return;string mode=synthesisChoice.SelectedIndex==0?"本地 TTS 直出":"RVC＋本地 TTS 混合输出";outputModeStatus.Text=(applied?"已应用输出模式：":"当前选择：")+mode+(applied?"":"（尚未点击应用时不会改变正在使用的配置）");}
    }
}
