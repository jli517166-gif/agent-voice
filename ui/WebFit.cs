using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AgentVoice {
    // Keeps the absolute web-derived pages usable below the design width.
    // Every page is laid out at the default window size first, so the first
    // real page size becomes the shared design base; after that each page is
    // scaled from that base instead of letting cards and buttons clip.
    internal static class WebFit {
        // The web design canvas inside the content area (see
        // assets/workspace/report.md: 978 x 604). Pages are created before the
        // form has a handle and report the default 200 x 100 panel size, so a
        // smaller observation is never a real layout pass.
        const int DesignWidth=978,DesignHeight=604;
        sealed class Base {
            internal Rectangle Bounds;
            internal float FontSize;
            internal FontFamily Family;
            internal FontStyle Style;
            internal Size Minimum;
        }
        static Size reference;
        internal static void Attach(ScenePanel page){
            if(page.Tag as string=="webfit")return;
            page.Tag="webfit";page.AutoScroll=false;
            var map=new Dictionary<Control,Base>();
            Capture(page,map);
            page.SizeChanged+=(s,e)=>Fit(page,map);
            Fit(page,map);
        }
        static void Fit(ScenePanel page,Dictionary<Control,Base> map){
            if(page.IsDisposed||map.Count==0)return;
            var size=page.ClientSize;
            if(size.Width<=0||size.Height<=0)return;
            if(reference.IsEmpty){
                if(size.Width<DesignWidth||size.Height<DesignHeight)return;
                reference=size;
            }
            Apply(page,map,reference.Width,reference.Height);FitLabels(page);
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
            // One uniform zoom for both axes and the font: a per-axis stretch
            // would re-wrap copy and change which lines fit.
            float zoom=Math.Min(sx,sy);
            foreach(var pair in map){
                var control=pair.Key;var b=pair.Value;
                control.MinimumSize=Size.Empty;
                control.SetBounds((int)Math.Round(b.Bounds.X*zoom),(int)Math.Round(b.Bounds.Y*zoom),Math.Max(1,(int)Math.Ceiling(b.Bounds.Width*zoom)),Math.Max(1,(int)Math.Ceiling(b.Bounds.Height*zoom)));
                float size=Math.Max(4.5f,b.FontSize*zoom*0.95f);
                if(Math.Abs(control.Font.Size-size)>.1f)control.Font=new Font(b.Family,size,b.Style);
            }
            // Resizing children alone can leave stale label surfaces behind;
            // force one synchronous repaint of the whole scaled page.
            page.Invalidate(true);page.Update();
        }
        static void FitLabels(Control parent){
            foreach(Control child in parent.Controls){
                var label=child as Label;
                if(label!=null&&!label.AutoSize){
                    int needed=TextRenderer.MeasureText(label.Text,label.Font,new Size(Math.Max(1,label.Width),0),TextFormatFlags.WordBreak).Height;
                    if(label.Height<needed)label.Height=needed;
                }
                FitLabels(child);
            }
        }
    }
}
