using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed class WelcomeForm:Form {
        public readonly WelcomeSurface Surface;
        public string Choice {get;private set;}
        public bool ReduceMotion {get{return Surface.ReduceMotion;}}
        public WelcomeForm(bool reduced,bool animate=true,bool sounds=false){
            Text="Agent Voice · 由你定调";AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
            ClientSize=new Size(1120,720);MinimumSize=new Size(840,580);StartPosition=FormStartPosition.CenterScreen;BackColor=WelcomeSurface.Navy;
            Surface=new WelcomeSurface(reduced,animate,sounds){Dock=DockStyle.Fill};Controls.Add(Surface);
            Surface.Choose+=value=>{Choice=value;DialogResult=DialogResult.OK;Close();};
            KeyPreview=true;KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape){Surface.Skip();e.Handled=true;}};
            Shown+=(s,e)=>Surface.Start();
        }
    }
    public sealed class WelcomeSurface:Panel {
        public static readonly Color Navy=Color.Black,Cyan=Color.FromArgb(34,211,238),White=Color.FromArgb(239,247,252);
        readonly Timer timer=new Timer{Interval=16};readonly Stopwatch watch=new Stopwatch();
        readonly Font brand=new Font("Segoe UI",22,FontStyle.Bold,GraphicsUnit.Pixel),title=new Font("Microsoft YaHei UI",54,FontStyle.Bold,GraphicsUnit.Pixel),body=new Font("Microsoft YaHei UI",17,FontStyle.Regular,GraphicsUnit.Pixel),small=new Font("Microsoft YaHei UI",13,FontStyle.Regular,GraphicsUnit.Pixel);
        readonly bool animate,playSounds;readonly InterfaceSounds effects=new InterfaceSounds();double seconds;bool reduced;Font motionFont;Bitmap artwork;
        public event Action<string> Choose;
        public readonly WelcomeAction StartButton,ImportButton,HomeButton,SkipButton;
        public readonly CheckBox MotionChoice;
        public bool ReduceMotion {get{return reduced;}}
        public bool Ready {get{return seconds>=3.4;}}
        internal bool AnimationRunning {get{return timer.Enabled;}}
        float ScaleFactor {get{return Math.Max(.1F,Math.Min(ClientSize.Width/1120F,ClientSize.Height/720F));}}
        public WelcomeSurface(bool reduceMotion,bool play,bool sounds=false){
            animate=play;playSounds=sounds;reduced=reduceMotion;seconds=reduceMotion?3.4:0;
            string art=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","keynote-voice.png");if(File.Exists(art))using(var source=Image.FromFile(art))artwork=new Bitmap(source);
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);BackColor=Navy;
            AccessibleName="Agent Voice 首次配置";
            StartButton=Action("开始配置","setup",true);ImportButton=Action("导入已有配置","import",false);HomeButton=Action("直接进入  →","home",false);HomeButton.TextOnly=true;
            SkipButton=new WelcomeAction(this){Text="跳过动画  →",TextOnly=true,AccessibleDescription="立即显示欢迎页，也可按 Esc。",TabIndex=0};SkipButton.Click+=(s,e)=>Skip();Controls.Add(SkipButton);
            MotionChoice=new CheckBox{Text="减少动画",AutoSize=false,BackColor=Navy,ForeColor=Color.FromArgb(155,178,195),Checked=reduceMotion,TabIndex=4,AccessibleDescription="开启后立即显示欢迎页，停止装饰动画。"};
            MotionChoice.CheckedChanged+=(s,e)=>{reduced=MotionChoice.Checked;if(reduced)Skip();Invalidate(true);};Controls.Add(MotionChoice);
            timer.Tick+=(s,e)=>{seconds=watch.Elapsed.TotalSeconds;if(seconds>=3.4){seconds=3.4;timer.Stop();watch.Stop();}RefreshStage();Invalidate(true);};
            LayoutControls();RefreshStage();
        }
        WelcomeAction Action(string text,string value,bool primary){var button=new WelcomeAction(this){Text=text,Primary=primary,TabIndex=value=="setup"?1:value=="import"?2:3};button.Click+=(s,e)=>{if(Ready&&Choose!=null)Choose(value);};Controls.Add(button);return button;}
        public void Start(){if(!animate||reduced||Ready){if(reduced)Skip();return;}if(playSounds)try{var store=new SettingsStore(AppDomain.CurrentDomain.BaseDirectory);int port=store.Read().port;bool listening=System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint=>endpoint.Port==port);if(!listening)effects.Play(store.Root,8);}catch(System.Net.NetworkInformation.NetworkInformationException){}watch.Start();timer.Start();}
        public void Skip(){seconds=3.4;timer.Stop();watch.Stop();RefreshStage();Invalidate(true);StartButton.Focus();}
        internal void Preview(double elapsed){timer.Stop();watch.Stop();seconds=Math.Max(0,Math.Min(3.4,elapsed));RefreshStage();Invalidate(true);}
        void RefreshStage(){
            Reveal(StartButton,2.95);Reveal(ImportButton,3.08);Reveal(HomeButton,3.16);SkipButton.Visible=!Ready;
        }
        void Reveal(WelcomeAction button,double begins){button.Reveal=Ease((float)((seconds-begins)/(3.4-begins)));button.Visible=button.Reveal>0;button.Enabled=Ready;}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(StartButton!=null)LayoutControls();}
        Rectangle BoundsAt(float x,float y,float w,float h){float scale=ScaleFactor,offsetX=(Width-1120*scale)/2,offsetY=(Height-720*scale)/2;return Rectangle.Round(new RectangleF(offsetX+x*scale,offsetY+y*scale,w*scale,h*scale));}
        void LayoutControls(){
            StartButton.Bounds=BoundsAt(100,420,360,56);ImportButton.Bounds=BoundsAt(100,492,212,48);HomeButton.Bounds=BoundsAt(326,492,150,48);SkipButton.Bounds=BoundsAt(900,30,160,42);MotionChoice.Bounds=BoundsAt(100,638,180,36);
            float fontSize=Math.Max(8,13*ScaleFactor);if(motionFont==null||Math.Abs(motionFont.Size-fontSize)>.05F){var prior=motionFont;motionFont=new Font("Microsoft YaHei UI",fontSize,FontStyle.Regular,GraphicsUnit.Pixel);MotionChoice.Font=motionFont;if(prior!=null)prior.Dispose();}
        }
        internal void Backdrop(Graphics g){g.Clear(Navy);}
        protected override void OnPaintBackground(PaintEventArgs e){Backdrop(e.Graphics);}
        static float Ease(float v){v=Math.Max(0,Math.Min(1,v));return v*v*(3-2*v);}
        static Color Fade(Color c,float opacity){return Color.FromArgb((int)(255*Math.Max(0,Math.Min(1,opacity))),c);}
        void TextAt(Graphics g,string text,Font font,Color color,RectangleF rectangle){using(var brush=new SolidBrush(color))g.DrawString(text,font,brush,rectangle);}
        protected override void OnPaint(PaintEventArgs e){
            base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float scale=ScaleFactor;g.TranslateTransform((Width-1120*scale)/2,(Height-720*scale)/2);g.ScaleTransform(scale,scale);
            float reveal=Ready?1:KeynoteArt.Ease(seconds/1.45),headline=Ready?1:KeynoteArt.Ease((seconds-.12)/.85);
            float width=560*(.88F+.12F*reveal),height=315*(.08F+.92F*reveal);
            KeynoteArt.Photo(g,artwork,new RectangleF(840-width/2,350-height/2,width,height),reveal);
            KeynoteArt.Blocks(g,new RectangleF(605,460,440,90),Ready?KeynoteArt.Duration:seconds,Cyan);
            TextAt(g,"Agent Voice",brand,White,new RectangleF(52,38,360,40));
            TextAt(g,"让声音，\n有你的名字。",title,Fade(White,headline),new RectangleF(96-(1-headline)*28,208,500,151));
            TextAt(g,"本地生成。自由选择。\n从一段声音，开始你的语音工作台。",body,Fade(Color.FromArgb(162,183,199),headline),new RectangleF(100,355,505,64));
            TextAt(g,"软件  /  引擎  /  音色  /  环境",small,Color.FromArgb(101,138,160),new RectangleF(686,564,390,32));
            if(Ready)TextAt(g,"随时返回配置中心，按你的方式继续。",small,Color.FromArgb(101,130,150),new RectangleF(284,646,550,30));
        }
        protected override void Dispose(bool disposing){if(disposing){timer.Stop();timer.Dispose();effects.Dispose();watch.Stop();brand.Dispose();title.Dispose();body.Dispose();small.Dispose();if(motionFont!=null)motionFont.Dispose();if(artwork!=null)artwork.Dispose();}base.Dispose(disposing);}
    }
    public sealed class WelcomeAction:Control {
        readonly WelcomeSurface surface;readonly Font label=new Font("Microsoft YaHei UI",16,FontStyle.Regular,GraphicsUnit.Pixel);bool hover,pressed;
        public bool Primary,TextOnly;public float Reveal=1;
        public WelcomeAction(WelcomeSurface owner){surface=owner;TabStop=true;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.PushButton;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable|ControlStyles.StandardClick,true);}
        protected override AccessibleObject CreateAccessibilityInstance(){return new ActionAccessible(this);}
        sealed class ActionAccessible:ControlAccessibleObject {readonly WelcomeAction owner;public ActionAccessible(WelcomeAction value):base(value){owner=value;}public override string DefaultAction{get{return "按下";}}public override void DoDefaultAction(){owner.PerformClick();}}
        public void PerformClick(){if(Enabled&&Visible)OnClick(EventArgs.Empty);}
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}protected override void OnMouseLeave(EventArgs e){hover=pressed=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left){pressed=true;Focus();}Invalidate();base.OnMouseDown(e);}protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
        protected override bool IsInputKey(Keys k){return (k&Keys.KeyCode)==Keys.Space||(k&Keys.KeyCode)==Keys.Enter||base.IsInputKey(k);}
        protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter){PerformClick();e.Handled=true;}base.OnKeyDown(e);}
        protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
        protected override void OnPaintBackground(PaintEventArgs e){surface.Backdrop(e.Graphics);}
        protected override void OnPaint(PaintEventArgs e){
            if(Width<2||Height<2)return;var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;float scale=Height/(Primary?56F:TextOnly?48F:48F);
            if(!TextOnly)using(var shape=Shapes.Rounded(new RectangleF(1,1,Width-2,Height-2),10*scale)){
                Color color=Primary?(pressed?Color.FromArgb(12,154,190):hover?Color.FromArgb(94,229,248):WelcomeSurface.Cyan):(hover?Color.FromArgb(23,45,63):Color.FromArgb(16,32,48));
                using(var fill=new SolidBrush(Color.FromArgb((int)(255*Reveal),color)))g.FillPath(fill,shape);
            }
            if(Focused&&ShowFocusCues)using(var pen=new Pen(WelcomeSurface.White,1))using(var path=Shapes.Rounded(new RectangleF(3,3,Width-6,Height-6),8*scale))g.DrawPath(pen,path);
            var saved=g.Save();g.ScaleTransform(scale,scale);using(var brush=new SolidBrush(Color.FromArgb((int)(255*Reveal),Primary?WelcomeSurface.Navy:WelcomeSurface.White)))using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})g.DrawString(Text,label,brush,new RectangleF(0,0,Width/scale,Height/scale),format);g.Restore(saved);
        }
        protected override void Dispose(bool disposing){if(disposing)label.Dispose();base.Dispose(disposing);}
    }
}
