using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentVoice {
    // A native task scene for first-run checks and preparation. It is deliberately
    // separate from the page body: the page stays readable underneath, the task
    // owns progress, cancellation state and the completion mark.
    internal sealed class WizardTaskForm:Form {
        readonly string[] steps;
        readonly Func<IProgress<string>,Task<string>> operation;
        readonly bool reduced;
        readonly Label status;
        readonly Button close;
        readonly ActivityTrack progress;
        readonly Timer timer=new Timer{Interval=900};
        readonly TaskMark mark;
        int index;
        internal string Result="";
        internal Exception Error;
        internal bool Finished;

        internal WizardTaskForm(string title,string[] steps,Func<IProgress<string>,Task<string>> operation,bool reduced){
            this.steps=steps;this.operation=operation;this.reduced=reduced;
            Text=title;FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;BackColor=Color.FromArgb(16,17,22);ForeColor=Color.FromArgb(240,240,248);ClientSize=new Size(780,560);Padding=new Padding(38,30,38,24);KeyPreview=true;
            var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,BackColor=BackColor};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,60));root.RowStyles.Add(new RowStyle(SizeType.Absolute,104));root.RowStyles.Add(new RowStyle(SizeType.Absolute,24));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,60));Controls.Add(root);
            root.Controls.Add(new Label{Text=title,Dock=DockStyle.Fill,Font=new Font("Microsoft YaHei UI",20,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true},0,0);
            var list=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,BackColor=BackColor,AutoScroll=false};root.Controls.Add(list,0,1);
            for(int i=0;i<steps.Length;i++)list.Controls.Add(new Label{Text="○  "+steps[i],Tag=i,AutoSize=true,Margin=new Padding(0,2,0,2),ForeColor=Color.FromArgb(120,122,140),Font=new Font("Microsoft YaHei UI",10)});
            progress=new ActivityTrack{Dock=DockStyle.Fill,ReducedMotion=reduced,Height=4,Visible=true};root.Controls.Add(progress,0,2);
            var body=new Panel{Dock=DockStyle.Fill,BackColor=BackColor};root.Controls.Add(body,0,3);
            mark=new TaskMark{Dock=DockStyle.Right,Width=180,Visible=false};body.Controls.Add(mark);
            status=new Label{Dock=DockStyle.Fill,Text="正在准备…",TextAlign=ContentAlignment.TopLeft,ForeColor=Color.FromArgb(170,174,193),AutoEllipsis=false,Padding=new Padding(0,12,190,8),Font=new Font("Microsoft YaHei UI",10)};body.Controls.Add(status);status.BringToFront();
            close=new Button{Text="请稍候",Dock=DockStyle.Right,Width=140,Height=42,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(180,154,255),ForeColor=Color.FromArgb(10,12,20),Visible=false,Enabled=false};close.FlatAppearance.BorderSize=0;close.Click+=(s,e)=>Close();var foot=new Panel{Dock=DockStyle.Fill,BackColor=BackColor};root.Controls.Add(foot,0,4);foot.Controls.Add(close);
            timer.Tick+=(s,e)=>{if(index<steps.Length-1)index++;Highlight();};
            Shown+=async (s,e)=>await Run();
            FormClosing+=(s,e)=>{if(!close.Visible)e.Cancel=true;};
        }

        async Task Run(){
            Highlight();
            if(!reduced)timer.Start();
            try{
                Result=await operation(new Progress<string>(text=>{if(!IsDisposed)status.Text=text;}));
                timer.Stop();index=steps.Length-1;Highlight();
                status.Text=Result;
                progress.Visible=false;mark.Visible=true;mark.Done=true;mark.Invalidate();
                close.Text="完成";close.Enabled=true;close.Visible=true;close.Focus();Finished=true;
            }catch(Exception error){
                timer.Stop();progress.Visible=false;Error=error;status.Text=error.Message;status.ForeColor=Color.FromArgb(255,150,160);close.Text="关闭";close.Enabled=true;close.Visible=true;close.Focus();Finished=true;
            }
        }

        void Highlight(){
            var list=Controls.Count>0?Controls[0] as TableLayoutPanel:null;
            if(list==null||list.GetControlFromPosition(0,1)==null)return;
            var flow=list.GetControlFromPosition(0,1) as FlowLayoutPanel;if(flow==null)return;
            for(int i=0;i<flow.Controls.Count;i++){
                var label=flow.Controls[i] as Label;if(label==null)continue;
                bool done=i<index,current=i==index;
                label.Text=(done?"●  ":current?"◐  ":"○  ")+steps[i];
                label.ForeColor=done?Color.FromArgb(180,154,255):current?Color.FromArgb(240,240,248):Color.FromArgb(120,122,140);
            }
        }

        sealed class TaskMark:Panel {
            internal bool Done;
            internal TaskMark(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);BackColor=Color.FromArgb(16,17,22);}
            protected override void OnPaint(PaintEventArgs e){
                base.OnPaint(e);if(!Done)return;var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
                float cx=Width/2f,cy=Height/2f;float s=Math.Min(Width,Height)/180f;g.TranslateTransform(cx,cy);g.ScaleTransform(s,s);
                using(var pen=new Pen(Color.FromArgb(235,240,248),1.5f))using(var accent=new Pen(Color.FromArgb(190,170,255),1.2f)){
                    g.DrawPolygon(pen,new[]{new PointF(0,-46),new PointF(46,0),new PointF(0,46),new PointF(-46,0)});
                    g.DrawPolygon(accent,new[]{new PointF(0,-68),new PointF(68,0),new PointF(0,68),new PointF(-68,0)});
                    g.DrawLine(pen,-22,0,0,-26);g.DrawLine(accent,-22,0,0,26);g.DrawLine(pen,0,-26,26,22);
                }
            }
        }
    }
}
