using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed class BusyOverlay:Control {
        readonly Timer timer=new Timer{Interval=45};
        readonly Bitmap snapshot;
        readonly Image custom;
        readonly MemoryStream imageStream;
        readonly int customFrames=1;
        string message;
        int frame;
        public BusyOverlay(string text,Bitmap background,string imagePath){
            message=text;snapshot=background;Dock=DockStyle.Fill;TabStop=true;
            SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);
            if(!string.IsNullOrWhiteSpace(imagePath)&&File.Exists(imagePath))try{imageStream=new MemoryStream(File.ReadAllBytes(imagePath),false);custom=Image.FromStream(imageStream);customFrames=Math.Max(1,custom.GetFrameCount(FrameDimension.Time));}catch{if(imageStream!=null)imageStream.Dispose();imageStream=null;}
            timer.Tick+=(s,e)=>{frame++;Invalidate();};timer.Start();
        }
        public void SetMessage(string value){message=value;Invalidate();}
        protected override void OnPaintBackground(PaintEventArgs e){if(snapshot!=null)e.Graphics.DrawImage(snapshot,ClientRectangle);else base.OnPaintBackground(e);}
        protected override void OnPaint(PaintEventArgs e){
            base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shade=new SolidBrush(Color.FromArgb(112,3,18,30)))e.Graphics.FillRectangle(shade,ClientRectangle);
            int width=Math.Min(480,Math.Max(280,Width-48)),height=230;var box=new Rectangle((Width-width)/2,(Height-height)/2,width,height);
            using(var path=Shapes.Rounded(box,18))using(var fill=new SolidBrush(Color.FromArgb(245,7,25,42)))using(var border=new Pen(Color.FromArgb(220,11,159,216),2)){e.Graphics.FillPath(fill,path);e.Graphics.DrawPath(border,path);}
            var visual=new Rectangle(box.X+width/2-58,box.Y+26,116,116);
            if(custom!=null){if(customFrames>1)custom.SelectActiveFrame(FrameDimension.Time,frame%customFrames);e.Graphics.DrawImage(custom,visual);}
            else{
                for(int i=0;i<10;i++){double angle=(i+frame*.24)*Math.PI*2/10;float alpha=(i+frame)%10/9f;int x=(int)(visual.X+visual.Width/2+Math.Cos(angle)*38),y=(int)(visual.Y+visual.Height/2+Math.Sin(angle)*38);using(var brush=new SolidBrush(Color.FromArgb(70+(int)(185*alpha),11,159,216)))e.Graphics.FillEllipse(brush,x-7,y-7,14,14);}
                using(var pen=new Pen(Color.FromArgb(235,206,246,255),4)){for(int i=0;i<6;i++){float x=visual.X+24+i*14;float wave=(float)Math.Sin((frame+i)*.55);float h=18+Math.Abs(wave)*42;e.Graphics.DrawLine(pen,x,visual.Y+58-h/2,x,visual.Y+58+h/2);}}
            }
            var textBox=new Rectangle(box.X+20,box.Bottom-66,box.Width-40,44);TextRenderer.DrawText(e.Graphics,message??"正在处理…",new Font(Font.FontFamily,11F,FontStyle.Bold),textBox,Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        }
        protected override void Dispose(bool disposing){if(disposing){timer.Stop();timer.Dispose();if(custom!=null)custom.Dispose();if(imageStream!=null)imageStream.Dispose();if(snapshot!=null)snapshot.Dispose();}base.Dispose(disposing);}
    }
}
