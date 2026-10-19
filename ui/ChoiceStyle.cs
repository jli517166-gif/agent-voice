using System.Drawing;
using System.Windows.Forms;

namespace AgentVoice {
    internal static class ChoiceStyle {
        internal static void Apply(ComboBox choice){choice.FlatStyle=FlatStyle.Flat;choice.DrawMode=DrawMode.OwnerDrawFixed;choice.DrawItem-=Draw;choice.DrawItem+=Draw;}
        static void Draw(object sender,DrawItemEventArgs e){
            var choice=(ComboBox)sender;bool selected=(e.State&DrawItemState.Selected)!=0;Color fill=selected?Color.FromArgb(64,55,87):choice.BackColor;
            using(var brush=new SolidBrush(fill))e.Graphics.FillRectangle(brush,e.Bounds);
            string text=e.Index>=0&&e.Index<choice.Items.Count?choice.GetItemText(choice.Items[e.Index]):"尚未选择";
            TextRenderer.DrawText(e.Graphics,text,e.Font,new Rectangle(e.Bounds.X+8,e.Bounds.Y,e.Bounds.Width-12,e.Bounds.Height),choice.ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            if((e.State&DrawItemState.Focus)!=0)e.DrawFocusRectangle();
        }
    }
}
