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
                    layouts.Add(new{size=size.ToString(),step=step,audit=check});
                    if(size==preferred&&(step==0||step==1||step==2||step==4||step==8||step==9)){await Task.Delay(1300);using(var stream=File.Create(Path.Combine(directory,"web-step-"+step+".png")))await form.Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);}
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
            await Wait(()=>Script(form,"document.querySelector('[data-action=\"reference\"]')!==null"));
            await form.Browser.ExecuteScriptAsync("document.querySelector('[data-action=\"reference\"]').click();document.querySelector('#referencePath').value='missing.wav';document.querySelector('#referenceText').value='';document.querySelector('[data-action=\"save-reference\"]').click()");
            await Wait(()=>Script(form,"document.querySelector('#drawerError')?.textContent.length>0"));
            await form.Browser.ExecuteScriptAsync("document.querySelector('#drawer').close()");
            // Sample a foreground window region while resizing; exclude other topmost apps.
            form.TopMost=true;form.Activate();form.WindowState=FormWindowState.Normal;form.ClientSize=new Size(1180,780);form.Location=new Point(30,30);await Task.Delay(200);
            var fractions=new List<double>();int excluded=0,stop=0;Rectangle region=Rectangle.Empty;object gate=new object();IntPtr root=form.Handle;
            Action update=()=>{lock(gate){region=form.RectangleToScreen(form.ClientRectangle);region.Width=(int)(region.Width*.72);}};
            update();var capture=Task.Run(()=>{
                while(Volatile.Read(ref stop)==0){Rectangle area;lock(gate)area=region;
                    var points=new[]{new Point(area.Left+3,area.Top+3),new Point(area.Right-3,area.Top+3),new Point(area.Left+3,area.Bottom-3),new Point(area.Right-3,area.Bottom-3)};
                    if(points.Any(p=>GetAncestor(WindowFromPoint(p),2)!=root)){excluded++;Thread.Sleep(12);continue;}
                    using(var bitmap=new Bitmap(Math.Max(1,area.Width),Math.Max(1,area.Height))){using(var g=Graphics.FromImage(bitmap))g.CopyFromScreen(area.Location,Point.Empty,area.Size);int white=0,total=0;for(int y=0;y<bitmap.Height;y+=16)for(int x=0;x<bitmap.Width;x+=16){var p=bitmap.GetPixel(x,y);if(p.R>245&&p.G>245&&p.B>245)white++;total++;}fractions.Add(white/(double)total);}Thread.Sleep(12);
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
        static IEnumerable<Control> Descendants(Control root){yield return root;foreach(Control child in root.Controls)foreach(var item in Descendants(child))yield return item;}
        static void CopyTree(string source,string target){Directory.CreateDirectory(target);foreach(string file in Directory.GetFiles(source))File.Copy(file,Path.Combine(target,Path.GetFileName(file)));foreach(string folder in Directory.GetDirectories(source))CopyTree(folder,Path.Combine(target,Path.GetFileName(folder)));}
    }
}
