using Button = AgentVoice.AnimeButton;
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
        DateTime nextDiscovery=DateTime.MinValue,nextMonitor=DateTime.MinValue;
        Rectangle monitorBounds,workArea;
        IntPtr last;
        public static bool IsCodexImage(string path){string normalized=(path??"").Replace('/','\\');if(normalized.IndexOf("\\OpenAI.Codex_",StringComparison.OrdinalIgnoreCase)>=0)return true;return Path.GetFileName(normalized).Equals("Codex.exe",StringComparison.OrdinalIgnoreCase)&&normalized.IndexOf("\\bin\\",StringComparison.OrdinalIgnoreCase)<0&&normalized.IndexOf("\\vendor\\",StringComparison.OrdinalIgnoreCase)<0;}
        bool IsCodex(IntPtr window){uint pid;GetWindowThreadProcessId(window,out pid);bool found;if(identities.TryGetValue(pid,out found))return found;IntPtr process=OpenProcess(0x1000,false,pid);try{var text=new StringBuilder(4096);int length=text.Capacity;found=process!=IntPtr.Zero&&QueryFullProcessImageName(process,0,text,ref length)&&IsCodexImage(text.ToString());identities[pid]=found;return found;}finally{if(process!=IntPtr.Zero)CloseHandle(process);}}
        bool Candidate(IntPtr window){Rect r;return window!=IntPtr.Zero&&IsWindowVisible(window)&&GetWindow(window,4)==IntPtr.Zero&&GetWindowRect(window,out r)&&r.right-r.left>=400&&r.bottom-r.top>=250&&IsCodex(window);}
        public FloatingTarget Read(IntPtr entry,IntPtr panel){
            if((DateTime.UtcNow-scanned).TotalSeconds>5){identities.Clear();scanned=DateTime.UtcNow;}
            IntPtr foreground=GetForegroundWindow(),root=GetAncestor(foreground,2),owner=GetAncestor(foreground,3),selected=IntPtr.Zero;
            if(root==last&&last!=IntPtr.Zero)selected=last;
            else if(Candidate(root))selected=root;
            else if(foreground==entry||foreground==panel||owner==entry||owner==panel)selected=last;
            if(selected==IntPtr.Zero&&Candidate(last))selected=last;
            if(selected==IntPtr.Zero&&DateTime.UtcNow>=nextDiscovery){nextDiscovery=DateTime.UtcNow.AddSeconds(1);EnumWindows((w,p)=>{if(Candidate(w)){selected=w;return false;}return true;},IntPtr.Zero);}
            if(selected==IntPtr.Zero)return null;
            Rect rect;if(!GetWindowRect(selected,out rect)||rect.right-rect.left<400||rect.bottom-rect.top<250)return null;
            int cloaked=0;try{DwmGetWindowAttribute(selected,14,out cloaked,4);}catch(DllNotFoundException){}
            last=selected;var bounds=Rectangle.FromLTRB(rect.left,rect.top,rect.right,rect.bottom);
            var center=new Point(bounds.Left+bounds.Width/2,bounds.Top+bounds.Height/2);
            if(!monitorBounds.Contains(center)||DateTime.UtcNow>=nextMonitor){var screen=Screen.FromRectangle(bounds);monitorBounds=screen.Bounds;workArea=screen.WorkingArea;nextMonitor=DateTime.UtcNow.AddSeconds(2);}
            return new FloatingTarget{handle=selected,bounds=bounds,workArea=workArea,minimized=IsIconic(selected)||!IsWindowVisible(selected)||cloaked!=0,active=root==selected||foreground==entry||foreground==panel||owner==entry||owner==panel};
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
        bool expanded,hovering;
        public bool Expanded{get{return expanded;}set{if(expanded!=value){expanded=value;Invalidate();}}}
        public event Action OpenPanel,PositionSaved;
        Point mouseStart,windowStart;bool moved;
        readonly ToolTip hint=new ToolTip();
        public FloatingEntry(float scale){AutoScaleMode=AutoScaleMode.None;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;Size=new Size((int)(110*scale),(int)(38*scale));Font=new Font("Microsoft YaHei UI",10F,FontStyle.Bold);Text="Codex 语音悬浮入口";AccessibleName=Text;AccessibleRole=AccessibleRole.PushButton;DoubleBuffered=true;Cursor=Cursors.Hand;hint.SetToolTip(this,"点击展开／关闭语音面板；按住拖动调整位置。");}
        protected override bool ShowWithoutActivation{get{return true;}}
        protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000080;return p;}}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;float s=Height/38F;Color accent=Color.FromArgb(0,174,221);using(var shape=Shapes.Rounded(new RectangleF(.5F,.5F,Width-1,Height-1),8*s)){if(hovering||expanded)using(var fill=new SolidBrush(Color.FromArgb(BackColor.GetBrightness()<.4F?27:233,BackColor.GetBrightness()<.4F?57:248,BackColor.GetBrightness()<.4F?76:253)))g.FillPath(fill,shape);using(var stroke=new Pen(Color.FromArgb(90,accent),1))g.DrawPath(stroke,shape);}using(var pen=new Pen(accent,1.7F*s)){pen.StartCap=pen.EndCap=LineCap.Round;for(int i=0;i<5;i++){float x=(12+i*3.5F)*s,h=(i==2?14:i%2==1?10:5)*s;g.DrawLine(pen,x,Height/2F-h/2,x,Height/2F+h/2);}}TextRenderer.DrawText(g,"语音",Font,new Rectangle((int)(34*s),0,Width-(int)(53*s),Height),ForeColor,TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter);using(var pen=new Pen(ForeColor,1.3F*s)){float x=Width-17*s,y=Height/2F;g.DrawLines(pen,expanded?new[]{new PointF(x-3*s,y+2*s),new PointF(x,y-1*s),new PointF(x+3*s,y+2*s)}:new[]{new PointF(x-3*s,y-1*s),new PointF(x,y+2*s),new PointF(x+3*s,y-1*s)});}}
        protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hovering=true;Invalidate();}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hovering=false;Invalidate();}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){Dragging=true;moved=false;mouseStart=Cursor.Position;windowStart=Location;Capture=true;}}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(!Dragging)return;Point delta=new Point(Cursor.Position.X-mouseStart.X,Cursor.Position.Y-mouseStart.Y);if(Math.Abs(delta.X)+Math.Abs(delta.Y)>5)moved=true;if(moved)Location=new Point(windowStart.X+delta.X,windowStart.Y+delta.Y);}
        protected override void OnMouseUp(MouseEventArgs e){if(Dragging){Dragging=false;Capture=false;if(moved&&PositionSaved!=null)PositionSaved();}base.OnMouseUp(e);}
        protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(e.Button==MouseButtons.Left&&!moved&&OpenPanel!=null)OpenPanel();}
        protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(Dragging&&!Capture){Dragging=false;if(moved&&PositionSaved!=null)PositionSaved();}}
        protected override void Dispose(bool disposing){if(disposing)hint.Dispose();base.Dispose(disposing);}
    }

    sealed class FloatingProfilePicker:ComboBox {
        public Color BorderColor=Color.LightBlue;
        public Color TextColor=Color.Black;
        public FloatingProfilePicker(){DrawMode=DrawMode.OwnerDrawFixed;}
        protected override void OnDrawItem(DrawItemEventArgs e){if(e.Index<0)return;bool selected=(e.State&DrawItemState.Selected)!=0;Color fill=selected?(BackColor.GetBrightness()<.4F?Color.FromArgb(20,82,110):Color.FromArgb(210,241,253)):BackColor;using(var brush=new SolidBrush(fill))e.Graphics.FillRectangle(brush,e.Bounds);TextRenderer.DrawText(e.Graphics,Convert.ToString(Items[e.Index]),Font,new Rectangle(e.Bounds.X+7,e.Bounds.Y,Math.Max(1,e.Bounds.Width-10),e.Bounds.Height),TextColor,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);}
        protected override void WndProc(ref Message message){base.WndProc(ref message);if(message.Msg!=0xF&&message.Msg!=0x317&&message.Msg!=0x318)return;if(message.Msg!=0xF&&message.WParam==IntPtr.Zero)return;using(var g=message.Msg==0xF?Graphics.FromHwnd(Handle):Graphics.FromHdc(message.WParam)){int width=Math.Max(20,Height-4);var arrow=new Rectangle(Width-width-1,1,width,Height-2);using(var fill=new SolidBrush(BackColor))g.FillRectangle(fill,arrow);using(var border=new Pen(BorderColor))g.DrawRectangle(border,0,0,Width-1,Height-1);g.SmoothingMode=SmoothingMode.AntiAlias;float x=arrow.Left+arrow.Width/2F,y=Height/2F;using(var pen=new Pen(TextColor,1.5F))g.DrawLines(pen,new[]{new PointF(x-4,y-2),new PointF(x,y+2),new PointF(x+4,y-2)});}}
    }

    sealed class FloatingPanel:Form {
        readonly SettingsStore store;readonly FloatingVoiceActions actions;readonly Action openMain,disable,reset;
        readonly List<Control> waiting=new List<Control>();
        readonly Label status;
        readonly PictureBox brand;
        readonly TableLayoutPanel grid;
        readonly Image[] artwork=new Image[4];
        Bitmap backdrop;
        bool dark;
        readonly ComboBox profiles;
        readonly Button quiet,automatic;
        int active,revision;bool refreshing;
        protected override bool ShowWithoutActivation{get{return true;}}
        public FloatingPanel(SettingsStore value,FloatingVoiceActions control,float scale,Action showMain,Action hideEntry,Action resetPosition){
            store=value;actions=control;openMain=showMain;disable=hideEntry;reset=resetPosition;
            AutoScaleMode=AutoScaleMode.None;FormBorderStyle=FormBorderStyle.None;ControlBox=false;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;Text="Codex 语音控制";Font=new Font("Microsoft YaHei UI",9.5F);ClientSize=new Size((int)(326*scale),(int)(376*scale));Padding=new Padding((int)(8*scale));DoubleBuffered=true;
            var names=new[]{"clean-0-fd79407a95b7.png","clean-1-94d1461c5b86.png","cutout-4-558a3d99f9e6.png","cutout-5-fb38fb77845b.png"};for(int i=0;i<names.Length;i++){string path=Path.Combine(store.Root,"assets","ui","codex-floating",names[i]);if(!File.Exists(path))path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","ui","codex-floating",names[i]);if(File.Exists(path))using(var image=Image.FromFile(path))artwork[i]=new Bitmap(image);}
            grid=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding((int)(6*scale)),ColumnCount=2,RowCount=10};grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));foreach(int height in new[]{32,48,38,34,20,32,38,50,32,18})grid.RowStyles.Add(new RowStyle(SizeType.Absolute,height*scale));Controls.Add(grid);
            brand=new PictureBox{Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,Margin=new Padding(2,2,(int)(90*scale),6),AccessibleName="Voice / Codex 语音控制"};brand.Click+=(s,e)=>Hide();grid.Controls.Add(brand,0,0);grid.SetColumnSpan(brand,2);
            quiet=MakeAction("■  停止朗读","quiet");quiet.Tag="primary";quiet.Font=new Font(Font.FontFamily,10.5F,FontStyle.Bold);grid.Controls.Add(quiet,0,1);grid.SetColumnSpan(quiet,2);
            grid.Controls.Add(MakeAction("开启服务","start"),0,2);grid.Controls.Add(MakeAction("关闭服务","stop"),1,2);
            automatic=MakeAction("自动朗读","auto");automatic.Tag="automatic";grid.Controls.Add(automatic,0,3);grid.SetColumnSpan(automatic,2);
            var voiceLabel=new Label{Text="声音方案",Dock=DockStyle.Fill,TextAlign=ContentAlignment.BottomLeft,Font=new Font(Font,FontStyle.Bold),Margin=new Padding(3,0,3,0)};grid.Controls.Add(voiceLabel,0,4);grid.SetColumnSpan(voiceLabel,2);
            profiles=new FloatingProfilePicker{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,ItemHeight=(int)(20*scale),Margin=new Padding(3,(int)(4*scale),3,2),AccessibleName="选择保存的声音方案"};grid.Controls.Add(profiles,0,5);grid.SetColumnSpan(profiles,2);waiting.Add(profiles);
            grid.Controls.Add(MakeAction("应用所选声音","profile"),0,6);grid.Controls.Add(MakeButton("打开主界面",()=>{Hide();openMain();}),1,6);
            status=new Label{Text="正在读取状态…",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true,Padding=new Padding(10,3,8,3),Margin=new Padding(3,4,3,4),AccessibleName="悬浮控制执行状态"};grid.Controls.Add(status,0,7);grid.SetColumnSpan(status,2);
            var resetButton=MakeButton("重置位置",()=>{Hide();reset();});resetButton.Tag="utility";grid.Controls.Add(resetButton,0,8);var hideButton=MakeButton("隐藏入口",()=>{Hide();disable();});hideButton.Tag="utility";grid.Controls.Add(hideButton,1,8);
            var hint=new Label{Text="再次点击「语音」或空白处关闭",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=new Font(Font.FontFamily,8F),Tag="muted"};hint.Click+=(s,e)=>Hide();grid.Controls.Add(hint,0,9);grid.SetColumnSpan(hint,2);grid.Click+=(s,e)=>Hide();
            MinimumSize=Size;
            Deactivate+=(s,e)=>Hide();
        }
        Button MakeButton(string text,Action action){var b=new Button{Text=text,Dock=DockStyle.Fill,BorderSize=1,Margin=new Padding(3),Cursor=Cursors.Hand};b.Click+=(s,e)=>action();return b;}
        Button MakeAction(string text,string command){var b=MakeButton(text,()=>Run(command));waiting.Add(b);return b;}
        public void Theme(bool value){dark=value;BackColor=dark?Color.FromArgb(19,34,49):Color.FromArgb(242,250,254);ForeColor=dark?Color.FromArgb(235,246,255):Color.FromArgb(22,50,74);grid.BackColor=dark?Color.FromArgb(23,40,57):Color.FromArgb(249,253,255);foreach(Control parent in Controls)PaintControls(parent);brand.Image=artwork[dark?3:2];if(backdrop!=null){BackgroundImage=null;backdrop.Dispose();backdrop=null;}var plate=artwork[dark?1:0];if(plate!=null){backdrop=new Bitmap(Width,Height);using(var g=Graphics.FromImage(backdrop)){g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(plate,new Rectangle(Point.Empty,Size),new Rectangle(dark?4:0,0,dark?plate.Width-4:(int)(plate.Width*.82),plate.Height),GraphicsUnit.Pixel);}BackgroundImage=backdrop;}Invalidate();}
        void PaintControls(Control node){node.ForeColor=ForeColor;node.BackColor=grid.BackColor;var button=node as Button;if(button!=null){string role=Convert.ToString(button.Tag);button.BorderColor=dark?Color.FromArgb(57,89,112):Color.FromArgb(185,222,239);button.BackColor=dark?Color.FromArgb(32,59,78):Color.FromArgb(231,246,253);if(role=="primary"){button.BackColor=Color.FromArgb(0,172,224);button.ForeColor=Color.White;button.BorderSize=0;}if(role=="utility"){button.BackColor=grid.BackColor;button.BorderSize=0;}if(role=="automatic"&&button.Text.EndsWith("开")){button.BackColor=dark?Color.FromArgb(12,76,100):Color.FromArgb(209,243,253);}}else if(node is ComboBox){node.BackColor=dark?Color.FromArgb(16,33,48):Color.White;var picker=node as FloatingProfilePicker;if(picker!=null){picker.BorderColor=dark?Color.FromArgb(57,89,112):Color.FromArgb(185,222,239);picker.TextColor=ForeColor;picker.ForeColor=picker.BorderColor;picker.Invalidate();}}if(Convert.ToString(node.Tag)=="muted")node.ForeColor=dark?Color.FromArgb(150,179,200):Color.FromArgb(92,128,153);foreach(Control child in node.Controls)PaintControls(child);if(node==status)node.BackColor=dark?Color.FromArgb(31,54,73):Color.FromArgb(225,244,253);}
        void Busy(){foreach(var c in waiting)c.Enabled=active==0;quiet.Enabled=true;}
        public async void Run(string command){if(active>0&&command!="quiet")return;string id=(profiles.SelectedItem as SavedVoice??new SavedVoice()).id;if(command=="profile"&&profiles.SelectedItem==null){status.Text="请先在主界面保存声音方案。";return;}int current=++revision;active++;Busy();status.Text="正在执行…";try{string text=await actions.Execute(command,id);if(!IsDisposed&&current==revision){status.Text=text;RefreshProfiles();}}catch(Exception error){if(!IsDisposed&&current==revision)status.Text="操作失败："+error.Message;}finally{active--;if(!IsDisposed)Busy();}}
        public void RefreshProfiles(){var cfg=store.Read();string selected=(profiles.SelectedItem as SavedVoice??new SavedVoice()).id;profiles.BeginUpdate();profiles.Items.Clear();foreach(var item in cfg.profiles)profiles.Items.Add(item);var match=cfg.profiles.FirstOrDefault(p=>p.id==selected)??cfg.profiles.FirstOrDefault(p=>FloatingVoiceActions.SameVoice(p.voice,cfg.voice))??cfg.profiles.FirstOrDefault();profiles.SelectedItem=match;profiles.EndUpdate();automatic.Text="自动朗读："+(cfg.harness["codex"]?"开":"关");PaintControls(automatic);}
        public async void RefreshStatus(){if(refreshing||active>0||IsDisposed)return;refreshing=true;int current=revision;try{string text=await actions.Status();if(!IsDisposed&&current==revision)status.Text=text;}catch(Exception error){if(!IsDisposed)status.Text="连接失败："+error.Message;}finally{refreshing=false;}}
        protected override bool ProcessCmdKey(ref Message msg,Keys key){if(key==Keys.Escape){Hide();return true;}return base.ProcessCmdKey(ref msg,key);}
        protected override void OnFormClosing(FormClosingEventArgs e){if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}base.OnFormClosing(e);}
        protected override void Dispose(bool disposing){if(disposing){brand.Image=null;BackgroundImage=null;if(backdrop!=null)backdrop.Dispose();foreach(var image in artwork)if(image!=null)image.Dispose();}base.Dispose(disposing);}
    }

    public sealed class CodexFloatingController:IDisposable {
        readonly SettingsStore store;readonly FloatingPreferences preferences;readonly FloatingEntry entry;readonly FloatingPanel panel;
        readonly Timer timer=new Timer{Interval=1000};readonly Func<IntPtr,IntPtr,FloatingTarget> targetReader;
        readonly WindowFollowEvents windowEvents;
        readonly float scale;FloatingTarget target;bool disposed,themeApplied,darkTheme;int refreshQueued;string recorded="";DateTime nextStatus=DateTime.MinValue,nextTheme=DateTime.MinValue;
        public event Action<bool> EnabledChanged;
        public bool Enabled{get{return preferences.enabled;}}
        public bool EntryVisible{get{return entry.Visible;}}
        public bool PanelVisible{get{return panel.Visible;}}
        public int PositionUpdates{get;private set;}
        internal bool NativeFollowActive{get{return windowEvents!=null&&windowEvents.Listening;}}
        public bool HasTarget{get{return target!=null;}}
        public Rectangle EntryBounds{get{return entry.Bounds;}}
        public Rectangle PanelBounds{get{return panel.Bounds;}}
        public IntPtr EntryHandle{get{return entry.Handle;}}
        public IntPtr PanelHandle{get{return panel.Handle;}}
        public CodexFloatingController(SettingsStore value,IFloatingVoiceService service,Action<Settings,bool> changed,Action openMain,Func<IntPtr,IntPtr,FloatingTarget> reader=null,bool watchOwnWindow=false){
            store=value;preferences=FloatingPreferences.Read(store);using(var g=Graphics.FromHwnd(IntPtr.Zero))scale=g.DpiX/96F;
            var tracker=new CodexWindowTracker();targetReader=reader??tracker.Read;
            entry=new FloatingEntry(scale);panel=new FloatingPanel(store,new FloatingVoiceActions(store,service,changed),scale,openMain,()=>SetEnabled(false),ResetPosition);
            panel.VisibleChanged+=(s,e)=>entry.Expanded=panel.Visible;
            entry.OpenPanel+=TogglePanel;entry.PositionSaved+=SavePosition;
            if(reader==null||watchOwnWindow){windowEvents=new WindowFollowEvents(()=>target==null?IntPtr.Zero:target.handle,QueueRefresh,watchOwnWindow);if(!windowEvents.Listening)timer.Interval=40;}
            timer.Tick+=(s,e)=>Refresh();timer.Start();Refresh();
        }
        public void SetEnabled(bool enabled){preferences.enabled=enabled;preferences.Save(store);if(EnabledChanged!=null)EnabledChanged(enabled);Refresh();}
        public void ResetPosition(){preferences.offsetX=preferences.offsetY=0;preferences.Save(store);Refresh();}
        void SavePosition(){if(target==null)return;var origin=FloatingLayout.Entry(target.bounds,target.workArea,entry.Size,scale,0,0);var clamped=FloatingLayout.Clamp(entry.Bounds,target.workArea);entry.Bounds=clamped;preferences.offsetX=(clamped.X-origin.X)/scale;preferences.offsetY=(clamped.Y-origin.Y)/scale;preferences.Save(store);}
        void QueueRefresh(){if(disposed||System.Threading.Interlocked.Exchange(ref refreshQueued,1)!=0)return;try{entry.BeginInvoke((Action)(()=>{System.Threading.Interlocked.Exchange(ref refreshQueued,0);if(!disposed)Refresh();}));}catch(InvalidOperationException){System.Threading.Interlocked.Exchange(ref refreshQueued,0);}}
        void Record(string reason){if(recorded==reason)return;recorded=reason;try{string folder=Path.Combine(store.Root,"data","logs");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"codex-floating-status.json"),Json.Encode(new{time=DateTime.UtcNow.ToString("o"),enabled=Enabled,targetFound=target!=null,state=reason,followMode=windowEvents!=null&&windowEvents.Listening?"windows-events":"fallback",fallbackIntervalMs=timer.Interval}),new UTF8Encoding(false));}catch(IOException){}catch(UnauthorizedAccessException){}}
        public void Refresh(){
            if(disposed)return;
            target=targetReader(entry.Handle,panel.Handle);
            if(!Enabled||target==null||target.minimized||!target.active){if(panel.Visible)panel.Hide();if(entry.Visible)entry.Hide();Record(!Enabled?"disabled":target==null?"codex-not-found":target.minimized?"codex-minimized":"other-app-active");return;}
            if(!entry.Dragging){var bounds=FloatingLayout.Entry(target.bounds,target.workArea,entry.Size,scale,preferences.offsetX,preferences.offsetY);if(entry.Bounds!=bounds){entry.Bounds=bounds;PositionUpdates++;}}
            if(!entry.Visible)entry.Show();Record("visible");
            if(panel.Visible){var bounds=FloatingLayout.Panel(entry.Bounds,target.workArea,panel.Size,(int)(8*scale));if(panel.Bounds!=bounds){panel.Bounds=bounds;PositionUpdates++;}}
            if(DateTime.UtcNow>=nextTheme){
                nextTheme=DateTime.UtcNow.AddSeconds(3);bool dark=true;
                try{using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")){dark=key==null||Convert.ToInt32(key.GetValue("AppsUseLightTheme",0))==0;}}catch{}
                if(!themeApplied||dark!=darkTheme)ApplyPalette(dark);
            }
            if(panel.Visible&&DateTime.UtcNow>=nextStatus){nextStatus=DateTime.UtcNow.AddSeconds(3);panel.RefreshStatus();}
        }
        public void TogglePanel(){TogglePanel(true);}
        internal void TogglePanel(bool activate){if(panel.Visible)panel.Hide();else OpenPanel(activate);}
        public void OpenPanel(){OpenPanel(true);}
        internal void OpenPanel(bool activate){if(disposed||target==null||!Enabled)return;panel.RefreshProfiles();panel.Bounds=FloatingLayout.Panel(entry.Bounds,target.workArea,panel.Size,(int)(8*scale));panel.Show();if(activate)panel.Activate();panel.RefreshStatus();}
        void ApplyPalette(bool dark){themeApplied=true;darkTheme=dark;entry.BackColor=dark?Color.FromArgb(23,40,57):Color.FromArgb(249,253,255);entry.ForeColor=dark?Color.FromArgb(235,246,255):Color.FromArgb(22,50,74);panel.Theme(dark);entry.Invalidate();}
        public void SavePreview(string directory,bool? forcedTheme=null){if(forcedTheme.HasValue)ApplyPalette(forcedTheme.Value);Directory.CreateDirectory(directory);using(var bitmap=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(bitmap,panel.ClientRectangle);bitmap.Save(Path.Combine(directory,"codex-floating-panel.png"));}using(var bitmap=new Bitmap(entry.Width,entry.Height)){entry.DrawToBitmap(bitmap,entry.ClientRectangle);bitmap.Save(Path.Combine(directory,"codex-floating-entry.png"));}}
        public void Dispose(){if(disposed)return;disposed=true;timer.Stop();timer.Dispose();if(windowEvents!=null)windowEvents.Dispose();panel.Dispose();entry.Dispose();}
    }
}
