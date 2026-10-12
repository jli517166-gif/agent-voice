using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace AgentVoice {
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
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(Card&&Host!=null){using(var path=Shapes.Rounded(new Rectangle(0,0,Width-1,Height-1),16)){using(var brush=new SolidBrush(Color.FromArgb(Host.HasBackground?210:245,Host.CardColor)))e.Graphics.FillPath(brush,path);using(var pen=new Pen(Color.FromArgb(45,Host.Accent)))e.Graphics.DrawPath(pen,path);}}}
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
