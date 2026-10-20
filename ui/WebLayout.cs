using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Button = AgentVoice.AnimeButton;

namespace AgentVoice {
    // Absolute, design-derived layout helpers for the workspace pages that
    // follow assets/workspace/layout.json. Coordinates are relative to the
    // page content area, which starts at (150, 76) inside the window.
    public sealed partial class MainForm {
        internal bool IsWebLayoutPage(string id){
            return id=="configuration"||id=="voices"||id=="connections"||id=="settings"||id=="training"
                ||id=="voiceEditor"||id=="harness"||id=="airi"||id=="connectionGuide"||id=="advancedTraining";
        }
        internal ScenePanel WebPage(string id){
            var page=NewPage(id);
            page.AutoScroll=false;
            page.Padding=new Padding(0);
            return page;
        }
        internal Label WebText(string text,float size,FontStyle style,int x,int y,int width,int height,ContentAlignment align){
            var font=new Font(Font.FontFamily,size,style);
            int needed=TextRenderer.MeasureText(text,font,new Size(Math.Max(1,width),0),TextFormatFlags.WordBreak).Height;
            var label=new Label{
                Text=text,Font=font,AutoSize=false,
                Bounds=new Rectangle(x,y,width,Math.Max(height,needed)),TextAlign=align,BackColor=Color.Transparent,UseMnemonic=false
            };
            return label;
        }
        internal Label WebText(string text,float size,FontStyle style,int x,int y,int width,int height){
            return WebText(text,size,style,x,y,width,height,ContentAlignment.TopLeft);
        }
        internal Label WebTitle(ScenePanel page,string title,string lead){
            var heading=WebText(title,26F,FontStyle.Bold,38,22,580,52,ContentAlignment.MiddleLeft);
            page.Controls.Add(heading);
            page.Controls.Add(WebText(lead,9.5F,FontStyle.Regular,38,80,560,24,ContentAlignment.MiddleLeft));
            return heading;
        }
        internal ScenePanel WebCard(Control parent,int x,int y,int width,int height){
            var card=new ScenePanel{Host=this,Card=true,Location=new Point(x,y),Size=new Size(width,height),Margin=new Padding(0)};
            parent.Controls.Add(card);
            return card;
        }
        internal Button WebButton(string text,string note,Action action,int x,int y,int width,int height,bool primary){
            Button button=note==null?Btn(text,action):EntryBtn(text,note,action);
            button.AutoSize=false;
            if(primary)Primary(button);
            button.MinimumSize=Size.Empty;button.MaximumSize=Size.Empty;
            button.Margin=new Padding(0);button.Padding=new Padding(4,0,4,0);
            button.SetBounds(x,y,width,height);
            return button;
        }
        internal Button WebButton(string text,string note,Action action,int x,int y,int width,int height){
            return WebButton(text,note,action,x,y,width,height,false);
        }
        internal Button WebAsyncButton(string text,string note,Func<Task> action,int x,int y,int width,int height,bool primary){
            Button button=AsyncBtn(text,action);
            if(!String.IsNullOrEmpty(note)){button.AccessibleDescription=note;tips.SetToolTip(button,note);}
            button.AutoSize=false;
            if(primary)Primary(button);
            button.MinimumSize=Size.Empty;button.MaximumSize=Size.Empty;
            button.Margin=new Padding(0);button.Padding=new Padding(4,0,4,0);
            button.SetBounds(x,y,width,height);
            return button;
        }
        internal Button WebAsyncButton(string text,string note,Func<Task> action,int x,int y,int width,int height){
            return WebAsyncButton(text,note,action,x,y,width,height,false);
        }
        internal Label WebCardTitle(ScenePanel card,string title){
            var label=WebText(title,10F,FontStyle.Bold,16,12,Math.Max(120,card.Width-32),24,ContentAlignment.MiddleLeft);
            card.Controls.Add(label);
            return label;
        }
        internal Label WebCardTitle(ScenePanel card,string title,int width){
            var label=WebText(title,10F,FontStyle.Bold,16,12,width,24,ContentAlignment.MiddleLeft);
            card.Controls.Add(label);
            return label;
        }
        internal CheckBox WebCheck(string text,int x,int y,int width,int height,bool value){
            return new CheckBox{Text=text,Checked=value,AutoSize=false,Bounds=new Rectangle(x,y,width,height),BackColor=Color.Transparent,Margin=new Padding(0)};
        }
        internal void WebDivider(Control parent,int x,int y,int width){
            parent.Controls.Add(new Panel{BackColor=Mix(CardColor,ForeColor,22),Bounds=new Rectangle(x,y,width,1),Margin=new Padding(0)});
        }
        internal Label WebHarnessStatus(string source,string name,int x,int y,int width){
            var label=WebText(name+" · 服务关闭",9F,FontStyle.Regular,x,y,width,20,ContentAlignment.MiddleLeft);
            label.Name="harnessStatus_"+source;label.Tag=name;
            if(!harnessIndicators.ContainsKey(source))harnessIndicators[source]=new System.Collections.Generic.List<Label>();
            harnessIndicators[source].Add(label);
            return label;
        }
    }
}
