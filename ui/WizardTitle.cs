using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AgentVoice {
    // Large first-run title. It reveals from the vertical centre of its own
    // layout box, so the text never jumps and the page does not reflow.
    internal sealed class WizardTitle:Control {
        internal const double Duration=1.0;
        readonly Timer timer=new Timer{Interval=16};
        readonly Stopwatch watch=new Stopwatch();
        double time=Duration;
        bool reduced;
        internal WizardTitle(){
            SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor,true);
            BackColor=Color.Transparent;ForeColor=Color.FromArgb(243,245,248);
            Font=new Font("Microsoft YaHei UI",28,FontStyle.Bold);
            AccessibleRole=AccessibleRole.StaticText;AccessibleName="配置页标题";
            timer.Tick+=(s,e)=>{time=Math.Min(Duration,watch.Elapsed.TotalSeconds);if(time>=Duration)timer.Stop();Invalidate();};
        }
        internal void Start(bool reduce){
            Stop();reduced=reduce;time=reduce?Duration:0;if(!reduce){watch.Restart();timer.Start();}Invalidate();
        }
        internal void Preview(){Stop();time=Duration;Invalidate();}
        internal void Stop(){timer.Stop();watch.Stop();}
        protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Invalidate();}
        protected override void OnPaint(PaintEventArgs e){
            base.OnPaint(e);if(string.IsNullOrEmpty(Text))return;
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            float progress=reduced?1f:(float)Math.Min(1,time/Duration);
            float reveal=1f-(float)Math.Pow(1-progress,3);
            float height=Math.Max(1,Height*reveal);
            var clip=new RectangleF(0,(Height-height)/2f,Width,height);
            var old=g.Clip;g.SetClip(clip);
            using(var brush=new SolidBrush(ForeColor))
            using(var format=new StringFormat{Alignment=StringAlignment.Near,LineAlignment=StringAlignment.Center,FormatFlags=0,Trimming=StringTrimming.None})
                g.DrawString(Text,Font,brush,new RectangleF(0,0,Width,Height),format);
            g.Clip=old;
            if(progress>.82f){
                float line=Width*.42f*Math.Min(1,(progress-.82f)/.18f);
                using(var pen=new Pen(Color.FromArgb(200,174,148,255),2f))
                    g.DrawLine(pen,0,Height-4,line,Height-4);
            }
        }
        protected override void Dispose(bool disposing){if(disposing){timer.Dispose();watch.Stop();}base.Dispose(disposing);}
    }
}

