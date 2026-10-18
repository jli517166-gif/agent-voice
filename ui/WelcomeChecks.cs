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
                using(var form=new MainForm(new SettingsStore(Path.Combine(directory,"bare-install")),true)){
                    form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);form.Show();Application.DoEvents();
                    form.ApplyWelcomeChoice("setup");if(!form.Controls.Find("setupResourceSummary",true).Any(c=>c.Visible&&c.Text.Contains("待补齐")))errors.Add("Bare installation setup must show missing resources");
                    form.ApplyWelcomeChoice("home");form.ShowPage("setup");form.ShowPage("home");
                }
            }catch(Exception error){errors.Add(error.ToString());}
            File.WriteAllText(Path.Combine(directory,"welcome-results.json"),Json.Encode(new{ok=errors.Count==0,frames=5,layouts=3,choices=3,serviceAutoStart=false,errors=errors}));return errors.Count==0?0:1;
        }
    }
}
