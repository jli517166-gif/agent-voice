using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed class SetupAnimation:Control {
        readonly Timer timer=new Timer{Interval=33};readonly Stopwatch watch=new Stopwatch();
        readonly int step;readonly string description;Font heading;
        readonly Font brand=new Font("Bahnschrift",17,FontStyle.Bold,GraphicsUnit.Pixel),copy=new Font("Microsoft YaHei UI",15,FontStyle.Regular,GraphicsUnit.Pixel),small=new Font("Microsoft YaHei UI",13,FontStyle.Regular,GraphicsUnit.Pixel);
        readonly string[] stories={"声音的旅程，\n从这里开始。","先选引擎，\n再定义声音。","就位，\n为声音蓄力。","找到，\n属于你的声音。","现在，\n听见它。","每段对话，\n都能被听见。","让桌宠，\n也能开口。","接下来，\n由你定调。"};
        public MainForm Host;public bool Reduced {get;private set;}
        double elapsed=KeynoteArt.Duration;bool routeActive;
        internal bool AnimationRunning {get{return timer.Enabled;}}
        internal int Illustration {get{return step;}}
        internal double Frame {get{return elapsed;}}
        public SetupAnimation(int index,string name,string note){
            step=index;description=note;AccessibleName=index<8?"第 "+(index+1)+" 步："+name+"。"+note:"Agent Voice 本地语音工作台";
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            timer.Tick+=(s,e)=>{elapsed=Math.Min(KeynoteArt.Duration,watch.Elapsed.TotalSeconds);if(elapsed>=KeynoteArt.Duration)Stop();Invalidate();};FitType();
        }
        public void Motion(bool reduced){Reduced=reduced;if(reduced){elapsed=KeynoteArt.Duration;Stop();Invalidate();}else if(Visible)Play();}
        public void RouteVisible(bool active){routeActive=active;if(active)Play();else Stop();}
        void Play(){if(Reduced||!Visible||!routeActive)return;elapsed=0;watch.Restart();timer.Start();Invalidate();}
        void Stop(){timer.Stop();watch.Stop();}
        protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(Visible)Play();else Stop();}
        protected override void OnParentChanged(EventArgs e){base.OnParentChanged(e);if(Parent==null)Stop();}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);FitType();}
        void FitType(){float size=step>=8?Math.Max(28,Math.Min(40,Width/19F)):Math.Max(32,Math.Min(56,Width/8.4F));if(heading!=null&&Math.Abs(heading.Size-size)<.1F)return;if(heading!=null)heading.Dispose();heading=new Font("Microsoft YaHei UI",size,FontStyle.Bold,GraphicsUnit.Pixel);}
        internal void Preview(double seconds){Stop();elapsed=Math.Max(0,Math.Min(KeynoteArt.Duration,seconds));Invalidate();}
        protected override void Dispose(bool disposing){if(disposing){timer.Dispose();if(heading!=null)heading.Dispose();brand.Dispose();copy.Dispose();small.Dispose();}base.Dispose(disposing);}
        protected override void OnPaintBackground(PaintEventArgs e){if(Host!=null&&Host.HasBackground)Host.PaintScene(e.Graphics,this);else e.Graphics.Clear(Host!=null&&Host.BackColor.GetBrightness()>.5F?Host.BackColor:Color.Black);}
        protected override void OnPaint(PaintEventArgs e){
            base.OnPaint(e);if(Host==null||Width<80||heading==null)return;var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            if(step>=8){
                var art=new RectangleF(Width*.39F,-Height*.39F,Width*.66F,Height*1.86F);KeynoteArt.Photo(g,Host.KeynoteArtwork,art,KeynoteArt.Ease((elapsed-.16)/1.4));
                KeynoteArt.Blocks(g,new RectangleF(Width*.54F,Height*.72F,Width*.39F,Height*.24F),elapsed,Host.Accent);DrawCopy(g,"让每句话，\n都有你的声音。",heading,Host.ForeColor,new RectangleF(18,14,Width*.57F,Height-12),KeynoteArt.Ease(elapsed/.7));return;
            }
            DrawCopy(g,"Agent Voice",brand,Host.ForeColor,new RectangleF(22,19,Math.Max(100,Width-44),28),1);
            DrawCopy(g,(step+1).ToString("00")+" / 08",small,Host.Accent,new RectangleF(22,55,150,24),1);
            float titleY=Math.Max(94,Height*.17F),titleHeight=heading.Size*2.65F;
            float enter=KeynoteArt.Ease((elapsed-.06)/.85),offset=(1-enter)*32;
            DrawCopy(g,stories[step],heading,Host.ForeColor,new RectangleF(18-offset,titleY,Width-38,titleHeight),enter);
            float descriptionY=titleY+titleHeight+12;DrawCopy(g,description,copy,Host.ForeColor,new RectangleF(23,descriptionY,Width-48,52),KeynoteArt.Ease((elapsed-.28)/.8));
            float artY=Math.Max(descriptionY+40,Height*.47F);KeynoteArt.Draw(g,new RectangleF(-16,artY,Width+32,Math.Max(100,Height-artY-32)),step,elapsed,Host.KeynoteArtwork,Host.Accent);
            float length=Math.Max(20,(Width-52)/8F);for(int n=0;n<8;n++)using(var pen=new Pen(n==step?Host.Accent:Color.FromArgb(52,Host.ForeColor),n==step?3:1))g.DrawLine(pen,24+n*length,Height-14,24+(n+1)*length-8,Height-14);
        }
        static void DrawCopy(Graphics g,string text,Font font,Color color,RectangleF area,float opacity){using(var brush=new SolidBrush(Color.FromArgb((int)(255*Math.Max(0,Math.Min(1,opacity))),color)))g.DrawString(text,font,brush,area);}
    }
}
