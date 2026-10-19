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

namespace AgentVoice {
    public sealed partial class MainForm {
        [DllImport("user32.dll")]static extern uint GetGuiResources(IntPtr process,uint kind);
        [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
        [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        public async Task<object> VerifyScrolling(string directory){
            Directory.CreateDirectory(directory);
            TopMost=true;ShowWindow(Handle,1);Activate();await Task.Delay(200);
            var reports=new List<object>();var errors=new List<string>();
            var original=Json.Copy(Config);string draft=body.Text;int controls=CountControls(this);
            string backdrop=Path.Combine(directory,"scroll-background.png");
            using(var image=new Bitmap(1080,750))using(var graphics=Graphics.FromImage(image)){
                graphics.Clear(Color.FromArgb(12,51,61));
                for(int x=0;x<1080;x+=40)using(var brush=new SolidBrush(Color.FromArgb(20+x/18,72,82)))graphics.FillRectangle(brush,x,0,20,750);
                using(var brush=new SolidBrush(Color.FromArgb(70,115,120)))for(int y=0;y<750;y+=48)graphics.FillRectangle(brush,0,y,1080,6);
                image.Save(backdrop);
            }
            uint gdiBefore=GetGuiResources(Process.GetCurrentProcess().Handle,0);
            try {
                body.Text="滚动与切换必须保留这份输入";
                foreach(bool patterned in new[]{false,true})foreach(var size in new[]{new Size(1080,750),new Size(900,620)}){
                    Config.appearance=Json.Copy(original.appearance);
                    if(patterned){Config.appearance.background=backdrop;Config.appearance.backgroundColor="#12313B";Config.appearance.textColor="#FAF4F8";}
                    ClientSize=size;ApplyTheme();await Task.Delay(120);
                    foreach(string id in pages.Keys.ToArray()){
                        ShowPage(id);var viewport=pages[id];
                        await Task.Delay(100);
                        int range=viewport.VerticalScroll.Maximum-viewport.VerticalScroll.LargeChange+1;
                        if(range<=0){reports.Add(new{page=id,size=size.ToString(),patterned=patterned,scrollable=false});continue;}
                        Rectangle screen=content.RectangleToScreen(content.ClientRectangle);
                        var screenLock=new object();int excludedFrames=0;uint ownProcess=(uint)Process.GetCurrentProcess().Id;
                        EventHandler trackWindow=(s,e)=>{lock(screenLock)screen=content.RectangleToScreen(content.ClientRectangle);};LocationChanged+=trackWindow;
                        var frames=new List<double>();Bitmap worst=null;double worstWhite=-1;int stop=0;
                        // Independent screen sampling can observe intermediate native child paints;
                        // DrawToBitmap alone only observes the settled final frame.
                        var capture=Task.Run(()=>{
                            while(Volatile.Read(ref stop)==0){
                                Rectangle region;lock(screenLock)region=screen;
                                bool owned=true;foreach(var point in new[]{new Point(region.Left+2,region.Top+2),new Point(region.Right-3,region.Top+2),new Point(region.Left+2,region.Bottom-3),new Point(region.Right-3,region.Bottom-3),new Point(region.Left+region.Width/2,region.Top+region.Height/2)}){uint owner;GetWindowThreadProcessId(WindowFromPoint(point),out owner);if(owner!=ownProcess){owned=false;break;}}
                                if(!owned){excludedFrames++;Thread.Sleep(8);continue;}
                                using(var shot=new Bitmap(region.Width,region.Height)){
                                    using(var graphics=Graphics.FromImage(shot))graphics.CopyFromScreen(region.Location,Point.Empty,region.Size);
                                    int white=0,total=0;
                                    for(int y=0;y<shot.Height;y+=8)for(int x=0;x<shot.Width;x+=8){var pixel=shot.GetPixel(x,y);total++;if(pixel.R>245&&pixel.G>245&&pixel.B>245)white++;}
                                    double fraction=(double)white/total;frames.Add(fraction);
                                    if(fraction>worstWhite){worstWhite=fraction;if(worst!=null)worst.Dispose();worst=(Bitmap)shot.Clone();}
                                }
                                Thread.Sleep(8);
                            }
                        });
                        var durations=new List<double>();var watch=Stopwatch.StartNew();int backgroundSamples=0,backgroundMismatches=0;
                        try {
                            for(int i=0;i<48;i++){
                                var step=Stopwatch.StartNew();
                                if(i%2==0)viewport.AutoScrollPosition=new Point(0,(i/2)%2==0?range:0);
                                else SendMessage(viewport.Handle,0x020A,new IntPtr((i%4==1?-120:120)<<16),IntPtr.Zero);
                                durations.Add(step.Elapsed.TotalMilliseconds);await Task.Delay(24);
                                if(patterned&&scaledBackground!=null){
                                    // Sample the uncovered right gutter: its image must remain
                                    // anchored to the window rather than move with scrolled cards.
                                    var local=new Point(viewport.ClientSize.Width-6,20);
                                    var position=viewport.PointToScreen(local);var origin=PointToClient(position);
                                    using(var strip=new Bitmap(1,Math.Max(1,viewport.ClientSize.Height-40))){
                                        using(var graphics=Graphics.FromImage(strip))graphics.CopyFromScreen(position,Point.Empty,strip.Size);
                                        for(int y=0;y<strip.Height;y+=23){
                                            if(origin.X<0||origin.X>=scaledBackground.Width||origin.Y+y>=scaledBackground.Height)continue;
                                            var actual=strip.GetPixel(0,y);var expected=scaledBackground.GetPixel(origin.X,origin.Y+y);backgroundSamples++;
                                            if(Math.Abs(actual.R-expected.R)+Math.Abs(actual.G-expected.G)+Math.Abs(actual.B-expected.B)>12)backgroundMismatches++;
                                        }
                                    }
                                }
                            }
                        }finally{Volatile.Write(ref stop,1);LocationChanged-=trackWindow;}
                        await capture;
                        if(worst!=null){worst.Save(Path.Combine(directory,id+"-"+size.Width+"-"+(patterned?"background":"original")+"-max-white.png"));worst.Dispose();}
                        if(patterned&&worstWhite>.15)errors.Add(id+": large white flash with dark background");
                        if(frames.Count<20)errors.Add(id+": insufficient owned-window frames; another window covered the test");
                        if(backgroundMismatches>0)errors.Add(id+": background moved or left stale pixels while scrolling ("+backgroundMismatches+" samples)");
                        if(body.Text!="滚动与切换必须保留这份输入")errors.Add("Scrolling lost input");
                        if(CountControls(this)!=controls)errors.Add("Scrolling created or removed controls");
                        reports.Add(new{page=id,size=size.ToString(),patterned=patterned,scrollable=true,scrollRange=range,
                            steps=48,screenFrames=frames.Count,excludedFrames=excludedFrames,maxWhiteFraction=worstWhite,whiteFractions=frames,
                            backgroundSamples=backgroundSamples,backgroundMismatches=backgroundMismatches,
                            maxDispatchMs=durations.Max(),meanDispatchMs=durations.Average(),elapsedMs=watch.Elapsed.TotalMilliseconds});
                    }
                }
                return new{ok=errors.Count==0,cases=reports,errors=errors,gdiBefore=gdiBefore,gdiAfter=GetGuiResources(Process.GetCurrentProcess().Handle,0),draftPreserved=true};
            }finally{Config=original;body.Text=draft;ClientSize=new Size(1080,750);ApplyTheme();ShowPage("home");}
        }
    }
    static class ScrollChecks {
        public static int Run(SettingsStore store,string[] args){
            string directory=args.Length>1?args[1]:Path.Combine(store.Root,"acceptance","scroll");int exit=1;
            using(var form=new MainForm(store,true)){
                form.StartPosition=FormStartPosition.Manual;form.Location=new Point(60,60);
                form.Shown+=async(s,e)=>{try{var result=await form.VerifyScrolling(directory);File.WriteAllText(Path.Combine(directory,"scroll-results.json"),Json.Encode(result));exit=Json.Encode(result).Contains("\"ok\":true")?0:1;}catch(Exception error){Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"scroll-error.txt"),error.ToString());}form.Close();};
                Application.Run(form);
            }return exit;
        }
    }
}
