using System;
using System.Drawing;
using System.Windows.Forms;

namespace AgentVoice {
    internal sealed class WizardCard:Control {
        internal Image Art;
        internal Action Act;
        bool hovering;
        internal WizardCard(){
            SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
            Cursor=Cursors.Hand;TabStop=true;AccessibleRole=AccessibleRole.PushButton;
        }
        protected override void OnPaint(PaintEventArgs e){
            if(Art!=null)e.Graphics.DrawImage(Art,ClientRectangle);
            if(hovering){using(var brush=new SolidBrush(Color.FromArgb(22,243,245,248)))e.Graphics.FillRectangle(brush,ClientRectangle);}
            if(Focused){using(var pen=new Pen(Color.FromArgb(190,174,148,255),2))e.Graphics.DrawRectangle(pen,1,1,Math.Max(0,Width-3),Math.Max(0,Height-3));}
        }
        protected override void OnMouseEnter(EventArgs e){hovering=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hovering=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnClick(EventArgs e){if(Act!=null)Act();base.OnClick(e);}
        protected override void Dispose(bool disposing){if(disposing&&Art!=null){Art.Dispose();Art=null;}base.Dispose(disposing);}
    }
}
