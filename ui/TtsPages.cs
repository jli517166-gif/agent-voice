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
            Add(card,Hint("本地 TTS 直出会跳过 RVC。混合输出会先生成 TTS，再用 RVC 二次变声。切换方式不会清除另一套设置。"));
            Add(card,Row(Primary(AsyncBtn("使用此方式",async()=>{await SaveDraft();UpdateOutputModeStatus(true);})),Btn("返回首页",()=>ShowPage("home")),Btn("打开 RVC",()=>ShowPage("profiles"))));
        }
        void OpenOutputMode(){ShowPage("outputMode");UpdateOutputModeStatus(false);Status("当前输出方式："+(synthesisChoice.SelectedIndex==0?"本地 TTS 直出":"RVC＋本地 TTS 混合输出")+"。选择后请点击“使用此方式”。");}
        void UpdateOutputModeStatus(bool applied){if(outputModeStatus==null||synthesisChoice==null)return;string mode=synthesisChoice.SelectedIndex==0?"本地 TTS 直出":"RVC＋本地 TTS 混合输出";outputModeStatus.Text=(applied?"已应用输出模式：":"当前选择：")+mode+(applied?"":"（尚未点击应用时不会改变正在使用的配置）");}
    }
}
