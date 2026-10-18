using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed class WelcomeForm:Form {
        public readonly WelcomeSurface Surface;
        public string Choice {get;private set;}
        public bool ReduceMotion {get{return Surface.ReduceMotion;}}
        public WelcomeForm(bool reduced,bool animate=true){
            Text="Agent Voice · 由你定调";AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
            ClientSize=new Size(1120,720);MinimumSize=new Size(840,580);StartPosition=FormStartPosition.CenterScreen;BackColor=WelcomeSurface.Navy;
            Surface=new WelcomeSurface(reduced,animate){Dock=DockStyle.Fill};Controls.Add(Surface);
            Surface.Choose+=value=>{Choice=value;DialogResult=DialogResult.OK;Close();};
            KeyPreview=true;KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape){Surface.Skip();e.Handled=true;}};
            Shown+=(s,e)=>Surface.Start();
        }
    }
    public sealed class WelcomeSurface:Panel {
        public static readonly Color Navy=Color.FromArgb(5,12,22),Cyan=Color.FromArgb(34,211,238),White=Color.FromArgb(239,247,252);
        readonly Timer timer=new Timer{Interval=16};readonly Stopwatch watch=new Stopwatch();
        readonly Font brand=new Font("Segoe UI",22,FontStyle.Bold,GraphicsUnit.Pixel),title=new Font("Microsoft YaHei UI",46,FontStyle.Bold,GraphicsUnit.Pixel),body=new Font("Microsoft YaHei UI",17,FontStyle.Regular,GraphicsUnit.Pixel),small=new Font("Microsoft YaHei UI",13,FontStyle.Regular,GraphicsUnit.Pixel);
        readonly bool animate;double seconds;bool reduced;Font motionFont;
        public event Action<string> Choose;
        public readonly WelcomeAction StartButton,ImportButton,HomeButton,SkipButton;
        public readonly CheckBox MotionChoice;
        public bool ReduceMotion {get{return reduced;}}
        public bool Ready {get{return seconds>=3.4;}}
        internal bool AnimationRunning {get{return timer.Enabled;}}
        float ScaleFactor {get{return Math.Max(.1F,Math.Min(ClientSize.Width/1120F,ClientSize.Height/720F));}}
        public WelcomeSurface(bool reduceMotion,bool play){
            animate=play;reduced=reduceMotion;seconds=reduceMotion?3.4:0;
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
        public void Start(){if(!animate||reduced||Ready){if(reduced)Skip();return;}watch.Start();timer.Start();}
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
            if(Ready){
                TextAt(g,"AGENT VOICE",brand,White,new RectangleF(52,38,360,40));
                using(var pen=new Pen(Cyan,3))g.DrawLine(pen,100,242,138,242);
                TextAt(g,"由你定调",title,White,new RectangleF(96,270,540,80));
                TextAt(g,"选择引擎、音色和连接方式，\n也可以稍后配置。",body,Color.FromArgb(162,183,199),new RectangleF(100,353,500,65));
                Wave(g,815,354,202,82,1,0);TextAt(g,"软件  /  引擎  /  音色  /  环境",small,Color.FromArgb(101,138,160),new RectangleF(686,490,380,32));
                TextAt(g,"随时返回配置中心，按你的方式继续。",small,Color.FromArgb(101,130,150),new RectangleF(284,646,550,30));
                return;
            }
            float time=(float)seconds;
            if(time<.5F){float width=8+215*Ease(time/.5F);using(var glow=new Pen(Color.FromArgb(26,Cyan),9))g.DrawLine(glow,560-width,332,560+width,332);using(var pen=new Pen(Cyan,2))g.DrawLine(pen,560-width,332,560+width,332);}
            else if(time<1.3F){float phase=(time-.5F)/.8F;Wave(g,560,332,224,90*(float)Math.Sin(phase*Math.PI),1,phase*6);}
            else if(time<2.2F){float phase=Ease((time-1.3F)/.9F);Wave(g,560,332,224*(1-phase),38*(1-phase),1-phase,0);for(int i=0;i<4;i++){float angle=(float)(i*Math.PI/2-Math.PI/4),radius=154*(1-phase)+32;float x=560+(float)Math.Cos(angle)*radius,y=332+(float)Math.Sin(angle)*radius;Module(g,x,y,i,1);}}
            else {
                float merge=Ease((time-2.2F)/.6F),transition=Ease((time-2.8F)/.6F);float x=560+(52-560)*transition,y=332+(48-332)*transition;
                if(transition<1)for(int i=0;i<4;i++)Module(g,560+(i%2==0?-22:22),280+(i<2?-22:22),i,1-transition);
                float size=1-.45F*transition;var saved=g.Save();g.TranslateTransform(x,y);g.ScaleTransform(size,size);TextAt(g,"AGENT VOICE",title,Fade(White,merge),new RectangleF(-182*(1-transition),-22,600,80));g.Restore(saved);
                if(time<2.8F)using(var pen=new Pen(Fade(Cyan,1-merge),2))g.DrawLine(pen,370+380*merge,292,370+380*merge,368);
                if(transition>0){
                    using(var pen=new Pen(Fade(Cyan,transition),3))g.DrawLine(pen,100,242,138,242);
                    TextAt(g,"由你定调",title,Fade(White,transition),new RectangleF(96,270+(1-transition)*10,540,80));
                    TextAt(g,"选择引擎、音色和连接方式，\n也可以稍后配置。",body,Fade(Color.FromArgb(162,183,199),transition),new RectangleF(100,353,500,65));
                    Wave(g,815,354,202,82,transition,0);
                }
            }
            if(time<2.8F)TextAt(g,"软件  ·  引擎  ·  音色  ·  环境",small,Color.FromArgb(102,141,164),new RectangleF(444,475,420,32));
        }
        void Module(Graphics g,float x,float y,int kind,float opacity){
            using(var pen=new Pen(Fade(kind%2==0?Cyan:White,opacity),1.8F)){
                if(kind==0){g.DrawRectangle(pen,x-17,y-13,34,26);g.DrawLine(pen,x-17,y-5,x+17,y-5);}
                else if(kind==1){g.DrawRectangle(pen,x-12,y-12,24,24);for(int k=-1;k<=1;k++){g.DrawLine(pen,x+k*8,y-18,x+k*8,y-12);g.DrawLine(pen,x+k*8,y+12,x+k*8,y+18);}}
                else if(kind==2){for(int k=-2;k<=2;k++)g.DrawLine(pen,x+k*6,y-(15-Math.Abs(k)*4),x+k*6,y+(15-Math.Abs(k)*4));}
                else {g.DrawLine(pen,x-16,y+13,x,y-15);g.DrawLine(pen,x,y-15,x+16,y+13);g.DrawLine(pen,x+16,y+13,x-16,y+13);}
            }
        }
        void Wave(Graphics g,float cx,float cy,float halfWidth,float amplitude,float opacity,float phase){
            if(halfWidth<1)return;var points=new PointF[121];for(int i=0;i<points.Length;i++){float v=i/120F*2-1;float envelope=(float)Math.Exp(-v*v*5);float pulse=(float)(Math.Sin(v*16+phase)+.38*Math.Sin(v*31-phase));points[i]=new PointF(cx+v*halfWidth,cy+pulse*amplitude*envelope);}
            using(var glow=new Pen(Fade(Cyan,opacity*.05F),13))g.DrawLines(glow,points);using(var glow=new Pen(Fade(Cyan,opacity*.12F),6))g.DrawLines(glow,points);using(var pen=new Pen(Fade(Cyan,opacity),2))g.DrawLines(pen,points);
        }
        protected override void Dispose(bool disposing){if(disposing){timer.Stop();timer.Dispose();watch.Stop();brand.Dispose();title.Dispose();body.Dispose();small.Dispose();if(motionFont!=null)motionFont.Dispose();}base.Dispose(disposing);}
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
