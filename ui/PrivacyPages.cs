using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentVoice {
    public sealed partial class MainForm {
        readonly List<AnimeButton> readingChoices=new List<AnimeButton>();int readingScopeIndex;
        void SelectReadingScope(int index){readingScopeIndex=index;for(int i=0;i<readingChoices.Count;i++){readingChoices[i].Tag=i==index?"primary":"secondary";if(!Accent.IsEmpty)StyleButton(readingChoices[i]);}}
        async Task ExportForSharing(){
            if(lastJob.Length==0)throw new InvalidOperationException("请先生成一条语音。");
            using(var dialog=new SaveFileDialog{Title="导出音频",Filter="WAV 音频|*.wav",FileName="语音-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".wav"}){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                var submitted=await Service.Request("/api/export/share",new{jobId=lastJob});string ident=Json.Text(submitted,"jobId");
                await WaitJob(ident);var job=await Service.Request("/api/jobs/"+ident);
                if(Json.Text(job,"state")!="completed")throw new InvalidOperationException("音频未生成完成："+Json.Text(job,"error"));
                var result=job["result"] as Dictionary<string,object>;byte[] audio=await Service.Audio(Json.Text(result,"audioUrl"));
                File.WriteAllBytes(dialog.FileName,audio);Status("音频已保存，使用原始生成结果。");
            }
        }
        void BuildPrivacy(){
            var card=SettingsCard("privacy","隐私与连接");
            Add(card,Hint("自动朗读只读取已开启宿主的可见回复。新安装默认关闭；MCP 手工朗读由你主动发起，不受自动范围限制。"));
            Add(card,Row(Btn("朗读开关",()=>ShowPage("harness")),Btn("本地数据说明",()=>ServiceClient.Open(Store.Resolve("docs/本地数据与隐私.md")))));
            Add(card,Hint("自动朗读范围"));var choices=new List<Control>();string[] names={"全部可见回复","指定项目","指定会话"};
            for(int i=0;i<names.Length;i++){int index=i;var button=Btn(names[i],()=>SelectReadingScope(index));button.AutoSize=false;button.Width=160;button.Height=36;readingChoices.Add(button);choices.Add(button);}Add(card,Row(choices.ToArray()));
            PathField(card,"privacyProject","项目目录（项目范围时必填）","",true);Field(card,"privacySession","会话 ID（会话范围时必填）");
            Add(card,Hint("宿主必须提供项目或会话信息；无法确认来源就跳过。不会把范围限制降级为全部读取。"));
            foreach(var item in new[]{new[]{"retainText","保留生成记录中的正文（默认关闭；不影响本次播放与重播）"},new[]{"deleteOnExit","关闭语音服务时清空已完成的生成音频与记录"},new[]{"keepBeyondAge","最新音频可以超过保留期限（旧版保留行为）"}}){var box=new CheckBox{Text=item[1],AutoSize=true};checks[item[0]]=box;Add(card,box);}
            NumberField(card,"logDays","日志保留天数（0 为不按时间清理，仍有大小限制）",0,365,1,0);
            Add(card,Row(Primary(AsyncBtn("保存隐私设置",async()=>{
                var next=Json.Copy(Config);next.privacy.harnessScope=new[]{"all","project","session"}[readingScopeIndex];next.privacy.projectRoot=fields["privacyProject"].Text.Trim();next.privacy.sessionId=fields["privacySession"].Text.Trim();
                if(next.privacy.harnessScope=="project"&&!Directory.Exists(Store.Resolve(next.privacy.projectRoot)))throw new InvalidOperationException("请选择已有项目目录。");
                if(next.privacy.harnessScope=="session"&&next.privacy.sessionId.Length==0)throw new InvalidOperationException("请填写会话 ID。");
                next.privacy.retainText=checks["retainText"].Checked;next.privacy.deleteOnExit=checks["deleteOnExit"].Checked;next.privacy.logRetentionDays=(int)numbers["logDays"].Value;next.cleanup.keepLatestBeyondAge=checks["keepBeyondAge"].Checked;
                await Persist(next,new{privacy=next.privacy,cleanup=next.cleanup});Status("隐私设置已保存。旧记录与备份不会被悄悄改写，可使用下方清理入口。");
            }))));
            Add(card,Row(AsyncBtn("清除已存正文",async()=>{var r=await PrivacyOperation("clear-records");Status("已清除 "+r["removed"]+" 条生成记录中的正文；原音频、模型、台词配置和备份保留。");}),AsyncBtn("清理可删除日志",async()=>{var r=await PrivacyOperation("clear-logs");Status("删除日志 "+r["removed"]+" 个；正在使用的文件可能跳过。备份与训练材料保留。");}),AsyncBtn("预览脱敏报告",PreviewDiagnostics)));
            Add(card,Row(Danger(AsyncBtn("撤销连接并更换密钥",async()=>{
                if(await Service.Running())throw new InvalidOperationException("请先关闭语音服务，再更换密钥。");
                if(MessageBox.Show(this,"旧密钥立即失效，自动朗读与 AIRI 关闭。MCP 需重载，AIRI 需重新接入；账号登录和声音保留。是否继续？","更换本地连接密钥",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
                var r=await PrivacyOperation("rotate");Config=Store.Read();BindSettings();Status(Json.Text(r,"message"));
            }))));
        }
        void BindPrivacy(){if(readingChoices.Count==0)return;SelectReadingScope(Math.Max(0,Array.IndexOf(new[]{"all","project","session"},Config.privacy.harnessScope)));fields["privacyProject"].Text=Config.privacy.projectRoot;fields["privacySession"].Text=Config.privacy.sessionId;checks["retainText"].Checked=Config.privacy.retainText;checks["deleteOnExit"].Checked=Config.privacy.deleteOnExit;checks["keepBeyondAge"].Checked=Config.cleanup.keepLatestBeyondAge;numbers["logDays"].Value=Config.privacy.logRetentionDays;}
        async Task<Dictionary<string,object>> PrivacyOperation(string action,object data=null){
            if(await Service.Running())return await Service.Request("/api/privacy/"+action,data??new{},30000);
            string input=Store.Resolve("data/privacy-request-"+Guid.NewGuid().ToString("N")+".json");Directory.CreateDirectory(Path.GetDirectoryName(input));
            try{File.WriteAllText(input,Json.Encode(data??new{}),new UTF8Encoding(false));return Json.Object(await Service.RunTool("privacy_tools.py","--root "+ServiceClient.Quote(Store.Root)+" --action "+action+" --input "+ServiceClient.Quote(input)));}
            finally{if(File.Exists(input))File.Delete(input);}
        }
        async Task PreviewDiagnostics(){
            var report=await PrivacyOperation("diagnostic");string text=Json.Encode(report);
            using(var dialog=new Form{Text="脱敏报告预览 · 不自动上传",Size=new Size(820,540),StartPosition=FormStartPosition.CenterParent,BackColor=Color.FromArgb(16,17,22),ForeColor=Color.White}){
                var view=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,Dock=DockStyle.Fill,Text=text,BackColor=dialog.BackColor,ForeColor=dialog.ForeColor,Font=new Font("Microsoft YaHei UI",10F)};
                var save=new AnimeButton{Text="保存这份报告",Dock=DockStyle.Bottom,Height=48,BackColor=Color.FromArgb(180,154,255),ForeColor=Color.Black};save.Click+=(s,e)=>{using(var file=new SaveFileDialog{Filter="JSON 报告|*.json",FileName="AgentVoice-脱敏诊断.json"})if(file.ShowDialog(dialog)==DialogResult.OK)File.WriteAllText(file.FileName,text,new UTF8Encoding(false));};dialog.Controls.Add(view);dialog.Controls.Add(save);dialog.ShowDialog(this);
            }
        }

    }
}
