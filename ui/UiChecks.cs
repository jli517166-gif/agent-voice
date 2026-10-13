using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace AgentVoice {
    public sealed partial class MainForm {
        static string FileDigest(string path){using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        static bool SameVoiceParameters(VoiceConfig a,VoiceConfig b){return typeof(VoiceConfig).GetFields().All(field=>object.Equals(field.GetValue(a),field.GetValue(b)));}
        bool SameVoiceAssets(VoiceConfig expected,VoiceConfig actual){
            var comparable=actual.Clone();
            foreach(string key in new[]{"model","index","reference"}){
                var field=typeof(VoiceConfig).GetField(key);string oldPath=(string)field.GetValue(expected),newPath=(string)field.GetValue(actual);
                if(oldPath.Length==0||newPath.Length==0){if(oldPath!=newPath)return false;}
                else if(FileDigest(Store.Resolve(oldPath))!=FileDigest(Store.Resolve(newPath)))return false;
                field.SetValue(comparable,oldPath);
            }
            return SameVoiceParameters(expected,comparable);
        }
        public async Task<object> VerifyBundle(string file,string directory){
            Directory.CreateDirectory(directory);var original=Store.Read();var results=new List<string>();
            Func<Task> wait=async()=>{for(int i=0;i<24000&&operation;i++)await Task.Delay(100);if(operation)throw new InvalidOperationException("模型传输按钮超时");};
            try{
                configurationTestPath=file;ShowPage("configuration");if(!File.Exists(file)){exportConfigurationButton.PerformClick();await wait();if(lastTransfer==null||!Convert.ToBoolean(lastTransfer["ok"])||Json.Text(lastTransfer,"engine")!="cosyvoice")throw new InvalidOperationException("本地模型导出失败："+footer.Text);results.Add("native engine-labelled model export");}else results.Add("previous native export reused for import recheck");
                var changed=Json.Copy(original);changed.voice.engine="sapi";changed.voice.synthesisMode="rvc";changed.voice.advancedEnabled=true;Store.Write(changed);Config=changed;BindVoice();BindSettings();
                lastTransfer=null;importConfigurationButton.PerformClick();await wait();if(lastTransfer==null||!Convert.ToBoolean(lastTransfer["ok"]))throw new InvalidOperationException("模型导入失败："+footer.Text);
                if(Config.voice.engine!="cosyvoice"||Config.voice.synthesisMode!="base"||Config.voice.advancedEnabled!=original.voice.advancedEnabled)throw new InvalidOperationException("本地声音设置未恢复");
                if(FileDigest(Store.Resolve(original.voice.reference))!=FileDigest(Store.Resolve(Config.voice.reference)))throw new InvalidOperationException("参考录音字节改变");
                string oldModel=Store.Resolve(original.voice.baseModel.Length>0?original.voice.baseModel:original.engines.cosyModel),newModel=Store.Resolve(Config.voice.baseModel);
                foreach(string name in new[]{"llm.pt","flow.pt","hift.pt"})if(FileDigest(Path.Combine(oldModel,name))!=FileDigest(Path.Combine(newModel,name)))throw new InvalidOperationException("模型权重校验失败："+name);
                var unchanged=new Dictionary<string,bool>{{"RVC model",Config.voice.model==original.voice.model},{"RVC index",Config.voice.index==original.voice.index},{"profiles",Config.profiles.Count==original.profiles.Count&&Config.profiles.Zip(original.profiles,(a,b)=>a.id==b.id&&a.name==b.name&&typeof(VoiceConfig).GetFields().All(f=>object.Equals(f.GetValue(a.voice),f.GetValue(b.voice)))).All(equal=>equal)},{"AIRI",Json.Encode(Config.airi)==Json.Encode(original.airi)},{"Harness",Json.Encode(Config.harness)==Json.Encode(original.harness)},{"appearance",Json.Encode(Config.appearance)==Json.Encode(original.appearance)},{"token",Config.token==original.token}};if(unchanged.Any(k=>!k.Value)){File.WriteAllText(Path.Combine(directory,"preservation-details.json"),Json.Encode(new{checks=unchanged,expectedProfiles=original.profiles,actualProfiles=Config.profiles}));throw new InvalidOperationException("本地模型导入改变了其他模块："+string.Join(",",unchanged.Where(k=>!k.Value).Select(k=>k.Key)));}
                results.Add("native model import preserves RVC and other modules");results.Add("model weights and reference hashes match");
                ShowPage("home");serviceToggle.PerformClick();await wait();if(!await Service.Running())throw new InvalidOperationException("导入后服务启动失败");
                body.Text="工作好久啦，起来动一动、喝口水吧～";FindButton(pages["home"],"仅生成").PerformClick();await wait();if(lastJob.Length==0)throw new InvalidOperationException("导入后生成失败："+footer.Text);
                var job=await Service.Request("/api/jobs/"+lastJob);var output=job["result"] as Dictionary<string,object>;
                if(Json.Text(job,"state")!="completed"||output==null||Json.Text(output,"synthesisMode")!="base")throw new InvalidOperationException("未经过本地直出链路");
                results.Add("real CosyVoice base-only synthesis after native import");File.Copy(Store.Resolve("data/output/"+Json.Text(output,"file")),Path.Combine(directory,"本地模型-导入后试听.wav"),true);
                serviceToggle.PerformClick();await wait();return new{ok=true,checks=results,engine="cosyvoice",bundle=file,assetCount=lastTransfer["assetCount"],duration=output["duration"]};
            }finally{configurationTestPath=null;}
        }
        public async Task<object> VerifyFlow(string directory){
            var results=new List<string>();Directory.CreateDirectory(directory);
            Func<Task> wait=async()=>{for(int i=0;i<2400&&operation;i++)await Task.Delay(100);if(operation)throw new InvalidOperationException("按钮操作超时");};
            serviceToggle.PerformClick();await wait();if(!await Service.Running())throw new InvalidOperationException("启动按钮未开启服务："+footer.Text);results.Add("native start button");
            ShowPage("voices");profileName.Text="真实流程配置 A";profileKind.SelectedIndex=1;synthesisChoice.SelectedIndex=1;ShowPage("home");pitch.Value=2;formant.Value=Config.voice.processing=="accepted"?0:.4M;saveVoiceButton.PerformClick();await wait();
            ShowPage("voices");FindButton(pages["voices"],"保存当前配置").PerformClick();await wait();ShowPage("home");string ident=Config.profiles.Last().id;VoiceConfig saved=Config.profiles.Last().voice.Clone();if(!File.Exists(Path.Combine(ProfileFolder(Config.profiles.Last()),"profile.json")))throw new InvalidOperationException("声音配置没有建立独立文件夹");results.Add("full RVC plus TTS profile saved without applying and owns a named folder");
            string switchFixture=Path.Combine(directory,"switch-fixture.pth");File.WriteAllBytes(switchFixture,new byte[]{1,2,3,4});processingChoice.SelectedIndex=0;modelPath.Text=switchFixture;indexPath.Text="";pitch.Value=-2;formant.Value=-.3M;saveVoiceButton.PerformClick();await wait();if(!Config.voice.model.EndsWith("switch-fixture.pth"))throw new InvalidOperationException("模型切换未应用");results.Add("different RVC model applied");
            ShowPage("voices");for(int i=0;i<profileList.Items.Count;i++)if(((SavedVoice)profileList.Items[i]).id==ident)profileList.SelectedIndex=i;
            var restore=FindButton(pages["voices"],"应用选中的配置");restore.PerformClick();await wait();if(!SameVoiceParameters(Config.voice,saved))throw new InvalidOperationException("声音配置未完整恢复");results.Add("native restore button preserves all parameters");
            ShowPage("home");body.Text="工作好久啦，起来动一动、喝口水吧～";generate.PerformClick();await wait();if(lastJob.Length==0)throw new InvalidOperationException("生成按钮没有提交任务");var job=await Service.Request("/api/jobs/"+lastJob);if(Json.Text(job,"state")!="completed")throw new InvalidOperationException(Json.Text(job,"error"));results.Add("native generation and local playback");
            ShowPage("voices");profileName.Text="真实本地配置";profileKind.SelectedIndex=0;FindButton(pages["voices"],"保存当前配置").PerformClick();await wait();var local=Config.profiles.Last();if(local.voice.synthesisMode!="base"||local.voice.model.Length!=0||Config.voice.synthesisMode!="rvc")throw new InvalidOperationException("本地保存类型错误或保存时改变了输出模式");results.Add("local snapshot excludes RVC and does not auto-apply");
            for(int i=0;i<profileList.Items.Count;i++)if(((SavedVoice)profileList.Items[i]).id==local.id)profileList.SelectedIndex=i;FindButton(pages["voices"],"应用选中的配置").PerformClick();await wait();if(Config.voice.synthesisMode!="base"||Config.voice.model!=saved.model||Config.voice.index!=saved.index||Config.voice.pitch!=saved.pitch)throw new InvalidOperationException("应用本地配置改变了 RVC 备用参数");results.Add("native local apply preserves RVC standby choice");
            ShowPage("home");FindButton(pages["home"],"仅生成").PerformClick();await wait();var localJob=await Service.Request("/api/jobs/"+lastJob);var localResult=localJob["result"] as Dictionary<string,object>;if(Json.Text(localJob,"state")!="completed"||Json.Text(localResult,"synthesisMode")!="base")throw new InvalidOperationException("本地配置未直出");results.Add("real base-only generation after local configuration apply");
            ShowPage("voices");for(int i=0;i<profileList.Items.Count;i++)if(((SavedVoice)profileList.Items[i]).id==ident)profileList.SelectedIndex=i;FindButton(pages["voices"],"应用选中的配置").PerformClick();await wait();
            ShowPage("voices");var remove=FindButton(pages["voices"],"删除配置");remove.PerformClick();await wait();if(Config.profiles.Any(p=>p.id==ident))throw new InvalidOperationException("删除按钮未生效");FindButton(pages["voices"],"撤销删除").PerformClick();await wait();if(!Config.profiles.Any(p=>p.id==ident))throw new InvalidOperationException("撤销按钮未生效");results.Add("native delete and undo buttons");
            Settings reopened=Store.Read();if(!SameVoiceParameters(reopened.voice,saved))throw new InvalidOperationException("重开持久化未通过");results.Add("reopened settings retain voice");
            ShowPage("home");serviceToggle.PerformClick();await wait();if(await Service.Running())throw new InvalidOperationException("关闭按钮未关闭服务");results.Add("native stop button");
            return new{ok=true,checks=results,fixture=new{scope=saved.assetScope,processing=saved.processing,model=Path.GetFileName(Store.Resolve(saved.model)),index=Path.GetFileName(Store.Resolve(saved.index)),reference=Path.GetFileName(Store.Resolve(saved.reference)),modelSha256=FileDigest(Store.Resolve(saved.model)),referenceSha256=saved.reference.Length==0?"":FileDigest(Store.Resolve(saved.reference))}};
        }
        static Button FindButton(Control root,string text){foreach(Control child in root.Controls){var button=child as Button;if(button!=null&&button.Text==text)return button;var nested=FindButton(child,text);if(nested!=null)return nested;}return null;}
        public object Verify(string directory){
            Directory.CreateDirectory(directory);var errors=new List<string>();int before=CountControls(this);
            int tooltipChecks=0;foreach(string text in new[]{"首页","RVC模型配置","保存声音配置","模型导入导出","训练音色","设置"}){var entry=FindButton(this,text);if(entry==null||string.IsNullOrWhiteSpace(tips.GetToolTip(entry))||string.IsNullOrWhiteSpace(entry.AccessibleDescription))errors.Add("入口缺少悬浮注释："+text);else tooltipChecks++;}
            if(!navigationButtons.ContainsKey("connections")||navigationButtons["connections"].Text!="连接与联动")errors.Add("重要的连接与联动入口未固定在主导航");
            if(Convert.ToString(serviceToggle.Tag)!="primary"||Convert.ToString(generate.Tag)!="primary"||Convert.ToString(saveVoiceButton.Tag)!="primary")errors.Add("首页主要操作没有视觉强调");
            ShowPage("profiles");var switchMode=FindButton(pages["profiles"],"切换输出模式");if(switchMode==null)errors.Add("RVC 页面缺少输出模式入口");else{switchMode.PerformClick();if(page!="outputMode"||outputModeStatus==null||!outputModeStatus.Text.Contains("本地 TTS")&&!outputModeStatus.Text.Contains("RVC＋本地 TTS"))errors.Add("输出模式入口未显示当前模式提示");}
            if(!pages.ContainsKey("training")||FindButton(pages["training"],"选择训练完成的模型目录")==null)errors.Add("CosyVoice 训练指导入口不完整");
            if(!pages.ContainsKey("connections")||FindButton(pages["connections"],"接入 Harness")==null||FindButton(pages["connections"],"联动 AIRI 桌宠")==null)errors.Add("连接中心入口不完整");
            if(!pages.ContainsKey("cosyReference")||FindButton(pages["cosyReference"],"应用参考条件")==null)errors.Add("CosyVoice 训练相关参考入口不完整");
            if(FindButton(pages["home"],"应用当前设置的声音配置")==null||FindButton(pages["home"],"应用当前本地声音")!=null)errors.Add("首页当前声音应用入口命名不正确");
            if(FindButton(pages["home"],"保存声音配置")!=null||FindButton(pages["home"],"模型导入导出")!=null)errors.Add("本地 TTS 仍保留重复的声音保存或模型传输入口");
            if(FindButton(pages["home"],"选择训练音色模型包")==null)errors.Add("首页缺少音色模型包库选择入口");
            if(FindButton(pages["configuration"],"导出轻量音色包")==null||FindButton(pages["configuration"],"导出完整便携包")==null||FindButton(pages["configuration"],"导入音色包")==null)errors.Add("两种音色包的导入导出入口不完整");
            string repeatedPackage=Path.Combine(directory,"小挽昼-轻量.cosyvoice.ttsmodel.cosyvoice.ttsmodel");
            if(PackageDisplayName("小挽昼-轻量.cosyvoice.ttsmodel.cosyvoice.ttsmodel")!="小挽昼"||NormalizePackagePath(repeatedPackage)!=Path.Combine(Path.GetFullPath(directory),"小挽昼-轻量.cosyvoice.ttsmodel"))errors.Add("音色包导出文件名未去除重复类型或后缀");
            if(FindButton(pages["engine"],"打开本地 TTS 引擎文件夹")==null)errors.Add("本地 TTS 引擎专用目录入口缺失");
            if(FindButton(pages["profiles"],"刷新目录")!=null||FindButton(pages["profiles"],"保存组合声音")!=null)errors.Add("RVC 页面仍保留重复模型列表或组合保存入口");
            if(FindButton(pages["voices"],"打开配置储存位置")==null)errors.Add("声音配置储存位置入口缺失");
            ShowCelebration("界面自检");Application.DoEvents();var celebration=shell.Controls.Cast<Control>().OfType<Celebration>().FirstOrDefault();if(celebration==null||celebration.Dock!=DockStyle.Fill||celebration.Width<shell.ClientSize.Width-2||celebration.Height<shell.ClientSize.Height-2)errors.Add("接入成功礼花没有覆盖完整页面");if(celebration!=null)celebration.Dispose();
            ShowPage("home");
            body.Text="页面切换不应丢失这份草稿";processingChoice.SelectedIndex=0;pitch.Value=3;formant.Value=.65M;
            bool wasPi=Config.harness["pi"];Config.harness["pi"]=true;
            foreach(string state in new[]{"verified","connected","recent_event","waiting"}){
                UpdateHarnessIndicators(new Dictionary<string,object>{{"pi",new Dictionary<string,object>{{"state",state}}}},true);
                bool shouldGreen=state=="verified";
                if(harnessIndicators["pi"].Any(label=>(label.ForeColor!=ForeColor)!=shouldGreen))errors.Add("Harness 状态颜色未匹配实际连接证据");
            }
            Config.harness["pi"]=false;UpdateHarnessIndicators(new Dictionary<string,object>{{"pi",new Dictionary<string,object>{{"state","connected"}}}},true);
            if(harnessIndicators["pi"].Any(label=>!label.Text.EndsWith("未启用")||label.ForeColor!=ForeColor))errors.Add("未启用 Harness 被误报为在线");
            Config.harness["pi"]=wasPi;UpdateHarnessIndicators(null,false);
            ShowPage("settings");ShowPage("airi");backButton.PerformClick();
            if(page!="settings"||!backButton.Visible)errors.Add("设置子页面未返回上一页");
            backButton.PerformClick();if(page!="home"||backButton.Visible)errors.Add("设置未返回首页或首页仍显示返回按钮");
            ShowPage("profiles");backButton.PerformClick();if(page!="home")errors.Add("模型配置未返回首页");
            ShowPage("profiles");ShowPage("settings");backButton.PerformClick();if(page!="profiles")errors.Add("跨入口导航历史未正确返回");
            ShowPage("home");ShowPage("settings");backButton.PerformClick();if(page!="home"||pageHistory.Count!=0)errors.Add("首页未重置旧导航历史");
            synthesisChoice.SelectedIndex=0;checks["advancedEnabled"].Checked=false;baseModelPath.Text="engines/test-base";var mode=ReadVoice();if(mode.synthesisMode!="base"||mode.advancedEnabled||mode.baseModel!="engines/test-base"||pitch.Enabled||formant.Enabled||numbers["indexRate"].Enabled)errors.Add("输出模式或高级旁路控件未正确绑定");synthesisChoice.SelectedIndex=1;checks["advancedEnabled"].Checked=true;baseModelPath.Text="";
            foreach(var size in new[]{new Size(900,620),new Size(1080,750)}){ClientSize=size;foreach(string id in pages.Keys){ShowPage(id);Application.DoEvents();CheckFlowLayout(pages[id],errors);}}ClientSize=new Size(1080,750);
            foreach(string id in pages.Keys){ShowPage(id);Application.DoEvents();using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(Path.Combine(directory,id+".png"));}}
            for(int i=0;i<120;i++){ShowPage(pages.Keys.ElementAt(i%pages.Count));Application.DoEvents();}
            if(CountControls(this)!=before)errors.Add("页面切换改变了控件数量");
            if(body.Text!="页面切换不应丢失这份草稿"||pitch.Value!=3||formant.Value!=.65M)errors.Add("页面切换丢失草稿或参数");
            var clone=ReadVoice().Clone();if(clone.pitch!=3||clone.formant!=.65||clone.referenceText!=fields["referenceText"].Text)errors.Add("声音快照不完整");
            var current=Json.Copy(Config);current.profiles.Add(new SavedVoice{name="自检",voice=clone});string temp=Path.Combine(directory,"config-test");var fixture=new SettingsStore(temp);fixture.Write(current);var read=fixture.Read();if(Json.Encode(read)!=Json.Encode(current))errors.Add("配置重开不一致");
            var record=read.profiles.Last();read.profiles.Remove(record);fixture.Write(read);if(fixture.Read().profiles.Any(p=>p.id==record.id))errors.Add("删除未持久化");read.profiles.Add(record);fixture.Write(read);if(fixture.Read().profiles.Last().voice.formant!=.65)errors.Add("撤销未恢复参数");
            foreach(var container in pages.Values)foreach(Control stack in container.Controls)foreach(Control card in stack.Controls)if(card is ScenePanel && card.Width<300)errors.Add("卡片宽度异常");
            ShowPage("home");if(pageTitle.Text!="首页"||navigationButtons["home"].BackColor!=Accent)errors.Add("页面标题或主导航选中状态不正确");return new{ok=errors.Count==0,pages=pages.Count,switches=120,controls=before,backNavigationChecks=5,harnessStateChecks=4,tooltipChecks=tooltipChecks,newFeatureChecks=13,errors=errors};
        }
        static int CountControls(Control control){return 1+control.Controls.Cast<Control>().Sum(c=>CountControls(c));}
        static void CheckFlowLayout(Control root,List<string> errors){
            var flow=root as FlowLayoutPanel;
            if(flow!=null){var children=flow.Controls.Cast<Control>().Where(c=>c.Visible).ToArray();for(int i=0;i<children.Length;i++)for(int j=i+1;j<children.Length;j++)if(children[i].Bounds.IntersectsWith(children[j].Bounds))errors.Add("控件重叠："+children[i].Text+" / "+children[j].Text);}
            foreach(Control child in root.Controls)CheckFlowLayout(child,errors);
        }
    }
    static class UiChecks {
        public static int Bundle(SettingsStore store,string[] args){
            if(args.Length<3)return 2;string file=args[1],directory=args[2];int exit=1;
            using(var form=new MainForm(store,true)){
                form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);
                form.Shown+=async(s,e)=>{try{var result=await form.VerifyBundle(file,directory);Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"bundle-results.json"),Json.Encode(result));exit=0;}catch(Exception error){Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"bundle-error.txt"),error.ToString());}try{if(await form.Service.Running())await form.Service.Stop();}catch{}form.Close();};Application.Run(form);
            }return exit;
        }
        public static int Flow(SettingsStore store,string[] args){string directory=args.Length>1?args[1]:Path.Combine(store.Root,"acceptance","ui-flow");int exit=1;using(var form=new MainForm(store,true)){form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);form.Shown+=async(s,e)=>{try{var results=await form.VerifyFlow(directory);File.WriteAllText(Path.Combine(directory,"flow-results.json"),Json.Encode(results));exit=0;}catch(Exception error){Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"flow-error.txt"),error.ToString());}try{if(await form.Service.Running())await form.Service.Stop();}catch{}form.Close();};Application.Run(form);}return exit;}
        public static int Run(SettingsStore store,string[] args){string directory=args.Length>1?args[1]:Path.Combine(store.Root,"acceptance","ui");try{using(var form=new MainForm(store,true)){form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);form.Show();Application.DoEvents();object result=form.Verify(directory);string json=Json.Encode(result);File.WriteAllText(Path.Combine(directory,"ui-results.json"),json);form.Dispose();return json.Contains("\"ok\":true")?0:1;}}catch(Exception exc){Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"ui-error.txt"),exc.ToString());return 1;}}
    }
}
