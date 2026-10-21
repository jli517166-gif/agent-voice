using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed partial class MainForm {
        internal object VisualRepairReport(string directory){
            var errors=new List<string>();
            var original=Config.appearance.background;
            string fixture=Path.Combine(directory,"bright-background.png");
            using(var b=new Bitmap(48,48)){using(var g=Graphics.FromImage(b))g.Clear(Color.White);b.Save(fixture);}
            foreach(string backdrop in new[]{"",fixture}){
                Config.appearance.background=backdrop;ApplyTheme();ShowPage("voices");Application.DoEvents();
                foreach(var button in navigationButtons.Values){
                    using(var b=new Bitmap(button.Width,button.Height)){
                        button.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));
                        if(b.GetPixel(0,0).ToArgb()!=Color.FromArgb(11,13,18).ToArgb())errors.Add("Navigation corner does not match sidebar: "+button.Name+" / "+backdrop);
                    }
                }
                using(var g=profileList.CreateGraphics())using(var title=new Font(Font.FontFamily,11F,FontStyle.Bold))using(var detail=new Font(Font.FontFamily,10.5F)){
                    int required=(int)Math.Ceiling(title.GetHeight(g)+detail.GetHeight(g))+UiPx(12);
                    if(profileList.ItemHeight<required)errors.Add("Profile text exceeds item row: "+profileList.ItemHeight+" < "+required);
                }
                using(var b=new Bitmap(Width,Height)){DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));b.Save(Path.Combine(directory,backdrop.Length==0?"profiles-plain.png":"profiles-background.png"));}
            }
            Config.appearance.background=original;ApplyTheme();
            return new{ok=errors.Count==0,errors=errors};
        }
    }
    internal static class VisualRepairChecks {
        internal static int Run(string[] args){
            string directory=Path.GetFullPath(args[1]);Directory.CreateDirectory(directory);
            var store=new SettingsStore(Path.Combine(directory,"installation-"+Guid.NewGuid().ToString("N")));
            var c=store.Read();for(int i=0;i<6;i++)c.profiles.Add(new SavedVoice{id="fixture"+i,name="方案 "+i+" · 两行文字应清晰显示",voice=c.voice.Clone()});store.Write(c);
            using(var form=new MainForm(store,true)){
                form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);form.Show();Application.DoEvents();
                string json=Json.Encode(form.VisualRepairReport(directory));File.WriteAllText(Path.Combine(directory,"visual-results.json"),json);return json.Contains("\"ok\":true")?0:1;
            }
        }
    }
}
