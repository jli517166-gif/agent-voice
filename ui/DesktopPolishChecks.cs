using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AgentVoice {
    internal static class DesktopPolishChecks {
        internal static int Run(string[] args){
            string directory=Path.GetFullPath(args[1]);Directory.CreateDirectory(directory);var errors=new List<string>();int manuals=0;
            try{
                using(var phrases=new PhraseEditorForm(new[]{"第一句"},"草稿")){
                    phrases.Show();Application.DoEvents();var input=phrases.Controls.Find("phraseEditor",true).OfType<TextBox>().Single();input.Text="第一句\r\n第二句\r\n第一句\r\n";
                    phrases.Controls.Find("savePhrases",true).OfType<AnimeButton>().Single().PerformClick();Application.DoEvents();
                    if(phrases.DialogResult!=DialogResult.OK||!phrases.Phrases.SequenceEqual(new[]{"第一句","第二句"}))errors.Add("Phrase editor save lost or duplicated content");
                    var settings=new SettingsStore(Path.Combine(directory,"phrase-settings"));var config=settings.Read();config.phrases=phrases.Phrases;settings.Write(config);if(!settings.Read().phrases.SequenceEqual(phrases.Phrases))errors.Add("Saved phrases did not survive reload");
                }
                string file=Path.Combine(directory,"没有关联程序的说明 (测试).md");File.WriteAllText(file,"# 安装指南\n\n中文说明，支持复制。\n\n```powershell\npython -m pip --version\n```\n\n[官方网站](https://example.com/manual)\n",Encoding.UTF8);
                ServiceClient.Open(file);Application.DoEvents();var view=Application.OpenForms.OfType<TextDocumentForm>().Single();
                if(!view.Content.Contains("安装指南")||!view.Content.Contains("python -m pip --version")||!view.Content.Contains("https://example.com/manual"))errors.Add("Built-in manual lost Chinese text, code or links");
                ServiceClient.Open(file);if(Application.OpenForms.OfType<TextDocumentForm>().Count()!=1)errors.Add("Repeated manual clicks created duplicate windows");
                view.Close(); // RichEdit does not support DrawToBitmap; actual display is checked with native window capture.
                foreach(string manual in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory,"*.md"))using(var reader=new TextDocumentForm(manual)){if(reader.Content.Length==0)errors.Add("Manual is empty: "+Path.GetFileName(manual));manuals++;}
                bool rejected=false;try{ServiceClient.Open(Path.Combine(directory,"missing.md"));}catch(InvalidOperationException error){rejected=error.Message.Contains("找不到文件或目录");}if(!rejected)errors.Add("Missing manual did not report an actionable file error");
                string png=Path.Combine(directory,"wide-image.png");using(var image=new Bitmap(320,80)){using(var g=Graphics.FromImage(image))g.Clear(Color.MediumPurple);image.Save(png,ImageFormat.Png);}
                foreach(string custom in new[]{"",png,Path.Combine(directory,"missing.gif")})using(var overlay=new BusyOverlay("正在导出声音配置，请稍候。",null,custom,Color.FromArgb(180,154,255),true)){
                    overlay.Dock=DockStyle.None;overlay.Size=new Size(900,620);overlay.CreateControl();int stopped=0;overlay.SetStopAction(()=>stopped++);overlay.SetStopAction(()=>stopped++);
                    var button=overlay.Controls.OfType<System.Windows.Forms.Button>().Single();using(var shot=new Bitmap(overlay.Width,overlay.Height)){overlay.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(directory,custom==png?"busy-image.png":custom.Length==0?"busy-default.png":"busy-fallback.png"));}
                    if(!overlay.ClientRectangle.Contains(button.Bounds))errors.Add("Loading stop button clipped");button.PerformClick();if(stopped!=1)errors.Add("Loading stop action duplicated or failed");
                    if(custom==png){using(var shot=new Bitmap(900,620)){overlay.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));if(shot.GetPixel(450,206).ToArgb()!=Color.MediumPurple.ToArgb())errors.Add("Static PNG did not load or was stretched to an old square mascot region");}}
                }
            }catch(Exception error){errors.Add(error.ToString());}
            File.WriteAllText(Path.Combine(directory,"desktop-results.json"),Json.Encode(new{ok=errors.Count==0,manuals=manuals,errors=errors}));return errors.Count==0?0:1;
        }
    }
}
