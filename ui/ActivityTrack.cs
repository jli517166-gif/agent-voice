using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace AgentVoice {
    // An activity indicator, not a simulated completion percentage.
    internal sealed class ActivityTrack:Control {
        readonly Timer timer=new Timer{Interval=40};readonly Stopwatch elapsed=Stopwatch.StartNew();bool reduced;
        internal bool ReducedMotion {get{return reduced;}set{reduced=value;UpdateTimer();Invalidate();}}
        internal ActivityTrack(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);BackColor=Color.FromArgb(16,17,22);timer.Tick+=(s,e)=>Invalidate();VisibleChanged+=(s,e)=>UpdateTimer();}
        void UpdateTimer(){if(Visible&&!reduced)timer.Start();else timer.Stop();}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);int y=Height/2;using(var track=new Pen(Color.FromArgb(48,44,62),3))e.Graphics.DrawLine(track,0,y,Width,y);int segment=Math.Max(12,Width/5);float phase=reduced?.5F:(float)((Math.Sin(elapsed.Elapsed.TotalSeconds*2.5)+1)/2);int left=(int)((Width-segment)*phase);using(var signal=new Pen(Color.FromArgb(180,154,255),3))e.Graphics.DrawLine(signal,left,y,left+segment,y);}
        protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    }
}
