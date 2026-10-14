using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed class AnimeButton:Button {
        bool hovering,pressed;
        public AnimeButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);}
        protected override void OnMouseEnter(EventArgs e){hovering=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hovering=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){pressed=true;Invalidate();base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
        static Color Shift(Color color,int amount){return Color.FromArgb(color.A,Math.Max(0,Math.Min(255,color.R+amount)),Math.Max(0,Math.Min(255,color.G+amount)),Math.Max(0,Math.Min(255,color.B+amount)));}
        protected override void OnPaint(PaintEventArgs e){
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;var rect=new Rectangle(1,1,Width-3,Height-3);if(rect.Width<2||rect.Height<2)return;
            Color top=hovering?Shift(BackColor,BackColor.GetBrightness()<.55F?16:-7):BackColor,bottom=Shift(top,BackColor.GetBrightness()<.55F?-8:5);if(pressed){top=Shift(top,-10);bottom=Shift(bottom,-10);}
            using(var shape=Shapes.Rounded(rect,Math.Min(13,Math.Max(6,Height/3))))using(var fill=new LinearGradientBrush(rect,top,bottom,90F)){e.Graphics.FillPath(fill,shape);if(FlatAppearance.BorderSize>0)using(var pen=new Pen(FlatAppearance.BorderColor,FlatAppearance.BorderSize))e.Graphics.DrawPath(pen,shape);}
            var textRect=new Rectangle(Padding.Left,0,Math.Max(1,Width-Padding.Horizontal),Height);TextRenderer.DrawText(e.Graphics,Text,Font,textRect,Enabled?ForeColor:SystemColors.GrayText,TextFormatFlags.VerticalCenter|(TextAlign==ContentAlignment.MiddleLeft?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter)|TextFormatFlags.EndEllipsis);
            if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(5,5,Width-10,Height-10));
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
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(Card&&Host!=null){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=Shapes.Rounded(new Rectangle(1,1,Width-3,Height-3),20)){using(var brush=new SolidBrush(Color.FromArgb(Host.HasBackground?222:248,Host.CardColor)))e.Graphics.FillPath(brush,path);using(var pen=new Pen(Color.FromArgb(82,Host.Accent),1.35F))e.Graphics.DrawPath(pen,path);}using(var accent=new SolidBrush(Color.FromArgb(175,Host.Accent)))e.Graphics.FillEllipse(accent,14,14,7,7);using(var glow=new Pen(Color.FromArgb(85,Host.Accent),2)){e.Graphics.DrawLine(glow,27,17,49,17);e.Graphics.DrawLine(glow,52,17,59,17);}}}
    }
    public sealed class BufferedTable:TableLayoutPanel {
        public BufferedTable(){SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);}
    }
    public sealed class BufferedFlow:FlowLayoutPanel {
        public BufferedFlow(){SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);}
    }
    public static class Shapes {
        public static GraphicsPath Rounded(Rectangle r,int radius){var p=new GraphicsPath();int d=Math.Max(1,radius*2);p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
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
