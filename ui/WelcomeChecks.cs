using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentVoice {
    static class WelcomeChecks {
        static IEnumerable<Control> All(Control root){foreach(Control c in root.Controls){yield return c;foreach(var child in All(c))yield return child;}}
        static void Pump(int milliseconds){var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<milliseconds){Application.DoEvents();Thread.Sleep(5);}}
        public static int Run(SettingsStore ignored,string[] args){
            string directory=args.Length>1?Path.GetFullPath(args[1]):ignored.Resolve("acceptance/onboarding");Directory.CreateDirectory(directory);var errors=new List<string>();
            try{
                var fixture=new SettingsStore(Path.Combine(directory,"fixture-"+Guid.NewGuid().ToString("N")));var settings=fixture.Read();var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();settings.port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();fixture.Write(settings);var state=new WelcomeState(fixture);string before=File.ReadAllText(fixture.FilePath);
                if(!SetupWizard.NeedsSetup(state.Read()))errors.Add("Fresh installation bypasses setup");
                var evidenceFlow=new SetupWorkflow(fixture);string stableEvidence=evidenceFlow.Evidence();
                var rawSettings=Json.Object(before);var rawVoice=(Dictionary<string,object>)rawSettings["voice"];
                for(int order=0;order<24;order++){
                    IEnumerable<KeyValuePair<string,object>> ordered=rawVoice.OrderBy(p=>p.Key,StringComparer.Ordinal);if(order%2==1)ordered=ordered.Reverse();
                    rawSettings["voice"]=ordered.ToDictionary(p=>p.Key,p=>p.Value);File.WriteAllText(fixture.FilePath,Json.Encode(rawSettings));
                    if(evidenceFlow.Evidence()!=stableEvidence)errors.Add("Equivalent reordered settings invalidated playback evidence");
                }
                fixture.Write(settings);before=File.ReadAllText(fixture.FilePath);
                var nestedA=Json.Object("{\"b\":[{\"y\":2,\"x\":1}],\"a\":true}");var nestedB=Json.Object("{\"a\":true,\"b\":[{\"x\":1,\"y\":2}]}");
                if(Json.Canonical(nestedA)!=Json.Canonical(nestedB)||Json.Canonical(new[]{1,2})==Json.Canonical(new[]{2,1}))errors.Add("Canonical JSON lost semantic identity or array order");
                using(var wizard=new SetupWizard(fixture,true)){
                    wizard.ShowInTaskbar=false;wizard.StartPosition=FormStartPosition.Manual;wizard.Location=new Point(-32000,-32000);wizard.Show();Application.DoEvents();
                    if(Application.OpenForms.Cast<Form>().Any(f=>f is MainForm))errors.Add("Workspace constructed before onboarding finished");
                    if(wizard.PageCount!=10||wizard.Controls.Find("nav_home",true).Length>0)errors.Add("First-boot workflow mixed with workspace navigation");
                    wizard.Go(5,false);if(wizard.Step!=1||!wizard.Next.Enabled)errors.Add("Native setup did not migrate removed legal page");
                    foreach(var size in new[]{new Size(1120,780),new Size(900,640),new Size(1440,900)}){
                        wizard.ClientSize=size;
                        foreach(int step in SetupWorkflow.PageOrder){
                            wizard.Go(step,false);Application.DoEvents();foreach(var title in wizard.Controls.Find("onboardingTitle_"+step,true).OfType<WizardTitle>())title.Preview();
                            if(wizard.Step!=step||wizard.VisiblePageCount!=1||wizard.RunningArtCount>1)errors.Add("Multiple steps or timers visible: "+step);
                            foreach(var c in new Control[]{wizard.Next,wizard.Back,wizard.Later,wizard.Skip,wizard.Motion,wizard.Sound}.Where(c=>c.Visible)){
                                var rect=wizard.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));if(!wizard.ClientRectangle.Contains(rect))errors.Add("Clipped primary navigation: "+c.Text+" @ "+size);
                            }
                            foreach(var a in wizard.Controls.Find("onboardingArt_"+step,true).OfType<WizardArt>())a.Preview(WizardArt.Duration);
                            if(size.Width==1120||step==3){using(var shot=SetupWizard.CaptureFrame(wizard))shot.Save(Path.Combine(directory,"setup-"+step+"-"+size.Width+".png"));}if(size.Width==1120&&step==2)File.WriteAllText(Path.Combine(directory,"header-layout.json"),Json.Encode(All(wizard).Where(c=>c is Label&&c.Visible).Select(c=>new{text=c.Text,bounds=c.Bounds.ToString(),parent=c.Parent.Bounds.ToString(),screen=wizard.RectangleToClient(c.RectangleToScreen(c.ClientRectangle)).ToString()}).ToArray()));
                        }
                    }
                    wizard.Motion.Checked=true;wizard.Go(2);if(wizard.TransitionRunning||wizard.RunningArtCount>0)errors.Add("Reduced motion starts an animation");
                    wizard.Motion.Checked=false;wizard.Go(1,false);wizard.Go(2);if(!wizard.TransitionRunning)errors.Add("Page transition did not run");wizard.Go(3);Pump(700);if(wizard.TransitionRunning||wizard.RunningArtCount>1)errors.Add("Interrupted transition leaked timers");Pump(450);if(wizard.RunningArtCount!=0)errors.Add("Step illustration did not settle");
                    wizard.Motion.Checked=true;wizard.Go(2,false);wizard.Engine.SelectedIndex=1;wizard.Next.PerformClick();var saveWatch=Stopwatch.StartNew();while(wizard.PendingAction!=null&&!wizard.PendingAction.IsCompleted&&saveWatch.ElapsedMilliseconds<12000)Pump(20);Pump(30);if(wizard.Busy||wizard.Step!=3||fixture.Read().voice.engine!="gpt-sovits"||fixture.Read().voice.gptVariant!="v5turbo")throw new InvalidOperationException("Engine next: step="+wizard.Step+", busy="+wizard.Busy+", error="+wizard.LastFailure+", trace="+wizard.AdvanceTrace);
                    wizard.Go(2,false);wizard.Engine.SelectedIndex=2;wizard.Skip.PerformClick();if(fixture.Read().voice.gptVariant!="v5turbo")errors.Add("Skipping applied unconfirmed engine");wizard.Back.PerformClick();if(wizard.Step!=2||state.Read().onboardingStep!=2)errors.Add("Back did not persist progress");
                    wizard.Offline.Checked=true;if(!state.Read().offline)errors.Add("Offline setting not persisted");
                    try{wizard.Flow.Confirm();errors.Add("Unplayed voice was confirmed");}catch(InvalidOperationException){}
                    var p=state.Read();p.voiceEvidence=wizard.Flow.Evidence();p.voiceJobId="fixture";p.heardVoice=true;state.Write(p);var changed=fixture.Read();changed.voice.speed=1.1;fixture.Write(changed);if(wizard.Flow.Evidence()==p.voiceEvidence)errors.Add("Changed voice reused old evidence");
                    wizard.Sound.Checked=false;if(state.Read().interfaceSounds)errors.Add("Sound setting not persisted");
                    wizard.Later.PerformClick();if(SetupWizard.NeedsSetup(state.Read())||!state.Read().onboardingDeferred||state.Read().onboardingComplete)errors.Add("Explicit defer semantics invalid");
                }
                var p2=state.Read();p2.onboardingDeferred=false;p2.onboardingComplete=false;p2.onboardingStep=7;state.Write(p2);
                using(var resumed=new SetupWizard(fixture,true)){if(resumed.Step!=7)errors.Add("Closing interrupted setup loses step");resumed.Dispose();}
                if(!SetupWizard.NeedsSetup(state.Read()))errors.Add("Interrupted setup marked as finished");
                fixture.Write(settings);before=File.ReadAllText(fixture.FilePath);
                using(var completed=new SetupWizard(fixture,true)){completed.Go(9,false);completed.Finish(false);}
                if(SetupWizard.NeedsSetup(state.Read())||!state.Read().onboardingComplete||state.Read().onboardingDeferred)errors.Add("Finish does not enter daily mode");
                if(before!=File.ReadAllText(fixture.FilePath))errors.Add("Navigation changed voice settings");
                using(var workspace=new MainForm(fixture,true)){workspace.ShowInTaskbar=false;workspace.StartPosition=FormStartPosition.Manual;workspace.Location=new Point(-32000,-32000);workspace.Show();Application.DoEvents();if(workspace.Controls.Find("onboardingStage",true).Length>0||workspace.Controls.Find("homeKeynote",true).Length>0||workspace.Controls.Find("nav_setup",true).Length>0)errors.Add("Workspace contains first-boot design");using(var shot=SetupWizard.CaptureFrame(workspace))shot.Save(Path.Combine(directory,"workspace.png"));}
                // Render choreography at half-speed sample times without delaying the tests.
                using(var beforeFrame=new Bitmap(400,220))using(var afterFrame=new Bitmap(400,220)){
                    using(var g=Graphics.FromImage(beforeFrame))g.Clear(Color.FromArgb(15,40,65));using(var g=Graphics.FromImage(afterFrame))g.Clear(Color.FromArgb(35,70,100));foreach(double time in new[]{0,.12,.24,.36,.48})using(var frame=new Bitmap(400,220)){using(var g=Graphics.FromImage(frame))WizardTransition.PaintFrame(g,beforeFrame,afterFrame,new Rectangle(0,0,400,220),time,1);frame.Save(Path.Combine(directory,"transition-half-speed-"+(time*2).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+".png"));}
                }
                using(var task=new WizardTaskForm("任务窗口检查",new[]{"步骤一","步骤二"},async progress=>{progress.Report("执行中…");await Task.Delay(120);return "任务完成";},true)){
                    var taskTimer=new System.Windows.Forms.Timer{Interval=50};bool captured=false;
                    taskTimer.Tick+=(s,e)=>{if(!task.Finished)return;if(!captured){captured=true;using(var shot=SetupWizard.CaptureFrame(task))shot.Save(Path.Combine(directory,"task-dialog.png"));}taskTimer.Stop();task.Close();};
                    taskTimer.Start();task.ShowDialog();taskTimer.Dispose();
                    if(task.Error!=null||task.Result!="任务完成")errors.Add("Task dialog did not complete");
                }
                if(InterfaceSounds.Allowed(true,true,false)||InterfaceSounds.Allowed(true,false,true)||InterfaceSounds.Allowed(false,false,false)||!InterfaceSounds.Allowed(true,false,false))errors.Add("Interface sound overlap policy invalid");
            }catch(Exception e){errors.Add(e.ToString());}
            File.WriteAllText(Path.Combine(directory,"welcome-results.json"),Json.Encode(new{ok=errors.Count==0,independentFirstBoot=true,steps=SetupWorkflow.PageOrder.Length,layouts=3,transitionMs=480,illustrationMs=960,reducedMotion=true,errors=errors}));return errors.Count==0?0:1;
        }
    }
}

