using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace AgentVoice {
    static class Program {
        [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
        [STAThread]static int Main(string[] args){
            // WinForms targets the v4 CLR for compatibility; opt out of the
            // legacy 260-character policy before the first filesystem call.
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling",false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths",false);
            SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            string root=AppPaths.CanonicalDirectory(AppDomain.CurrentDomain.BaseDirectory);
            var store=new SettingsStore(root);
            if(args.Length==2&&args[0]=="--manual"){using(var manual=new TextDocumentForm(args[1]))Application.Run(manual);return 0;}
            if(args.Contains("--desktop-ui-test"))return DesktopPolishChecks.Run(args);
            if(args.Contains("--quickengine-test"))return QuickEngineChecks.Run(args);
            if(args.Contains("--visual-repair-test"))return VisualRepairChecks.Run(args);
            if(args.Contains("--websetup-unit"))return EmbeddedSetupChecks.Run(store,args);
            if(args.Contains("--websetup-test"))return EmbeddedWebChecks.Run(store,args);
            if(args.Contains("--environmenttest"))return UiChecks.Environment(store,args);
            if(args.Contains("--layout-preview")){using(var preview=new MainForm(store,true)){preview.Text+=" · 布局验收";Application.Run(preview);}return 0;}
            if(args.Contains("--layouttest"))return WindowLayoutRunner.Run(store,args);
            if(args.Contains("--selftest"))return UiChecks.Run(store,args);
            if(args.Contains("--scrolltest"))return ScrollChecks.Run(store,args);
            if(args.Contains("--flowtest"))return UiChecks.Flow(store,args);
            if(args.Contains("--targettest"))return UiChecks.Target(store,args);
            if(args.Contains("--welcometest"))return WelcomeChecks.Run(store,args);
            if(args.Contains("--moduletest"))return ModuleChecks.Run(store,args);
            if(args.Contains("--import-module"))return ModulePackages.ImportCommand(store,args);
            if(args.Contains("--setupvoicetest"))return UiChecks.SetupVoice(store,args);
            if(args.Contains("--native-setup-preview")){using(var preview=new SetupWizard(store))Application.Run(preview);return 0;}
            if(args.Contains("--welcome-preview")||args.Contains("--setup-preview")){using(var preview=SetupEntry.Create(store))Application.Run(preview);return 0;}
            string key;using(var sha=SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(root.ToLowerInvariant()))).Replace("-","").Substring(0,24);
            string wakeName="Local\\AgentVoicePublicWake-"+key;
            bool created;using(var mutex=new Mutex(true,"Local\\AgentVoicePublic-"+key,out created)){
                if(!created){try{using(var wake=EventWaitHandle.OpenExisting(wakeName))wake.Set();}catch(WaitHandleCannotBeOpenedException){}return 0;}
                try{
                    if(!File.Exists(store.FilePath))store.Write(store.Read());
                    var welcomeState=new WelcomeState(store);var preference=welcomeState.Read();
                    if(SetupWizard.NeedsSetup(preference)||args.Contains("--setup"))using(var setup=SetupEntry.Create(store)){
                        using(var setupWake=new EventWaitHandle(false,EventResetMode.AutoReset,wakeName)){
                            var registration=ThreadPool.RegisterWaitForSingleObject(setupWake,(s,t)=>{if(setup.IsHandleCreated&&!setup.IsDisposed)setup.BeginInvoke((Action)(()=>{setup.Show();setup.WindowState=FormWindowState.Normal;setup.Activate();}));},null,Timeout.Infinite,false);
                            try{if(setup.ShowDialog()!=DialogResult.OK)return 0;}finally{registration.Unregister(null);}
                        }
                    }
                    using(var wake=new EventWaitHandle(false,EventResetMode.AutoReset,wakeName))using(var form=new MainForm(store)){
                        var registration=ThreadPool.RegisterWaitForSingleObject(wake,(s,t)=>{if(form.IsHandleCreated&&!form.IsDisposed)form.BeginInvoke((Action)(()=>{form.Show();form.WindowState=FormWindowState.Normal;form.Activate();}));},null,Timeout.Infinite,false);
                        try{Application.Run(form);}finally{registration.Unregister(null);}
                    }return 0;
                }
                catch(Exception exc){Directory.CreateDirectory(Path.Combine(root,"data","logs"));File.AppendAllText(Path.Combine(root,"data","logs","ui-error.log"),DateTime.Now+" "+exc+Environment.NewLine);return 1;}
            }
        }
    }
}
