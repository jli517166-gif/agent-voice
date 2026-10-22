using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed class AnimeButton:Control {
        bool hovering,pressed;
        Bitmap surface;
        string surfaceKey;
        public int BorderSize {get;set;}
        public Color BorderColor {get;set;}
        public ContentAlignment TextAlign {get;set;}
        public AnimeButton(){BorderColor=Color.Teal;TextAlign=ContentAlignment.MiddleCenter;TabStop=true;AccessibleRole=AccessibleRole.PushButton;SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw|ControlStyles.Selectable|ControlStyles.StandardClick,true);}
        public override Size GetPreferredSize(Size proposedSize){Size text=TextRenderer.MeasureText(Text,Font);return new Size(Math.Max(MinimumSize.Width,text.Width+Padding.Horizontal+12),Math.Max(MinimumSize.Height,text.Height+Padding.Vertical+16));}
        public void PerformClick(){if(Enabled)OnClick(EventArgs.Empty);}
        protected override AccessibleObject CreateAccessibilityInstance(){return new ButtonAccessibleObject(this);}
        sealed class ButtonAccessibleObject:ControlAccessibleObject {readonly AnimeButton owner;public ButtonAccessibleObject(AnimeButton value):base(value){owner=value;}public override string DefaultAction{get{return "按下";}}public override void DoDefaultAction(){owner.PerformClick();}}
        protected override void Dispose(bool disposing){if(disposing&&surface!=null){surface.Dispose();surface=null;}base.Dispose(disposing);}
        protected override bool IsInputKey(Keys keyData){return (keyData&Keys.KeyCode)==Keys.Space||(keyData&Keys.KeyCode)==Keys.Enter||base.IsInputKey(keyData);}
        protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Space){pressed=true;Invalidate();e.Handled=true;}else if(e.KeyCode==Keys.Enter){PerformClick();e.Handled=true;}base.OnKeyDown(e);}
        protected override void OnKeyUp(KeyEventArgs e){if(e.KeyCode==Keys.Space&&pressed){pressed=false;Invalidate();PerformClick();e.Handled=true;}base.OnKeyUp(e);}
        // Keep a rectangular child window. A native rounded Region leaves stale
        // edge pixels when the parent scrolls or the window is scaled by Windows.
        protected override void OnPaintBackground(PaintEventArgs e){
            MainForm host=FindForm() as MainForm;
            for(Control ancestor=Parent;ancestor!=null;ancestor=ancestor.Parent){
                var card=ancestor as ScenePanel;
                if(card!=null&&card.Card&&card.Host!=null){e.Graphics.Clear(card.Host.CardColor);return;}
                // Rounded corners belong to their actual opaque parent surface,
                // including the fixed sidebar, not the window's photo beneath it.
                if(!(ancestor is ScenePanel)&&!(ancestor is Form)&&ancestor.BackColor.A==255){e.Graphics.Clear(ancestor.BackColor);return;}
            }
            if(host!=null)host.PaintScene(e.Graphics,this);else e.Graphics.Clear(Parent==null?SystemColors.Control:Parent.BackColor);
        }
        protected override void OnMouseEnter(EventArgs e){hovering=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hovering=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left){Focus();pressed=true;}Invalidate();base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
        protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
        protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
        static Color Shift(Color color,int amount){return Color.FromArgb(color.A,Math.Max(0,Math.Min(255,color.R+amount)),Math.Max(0,Math.Min(255,color.G+amount)),Math.Max(0,Math.Min(255,color.B+amount)));}
        static Bitmap RenderSurface(Size size,Color fill,Color stroke,int border){
            const int scale=3;
            using(var large=new Bitmap(size.Width*scale,size.Height*scale,PixelFormat.Format32bppPArgb)){
                using(var g=Graphics.FromImage(large)){
                    g.Clear(Color.Transparent);g.ScaleTransform(scale,scale);g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.None;
                    float inset=Math.Max(.5F,border/2F);var rect=new RectangleF(inset,inset,size.Width-inset*2,size.Height-inset*2);
                    using(var shape=Shapes.Rounded(rect,Math.Min(9,Math.Max(6,size.Height/2F-inset)))){
                        using(var brush=new SolidBrush(fill))g.FillPath(brush,shape);
                        if(border>0)using(var pen=new Pen(stroke,border)){pen.Alignment=PenAlignment.Center;g.DrawPath(pen,shape);}
                    }
                }
                var result=new Bitmap(size.Width,size.Height,PixelFormat.Format32bppPArgb);
                using(var g=Graphics.FromImage(result))using(var attributes=new ImageAttributes()){
                    g.CompositingMode=CompositingMode.SourceCopy;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(large,new Rectangle(Point.Empty,size),0,0,large.Width,large.Height,GraphicsUnit.Pixel,attributes);
                }
                return result;
            }
        }
        protected override void OnPaint(PaintEventArgs e){
            if(Width<4||Height<4)return;
            Color fill=hovering?Shift(BackColor,BackColor.GetBrightness()<.55F?12:-7):BackColor;if(pressed)fill=Shift(fill,-10);
            int border=Focused&&ShowFocusCues?Math.Max(2,BorderSize):BorderSize;Color stroke=BorderColor;
            string key=Width+":"+Height+":"+fill.ToArgb()+":"+stroke.ToArgb()+":"+border;
            if(surface==null||surfaceKey!=key){if(surface!=null)surface.Dispose();surface=RenderSurface(Size,fill,stroke,border);surfaceKey=key;}
            e.Graphics.DrawImageUnscaled(surface,Point.Empty);
            var textRect=new Rectangle(Padding.Left,0,Math.Max(1,Width-Padding.Horizontal),Height);TextRenderer.DrawText(e.Graphics,Text,Font,textRect,Enabled?ForeColor:SystemColors.GrayText,TextFormatFlags.VerticalCenter|(TextAlign==ContentAlignment.MiddleLeft?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter)|TextFormatFlags.EndEllipsis);
        }
    }
    public sealed class ScenePanel:Panel {
        internal void RefreshScrollExtent(){if(AutoScroll)AdjustFormScrollbars(true);}
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
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(Card&&Host!=null){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;var rect=new Rectangle(1,1,Width-3,Height-3);using(var path=Shapes.Rounded(rect,12))using(var fill=new SolidBrush(Host.CardColor))e.Graphics.FillPath(fill,path);}}
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
