using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AgentVoice {
    static class WelcomeChecks {
        public static int Run(SettingsStore store,string[] args){
            string directory=args.Length>1?Path.GetFullPath(args[1]):store.Resolve("acceptance/welcome");Directory.CreateDirectory(directory);var errors=new List<string>();
            try{
                var fixture=new SettingsStore(Path.Combine(directory,"preference-fixture-"+Guid.NewGuid().ToString("N")));var state=new WelcomeState(fixture);
                string voicePath=fixture.FilePath;Directory.CreateDirectory(Path.GetDirectoryName(voicePath));File.WriteAllText(voicePath,"untouched voice settings");byte[] voiceBefore=File.ReadAllBytes(voicePath);
                if(state.Read().seen)errors.Add("Fresh installation must show the welcome screen");
                using(var form=new WelcomeForm(false,false)){
                    form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);form.Show();Application.DoEvents();
                    if(form.Surface.Ready||form.Surface.StartButton.Visible||!form.Surface.SkipButton.Visible)errors.Add("Intro must allow skip without exposing hidden actions");
                    foreach(double time in new[]{0,.9,1.7,2.5,3.4}){form.Surface.Preview(time);Application.DoEvents();using(var shot=new Bitmap(form.Surface.Width,form.Surface.Height)){form.Surface.DrawToBitmap(shot,form.Surface.ClientRectangle);shot.Save(Path.Combine(directory,"frame-"+time.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+".png"));}}
                    foreach(var size in new[]{new Size(1120,720),new Size(1680,1080),new Size(840,580)}){
                        form.ClientSize=size;form.Surface.Preview(3.4);Application.DoEvents();
                        var controls=form.Surface.Controls.Cast<Control>().Where(c=>c.Visible).ToArray();
                        foreach(var control in controls)if(!form.Surface.ClientRectangle.Contains(control.Bounds))errors.Add("Clipped welcome control: "+control.Text+" @ "+size);
                        for(int i=0;i<controls.Length;i++)for(int j=i+1;j<controls.Length;j++)if(controls[i].Bounds.IntersectsWith(controls[j].Bounds))errors.Add("Overlapping welcome controls @ "+size);
                        using(var shot=new Bitmap(form.Surface.Width,form.Surface.Height)){form.Surface.DrawToBitmap(shot,form.Surface.ClientRectangle);shot.Save(Path.Combine(directory,"welcome-"+size.Width+".png"));}
                    }
                    form.Surface.Preview(.2);form.Surface.SkipButton.PerformClick();if(!form.Surface.Ready||form.Surface.AnimationRunning)errors.Add("Skipping intro must stop the timer immediately");
                    form.Close();if(form.Choice!=null||state.Read().seen)errors.Add("Closing welcome must not commit a choice");
                }
                foreach(string choice in new[]{"setup","import","home"})using(var form=new WelcomeForm(true,false)){
                    form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);form.Show();Application.DoEvents();
                    if(!form.Surface.Ready||form.Surface.AnimationRunning)errors.Add("Reduced motion must skip animation");
                    var button=choice=="setup"?form.Surface.StartButton:choice=="import"?form.Surface.ImportButton:form.Surface.HomeButton;button.PerformClick();
                    if(form.Choice!=choice)errors.Add("Wrong welcome destination: "+choice);state.Choose(choice,form.ReduceMotion);
                    var persisted=new WelcomeState(fixture).Read();if(!persisted.seen||!persisted.reduceMotion||persisted.lastChoice!=choice)errors.Add("Welcome choice was not persisted");
                }
                if(!voiceBefore.SequenceEqual(File.ReadAllBytes(voicePath)))errors.Add("Welcome preferences modified the voice configuration");
                using(var form=new WelcomeForm(false,true)){
                    form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);form.Show();Application.DoEvents();
                    if(!form.Surface.AnimationRunning)errors.Add("Normal first run did not start animation");
                    form.Surface.MotionChoice.Checked=true;
                    if(!form.Surface.Ready||form.Surface.AnimationRunning)errors.Add("Changing reduced motion during intro did not stop animation");
                    form.Close();
                }
                using(var form=new MainForm(new SettingsStore(Path.Combine(directory,"bare-install-"+Guid.NewGuid().ToString("N"))),true)){
                    form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);form.Show();Application.DoEvents();
                    form.ApplyWelcomeChoice("setup");if(!form.Controls.Find("setupResourceSummary",true).Any(c=>c.Visible&&c.Text.Contains("待补齐")))errors.Add("Bare installation setup must show missing resources");
                    form.ApplyWelcomeChoice("home");form.ShowPage("setup");form.ShowPage("home");
                    errors.AddRange(form.VerifySetupFlow(directory));
                }
            }catch(Exception error){errors.Add(error.ToString());}
            File.WriteAllText(Path.Combine(directory,"welcome-results.json"),Json.Encode(new{ok=errors.Count==0,frames=5,layouts=3,choices=3,serviceAutoStart=false,errors=errors}));return errors.Count==0?0:1;
        }
    }
    public sealed partial class MainForm {
        internal List<string> VerifySetupFlow(string directory){
            var errors=new List<string>();var original=Json.Copy(Config);string draft=body.Text;int controls=CountControls(this);
            ShowPage("setup");for(int i=0;i<24;i++){
                GoSetup(i%8);Application.DoEvents();
                if(SetupStep!=i%8||setupPanels.Count(c=>c.Visible)!=1||page!=setupIds[i%8]||pages.Count(route=>route.Value.Visible)!=1)errors.Add("Setup must show exactly one independent route");
                if(setupAnimations.Where((a,n)=>n!=i%8).Any(a=>a.AnimationRunning))errors.Add("Hidden step animation still running");
                if(i<8){foreach(double frame in new[]{0,.65,1.8}){setupAnimations[i].Preview(frame);using(var shot=new Bitmap(Width,Height)){DrawToBitmap(shot,new Rectangle(0,0,Width,Height));shot.Save(Path.Combine(directory,"setup-step-"+i+"-"+frame.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+".png"));}}}
            }
            if(setupPanels.Distinct().Count()!=8||setupAnimations.Select(a=>a.Illustration).Distinct().Count()!=8||Controls.Find("setupStepChoice",true).Length!=0)errors.Add("Wizard needs eight distinct pages and illustrations without step dropdown");
            if(CountControls(this)!=controls||body.Text!=draft)errors.Add("Setup navigation leaked controls or lost input");
            var initialSize=ClientSize;
            foreach(var size in new[]{new Size(1080,750),new Size(900,620)}){
                ClientSize=size;
                for(int step=0;step<8;step++){
                    GoSetup(step);Application.DoEvents();var route=setupPanels[step];var viewport=route.Controls.Find("setupScroller_"+step,true).Single() as ScenePanel;
                    foreach(var button in setupNavigation[step].Where(b=>b.Visible)){
                        var bounds=route.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                        if(!route.ClientRectangle.Contains(bounds)||bounds.IntersectsWith(route.RectangleToClient(viewport.RectangleToScreen(viewport.ClientRectangle))))errors.Add("Setup navigation clipped or overlaps content at "+size+" step "+step);
                    }
                    if(step==2&&viewport.VerticalScroll.Maximum-viewport.VerticalScroll.LargeChange+1<=0)errors.Add("Setup overflow has no reachable scrollbar at "+size);
                }
                GoSetup(2);using(var shot=new Bitmap(Width,Height)){DrawToBitmap(shot,new Rectangle(0,0,Width,Height));shot.Save(Path.Combine(directory,"setup-size-"+size.Width+".png"));}
            }
            ClientSize=initialSize;
            setupMotionChoices[2].Checked=true;if(setupAnimations.Any(a=>a.AnimationRunning)||!SetupPreference().reduceMotion)errors.Add("Reduced motion did not stop all step animations");
            setupMotionChoices[2].Checked=false;
            GoSetup(3);GoSetup(2);var animationWatch=System.Diagnostics.Stopwatch.StartNew();
            while(animationWatch.ElapsedMilliseconds<2100){Application.DoEvents();System.Threading.Thread.Sleep(10);}
            if(setupAnimations.Any(a=>a.AnimationRunning))errors.Add("Step animation did not settle within its finite duration");
            var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();Config.port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();Store.Write(Config);
            GoSetup(1);setupEngineChoice.SelectedIndex=1;setupNext.PerformClick();var nextWatch=System.Diagnostics.Stopwatch.StartNew();
            while(operation&&nextWatch.ElapsedMilliseconds<8000){Application.DoEvents();System.Threading.Thread.Sleep(10);}
            if(operation||SetupStep!=2||Config.voice.engine!="gpt-sovits"||Store.Read().voice.gptVariant!="v5turbo")errors.Add("Next did not save selected TTS before advancing");
            GoSetup(1);setupEngineChoice.SelectedIndex=2;setupSkip.PerformClick();if(Config.voice.gptVariant!="v5turbo")errors.Add("Skip unexpectedly applied a new engine variant");
            GoSetup(2);setupSkip.PerformClick();if(SetupStep!=3||SetupPreference().setupStates["step2"]!="skipped")errors.Add("Skip must preserve an explicit skipped state");
            setupBack.PerformClick();if(SetupStep!=2||new WelcomeState(Store).Read().setupStep!=2)errors.Add("Setup progress did not persist");
            setupOffline.Checked=true;if(!new WelcomeState(Store).Read().offline)errors.Add("Offline preference not persisted");
            try{ConfirmSetupVoice();errors.Add("Unplayed voice could be confirmed");}catch(InvalidOperationException){}
            var p=SetupPreference();p.voiceEvidence=VoiceEvidence();p.voiceJobId="fixture";p.heardVoice=true;new WelcomeState(Store).Write(p);
            Config.voice.speed=1.1;GoSetup(7);if(setupResult.Text.Contains("上次实际播放完成")||setupResult.Text.Contains("听感确认：已确认"))errors.Add("Changed voice reused stale playback evidence");
            SetSetupConnections(new Dictionary<string,object>{{"verification",new Dictionary<string,object>{{"codex",new Dictionary<string,object>{{"state","waiting"}}}}}});if(setupHarnessSummary.Contains("播放验证通过"))errors.Add("Waiting challenge was treated as verified");
            SetSetupConnections(null);if(!setupHarnessSummary.Contains("未验证"))errors.Add("Offline service retained a green connection");
            Config=original;Store.Write(original);setupOffline.Checked=false;GoSetup(0);BindVoice();ShowPage("home");if(setupAnimations.Any(a=>a.AnimationRunning))errors.Add("Leaving setup did not stop animation timers");return errors;
        }
    }
}
