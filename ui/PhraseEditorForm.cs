using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AgentVoice {
    internal sealed class PhraseEditorForm:Form {
        readonly TextBox editor;
        internal List<string> Phrases {get;private set;}
        internal static List<string> Parse(string text){
            var items=text.Replace("\r\n","\n").Split('\n').Select(line=>line.Trim()).Where(line=>line.Length>0).Distinct().ToList();
            if(items.Count>200)throw new InvalidOperationException("常用句最多保存 200 条。");
            if(items.Any(line=>line.Length>4000))throw new InvalidOperationException("每条常用句不能超过 4000 字。");
            return items;
        }
        internal PhraseEditorForm(IEnumerable<string> items,string draft){
            Text="常用句管理";Font=new Font("Microsoft YaHei UI",11F);AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(640,450);MinimumSize=new Size(480,360);StartPosition=FormStartPosition.CenterParent;
            BackColor=Color.FromArgb(22,23,30);ForeColor=Color.White;Padding=new Padding(22);
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));Controls.Add(layout);
            layout.Controls.Add(new Label{Text="每行一句，可直接编辑或删除。空行和重复句会自动合并。",Dock=DockStyle.Fill},0,0);
            editor=new TextBox{Name="phraseEditor",Multiline=true,AcceptsReturn=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BackColor=Color.FromArgb(32,33,42),ForeColor=ForeColor,Text=string.Join(Environment.NewLine,items),MaxLength=1000000};layout.Controls.Add(editor,0,1);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,10,0,0)};layout.Controls.Add(actions,0,2);
            var save=new AnimeButton{Text="保存",Name="savePhrases",Width=92,Height=38,BackColor=Color.FromArgb(180,154,255),ForeColor=Color.FromArgb(18,16,26)};save.Click+=(s,e)=>{try{Phrases=Parse(editor.Text);DialogResult=DialogResult.OK;}catch(Exception error){MessageBox.Show(this,error.Message,"请检查常用句");}};actions.Controls.Add(save);
            var cancel=new AnimeButton{Text="取消",Width=92,Height=38,BackColor=Color.FromArgb(43,44,54)};cancel.Click+=(s,e)=>{DialogResult=DialogResult.Cancel;};actions.Controls.Add(cancel);
            KeyPreview=true;KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape){DialogResult=DialogResult.Cancel;e.Handled=true;}};
            var add=new AnimeButton{Text="加入当前输入",Width=150,Height=38,BackColor=Color.FromArgb(43,44,54),Enabled=!string.IsNullOrWhiteSpace(draft)};add.Click+=(s,e)=>{editor.AppendText((editor.Text.Length==0?"":Environment.NewLine)+draft.Trim());};actions.Controls.Add(add);
        }
    }
}
