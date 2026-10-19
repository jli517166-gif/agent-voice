using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace AgentVoice {
    internal sealed class WizardArt:Control {
        internal const double Duration=.96;
        readonly Timer timer=new Timer{Interval=16};readonly Stopwatch watch=new Stopwatch();
        internal readonly int Step;
        double time=Duration;bool reduced;
        internal bool Running {get{return timer.Enabled;}}
        internal WizardArt(int step){Step=step;SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);BackColor=Color.FromArgb(11,13,18);timer.Tick+=(s,e)=>{time=Math.Min(Duration,watch.Elapsed.TotalSeconds);if(time>=Duration)timer.Stop();Invalidate();};AccessibleRole=AccessibleRole.Graphic;AccessibleName="配置步骤示意动画";}
        internal void Start(bool reduce){Stop();reduced=reduce;time=reduce?Duration:0;if(!reduce){watch.Restart();timer.Start();}Invalidate();}
        internal void Stop(){timer.Stop();watch.Stop();}
        internal void Preview(double t){Stop();time=t;Invalidate();}
        static float E(double t){t=Math.Max(0,Math.Min(1,t));return (float)(1-Math.Pow(1-t,3));}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;float reveal=E(time/.5),settle=E((time-.16)/.68);float cx=Width/2f,cy=Height/2f;g.TranslateTransform(cx,cy+(1-reveal)*10);float s=Math.Min(1.1f,Math.Max(.65f,Height/132f));g.ScaleTransform(s,s);
            Color cyan=Color.FromArgb(243,245,248),blue=Color.FromArgb(174,148,255);using(var dim=new Pen(Color.FromArgb(30,cyan),1))g.DrawEllipse(dim,-55,-55,110,110);
            for(int n=0;n<19;n++){float x=(n-9)*13;float amp=(float)(9+17*Math.Abs(Math.Sin(n*.67+Step*.5)));float pulse=reduced||time>=Duration?0:(float)Math.Sin(time*10-n*.25)*6*(1-settle);float h=Math.Max(4,(amp+pulse)*E((time-n*.012)/.5));using(var b=new SolidBrush(n%3==0?blue:cyan)){float alpha=Step==0||Step==4||Step==5?1:.18f;b.Color=Color.FromArgb((int)(220*reveal*alpha),b.Color);for(float y=0;y<h;y+=7){g.FillRectangle(b,x-3,30-y,6,4);g.FillRectangle(b,x-3,37+y,6,4);}}}
            using(var p=new Pen(Color.FromArgb((int)(255*reveal),cyan),2.4f))using(var white=new Pen(Color.FromArgb((int)(245*settle),Color.White),2.4f)){
                switch(Step){
                    case 1:g.DrawRectangle(p,-35,-37,70,45);g.DrawLine(white,-18,15,18,15);g.DrawLine(white,0,8,0,15);g.DrawLines(white,new[]{new Point(-15,-18),new Point(-3,-6),new Point(18,-27)});break;
                    case 2:g.DrawRectangle(p,-25,-35,50,50);for(int k=0;k<4;k++){g.DrawLine(white,-34,-29+k*12,-25,-29+k*12);g.DrawLine(white,25,-29+k*12,34,-29+k*12);}g.DrawRectangle(white,-12,-22,24,24);break;
                    case 3:for(int k=0;k<3;k++){float y=-39+k*20;g.DrawRectangle(k==2?white:p,-35,y,70,14);g.DrawLine(white,22,y+7,27,y+7);}break;
                    case 4:for(int k=0;k<7;k++){float h=13+24*(float)Math.Abs(Math.Sin(k*.9));g.DrawLine(k%2==0?p:white,(k-3)*9,-h,(k-3)*9,h-19);}break;
                    case 5:g.DrawEllipse(p,-32,-42,64,64);g.DrawPolygon(white,new[]{new Point(-7,-26),new Point(-7,7),new Point(17,-9)});break;
                    case 6:g.DrawRectangle(p,-65,-29,42,34);g.DrawRectangle(white,23,-29,42,34);g.DrawLine(p,-21,-12,21,-12);using(var dot=new SolidBrush(Color.FromArgb((int)(210*settle),cyan)))for(int k=0;k<3;k++){int x=-14+k*14;g.FillEllipse(dot,x-2,-14,4,4);}break;
                    case 7:g.DrawArc(p,-37,-40,74,58,35,290);g.DrawLine(p,-27,3,-30,18);g.DrawLine(p,-30,18,-10,6);g.DrawEllipse(white,-16,-17,3,3);g.DrawEllipse(white,13,-17,3,3);g.DrawArc(white,-9,-14,18,18,20,140);break;
                    case 8:g.DrawEllipse(p,-35,-43,70,70);g.DrawLines(white,new[]{new Point(-17,-8),new Point(-3,5),new Point(20,-22)});break;
                    default:for(int k=0;k<9;k++){float h=14+32*(float)Math.Abs(Math.Sin(k*.8));g.DrawLine(k%3==0?white:p,(k-4)*10,-h,(k-4)*10,h-28);}break;
                }
            }
        }
        protected override void Dispose(bool disposing){if(disposing){timer.Dispose();watch.Stop();}base.Dispose(disposing);}
    }
    internal sealed class WizardTransition:Control {
        internal const double Duration=.48;
        readonly Timer timer=new Timer{Interval=16};readonly Stopwatch watch=new Stopwatch();
        Bitmap previous,incoming;readonly Action finished;readonly int direction;readonly string kind;readonly double speed;
        internal WizardTransition(Bitmap before,Bitmap after,int sign,Action done,string kind="quick",double playbackSpeed=1){previous=before;incoming=after;finished=done;direction=sign;this.kind=kind;speed=playbackSpeed;Dock=DockStyle.Fill;TabStop=false;SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);timer.Tick+=(s,e)=>{if(watch.Elapsed.TotalSeconds*speed>=Duration){Complete();return;}Invalidate();};MouseDown+=(s,e)=>Complete();}
        internal void Begin(){watch.Start();timer.Start();}
        internal void Complete(){if(IsDisposed)return;timer.Stop();var done=finished;Dispose();if(done!=null)done();}
        static float Ease(double t){t=Math.Max(0,Math.Min(1,t));return (float)(1-Math.Pow(1-t,3));}
        internal static void PaintFrame(Graphics g,Bitmap before,Bitmap after,Rectangle rect,double time,int direction,string kind="quick"){
            g.Clear(Color.FromArgb(11,13,18));
            if(kind=="b1"){
                DrawLayer(g,before,rect,1f);
                float ease=Ease(time/.38);float x=rect.Right-rect.Width*ease;
                using(var path=new GraphicsPath()){
                    path.AddLine(rect.Right,rect.Top,x+rect.Width*.16f,rect.Top);
                    path.AddBezier(x+rect.Width*.16f,rect.Top,x+rect.Width*.03f,rect.Top+rect.Height*.35f,x,rect.Top+rect.Height*.7f,x,rect.Bottom);
                    path.AddLine(x,rect.Bottom,rect.Right,rect.Bottom);path.CloseFigure();
                    var old=g.Clip;g.SetClip(path);g.DrawImage(after,rect);g.Clip=old;
                    using(var edge=new Pen(Color.FromArgb(210,174,148,255),1.4f))g.DrawPath(edge,path);
                }
                return;
            }
            if(kind=="glass"){
                DrawLayer(g,before,rect,1f);
                float alpha=Ease((time-.06)/.4);
                DrawLayer(g,after,rect,alpha);
                using(var pen=new Pen(Color.FromArgb((int)(90*alpha),243,245,248),1f))g.DrawEllipse(pen,rect.X+rect.Width*.18f,rect.Y+rect.Height*.16f,rect.Width*.64f,rect.Height*.68f);
                return;
            }
            float exit=Ease(time/.29),enter=Ease((time-.12)/.36);
            DrawLayer(g,before,new Rectangle(rect.X-(int)(22*exit)*direction,rect.Y-(int)(3*Math.Sin(exit*Math.PI)),rect.Width,rect.Height),1-exit);
            DrawLayer(g,after,new Rectangle(rect.X+(int)(26*(1-enter))*direction,rect.Y+(int)(4*Math.Sin(enter*Math.PI)),rect.Width,rect.Height),enter);
        }
        static void DrawLayer(Graphics g,Image image,Rectangle rect,float opacity){if(opacity<=0)return;using(var attributes=new ImageAttributes()){var matrix=new ColorMatrix();matrix.Matrix33=opacity;attributes.SetColorMatrix(matrix);g.DrawImage(image,rect,0,0,image.Width,image.Height,GraphicsUnit.Pixel,attributes);}}
        protected override void OnPaint(PaintEventArgs e){PaintFrame(e.Graphics,previous,incoming,ClientRectangle,watch.Elapsed.TotalSeconds*speed,direction,kind);}
        protected override void Dispose(bool disposing){if(disposing){timer.Dispose();watch.Stop();if(previous!=null){previous.Dispose();previous=null;}if(incoming!=null){incoming.Dispose();incoming=null;}}base.Dispose(disposing);}
    }
}
