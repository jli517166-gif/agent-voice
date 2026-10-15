using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AgentVoice {
    public interface IFloatingVoiceService {
        Task<Dictionary<string,object>> Request(string path,object data=null,int timeout=5000);
        Task<bool> Running();
        Task Start();
        Task Stop();
    }

    // UI preferences stay outside voice settings and exported voice packages.
    public sealed class FloatingPreferences {
        public bool enabled=true;
        public double offsetX=0,offsetY=0;
        public static FloatingPreferences Read(SettingsStore store){try{string p=Path.Combine(store.Root,"data","codex-floating.json");return File.Exists(p)?Json.Decode<FloatingPreferences>(File.ReadAllText(p)):new FloatingPreferences();}catch{return new FloatingPreferences();}}
        public void Save(SettingsStore store){string path=Path.Combine(store.Root,"data","codex-floating.json");Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".tmp";File.WriteAllText(temp,Json.Encode(this),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
    }

    public sealed class FloatingTarget {
        public IntPtr handle;
        public Rectangle bounds,workArea;
        public bool active,minimized;
    }

    public static class FloatingLayout {
        public static Rectangle Clamp(Rectangle value,Rectangle work){return new Rectangle(Math.Max(work.Left,Math.Min(value.X,work.Right-value.Width)),Math.Max(work.Top,Math.Min(value.Y,work.Bottom-value.Height)),Math.Min(value.Width,work.Width),Math.Min(value.Height,work.Height));}
        public static Rectangle Entry(Rectangle window,Rectangle work,Size size,float scale,double offsetX,double offsetY){int gap=(int)(10*scale);int x=window.Right+gap;if(x+size.Width>work.Right)x=window.Right-size.Width-gap;int y=window.Bottom-(int)(150*scale);return Clamp(new Rectangle(x+(int)(offsetX*scale),y+(int)(offsetY*scale),size.Width,size.Height),work);}
        public static Rectangle Panel(Rectangle entry,Rectangle work,Size size,int gap){int x=Math.Max(work.Left,Math.Min(entry.Right-size.Width,work.Right-size.Width));foreach(var candidate in new[]{new Rectangle(x,entry.Top-size.Height-gap,size.Width,size.Height),new Rectangle(x,entry.Bottom+gap,size.Width,size.Height),new Rectangle(entry.Left-size.Width-gap,Math.Max(work.Top,Math.Min(entry.Top,work.Bottom-size.Height)),size.Width,size.Height),new Rectangle(entry.Right+gap,Math.Max(work.Top,Math.Min(entry.Top,work.Bottom-size.Height)),size.Width,size.Height)})if(work.Contains(candidate))return candidate;return Clamp(new Rectangle(x,entry.Top-size.Height-gap,size.Width,size.Height),work);}
    }

    // Read window identity and bounds only. Never inspect or modify chat content.
    public sealed class CodexWindowTracker {
        delegate bool EnumCallback(IntPtr window,IntPtr parameter);
        [StructLayout(LayoutKind.Sequential)]struct Rect {public int left,top,right,bottom;}
        [DllImport("user32.dll")]static extern bool EnumWindows(EnumCallback callback,IntPtr parameter);
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect rectangle);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
        [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flags);
        [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr window,uint command);
        [DllImport("kernel32.dll")]static extern IntPtr OpenProcess(uint access,bool inherit,uint processId);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool QueryFullProcessImageName(IntPtr process,int flags,StringBuilder name,ref int size);
        [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
        [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
        readonly Dictionary<uint,bool> identities=new Dictionary<uint,bool>();
        DateTime scanned=DateTime.MinValue;
        IntPtr last;
        public static bool IsCodexImage(string path){string normalized=(path??"").Replace('/','\\');if(normalized.IndexOf("\\OpenAI.Codex_",StringComparison.OrdinalIgnoreCase)>=0)return true;return Path.GetFileName(normalized).Equals("Codex.exe",StringComparison.OrdinalIgnoreCase)&&normalized.IndexOf("\\bin\\",StringComparison.OrdinalIgnoreCase)<0&&normalized.IndexOf("\\vendor\\",StringComparison.OrdinalIgnoreCase)<0;}
        bool IsCodex(IntPtr window){uint pid;GetWindowThreadProcessId(window,out pid);bool found;if(identities.TryGetValue(pid,out found))return found;IntPtr process=OpenProcess(0x1000,false,pid);try{var text=new StringBuilder(4096);int length=text.Capacity;found=process!=IntPtr.Zero&&QueryFullProcessImageName(process,0,text,ref length)&&IsCodexImage(text.ToString());identities[pid]=found;return found;}finally{if(process!=IntPtr.Zero)CloseHandle(process);}}
        bool Candidate(IntPtr window){Rect r;return window!=IntPtr.Zero&&IsWindowVisible(window)&&GetWindow(window,4)==IntPtr.Zero&&GetWindowRect(window,out r)&&r.right-r.left>=400&&r.bottom-r.top>=250&&IsCodex(window);}
        public FloatingTarget Read(IntPtr entry,IntPtr panel){
            if((DateTime.UtcNow-scanned).TotalSeconds>5){identities.Clear();scanned=DateTime.UtcNow;}
            IntPtr foreground=GetForegroundWindow(),root=GetAncestor(foreground,2),owner=GetAncestor(foreground,3),selected=IntPtr.Zero;
            if(Candidate(root))selected=root;
            else if(foreground==entry||foreground==panel||owner==entry||owner==panel)selected=last;
            if(selected==IntPtr.Zero&&Candidate(last))selected=last;
            if(selected==IntPtr.Zero){EnumWindows((w,p)=>{if(Candidate(w)){selected=w;return false;}return true;},IntPtr.Zero);}
            if(selected==IntPtr.Zero)return null;
            Rect rect;if(!GetWindowRect(selected,out rect)||rect.right-rect.left<400||rect.bottom-rect.top<250)return null;
            int cloaked=0;try{DwmGetWindowAttribute(selected,14,out cloaked,4);}catch(DllNotFoundException){}
            last=selected;var bounds=Rectangle.FromLTRB(rect.left,rect.top,rect.right,rect.bottom);
            return new FloatingTarget{handle=selected,bounds=bounds,workArea=Screen.FromRectangle(bounds).WorkingArea,minimized=IsIconic(selected)||!IsWindowVisible(selected)||cloaked!=0,active=root==selected||foreground==entry||foreground==panel||owner==entry||owner==panel};
        }
    }

    public sealed class FloatingVoiceActions {
        readonly SettingsStore store;
        readonly IFloatingVoiceService service;
        readonly Action<Settings,bool> changed;
        public FloatingVoiceActions(SettingsStore value,IFloatingVoiceService client,Action<Settings,bool> onChange){store=value;service=client;changed=onChange;}
        public static bool SameVoice(VoiceConfig a,VoiceConfig b){return a!=null&&b!=null&&typeof(VoiceConfig).GetFields().All(f=>object.Equals(f.GetValue(a),f.GetValue(b)));}
        public async Task<string> Status(){var cfg=store.Read();bool on=cfg.harness["codex"];var profile=cfg.profiles.FirstOrDefault(p=>SameVoice(p.voice,cfg.voice));string voice="\r\n声音："+(profile==null?"当前自定义配置":profile.name);if(!await service.Running())return "服务：已关闭  ·  自动朗读："+(on?"开":"关")+voice;var h=await service.Request("/health");return "服务：已开启  ·  自动朗读："+(on?"开":"关")+voice+(Json.Text(h,"error").Length>0?"\r\n"+Json.Text(h,"error"):"");}
        public async Task<string> Execute(string action,string profileId=null){
            if(action=="start"){await service.Start();return "语音服务已开启。";}
            if(action=="stop"){if(await service.Running())await service.Stop();return "语音服务已关闭。";}
            if(action=="quiet"){if(await service.Running())await service.Request("/stop",new {});return "已停止朗读并清空队列。";}
            var cfg=store.Read();bool voice=false;
            if(action=="profile"){var item=cfg.profiles.FirstOrDefault(p=>p.id==profileId);if(item==null)throw new InvalidOperationException("声音方案已不存在，请重新选择。");cfg.voice=item.voice.Clone();voice=true;}
            else if(action=="auto")cfg.harness["codex"]=!cfg.harness["codex"];
            else throw new InvalidOperationException("未知控制操作。");
            if(await service.Running()){
                if(voice)await service.Request("/api/config",new {voice=cfg.voice},90000);
                else{await service.Request("/api/config",new {harness=cfg.harness});await service.Request("/api/connections/reload",new {});}
            }else store.Write(cfg);
            if(changed!=null)changed(store.Read(),voice);
            return voice?"已应用声音方案。":"Codex 自动朗读已"+(cfg.harness["codex"]?"开启。":"关闭。");
        }
    }

    sealed class FloatingEntry:Form {
        public bool Dragging;
        public event Action OpenPanel,PositionSaved;
        Point mouseStart,windowStart;bool moved;
        readonly ToolTip hint=new ToolTip();
        public FloatingEntry(float scale){AutoScaleMode=AutoScaleMode.None;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;Size=new Size((int)(104*scale),(int)(38*scale));Font=new Font("Microsoft YaHei UI",10F);Text="Codex 语音悬浮入口";AccessibleName=Text;AccessibleRole=AccessibleRole.PushButton;DoubleBuffered=true;Cursor=Cursors.Hand;hint.SetToolTip(this,"点击控制语音；按住拖动调整位置。设置／托盘中可关闭。");}
        protected override bool ShowWithoutActivation{get{return true;}}
        protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000080;return p;}}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;float s=Height/38F;using(var pen=new Pen(ForeColor,1.6F*s)){pen.StartCap=pen.EndCap=LineCap.Round;for(int i=0;i<5;i++){float x=(12+i*3.5F)*s,h=(i==2?14:i%2==1?10:5)*s;g.DrawLine(pen,x,Height/2F-h/2,x,Height/2F+h/2);}}TextRenderer.DrawText(g,"语音",Font,new Rectangle((int)(34*s),0,Width-(int)(36*s),Height),ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter);}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){Dragging=true;moved=false;mouseStart=Cursor.Position;windowStart=Location;Capture=true;}}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(!Dragging)return;Point delta=new Point(Cursor.Position.X-mouseStart.X,Cursor.Position.Y-mouseStart.Y);if(Math.Abs(delta.X)+Math.Abs(delta.Y)>5)moved=true;if(moved)Location=new Point(windowStart.X+delta.X,windowStart.Y+delta.Y);}
        protected override void OnMouseUp(MouseEventArgs e){if(Dragging){Dragging=false;Capture=false;if(moved&&PositionSaved!=null)PositionSaved();}base.OnMouseUp(e);}
        protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(e.Button==MouseButtons.Left&&!moved&&OpenPanel!=null)OpenPanel();}
        protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(Dragging&&!Capture){Dragging=false;if(moved&&PositionSaved!=null)PositionSaved();}}
        protected override void Dispose(bool disposing){if(disposing)hint.Dispose();base.Dispose(disposing);}
    }

    sealed class FloatingPanel:Form {
        readonly SettingsStore store;readonly FloatingVoiceActions actions;readonly Action openMain,disable,reset;
        readonly List<Control> waiting=new List<Control>();
        readonly Label status,title;
        readonly ComboBox profiles;
        readonly Button quiet,automatic;
        int active,revision;bool refreshing;
        protected override bool ShowWithoutActivation{get{return true;}}
        public FloatingPanel(SettingsStore value,FloatingVoiceActions control,float scale,Action showMain,Action hideEntry,Action resetPosition){
            store=value;actions=control;openMain=showMain;disable=hideEntry;reset=resetPosition;
            AutoScaleMode=AutoScaleMode.None;FormBorderStyle=FormBorderStyle.None;ControlBox=false;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;Text="Codex 语音控制";Font=new Font("Microsoft YaHei UI",9.5F);ClientSize=new Size((int)(326*scale),(int)(306*scale));
            var grid=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding((int)(12*scale)),ColumnCount=2,RowCount=7};grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));foreach(int height in new[]{30,42,42,36,38,48,40})grid.RowStyles.Add(new RowStyle(SizeType.Absolute,height*scale));Controls.Add(grid);
            title=new Label{Text="语音控制 · Codex",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Font=new Font(Font,FontStyle.Bold)};grid.Controls.Add(title,0,0);
            var close=MakeButton("收起",()=>Hide());grid.Controls.Add(close,1,0);
            grid.Controls.Add(MakeAction("开启服务","start"),0,1);grid.Controls.Add(MakeAction("关闭服务","stop"),1,1);
            quiet=MakeAction("停止朗读","quiet");grid.Controls.Add(quiet,0,2);automatic=MakeAction("自动朗读","auto");grid.Controls.Add(automatic,1,2);
            profiles=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,Margin=new Padding(3,(int)(5*scale),3,3),AccessibleName="选择保存的声音方案"};grid.Controls.Add(profiles,0,3);grid.SetColumnSpan(profiles,2);waiting.Add(profiles);
            grid.Controls.Add(MakeAction("应用所选声音","profile"),0,4);grid.Controls.Add(MakeButton("打开主界面",()=>{Hide();openMain();}),1,4);
            status=new Label{Text="正在读取状态…",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true,AccessibleName="悬浮控制执行状态"};grid.Controls.Add(status,0,5);grid.SetColumnSpan(status,2);
            grid.Controls.Add(MakeButton("重置入口位置",()=>{Hide();reset();}),0,6);grid.Controls.Add(MakeButton("隐藏悬浮入口",()=>{Hide();disable();}),1,6);
            MinimumSize=Size;
        }
        Button MakeButton(string text,Action action){var b=new Button{Text=text,Dock=DockStyle.Fill,FlatStyle=FlatStyle.Flat,Margin=new Padding(3),UseVisualStyleBackColor=false};b.FlatAppearance.BorderSize=0;b.Click+=(s,e)=>action();return b;}
        Button MakeAction(string text,string command){var b=MakeButton(text,()=>Run(command));waiting.Add(b);return b;}
        public void Theme(bool dark){BackColor=dark?Color.FromArgb(35,38,43):Color.FromArgb(248,249,251);ForeColor=dark?Color.FromArgb(237,240,245):Color.FromArgb(28,34,42);foreach(Control parent in Controls)PaintControls(parent,dark);}
        void PaintControls(Control node,bool dark){node.ForeColor=ForeColor;node.BackColor=node is Button||node is ComboBox?(dark?Color.FromArgb(51,56,63):Color.FromArgb(233,237,242)):BackColor;foreach(Control child in node.Controls)PaintControls(child,dark);}
        void Busy(){foreach(var c in waiting)c.Enabled=active==0;quiet.Enabled=true;}
        public async void Run(string command){if(active>0&&command!="quiet")return;string id=(profiles.SelectedItem as SavedVoice??new SavedVoice()).id;if(command=="profile"&&profiles.SelectedItem==null){status.Text="请先在主界面保存声音方案。";return;}int current=++revision;active++;Busy();status.Text="正在执行…";try{string text=await actions.Execute(command,id);if(!IsDisposed&&current==revision){status.Text=text;RefreshProfiles();}}catch(Exception error){if(!IsDisposed&&current==revision)status.Text="操作失败："+error.Message;}finally{active--;if(!IsDisposed)Busy();}}
        public void RefreshProfiles(){var cfg=store.Read();string selected=(profiles.SelectedItem as SavedVoice??new SavedVoice()).id;profiles.BeginUpdate();profiles.Items.Clear();foreach(var item in cfg.profiles)profiles.Items.Add(item);var match=cfg.profiles.FirstOrDefault(p=>p.id==selected)??cfg.profiles.FirstOrDefault(p=>FloatingVoiceActions.SameVoice(p.voice,cfg.voice))??cfg.profiles.FirstOrDefault();profiles.SelectedItem=match;profiles.EndUpdate();automatic.Text="自动朗读："+(cfg.harness["codex"]?"开":"关");}
        public async void RefreshStatus(){if(refreshing||active>0||IsDisposed)return;refreshing=true;int current=revision;try{string text=await actions.Status();if(!IsDisposed&&current==revision)status.Text=text;}catch(Exception error){if(!IsDisposed)status.Text="连接失败："+error.Message;}finally{refreshing=false;}}
        protected override bool ProcessCmdKey(ref Message msg,Keys key){if(key==Keys.Escape){Hide();return true;}return base.ProcessCmdKey(ref msg,key);}
        protected override void OnFormClosing(FormClosingEventArgs e){if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}base.OnFormClosing(e);}
    }

    public sealed class CodexFloatingController:IDisposable {
        readonly SettingsStore store;readonly FloatingPreferences preferences;readonly FloatingEntry entry;readonly FloatingPanel panel;
        readonly Timer timer=new Timer{Interval=250};readonly Func<IntPtr,IntPtr,FloatingTarget> targetReader;
        readonly float scale;FloatingTarget target;bool disposed;string recorded="";DateTime nextStatus=DateTime.MinValue,nextTheme=DateTime.MinValue;
        public event Action<bool> EnabledChanged;
        public bool Enabled{get{return preferences.enabled;}}
        public bool EntryVisible{get{return entry.Visible;}}
        public bool HasTarget{get{return target!=null;}}
        public Rectangle EntryBounds{get{return entry.Bounds;}}
        public Rectangle PanelBounds{get{return panel.Bounds;}}
        public IntPtr EntryHandle{get{return entry.Handle;}}
        public IntPtr PanelHandle{get{return panel.Handle;}}
        public CodexFloatingController(SettingsStore value,IFloatingVoiceService service,Action<Settings,bool> changed,Action openMain,Func<IntPtr,IntPtr,FloatingTarget> reader=null){
            store=value;preferences=FloatingPreferences.Read(store);using(var g=Graphics.FromHwnd(IntPtr.Zero))scale=g.DpiX/96F;
            var tracker=new CodexWindowTracker();targetReader=reader??tracker.Read;
            entry=new FloatingEntry(scale);panel=new FloatingPanel(store,new FloatingVoiceActions(store,service,changed),scale,openMain,()=>SetEnabled(false),ResetPosition);
            entry.OpenPanel+=OpenPanel;entry.PositionSaved+=SavePosition;timer.Tick+=(s,e)=>Refresh();timer.Start();Refresh();
        }
        public void SetEnabled(bool enabled){preferences.enabled=enabled;preferences.Save(store);if(EnabledChanged!=null)EnabledChanged(enabled);Refresh();}
        public void ResetPosition(){preferences.offsetX=preferences.offsetY=0;preferences.Save(store);Refresh();}
        void SavePosition(){if(target==null)return;var origin=FloatingLayout.Entry(target.bounds,target.workArea,entry.Size,scale,0,0);var clamped=FloatingLayout.Clamp(entry.Bounds,target.workArea);entry.Bounds=clamped;preferences.offsetX=(clamped.X-origin.X)/scale;preferences.offsetY=(clamped.Y-origin.Y)/scale;preferences.Save(store);}
        void Record(string reason){if(recorded==reason)return;recorded=reason;try{string folder=Path.Combine(store.Root,"data","logs");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"codex-floating-status.json"),Json.Encode(new{time=DateTime.UtcNow.ToString("o"),enabled=Enabled,targetFound=target!=null,state=reason}),new UTF8Encoding(false));}catch(IOException){}catch(UnauthorizedAccessException){}}
        public void Refresh(){if(disposed)return;target=targetReader(entry.Handle,panel.Handle);if(!Enabled||target==null||target.minimized||!target.active){panel.Hide();entry.Hide();Record(!Enabled?"disabled":target==null?"codex-not-found":target.minimized?"codex-minimized":"other-app-active");return;}if(!entry.Dragging)entry.Bounds=FloatingLayout.Entry(target.bounds,target.workArea,entry.Size,scale,preferences.offsetX,preferences.offsetY);if(!entry.Visible)entry.Show();Record("visible");if(panel.Visible)panel.Bounds=FloatingLayout.Panel(entry.Bounds,target.workArea,panel.Size,(int)(8*scale));if(DateTime.UtcNow>=nextTheme){nextTheme=DateTime.UtcNow.AddSeconds(3);bool dark=true;try{using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")){dark=key==null||Convert.ToInt32(key.GetValue("AppsUseLightTheme",0))==0;}}catch{}entry.BackColor=dark?Color.FromArgb(42,46,52):Color.FromArgb(244,247,250);entry.ForeColor=dark?Color.White:Color.FromArgb(25,35,45);panel.Theme(dark);entry.Invalidate();}if(panel.Visible&&DateTime.UtcNow>=nextStatus){nextStatus=DateTime.UtcNow.AddSeconds(3);panel.RefreshStatus();}}
        public void OpenPanel(){OpenPanel(true);}
        internal void OpenPanel(bool activate){if(disposed||target==null||!Enabled)return;panel.RefreshProfiles();panel.Bounds=FloatingLayout.Panel(entry.Bounds,target.workArea,panel.Size,(int)(8*scale));panel.Show();if(activate)panel.Activate();panel.RefreshStatus();}
        public void SavePreview(string directory){Directory.CreateDirectory(directory);using(var bitmap=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(bitmap,panel.ClientRectangle);bitmap.Save(Path.Combine(directory,"codex-floating-panel.png"));}using(var bitmap=new Bitmap(entry.Width,entry.Height)){entry.DrawToBitmap(bitmap,entry.ClientRectangle);bitmap.Save(Path.Combine(directory,"codex-floating-entry.png"));}}
        public void Dispose(){if(disposed)return;disposed=true;timer.Stop();timer.Dispose();panel.Dispose();entry.Dispose();}
    }
}
