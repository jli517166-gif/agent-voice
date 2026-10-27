using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace AgentVoice {
    static class EmbeddedWebChecks {
        [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr handle,uint flags);
        static async Task Wait(Func<Task<bool>> condition,int timeout=15000){var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<timeout){if(await condition())return;await Task.Delay(40);}throw new TimeoutException("Embedded setup did not reach the expected state");}
        static async Task<bool> Script(EmbeddedSetupForm form,string expression){return await form.Browser.ExecuteScriptAsync(expression)=="true";}
        internal static int Run(SettingsStore store,string[] args){
            string directory=args.Length>1?Path.GetFullPath(args[1]):store.Resolve("acceptance/embedded-setup");Directory.CreateDirectory(directory);int result=1;
            // Keep settings and services isolated from the user's installation.
            var fixture=new SettingsStore(Path.Combine(directory,"installation-"+Guid.NewGuid().ToString("N")));
            using(var health=new SetupProbeServer()){
            health.WrongService=true;
            var config=fixture.Read();config.port=health.Port;fixture.Write(config);
            CopyTree(store.Resolve("assets/setup"),fixture.Resolve("assets/setup"));
            var preference=new WelcomeState(fixture);
            preference.Write(new WelcomePreferences{interfaceSounds=false});
            using(var form=new EmbeddedSetupForm(fixture,true)){
                form.Shown+=async(s,e)=>{
                    try{await Verify(form,directory);result=0;}
                    catch(Exception error){File.WriteAllText(Path.Combine(directory,"web-error.txt"),error.ToString());}
                    await Wait(()=>Task.FromResult(!form.Controller.Busy));form.Close();
                };
                Application.Run(form);
            }
            }
            return result;
        }
        static async Task Verify(EmbeddedSetupForm form,string directory){
            await Wait(()=>Task.FromResult(form.Ready),30000);
            await Task.Delay(200); // Initial unrelated-service probe must be harmless.
            var isolated=form.Controller.Flow.Config;isolated.port=1;form.Controller.Flow.Store.Write(isolated);
            await Wait(()=>Script(form,"document.querySelector('#page').classList.contains('step-0')"));
            if(EmbeddedSetupForm.Trusted("https://evil.example")||EmbeddedSetupForm.Trusted("file:///index.html")||EmbeddedSetupForm.Trusted("https://setup.agent-voice.local.evil.example"))throw new Exception("Remote bridge origin accepted");
            var errors=new List<string>();var layouts=new List<object>();
            await form.Controller.Execute("go",new Dictionary<string,object>{{"step",5}});form.Browser.CoreWebView2.Reload();
            await Wait(()=>Script(form,"document.querySelector('[data-action=\"legal-read\"]')!==null"));
            if(!await Script(form,"document.querySelector('#next').disabled && document.querySelector('#skip').hidden && document.querySelector('#position').textContent==='第 1 步 / 共 9 步 · 使用须知'"))errors.Add("Legal gate or displayed first step is wrong");
            if(!await Script(form,"(()=>{const hint=document.querySelector('#legalReminder'),button=document.querySelector('[data-action=\"legal-read\"]');return hint?.textContent.includes('请先点击') && hint.getBoundingClientRect().bottom<=button.getBoundingClientRect().top})()"))errors.Add("Required reading instruction missing or overlaps its action");
            bool legalBlocked=false;try{await form.Controller.Execute("next",new Dictionary<string,object>());}catch(InvalidOperationException){legalBlocked=true;}
            if(!legalBlocked)errors.Add("Backend next bypassed legal acknowledgement");
            await form.Browser.ExecuteScriptAsync("document.querySelector('[data-action=\"legal-read\"]').click()");
            await Wait(()=>Script(form,"document.querySelector('#next').disabled===false"));

            if(Environment.GetEnvironmentVariable("AGENT_VOICE_DOWNLOAD_CHECK_ONLY")=="1"){
                string python=Environment.GetEnvironmentVariable("AGENT_VOICE_TEST_PYTHON");if(string.IsNullOrEmpty(python))throw new Exception("Pause UI fixture requires a test interpreter");
                await CheckPauseButtons(form,directory,python);return;
            }
            var preferred=form.ClientSize;
            foreach(var size in new[]{new Size(1180,780),new Size(900,640),new Size(1600,960),preferred}){
                form.ClientSize=size;await Task.Delay(140);
                for(int step=0;step<10;step++){
                    await Wait(()=>Task.FromResult(!form.Controller.Busy));
                    await form.Controller.Execute("go",new Dictionary<string,object>{{"step",step}});
                    form.Browser.CoreWebView2.Reload();
                    await Wait(()=>Script(form,"document.querySelector('#page')?.classList.contains('step-"+step+"')===true"));
                    await Task.Delay(80);
                    string audit=await form.Browser.ExecuteScriptAsync(@"(()=>{const all=[...document.querySelectorAll('header,footer,footer button')].filter(x=>!x.hidden);const outside=all.filter(x=>{const r=x.getBoundingClientRect();return r.left<-.5||r.top<-.5||r.right>innerWidth+.5||r.bottom>innerHeight+.5}).map(x=>x.id||x.tagName);return {outside,step:document.querySelector('#page').className,width:innerWidth,height:innerHeight,bodyScroll:document.body.scrollWidth>innerWidth,font:getComputedStyle(document.querySelector('.lead')).fontSize,art:document.querySelector('.art img').naturalWidth}})()");
                    var check=Json.Object(audit);if(((System.Collections.ICollection)check["outside"]).Count>0||Convert.ToBoolean(check["bodyScroll"])||Convert.ToInt32(check["art"])==0)errors.Add("Page clipped or artwork missing: "+step+" / "+size);
                    if(!await Script(form,@"(()=>{const img=document.querySelector('.hero-object').getBoundingClientRect();return [...document.querySelectorAll('.art-label,.scene-tags,.signal-route,.dependency-grid')].every(el=>{if(getComputedStyle(el).display==='none')return true;const r=el.getBoundingClientRect();return r.bottom<=img.top||r.top>=img.bottom||r.right<=img.left||r.left>=img.right})})()"))errors.Add("Artwork overlaps a caption: "+step+" / "+size);
                    string expected=SetupWorkflow.StepLabel(step);
                    if(!await Script(form,"document.querySelector('#position').textContent==="+Json.Encode(expected)))errors.Add("Displayed step count differs: "+step);
                    layouts.Add(new{size=size.ToString(),step=step,audit=check});
                    if(size==preferred&&(step==0||step==1||step==2||step==4||step==5||step==6||step==7||step==8||step==9)){await Task.Delay(1300);using(var stream=File.Create(Path.Combine(directory,"web-step-"+step+".png")))await form.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);}
                }
            }
            await Wait(()=>Task.FromResult(!form.Controller.Busy));
            await form.Controller.Execute("go",new Dictionary<string,object>{{"step",2}});form.Browser.CoreWebView2.Reload();
            await Wait(()=>Script(form,"document.querySelector('#page')?.classList.contains('step-2')===true"));
            await form.Browser.ExecuteScriptAsync("document.querySelector('[data-engine=\"1\"]').click();document.querySelector('[data-engine=\"2\"]').click();document.querySelector('#next').click();");
            await Wait(()=>Script(form,"document.querySelector('#page')?.classList.contains('step-3')===true"));
            await Wait(()=>Task.FromResult(!form.Controller.Busy));
            if(!await Script(form,"document.querySelector('#feedback').hidden"))errors.Add("Navigation left a misleading pending-operation panel");
            if(form.Controller.Flow.Config.voice.engine!="cosyvoice"||!form.Controller.Flow.InstallEngines().SequenceEqual(new[]{0,1,2}))errors.Add("Multi-select did not save installation targets independently");
            await form.Controller.Execute("preferences",new Dictionary<string,object>{{"offline",true},{"reduceMotion",true},{"sound",false}});
            form.Browser.CoreWebView2.Reload();
            await Wait(()=>Script(form,"document.querySelector('#localEngine')?.open===true"));
            await form.Browser.ExecuteScriptAsync("document.querySelector('[data-action=\"download-engine\"]').click()");
            await Wait(()=>Script(form,"document.querySelector('#feedback').classList.contains('error')"));
            if(!await Script(form,"document.querySelector('#localEngine').open"))errors.Add("Download failure did not expose local alternative");
            await Wait(()=>Task.FromResult(!form.Controller.Busy));
            await form.Controller.Execute("go",new Dictionary<string,object>{{"step",5}});form.Browser.CoreWebView2.Reload();
            await Wait(()=>Script(form,"document.querySelector('[data-action=\"legal-read\"]')!==null"));
            if(!await Script(form,"document.querySelector('.body').textContent.includes('授权') && !document.querySelector('[data-action=\"reference\"], [data-action=\"profiles\"]')"))errors.Add("Legal page retained voice selection or omitted rights notice");
            await form.Browser.ExecuteScriptAsync("document.querySelector('[data-action=\"legal-read\"]').click()");
            await Wait(()=>Task.FromResult(!form.Controller.Busy));
            await form.Controller.Execute("go",new Dictionary<string,object>{{"step",6}});form.Browser.CoreWebView2.Reload();
            await Wait(()=>Script(form,"document.querySelector('.body')?.textContent.includes('Apache-2.0')===true"));
            if(!await Script(form,"document.querySelector('.body').textContent.includes('MIT') && !document.querySelector('[data-action=\"preview\"], [data-action=\"confirm\"]')"))errors.Add("License page retained audition or omitted source license");
            string testPython=Environment.GetEnvironmentVariable("AGENT_VOICE_TEST_PYTHON");if(!string.IsNullOrEmpty(testPython))await CheckPauseButtons(form,directory,testPython);
            // Sample a foreground window region while resizing; exclude other topmost apps.
            form.TopMost=true;form.Activate();form.WindowState=FormWindowState.Normal;form.ClientSize=new Size(1180,780);form.Location=new Point(30,30);await Task.Delay(200);
            var fractions=new List<double>();int excluded=0,stop=0;Rectangle region=Rectangle.Empty;object gate=new object();IntPtr root=form.Handle;
            Action update=()=>{lock(gate){region=form.RectangleToScreen(form.ClientRectangle);region.Width=(int)(region.Width*.72);}};
            update();var capture=Task.Run(()=>{
                while(Volatile.Read(ref stop)==0){Rectangle area;lock(gate)area=region;
                    var points=new[]{new Point(area.Left+3,area.Top+3),new Point(area.Right-3,area.Top+3),new Point(area.Left+3,area.Bottom-3),new Point(area.Right-3,area.Bottom-3)};
                    if(points.Any(p=>GetAncestor(WindowFromPoint(p),2)!=root)){excluded++;Thread.Sleep(12);continue;}
                    using(var bitmap=new Bitmap(Math.Max(1,area.Width),Math.Max(1,area.Height))){using(var g=Graphics.FromImage(bitmap))g.CopyFromScreen(area.Location,Point.Empty,area.Size);int white=0,total=0;for(int y=0;y<bitmap.Height;y+=16)for(int x=0;x<bitmap.Width;x+=16){var p=bitmap.GetPixel(x,y);if(p.R>245&&p.G>245&&p.B>245)white++;total++;}if(points.Any(p=>GetAncestor(WindowFromPoint(p),2)!=root)){excluded++;continue;}double fraction=white/(double)total;if(fraction>.28&&!File.Exists(Path.Combine(directory,"resize-white-frame.png")))bitmap.Save(Path.Combine(directory,"resize-white-frame.png"));fractions.Add(fraction);}Thread.Sleep(12);
                }
            });
            try{for(int i=0;i<36;i++){if(i%9==0)form.WindowState=FormWindowState.Maximized;else{form.WindowState=FormWindowState.Normal;form.ClientSize=new Size(1100+(i%5)*80,720+(i%3)*45);}update();await Task.Delay(60);}}
            finally{Volatile.Write(ref stop,1);}await capture;
            if(fractions.Count<20)errors.Add("Insufficient owned-window frames for resize test");
            if(fractions.Any(x=>x>.28))errors.Add("Large white frame while resizing");
            form.TopMost=false;
            // Missing runtime must preserve a native fallback and defer path.
            using(var fallback=new EmbeddedSetupForm(form.Controller.Flow.Store,true,true)){
                fallback.Show();await Task.Delay(250);
                if(fallback.Ready||!fallback.Controls.Cast<Control>().SelectMany(Descendants).Any(c=>c.Text=="使用简洁配置"))errors.Add("Missing runtime has no native fallback");
                fallback.Close();
            }
            var result=new{ok=errors.Count==0,pages=10,layouts=layouts,frames=fractions.Count,excludedFrames=excluded,maxWhite=fractions.Count==0?0:fractions.Max(),errors=errors};
            File.WriteAllText(Path.Combine(directory,"web-results.json"),Json.Encode(result));if(errors.Count>0)throw new Exception(string.Join("\n",errors));
        }
        static async Task CheckPauseButtons(EmbeddedSetupForm form,string directory,string python){
            var controller=form.Controller;var store=controller.Flow.Store;
            EmbeddedSetupChecks.PreparePauseFixture(store,python);controller.Flow.SelectInstallEngines(new[]{0});
            await controller.Execute("preferences",new Dictionary<string,object>{{"offline",false},{"reduceMotion",true},{"sound",false}});
            await controller.Execute("go",new Dictionary<string,object>{{"step",3}});form.Browser.CoreWebView2.Reload();
            await Wait(()=>Script(form,"document.querySelector('[data-action=\"download-engine\"]')!==null"));
            await form.Browser.ExecuteScriptAsync("document.querySelector('[data-action=\"download-engine\"]').click()");
            try{
                await Wait(()=>Script(form,"!document.querySelector('#downloadControls').hidden && !document.querySelector('#pauseDownload').disabled"));
                await Wait(()=>Task.FromResult(File.Exists(store.Resolve("counter.txt"))));
                await form.Browser.ExecuteScriptAsync("document.querySelector('#pauseDownload').click()");
                await Wait(()=>Script(form,"document.body.classList.contains('download-paused')"));
                string frozen=File.ReadAllText(store.Resolve("counter.txt"));await Task.Delay(200);
                if(File.ReadAllText(store.Resolve("counter.txt"))!=frozen)throw new Exception("UI pause did not stop the owned worker");
                await form.Browser.ExecuteScriptAsync("document.querySelector('#pauseDownload').click()");
                await Wait(()=>Task.FromResult(File.ReadAllText(store.Resolve("counter.txt"))!=frozen));
                await form.Browser.ExecuteScriptAsync("document.querySelector('#pauseDownload').click()");
                await Wait(()=>Script(form,"document.body.classList.contains('download-paused')"));
                using(var stream=File.Create(Path.Combine(directory,"web-download-paused.png")))await form.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
                await form.Browser.ExecuteScriptAsync("document.querySelector('#cancelDownload').click()");
                await Wait(()=>Task.FromResult(!controller.Busy));
                await Wait(()=>Script(form,"document.querySelector('#downloadControls').hidden && document.querySelector('#taskTitle').textContent==='已停止' && !document.querySelector('#feedback').classList.contains('error')"));
                File.WriteAllText(Path.Combine(directory,"web-download-pause.json"),"{\"ok\":true,\"actualWorkerFrozen\":true,\"resume\":true,\"stopWhilePaused\":true}");
            }finally{if(controller.Busy&&controller.Flow.Service.Download!=null){controller.Flow.Service.Download.Cancel();}}
        }
        static IEnumerable<Control> Descendants(Control root){yield return root;foreach(Control child in root.Controls)foreach(var item in Descendants(child))yield return item;}
        static void CopyTree(string source,string target){Directory.CreateDirectory(target);foreach(string file in Directory.GetFiles(source))File.Copy(file,Path.Combine(target,Path.GetFileName(file)));foreach(string folder in Directory.GetDirectories(source))CopyTree(folder,Path.Combine(target,Path.GetFileName(folder)));}
    }
}
