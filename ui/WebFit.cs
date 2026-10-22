using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.CompilerServices;

namespace AgentVoice {
    // A bounded, DPI-aware content column. Keep type readable; scroll short
    // viewports instead of squeezing the entire page or stretching every card.
    internal static class WebFit {
        internal static readonly Size DesignCanvas=new Size(978,604);
        sealed class Base {
            internal Rectangle Bounds;
            internal float FontSize;
            internal string Family;
            internal FontStyle Style;
            internal Font RenderFont;
        }
        sealed class Layout {internal Dictionary<Control,Base> Map;internal Size Canvas;internal bool Fitting;}
        static readonly ConditionalWeakTable<ScenePanel,Layout> layouts=new ConditionalWeakTable<ScenePanel,Layout>();
        internal static Size CanvasFor(ScenePanel page){Layout layout;return layouts.TryGetValue(page,out layout)?layout.Canvas:DesignCanvas;}
        internal static void Attach(ScenePanel page){
            Layout existing;if(layouts.TryGetValue(page,out existing)){Fit(page,existing);return;}
            page.Tag="webfit";page.AutoScroll=true;
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
            if(layout.Fitting||page.IsDisposed||map.Count==0||page.ClientSize.Width<=0||page.ClientSize.Height<=0)return;
            var host=page.FindForm() as MainForm;
            float dpi=host==null?1F:host.InterfaceScale;
            // Width includes the native scrollbar; ClientSize does not. Reserve
            // it exactly once so showing a scrollbar cannot change the scale.
            int available=Math.Max(1,page.Width-SystemInformation.VerticalScrollBarWidth);
            float scale=Math.Max(.92F*dpi,Math.Min(1.12F*dpi,available/(float)layout.Canvas.Width));
            int inset=Math.Max(0,(available-(int)Math.Ceiling(layout.Canvas.Width*scale))/2);
            layout.Fitting=true;
            page.SuspendLayout();
            try {
                page.AutoScrollPosition=Point.Empty;
                foreach(var pair in map){
                    var control=pair.Key;var b=pair.Value;
                    control.MinimumSize=Size.Empty;control.MaximumSize=Size.Empty;
                    if(control is Label||control is AnimeButton||control is System.Windows.Forms.Button)control.AutoSize=false;
                    control.SetBounds((int)Math.Round(b.Bounds.X*scale)+(control.Parent==page?inset:0),(int)Math.Round(b.Bounds.Y*scale),Math.Max(1,(int)Math.Ceiling(b.Bounds.Width*scale)),Math.Max(1,(int)Math.Ceiling(b.Bounds.Height*scale)));
                    float size=Math.Max(11F,b.FontSize);
                    if(Math.Abs(control.Font.Size-size)>.1f){
                        var old=b.RenderFont;b.RenderFont=new Font(b.Family,size,b.Style);control.Font=b.RenderFont;if(old!=null)old.Dispose();
                    }
                }
                FitLabels(page);
                int documentHeight=(int)Math.Ceiling(layout.Canvas.Height*scale);
                // Give spare height to the writing area, not to buttons or type.
                if(HasSpeechEditor(map)){
                    int extra=Math.Max(0,Math.Min((int)(200*dpi),page.Height-documentHeight-(int)(32*dpi)));
                    foreach(var control in map.Keys){
                        if(control.Name=="speechEditor"||control.Name=="speechEditorCard"||control.Name=="speechQueue")control.Height+=extra;
                        if(control.Name=="speechPhrases"||control.Name=="speechActions")control.Top+=extra;
                    }
                    documentHeight+=extra;
                }
                // Leave room beneath the final action when scrolled to the end.
                // The persistent status row is outside this viewport.
                page.AutoScrollMinSize=new Size((int)Math.Ceiling(layout.Canvas.Width*scale),documentHeight+(int)Math.Ceiling(28*dpi));
            } finally {page.ResumeLayout(true);page.RefreshScrollExtent();layout.Fitting=false;}
            page.Invalidate(true);
        }
        static bool HasSpeechEditor(Dictionary<Control,Base> map){foreach(var control in map.Keys)if(control.Name=="speechEditor")return true;return false;}
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
