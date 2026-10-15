using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed class AnimeButton:Button {
        bool hovering,pressed;
        public AnimeButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor,true);}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(Width<3||Height<3)return;using(var shape=Shapes.Rounded(new RectangleF(0,0,Width,Height),Math.Min(15,Math.Max(7,Height/2F)))){var previous=Region;Region=new Region(shape);if(previous!=null)previous.Dispose();}}
        protected override void OnMouseEnter(EventArgs e){hovering=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hovering=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){pressed=true;Invalidate();base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
        static Color Shift(Color color,int amount){return Color.FromArgb(color.A,Math.Max(0,Math.Min(255,color.R+amount)),Math.Max(0,Math.Min(255,color.G+amount)),Math.Max(0,Math.Min(255,color.B+amount)));}
        protected override void OnPaint(PaintEventArgs e){
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;e.Graphics.CompositingQuality=CompositingQuality.HighQuality;var rect=new RectangleF(.6F,.6F,Width-1.2F,Height-1.2F);if(rect.Width<2||rect.Height<2)return;
            Color top=hovering?Shift(BackColor,BackColor.GetBrightness()<.55F?16:-7):BackColor,bottom=Shift(top,BackColor.GetBrightness()<.55F?-8:5);if(pressed){top=Shift(top,-10);bottom=Shift(bottom,-10);}
            using(var shape=Shapes.Rounded(rect,Math.Min(15,Math.Max(7,Height/2F))))using(var fill=new LinearGradientBrush(rect,top,bottom,90F)){e.Graphics.FillPath(fill,shape);int border=Focused?Math.Max(2,FlatAppearance.BorderSize):FlatAppearance.BorderSize;if(border>0)using(var pen=new Pen(Focused?Shift(FlatAppearance.BorderColor,22):FlatAppearance.BorderColor,border)){pen.Alignment=PenAlignment.Inset;e.Graphics.DrawPath(pen,shape);}}
            var textRect=new Rectangle(Padding.Left,0,Math.Max(1,Width-Padding.Horizontal),Height);TextRenderer.DrawText(e.Graphics,Text,Font,textRect,Enabled?ForeColor:SystemColors.GrayText,TextFormatFlags.VerticalCenter|(TextAlign==ContentAlignment.MiddleLeft?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter)|TextFormatFlags.EndEllipsis);
        }
    }
    public sealed class ScenePanel:Panel {
        public MainForm Host;
        public bool Card;
        public bool ComposeChildren;
        public ScenePanel(){SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.ResizeRedraw,true);}
        protected override CreateParams CreateParams {
            get {var parameters=base.CreateParams;if(ComposeChildren)parameters.ExStyle|=0x02000000;return parameters;}
        }
        protected override void OnScroll(ScrollEventArgs e){
            base.OnScroll(e);
            // AutoScroll reuses moved pixels. A window-anchored backdrop must be
            // repainted at the new child coordinates, including transparent descendants.
            Invalidate(true);
        }
        protected override void OnPaintBackground(PaintEventArgs e){if(Host==null){base.OnPaintBackground(e);return;}Host.PaintScene(e.Graphics,this);}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(Card&&Host!=null){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;var rect=new Rectangle(1,1,Width-3,Height-3);using(var path=Shapes.Rounded(rect,18)){Color surface=Color.FromArgb(Host.HasBackground?228:250,Host.CardColor);using(var fill=new LinearGradientBrush(rect,surface,Shift(surface,Host.BackColor.GetBrightness()<.4F?5:-3),90F))e.Graphics.FillPath(fill,path);using(var pen=new Pen(Color.FromArgb(118,Host.Accent),1.2F))e.Graphics.DrawPath(pen,path);}}}
        static Color Shift(Color color,int amount){return Color.FromArgb(color.A,Math.Max(0,Math.Min(255,color.R+amount)),Math.Max(0,Math.Min(255,color.G+amount)),Math.Max(0,Math.Min(255,color.B+amount)));}
    }
    public sealed class BufferedTable:TableLayoutPanel {
        public BufferedTable(){SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);}
    }
    public sealed class BufferedFlow:FlowLayoutPanel {
        public BufferedFlow(){SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);}
    }
    public static class Shapes {
        public static GraphicsPath Rounded(Rectangle r,int radius){var p=new GraphicsPath();int d=Math.Max(1,radius*2);p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
        public static GraphicsPath Rounded(RectangleF r,float radius){var p=new GraphicsPath();float d=Math.Max(1,radius*2);p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    }
    public static class Icons {
        public static string Make(string source,string directory){
            Directory.CreateDirectory(directory);string target=Path.Combine(directory,"custom.ico");
            using(var image=Image.FromFile(source))using(var output=new MemoryStream())using(var writer=new BinaryWriter(output)){
                int[] sizes={16,24,32,48,64,128,256};var blocks=new byte[sizes.Length][];
                for(int n=0;n<sizes.Length;n++){int size=sizes[n];using(var bmp=new Bitmap(size,size,PixelFormat.Format32bppArgb))using(var g=Graphics.FromImage(bmp)){
                    g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.Clear(Color.Transparent);
                    using(var clip=Shapes.Rounded(new Rectangle(0,0,size,size),Math.Max(3,size/5))){g.SetClip(clip);float scale=Math.Max((float)size/image.Width,(float)size/image.Height);float w=image.Width*scale,h=image.Height*scale;g.DrawImage(image,(size-w)/2,(size-h)/2,w,h);}
                    using(var stream=new MemoryStream()){bmp.Save(stream,ImageFormat.Png);blocks[n]=stream.ToArray();}
                }}
                writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;
                for(int n=0;n<sizes.Length;n++){writer.Write((byte)(sizes[n]==256?0:sizes[n]));writer.Write((byte)(sizes[n]==256?0:sizes[n]));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(blocks[n].Length);writer.Write(offset);offset+=blocks[n].Length;}
                foreach(var block in blocks)writer.Write(block);writer.Flush();File.WriteAllBytes(target,output.ToArray());
            }return target;
        }
        public static void Shortcut(string exe,string icon){
            Type type=Type.GetTypeFromProgID("WScript.Shell");dynamic shell=Activator.CreateInstance(type);
            try {dynamic link=shell.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"agent语音.lnk"));link.TargetPath=exe;link.WorkingDirectory=Path.GetDirectoryName(exe);link.IconLocation=icon+",0";link.Description="agent语音";link.Save();System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);}
            finally {System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
        }
    }
}
