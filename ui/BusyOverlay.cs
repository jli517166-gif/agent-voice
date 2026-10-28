using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed class BusyOverlay:Control {
        readonly Timer timer=new Timer{Interval=40};
        readonly Stopwatch elapsed=Stopwatch.StartNew();
        readonly Bitmap snapshot;
        readonly Image custom;
        readonly MemoryStream imageStream;
        readonly Color accent;
        readonly bool reduced;
        readonly int[] frameDelays;
        string message;
        Button stopButton;
        Button pauseDownload,cancelDownload;Func<DownloadJob> currentDownload;
        public void SetDownloadControl(Func<DownloadJob> current){
            currentDownload=current;
            pauseDownload=new Button{Text="暂停下载",BackColor=Color.FromArgb(46,43,60),ForeColor=Color.White,FlatStyle=FlatStyle.Flat,Visible=false};
            cancelDownload=new Button{Text="停止下载",BackColor=Color.FromArgb(46,43,60),ForeColor=Color.White,FlatStyle=FlatStyle.Flat,Visible=false};
            pauseDownload.FlatAppearance.BorderSize=cancelDownload.FlatAppearance.BorderSize=0;
            pauseDownload.Click+=(s,e)=>{var job=currentDownload();if(job==null)return;if(job.Paused)job.Resume();else job.Pause();Invalidate();};
            cancelDownload.Click+=(s,e)=>{var job=currentDownload();if(job!=null)job.Cancel();Invalidate();};Controls.Add(pauseDownload);Controls.Add(cancelDownload);
        }
        public void SetStopAction(Action action){
            if(stopButton!=null)return;
            stopButton=new Button{Text="停止朗读",AccessibleName="停止朗读",BackColor=Color.FromArgb(46,43,60),ForeColor=Color.White,FlatStyle=FlatStyle.Flat};
            stopButton.FlatAppearance.BorderSize=0;stopButton.Click+=(s,e)=>action();Controls.Add(stopButton);Invalidate();
        }
        public BusyOverlay(string text,Bitmap background,string imagePath,Color? highlight=null,bool reduceMotion=false){
            message=text;snapshot=background;accent=highlight??Color.FromArgb(180,154,255);reduced=reduceMotion;Dock=DockStyle.Fill;TabStop=true;BackColor=Color.FromArgb(16,17,22);
            SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);
            if(!string.IsNullOrWhiteSpace(imagePath)&&File.Exists(imagePath)){
                try{
                    imageStream=new MemoryStream(File.ReadAllBytes(imagePath),false);custom=Image.FromStream(imageStream);
                    if(custom.FrameDimensionsList.Contains(FrameDimension.Time.Guid)){
                        int frames=custom.GetFrameCount(FrameDimension.Time);frameDelays=new int[frames];byte[] delays=null;
                        try{delays=custom.GetPropertyItem(0x5100).Value;}catch(ArgumentException){}
                        for(int i=0;i<frames;i++)frameDelays[i]=delays!=null&&delays.Length>i*4+3?Math.Max(20,BitConverter.ToInt32(delays,i*4)*10):100;
                    }
                }catch{if(custom!=null)custom.Dispose();custom=null;if(imageStream!=null)imageStream.Dispose();imageStream=null;}
            }
            timer.Interval=reduced?200:40;timer.Tick+=(s,e)=>Invalidate();VisibleChanged+=(s,e)=>{if(Visible)timer.Start();else timer.Stop();};timer.Start();
        }
        public void SetMessage(string value){message=value;Invalidate();}
        protected override void OnPaintBackground(PaintEventArgs e){if(snapshot!=null)e.Graphics.DrawImage(snapshot,ClientRectangle);else e.Graphics.Clear(BackColor);}
        protected override void OnPaint(PaintEventArgs e){
            base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;float scale=g.DpiX/96F;
            var download=currentDownload==null?null:currentDownload();bool hasDownload=download!=null;
            Dictionary<string,object> downloadState=null;if(pauseDownload!=null){pauseDownload.Visible=cancelDownload.Visible=hasDownload;if(hasDownload){pauseDownload.Text=download.Paused?"继续下载":"暂停下载";pauseDownload.Enabled=cancelDownload.Enabled=!download.Cancelled;downloadState=Json.Object(Json.Encode(download.Snapshot()));string status=Json.Text(downloadState,"message");if(status.Length>0)message=status;}}
            using(var shade=new SolidBrush(Color.FromArgb(185,9,10,15)))g.FillRectangle(shade,ClientRectangle);
            int width=Math.Min((int)(520*scale),Math.Max(1,Width-(int)(48*scale))),height=Math.Min((int)((stopButton==null&&!hasDownload?270:322)*scale),Math.Max(1,Height-(int)(32*scale)));
            var box=new Rectangle((Width-width)/2,(Height-height)/2,width,height);
            using(var shadow=Shapes.Rounded(new Rectangle(box.X,box.Y+(int)(8*scale),box.Width,box.Height),18))using(var brush=new SolidBrush(Color.FromArgb(40,0,0,0)))g.FillPath(brush,shadow);
            using(var path=Shapes.Rounded(box,18))using(var fill=new SolidBrush(Color.FromArgb(27,28,36)))g.FillPath(fill,path);
            if(stopButton!=null){stopButton.Size=new Size((int)(132*scale),(int)(36*scale));stopButton.Location=new Point(box.X+(width-stopButton.Width)/2,box.Bottom-(int)(54*scale));}
            if(hasDownload){pauseDownload.SetBounds(box.X+(width-(int)(284*scale))/2,box.Bottom-(int)(54*scale),(int)(132*scale),(int)(36*scale));cancelDownload.SetBounds(pauseDownload.Right+(int)(20*scale),pauseDownload.Top,pauseDownload.Width,pauseDownload.Height);}
            var visual=new Rectangle(box.X+(int)(32*scale),box.Y+(int)(30*scale),box.Width-(int)(64*scale),(int)(92*scale));
            if(custom!=null){
                if(!reduced&&frameDelays!=null&&frameDelays.Length>1){long t=elapsed.ElapsedMilliseconds%frameDelays.Sum();int index=0;while(t>=frameDelays[index]&&index<frameDelays.Length-1)t-=frameDelays[index++];custom.SelectActiveFrame(FrameDimension.Time,index);}
                float ratio=Math.Min((float)visual.Width/custom.Width,(float)visual.Height/custom.Height);float w=custom.Width*ratio,h=custom.Height*ratio;g.DrawImage(custom,visual.X+(visual.Width-w)/2,visual.Y+(visual.Height-h)/2,w,h);
            }else{
                float gap=14*scale,bar=5*scale,mid=visual.X+visual.Width/2F;
                for(int i=0;i<11;i++){
                    double phase=reduced?i*.55:elapsed.Elapsed.TotalSeconds*3.8-i*.48;float h=(float)(18+Math.Pow((Math.Sin(phase)+1)/2,1.3)*46)*scale;
                    var rect=new RectangleF(mid+(i-5)*gap-bar/2,visual.Y+(visual.Height-h)/2,bar,h);
                    using(var shape=Shapes.Rounded(rect,bar/2))using(var fill=new SolidBrush(Color.FromArgb(140+(int)(100*(1-Math.Abs(i-5)/6F)),accent)))g.FillPath(fill,shape);
                }
            }
            using(var titleFont=new Font("Microsoft YaHei UI",13F,FontStyle.Bold))
                TextRenderer.DrawText(g,"正在处理",titleFont,new Rectangle(box.X+(int)(24*scale),box.Y+(int)(140*scale),box.Width-(int)(48*scale),(int)(28*scale)),Color.FromArgb(242,242,248),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            using(var textFont=new Font("Microsoft YaHei UI",10.5F))
                TextRenderer.DrawText(g,message??"请稍候，完成后会自动返回。",textFont,new Rectangle(box.X+(int)(28*scale),box.Y+(int)(180*scale),box.Width-(int)(56*scale),(int)(62*scale)),Color.FromArgb(189,190,207),TextFormatFlags.HorizontalCenter|TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis);
            if(hasDownload&&downloadState!=null){double percent;double.TryParse(Json.Text(downloadState,"percent"),out percent);bool known=Convert.ToInt64(downloadState["totalBytes"])>0;var track=new RectangleF(box.X+(int)(36*scale),box.Y+(int)(250*scale),box.Width-(int)(72*scale),(int)(8*scale));using(var shape=Shapes.Rounded(track,track.Height/2))using(var fill=new SolidBrush(Color.FromArgb(65,255,255,255)))g.FillPath(fill,shape);float ratio=known?(float)Math.Max(0,Math.Min(1,percent/100d)):(float)((elapsed.Elapsed.TotalSeconds*.25)%1);var value=new RectangleF(track.X,track.Y,Math.Max(track.Height,track.Width*ratio),track.Height);using(var shape=Shapes.Rounded(value,value.Height/2))using(var fill=new SolidBrush(accent))g.FillPath(fill,shape);using(var font=new Font("Microsoft YaHei UI",9F))TextRenderer.DrawText(g,known?percent.ToString("0.0")+"%":"正在计算总量",font,new Rectangle(box.X,box.Y+(int)(263*scale),box.Width,(int)(22*scale)),Color.FromArgb(210,210,224),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
        }
        protected override void Dispose(bool disposing){if(disposing){timer.Stop();timer.Dispose();if(custom!=null)custom.Dispose();if(imageStream!=null)imageStream.Dispose();if(snapshot!=null)snapshot.Dispose();}base.Dispose(disposing);}
    }
}
