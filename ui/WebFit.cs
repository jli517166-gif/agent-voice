using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.CompilerServices;

namespace AgentVoice {
    // Map the design canvas to available space. Text uses its native point size
    // on large windows; enlarging coordinates must not magnify fonts a second time.
    internal static class WebFit {
        internal static readonly Size DesignCanvas=new Size(978,604);
        sealed class Base {
            internal Rectangle Bounds;
            internal float FontSize;
            internal string Family;
            internal FontStyle Style;
            internal Font RenderFont;
        }
        sealed class Layout {internal Dictionary<Control,Base> Map;internal Size Canvas;}
        static readonly ConditionalWeakTable<ScenePanel,Layout> layouts=new ConditionalWeakTable<ScenePanel,Layout>();
        internal static Size CanvasFor(ScenePanel page){Layout layout;return layouts.TryGetValue(page,out layout)?layout.Canvas:DesignCanvas;}
        internal static void Attach(ScenePanel page){
            Layout existing;if(layouts.TryGetValue(page,out existing))return;
            page.Tag="webfit";page.AutoScroll=false;
            var map=new Dictionary<Control,Base>();Capture(page,map);
            int right=DesignCanvas.Width,bottom=DesignCanvas.Height;
            foreach(var pair in map)if(pair.Key.Parent==page){right=Math.Max(right,pair.Value.Bounds.Right);bottom=Math.Max(bottom,pair.Value.Bounds.Bottom);}
            if(bottom>DesignCanvas.Height)bottom+=12;
            var layout=new Layout{Map=map,Canvas=new Size(right,bottom)};layouts.Add(page,layout);
            page.SizeChanged+=(s,e)=>Fit(page,layout);
            page.Disposed+=(s,e)=>{foreach(var b in map.Values)if(b.RenderFont!=null)b.RenderFont.Dispose();layouts.Remove(page);};
            Fit(page,layout);
        }
        static void Capture(Control parent,Dictionary<Control,Base> map){
            foreach(Control child in parent.Controls){
                if(child.Dock!=DockStyle.None)continue;
                map[child]=new Base{Bounds=child.Bounds,FontSize=child.Font.Size,Family=child.Font.FontFamily.Name,Style=child.Font.Style};
                Capture(child,map);
            }
        }
        static void Fit(ScenePanel page,Layout layout){
            var map=layout.Map;
            if(page.IsDisposed||map.Count==0||page.ClientSize.Width<=0||page.ClientSize.Height<=0)return;
            float sx=page.ClientSize.Width/(float)layout.Canvas.Width,sy=page.ClientSize.Height/(float)layout.Canvas.Height;
            float textScale=Math.Min(1F,Math.Min(sx,sy));
            page.SuspendLayout();
            try {
                foreach(var pair in map){
                    var control=pair.Key;var b=pair.Value;
                    control.MinimumSize=Size.Empty;control.MaximumSize=Size.Empty;
                    if(control is Label||control is System.Windows.Forms.Button)control.AutoSize=false;
                    control.SetBounds((int)Math.Round(b.Bounds.X*sx),(int)Math.Round(b.Bounds.Y*sy),Math.Max(1,(int)Math.Ceiling(b.Bounds.Width*sx)),Math.Max(1,(int)Math.Ceiling(b.Bounds.Height*sy)));
                    float size=Math.Max(4.5f,b.FontSize*textScale*.95f);
                    if(Math.Abs(control.Font.Size-size)>.1f){
                        var old=b.RenderFont;b.RenderFont=new Font(b.Family,size,b.Style);control.Font=b.RenderFont;if(old!=null)old.Dispose();
                    }
                }
                FitLabels(page);
            } finally {page.ResumeLayout(false);}
            page.Invalidate(true);
        }
        static void FitLabels(Control parent){
            foreach(Control child in parent.Controls){
                var label=child as Label;
                if(label!=null&&!label.AutoSize&&!label.AutoEllipsis){
                    int needed=TextRenderer.MeasureText(label.Text,label.Font,new Size(Math.Max(1,label.Width),0),TextFormatFlags.WordBreak).Height;
                    if(label.Height<needed)label.Height=needed;
                }
                FitLabels(child);
            }
        }
    }
}
