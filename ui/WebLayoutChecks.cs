using Button = AgentVoice.AnimeButton;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AgentVoice {
    // Acceptance checks for the five web-styled sub pages. The design has no
    // captured coordinates for them, so the guard rails are shared: buttons
    // must show their whole label, wrapped copy must fit its label, cards and
    // interactive controls must not overlap, and nothing may leave its parent.
    public sealed partial class MainForm {
        internal static readonly string[] WebSubpages={"voiceEditor","harness","airi","connectionGuide","advancedTraining"};
        internal List<string> AuditWebSubpages(string directory,out int controls){
            Directory.CreateDirectory(directory);
            var layout=new List<object>();
            var errors=new List<string>();
            foreach(string id in WebSubpages)AuditWebPage(id,"default",layout,errors,directory);
            foreach(string id in WebSubpages)WebSubpageShot(id,directory,id);
            int defaultErrors=errors.Count;
            int defaultControls=layout.Count;
            ClientSize=new Size(900,620);Application.DoEvents();
            foreach(string id in WebSubpages)AuditWebPage(id,"900x620",layout,errors,directory);
            foreach(string id in WebSubpages)WebSubpageShot(id,directory,id+"-900");
            foreach(string id in WebSubpages)WebSubpageScreenShot(id,directory,id+"-900-screen");
            int scaledControls=layout.Count;
            Size=new Size(MinimumSize.Width,MinimumSize.Height);Application.DoEvents();
            foreach(string id in WebSubpages)AuditWebPage(id,"minimum",layout,errors,directory);
            foreach(string id in WebSubpages)WebSubpageShot(id,directory,id+"-minimum");
            ClientSize=new Size(1180,800);Application.DoEvents();
            controls=layout.Count;
            var summary=new{ok=errors.Count==0,controls=controls,defaultControls=defaultControls,defaultErrors=defaultErrors,scaledControls=scaledControls-defaultControls,minimumControls=controls-scaledControls,screens=WebSubpages.Select(id=>id+".png").Concat(WebSubpages.Select(id=>id+"-900.png")).Concat(WebSubpages.Select(id=>id+"-minimum.png")).ToArray(),errors=errors};
            File.WriteAllText(Path.Combine(directory,"subpage-layout.json"),Json.Encode(layout));
            File.WriteAllText(Path.Combine(directory,"subpage-audit.json"),Json.Encode(summary));
            return errors;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window,IntPtr hdc,uint flags);
        void WebSubpageScreenShot(string id,string directory,string name){
            ShowPage(id);Application.DoEvents();TopMost=true;Location=new Point(40,40);Show();Activate();Application.DoEvents();System.Threading.Thread.Sleep(250);pages[id].Refresh();Application.DoEvents();System.Threading.Thread.Sleep(120);
            Rectangle screen=RectangleToScreen(ClientRectangle);
            using(var bitmap=new Bitmap(Math.Max(1,screen.Width),Math.Max(1,screen.Height))){using(var graphics=Graphics.FromImage(bitmap))graphics.CopyFromScreen(screen.Location,Point.Empty,bitmap.Size);bitmap.Save(Path.Combine(directory,name+".png"));}
            TopMost=false;
        }
        void WebSubpageShot(string id,string directory,string name){
            ShowPage(id);Application.DoEvents();pages[id].Refresh();Application.DoEvents();
            using(var bitmap=new Bitmap(Math.Max(1,Width),Math.Max(1,Height))){
                bool printed=false;
                using(var graphics=Graphics.FromImage(bitmap)){
                    IntPtr dc=graphics.GetHdc();
                    try{printed=PrintWindow(Handle,dc,2);}finally{graphics.ReleaseHdc(dc);}
                }
                if(!printed)DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));
                bitmap.Save(Path.Combine(directory,name+".png"));
            }
        }
        void AuditWebPage(string id,string mode,List<object> layout,List<string> errors,string directory){
            ShowPage(id);Application.DoEvents();pages[id].Refresh();Application.DoEvents();
            AuditControlTree(pages[id],pages[id],id+"@"+mode,layout,errors,directory);
        }
        static bool Interactive(Control control){
            return control is Button||control is TextBoxBase||control is ComboBox||control is NumericUpDown||control is CheckBox||control is ListBox;
        }
        static string Shorten(string text){
            string flat=(text??"").Replace("\r"," ").Replace("\n"," ");
            return flat.Length<=20?flat:flat.Substring(0,20)+"…";
        }
        static string ControlPath(Control control){
            var parts=new List<string>();
            for(Control current=control;current!=null;current=current.Parent){
                string name;
                if(current is Button)name="button["+Shorten(((Button)current).Text)+"]";
                else if(current is Label)name="label["+Shorten(((Label)current).Text)+"]";
                else if(current is ScenePanel)name=((ScenePanel)current).Card?"card":"panel";
                else name=current.GetType().Name;
                parts.Insert(0,name);
                if(current is MainForm)break;
            }
            return String.Join("/",parts.ToArray());
        }
        void AuditControlTree(Control parent,ScenePanel page,string tag,List<object> layout,List<string> errors,string directory){
            var kids=new List<Control>();
            foreach(Control child in parent.Controls)if(child.Visible)kids.Add(child);
            foreach(Control child in kids){
                Rectangle document=parent==page&&page.AutoScroll?page.DisplayRectangle:parent.ClientRectangle;
                if(child.Right>document.Right+1)errors.Add(tag+" 超出右边界："+ControlPath(child)+" right="+child.Right+" > "+document.Right);
                if(child.Bottom>document.Bottom+1)errors.Add(tag+" 超出下边界："+ControlPath(child)+" bottom="+child.Bottom+" > "+document.Bottom);
                var button=child as Button;
                if(button!=null){
                    int room=button.Width-button.Padding.Horizontal;
                    int need=TextRenderer.MeasureText(button.Text,button.Font).Width;
                    layout.Add(new{mode=tag,kind="button",path=ControlPath(button),text=button.Text,font=Math.Round(button.Font.Size,1),x=button.Left,y=button.Top,w=button.Width,h=button.Height,need=need,room=room});
                    if(need>room)errors.Add(tag+" 按钮文字被裁切："+button.Text+" 需要 "+need+"px，可用 "+room+"px（"+ControlPath(button)+"）");
                }
                var label=child as Label;
                if(label!=null&&label.Text.Length>0){
                    // A line box is taller than its ink; only the ink height of
                    // the last line has to fit, so measure lines then require
                    // (lines-1) line boxes plus one glyph box.
                    var font=label.Font;
                    int line=Math.Max(1,(int)Math.Round(font.GetHeight()));
                    var need=TextRenderer.MeasureText(label.Text,font,new Size(Math.Max(8,label.Width),0),TextFormatFlags.WordBreak);
                    int lines=Math.Max(1,(int)Math.Round(need.Height/(double)line));
                    int ink=(int)Math.Ceiling(font.Height*0.7);
                    int required=need.Height-(line-ink);
                    layout.Add(new{mode=tag,kind="label",path=ControlPath(label),text=Shorten(label.Text),font=Math.Round(font.Size,1),x=label.Left,y=label.Top,w=label.Width,h=label.Height,needH=need.Height,lines=lines,required=required});
                    if(label.Height<required-1)errors.Add(tag+" 标签文字被裁切："+ControlPath(label)+" 需要 "+required+"px 高（"+lines+" 行），实际 "+label.Height+"px");
                }
                if(child is Label&&child.Text.Length>40&&tag=="connectionGuide@900x620"&&!File.Exists(Path.Combine(directory,"label-dump.png"))){
                    using(var shot=new Bitmap(Math.Max(1,child.Width),Math.Max(1,child.Height))){child.DrawToBitmap(shot,new Rectangle(0,0,shot.Width,shot.Height));shot.Save(Path.Combine(directory,"label-dump.png"));}
                }                if(child.Controls.Count>0)AuditControlTree(child,page,tag,layout,errors,directory);
            }
            for(int i=0;i<kids.Count;i++)for(int j=i+1;j<kids.Count;j++){
                Control one=kids[i],two=kids[j];
                if(!(one is ScenePanel||two is ScenePanel||Interactive(one)||Interactive(two)))continue;
                Rectangle hit=Rectangle.Intersect(one.Bounds,two.Bounds);
                if(hit.Width>2&&hit.Height>2)errors.Add(tag+" 重叠："+ControlPath(one)+" "+one.Bounds+" / "+ControlPath(two)+" "+two.Bounds+"，交集 "+hit);
            }
        }
    }
}
