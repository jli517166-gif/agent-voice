using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AgentVoice {
    // Local manuals must remain readable on a fresh Windows install with no .md association.
    internal sealed class TextDocumentForm:Form {
        static readonly Dictionary<string,TextDocumentForm> windows=new Dictionary<string,TextDocumentForm>(StringComparer.OrdinalIgnoreCase);
        readonly RichTextBox reader;
        readonly Font body=new Font("Microsoft YaHei UI",11F),heading=new Font("Microsoft YaHei UI",18F,FontStyle.Bold),code=new Font("Consolas",10.5F);
        internal string DocumentPath {get;private set;}
        internal string Content {get{return reader.Text;}}
        internal static bool Supported(string path){return new[]{".md",".txt",".log",".json",".yaml",".yml",".csv"}.Contains(Path.GetExtension(path).ToLowerInvariant());}
        internal static void Open(string path){
            string full=Path.GetFullPath(path);TextDocumentForm existing;
            if(windows.TryGetValue(full,out existing)&&!existing.IsDisposed){existing.WindowState=FormWindowState.Normal;existing.Activate();return;}
            var view=new TextDocumentForm(full);windows[full]=view;view.FormClosed+=(s,e)=>windows.Remove(full);
            if(Form.ActiveForm!=null)view.Show(Form.ActiveForm);else view.Show();
        }
        internal TextDocumentForm(string path){
            DocumentPath=Path.GetFullPath(path);Text=Path.GetFileName(path)+" · Agent Voice";StartPosition=FormStartPosition.CenterParent;Size=new Size(1000,780);MinimumSize=new Size(640,440);AutoScaleMode=AutoScaleMode.Dpi;
            BackColor=Color.FromArgb(16,17,22);ForeColor=Color.FromArgb(235,237,245);Padding=new Padding(26,18,26,18);
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));Controls.Add(layout);
            layout.Controls.Add(new Label{Text=Path.GetFileName(path),Dock=DockStyle.Fill,Font=heading,AutoEllipsis=true},0,0);
            reader=new RichTextBox{Name="manualReader",ReadOnly=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,BackColor=BackColor,ForeColor=ForeColor,Font=body,DetectUrls=true,WordWrap=true,ScrollBars=RichTextBoxScrollBars.Vertical,AccessibleName="手册正文"};layout.Controls.Add(reader,0,1);
            reader.LinkClicked+=(s,e)=>{Uri address;if(Uri.TryCreate(e.LinkText,UriKind.Absolute,out address)&&(address.Scheme=="https"||address.Scheme=="http"))try{ServiceClient.Open(address.AbsoluteUri);}catch(Exception error){MessageBox.Show(this,error.Message,"无法打开链接");}};
            layout.Controls.Add(new Label{Text="本机手册 · Ctrl+C 复制选中文字 · Esc 关闭",Dock=DockStyle.Fill,Font=body,ForeColor=Color.FromArgb(170,174,193),TextAlign=ContentAlignment.BottomLeft},0,2);
            string text=File.ReadAllText(DocumentPath,Encoding.UTF8);
            if(Path.GetExtension(path).Equals(".md",StringComparison.OrdinalIgnoreCase))RenderMarkdown(text);else reader.Text=text;
            reader.Select(0,0);KeyPreview=true;KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape){Close();e.Handled=true;}};
        }
        void RenderMarkdown(string text){
            bool fenced=false;foreach(string raw in text.Replace("\r\n","\n").Split('\n')){
                if(raw.TrimStart().StartsWith("```")){fenced=!fenced;reader.AppendText("\n");continue;}
                string line=raw;bool title=!fenced&&Regex.IsMatch(line,@"^#{1,6}\s");
                if(title)line=Regex.Replace(line,@"^#{1,6}\s+","");
                if(!fenced){line=Regex.Replace(line,@"\[([^\]]+)\]\((https?://[^\s)]+)\)","$1 $2");line=line.Replace("**","").Replace("`","");}
                reader.SelectionFont=fenced?code:title?heading:body;reader.SelectionColor=title?Color.FromArgb(210,194,255):ForeColor;reader.AppendText(line+"\n");
            }
        }
        protected override void Dispose(bool disposing){if(disposing){base.Dispose(true);body.Dispose();heading.Dispose();code.Dispose();}else base.Dispose(false);}
    }
}
