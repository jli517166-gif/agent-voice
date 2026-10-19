using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AgentVoice {
    // Keeps the absolute web-derived pages usable below the design width.
    // Captures the layout once at the default window size, then scales every
    // child control and font from that base instead of letting actions clip.
    internal static class WebFit {
        sealed class Base {
            internal Rectangle Bounds;
            internal float FontSize;
            internal FontFamily Family;
            internal FontStyle Style;
            internal Size Minimum;
        }
        internal static void Attach(ScenePanel page){
            if(page.Tag as string=="webfit")return;
            page.Tag="webfit";page.AutoScroll=false;
            var map=new Dictionary<Control,Base>();
            int width=Math.Max(1,page.ClientSize.Width),height=Math.Max(1,page.ClientSize.Height);
            Capture(page,map);
            page.SizeChanged+=(s,e)=>Apply(page,map,width,height);
        }
        static void Capture(Control parent,Dictionary<Control,Base> map){
            foreach(Control child in parent.Controls){
                if(child.Dock!=DockStyle.None)continue;
                map[child]=new Base{Bounds=child.Bounds,FontSize=child.Font.Size,Family=child.Font.FontFamily,Style=child.Font.Style,Minimum=child.MinimumSize};
                Capture(child,map);
            }
        }
        static void Apply(ScenePanel page,Dictionary<Control,Base> map,int baseWidth,int baseHeight){
            if(page.ClientSize.Width<=0||page.ClientSize.Height<=0)return;
            float sx=page.ClientSize.Width/(float)baseWidth,sy=page.ClientSize.Height/(float)baseHeight;
            foreach(var pair in map){
                var control=pair.Key;var b=pair.Value;
                control.MinimumSize=Size.Empty;
                control.SetBounds((int)(b.Bounds.X*sx),(int)(b.Bounds.Y*sy),Math.Max(1,(int)(b.Bounds.Width*sx)),Math.Max(1,(int)(b.Bounds.Height*sy)));
                float size=Math.Max(7f,b.FontSize*Math.Min(sx,sy));
                if(Math.Abs(control.Font.Size-size)>.1f)control.Font=new Font(b.Family,size,b.Style);
            }
        }
    }
}
