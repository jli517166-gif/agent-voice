using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AgentVoice {
    // One short, silent illustration per route. This is navigation artwork,
    // never a simulated download, device check or connection success indicator.
    public sealed class SetupAnimation:Control {
        readonly Timer timer=new Timer{Interval=33};
        readonly Stopwatch watch=new Stopwatch();
        readonly int step;readonly string title,description;
        readonly Font heading=new Font("Microsoft YaHei UI",23,FontStyle.Bold,GraphicsUnit.Pixel);
        readonly Font copy=new Font("Microsoft YaHei UI",14,FontStyle.Regular,GraphicsUnit.Pixel);
        readonly Font small=new Font("Segoe UI",12,FontStyle.Bold,GraphicsUnit.Pixel);
        public MainForm Host;public bool Reduced {get;private set;}
        double elapsed=1.8;bool routeActive;
        internal bool AnimationRunning {get{return timer.Enabled;}}
        internal int Illustration {get{return step;}}
        internal double Frame {get{return elapsed;}}
        public SetupAnimation(int index,string name,string note){
            step=index;title=name;description=note;AccessibleName="第 "+(index+1)+" 步："+name+"。"+note;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            timer.Tick+=(s,e)=>{elapsed=Math.Min(1.8,watch.Elapsed.TotalSeconds);if(elapsed>=1.8)Stop();Invalidate();};
        }
        public void Motion(bool reduced){Reduced=reduced;if(reduced){elapsed=1.8;Stop();Invalidate();}else if(Visible)Play();}
        public void RouteVisible(bool active){routeActive=active;if(active)Play();else Stop();}
        void Play(){if(Reduced||!Visible||!routeActive)return;elapsed=0;watch.Restart();timer.Start();Invalidate();}
        void Stop(){timer.Stop();watch.Stop();}
        protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(Visible)Play();else Stop();}
        protected override void OnParentChanged(EventArgs e){base.OnParentChanged(e);if(Parent==null)Stop();}
        internal void Preview(double seconds){Stop();elapsed=Math.Max(0,Math.Min(1.8,seconds));Invalidate();}
        protected override void Dispose(bool disposing){if(disposing){timer.Dispose();heading.Dispose();copy.Dispose();small.Dispose();}base.Dispose(disposing);}
        protected override void OnPaintBackground(PaintEventArgs e){if(Host!=null)Host.PaintScene(e.Graphics,this);else e.Graphics.Clear(BackColor);}
        static float Ease(float x){x=Math.Max(0,Math.Min(1,x));return 1-(1-x)*(1-x)*(1-x);}
        protected override void OnPaint(PaintEventArgs e){
            base.OnPaint(e);if(Host==null||Width<80)return;
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            int art=Math.Min(190,Width/3),textWidth=Math.Max(100,Width-art-28);var muted=Color.FromArgb(Host.ForeColor.A,Host.ForeColor.R,Host.ForeColor.G,Host.ForeColor.B);
            TextRenderer.DrawText(g,"SETUP  /  "+(step+1).ToString("00")+" — 08",small,new Rectangle(8,8,textWidth,22),Host.Accent,TextFormatFlags.Left);
            TextRenderer.DrawText(g,title,heading,new Rectangle(6,37,textWidth,37),Host.ForeColor,TextFormatFlags.Left|TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g,description,copy,new Rectangle(8,83,textWidth,47),muted,TextFormatFlags.Left|TextFormatFlags.WordBreak);
            float segment=(Width-16)/8F;for(int n=0;n<8;n++)using(var pen=new Pen(n==step?Host.Accent:Color.FromArgb(45,Host.ForeColor),n==step?3:1))g.DrawLine(pen,8+n*segment,Height-6,8+(n+1)*segment-8,Height-6);
            var state=g.Save();float size=Math.Min(art-18,122);g.TranslateTransform(Width-art/2F-4,66);g.ScaleTransform(size/140,size/140);
            float reveal=Ease((float)(elapsed/1.2)),drift=(1-reveal)*18;g.TranslateTransform(0,drift);
            using(var faint=new Pen(Color.FromArgb(48,Host.Accent),1.5F))g.DrawEllipse(faint,-67,-57,134,114);
            using(var ink=new Pen(Host.Accent,2.7F))using(var soft=new Pen(Color.FromArgb(120,Host.ForeColor),2))using(var fill=new SolidBrush(Host.Accent)){
                ink.StartCap=ink.EndCap=LineCap.Round;soft.StartCap=soft.EndCap=LineCap.Round;
                switch(step){
                    case 0: // Monitor: a sweep settles on the computer.
                        g.DrawRectangle(ink,-43,-30,86,55);g.DrawLine(soft,0,25,0,40);g.DrawLine(ink,-22,40,22,40);
                        g.DrawLine(soft,-34,-22+37*reveal,34,-22+37*reveal);break;
                    case 1: // Processor: pins connect to a central voice engine.
                        g.DrawRectangle(ink,-30,-30,60,60);for(int n=-18;n<=18;n+=18){g.DrawLine(soft,n,-43,n,-31);g.DrawLine(soft,n,31,n,43);g.DrawLine(soft,-43,n,-31,n);g.DrawLine(soft,31,n,43,n);}
                        g.DrawEllipse(ink,-13,-13,26,26);float pulse=3+(1-reveal)*7;g.FillEllipse(fill,-pulse,-pulse,pulse*2,pulse*2);break;
                    case 2: // Environment: layers arrive one after another.
                        for(int n=0;n<3;n++){float y=-30+n*25-(1-Ease((float)(elapsed/.9-n*.18)))*12;g.DrawPolygon(n==1?ink:soft,new[]{new PointF(-38,y),new PointF(0,y-14),new PointF(38,y),new PointF(0,y+14)});}break;
                    case 3: // Voice fingerprint: timbre bars, no microphone requirement.
                        for(int n=0;n<9;n++){float h=10+(float)Math.Sin(n*1.31+1)*8+14;h*=.5F+.5F*reveal;g.DrawLine(n%2==0?ink:soft,-40+n*10,-h,-40+n*10,h);}break;
                    case 4: // Speaker: waves expand and settle.
                        g.DrawPolygon(ink,new[]{new PointF(-40,-12),new PointF(-25,-12),new PointF(-7,-30),new PointF(-7,30),new PointF(-25,12),new PointF(-40,12)});
                        for(int n=0;n<3;n++){float r=(26+n*17)*(.65F+.35F*reveal);g.DrawArc(n==1?ink:soft,-7-r/2,-r/2,r,r,-58,116);}break;
                    case 5: // Harness: a message travels between two endpoints.
                        g.DrawRectangle(soft,-51,-21,32,42);g.DrawRectangle(soft,19,-21,32,42);g.DrawLine(ink,-12,0,12,0);g.FillEllipse(fill,-15+24*reveal,-4,8,8);break;
                    case 6: // AIRI: speech bubble and a restrained character silhouette.
                        g.DrawEllipse(soft,-36,-23,48,48);g.DrawArc(ink,-45,15,67,43,185,170);g.DrawEllipse(ink,11,-40,39,29);g.DrawLine(ink,16,-15,12,-6);g.FillEllipse(fill,-23,-6,3,4);g.FillEllipse(fill,-7,-6,3,4);for(int n=0;n<3;n++)if(elapsed>n*.22)g.FillEllipse(fill,20+n*7,-28,3,3);break;
                    default: // Summary: four modules assembled, not a success checkmark.
                        for(int n=0;n<4;n++){float x=(n%2)*40-35,y=(n/2)*40-35;float arrive=Ease((float)(elapsed/.9-n*.15));g.DrawRectangle(n==0?ink:soft,x+(n%2==0?-1:1)*(1-arrive)*16,y,29,29);}break;
                }
            }
            g.Restore(state);
        }
    }
}
