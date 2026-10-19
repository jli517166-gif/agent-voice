using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed partial class MainForm {
        readonly List<CheckBox> setupSoundChoices=new List<CheckBox>();
        WelcomePreferences SetupPreference(){return new WelcomeState(Store).Read();}
        void SetInterfaceSounds(bool enabled){var p=SetupPreference();if(p.interfaceSounds==enabled)return;p.interfaceSounds=enabled;new WelcomeState(Store).Write(p);foreach(var item in setupSoundChoices)item.Checked=enabled;if(!enabled)interfaceSounds.Stop();}
        void RunConfiguration(){
            if(operation)return;poll.Stop();Hide();
            try{using(var wizard=new SetupWizard(Store))wizard.ShowDialog();Config=Store.Read();BindVoice();BindSettings();ApplyTheme();Status("已返回工作台；实际接入状态由服务重新核对。");}
            finally{Show();WindowState=FormWindowState.Normal;Activate();if(!testingMode)poll.Start();}
        }
        internal void ApplyWelcomeChoice(string choice){ShowPage(choice=="import"?"configuration":"home");}
        internal async System.Threading.Tasks.Task VerifySetupPlayback(){
            var flow=new SetupWorkflow(Store);if(await flow.Service.Running())throw new InvalidOperationException("此验收只用于独立且服务关闭的目录。");
            Exception failure=null;try{await flow.Preview();if(flow.State.Read().heardVoice)throw new InvalidOperationException("实际播放不应自动确认听感。");flow.Confirm();if(!flow.State.Read().heardVoice)throw new InvalidOperationException("听感确认未保存。");}catch(Exception error){failure=error;}
            if(await flow.Service.Running())await flow.Service.Stop();if(failure!=null)throw failure;
        }
    }
}
