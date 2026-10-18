using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AgentVoice {
    // Large presentation artwork. These scenes explain a step, not its result.
    internal static class KeynoteArt {
        public const double Duration=2.8;
        public static float Ease(double x){float v=(float)Math.Max(0,Math.Min(1,x));return 1-(1-v)*(1-v)*(1-v)*(1-v);}
        public static void Photo(Graphics g,Image image,RectangleF bounds,float alpha){
            if(image==null||bounds.Width<1||bounds.Height<1)return;
            using(var attributes=new ImageAttributes()){var matrix=new ColorMatrix();matrix.Matrix33=Math.Max(0,Math.Min(1,alpha));attributes.SetColorMatrix(matrix);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(image,Rectangle.Round(bounds),0,0,image.Width,image.Height,GraphicsUnit.Pixel,attributes);}
        }
        static void Glow(Graphics g,Color color,RectangleF r,int alpha){using(var path=new GraphicsPath()){path.AddEllipse(r);using(var brush=new PathGradientBrush(path)){brush.CenterColor=Color.FromArgb(alpha,color);brush.SurroundColors=new[]{Color.FromArgb(0,color)};g.FillPath(brush,path);}}}
        static void Face(Graphics g,PointF[] points,Color first,Color second){using(var brush=new LinearGradientBrush(new RectangleF(0,0,1000,600),first,second,75))g.FillPolygon(brush,points);using(var pen=new Pen(Color.FromArgb(110,170,222,255),1.1F))g.DrawPolygon(pen,points);}
        static void Slab(Graphics g,float x,float y,float width,float height,float depth,Color color){
            var top=new[]{new PointF(x,y),new PointF(x+width,y-height*.35F),new PointF(x+width+height*.38F,y+height*.62F),new PointF(x+height*.38F,y+height)};
            Face(g,new[]{top[3],top[2],new PointF(top[2].X,top[2].Y+depth),new PointF(top[3].X,top[3].Y+depth)},Color.FromArgb(5,41,87),color);
            Face(g,new[]{top[1],top[2],new PointF(top[2].X,top[2].Y+depth),new PointF(top[1].X,top[1].Y+depth)},Color.FromArgb(5,19,31),Color.FromArgb(24,75,113));
            Face(g,top,Color.FromArgb(225,239,248),color);
        }
        static void Label(Graphics g,string text,float x,float y,float size,Color color){using(var font=new Font("Microsoft YaHei UI",size,FontStyle.Bold,GraphicsUnit.Pixel))using(var brush=new SolidBrush(color))g.DrawString(text,font,brush,x,y);}
        public static void Blocks(Graphics g,RectangleF area,double time,Color accent){
            float beat=1-Ease((time-1.65)/.95);using(var fill=new SolidBrush(accent)){
                int columns=32;float pitch=area.Width/columns;
                for(int n=0;n<columns;n++){
                    double envelope=Math.Exp(-Math.Pow((n-15.5)/12,2));double level=2+envelope*(3+5*Math.Abs(Math.Sin(n*.39+time*5.5))*beat);int rows=(int)level;
                    for(int row=0;row<rows;row++){fill.Color=Color.FromArgb((int)(70+120*(1-row/10F)),accent);float height=Math.Max(3,area.Height/11);g.FillRectangle(fill,area.X+n*pitch,area.Bottom-(row+1)*(height+3),Math.Max(2,pitch-5),height);}
                }
            }
        }
        public static void Draw(Graphics graphics,RectangleF area,int step,double time,Image artwork,Color accent){
            if(area.Width<5||area.Height<5)return;var state=graphics.Save();graphics.TranslateTransform(area.X,area.Y);graphics.ScaleTransform(area.Width/1000,area.Height/600);var g=graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            float arrive=Ease((time-.25)/1.45),drift=(1-arrive)*65;g.TranslateTransform(0,drift);
            Glow(g,accent,new RectangleF(0,310,1000,230),95);
            using(var light=new Pen(Color.FromArgb(170,accent),2.2F))using(var white=new Pen(Color.FromArgb(155,224,245,255),2))using(var dot=new SolidBrush(accent)){
                switch(step){
                    case 0:
                        Face(g,new[]{new PointF(143,76),new PointF(780,27),new PointF(780,374),new PointF(143,423)},Color.FromArgb(21,51,74),Color.FromArgb(3,9,19));
                        if(artwork!=null){var screen=g.Save();g.TranslateTransform(163,100);Photo(g,artwork,new RectangleF(0,0,597,308),arrive);g.Restore(screen);}
                        Face(g,new[]{new PointF(143,423),new PointF(780,374),new PointF(904,450),new PointF(288,509)},Color.FromArgb(201,219,230),Color.FromArgb(8,42,70));
                        g.DrawLine(light,287,494,901,436);break;
                    case 1:
                        for(int n=0;n<3;n++){float lift=(1-Ease((time-.18-n*.16)/1.2))*100;float x=95+n*235,y=115+n*25-lift;Slab(g,x,y,195,205,26,n==1?accent:Color.FromArgb(35,107,185));Label(g,n==0?"CosyVoice":n==1?"v5 Turbo":"ProPlus",x+27,y+74,25,Color.FromArgb(5,23,42));}break;
                    case 2:
                        for(int n=2;n>=0;n--){float shift=(1-Ease((time-.2-n*.16)/1.3))*95;Slab(g,190,180+n*83-shift,470,155,19,n==1?accent:Color.FromArgb(41,113,171));}
                        for(int n=0;n<5;n++){float x=280+n*89;g.DrawLine(white,x,390,x,390-105*arrive);}break;
                    case 3:
                        Photo(g,artwork,new RectangleF(-120,-45,1240,697),arrive);break;
                    case 4:
                        Photo(g,artwork,new RectangleF(-85,0,1170,658),arrive);
                        for(int n=0;n<3;n++){float w=500+n*125+45*arrive;g.DrawArc(n==1?light:white,500-w/2,380-n*16,w,95+n*21,0,180);}break;
                    case 5:
                        float reach=Ease((time-.35)/1.4);Slab(g,65,185,265,210,20,Color.FromArgb(47,122,184));Slab(g,610,130,245,210,20,accent);
                        Label(g,"Codex",110,260,38,Color.FromArgb(2,20,38));Label(g,"DeepSeek",648,206,34,Color.FromArgb(2,20,38));
                        using(var path=new GraphicsPath()){path.AddBezier(365,328,490,110,535,440,650,277);g.DrawPath(white,path);}g.FillEllipse(dot,372+235*reach,276-(float)Math.Sin(reach*Math.PI)*66,17,17);break;
                    case 6:
                        Glow(g,accent,new RectangleF(130,-30,710,580),130);
                        using(var circle=new LinearGradientBrush(new Rectangle(235,95,415,310),Color.FromArgb(209,241,255),Color.FromArgb(0,85,166),40))g.FillEllipse(circle,235,95,415,310);
                        g.DrawArc(white,255,114,373,273,190,180);for(int n=0;n<3;n++){float y=(1-Ease((time-.4-n*.15)/.7))*35;g.FillEllipse(dot,353+n*63,222+y,27,27);}
                        Face(g,new[]{new PointF(342,371),new PointF(390,393),new PointF(319,450)},Color.FromArgb(207,240,254),accent);break;
                    default:
                        for(int n=0;n<4;n++){float enter=Ease((time-.2-n*.13)/1.1),x=135+(n%2)*395+(n%2==0?-90:90)*(1-enter),y=110+(n/2)*178;Slab(g,x,y,230,135,18,n==1?accent:Color.FromArgb(33,108,172));Label(g,new[]{"引擎","音色","环境","连接"}[n],x+58,y+43,34,Color.FromArgb(3,22,41));}break;
                }
            }
            Blocks(g,new RectangleF(100,415,800,125),time,accent);
            graphics.Restore(state);
        }
    }
}
