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
            SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            string root=AppPaths.CanonicalDirectory(AppDomain.CurrentDomain.BaseDirectory);
            var store=new SettingsStore(root);
            if(args.Contains("--environmenttest"))return UiChecks.Environment(store,args);
            if(args.Contains("--selftest"))return UiChecks.Run(store,args);
            if(args.Contains("--scrolltest"))return ScrollChecks.Run(store,args);
            if(args.Contains("--flowtest"))return UiChecks.Flow(store,args);
            if(args.Contains("--targettest"))return UiChecks.Target(store,args);
            if(args.Contains("--bundletest"))return UiChecks.Bundle(store,args);
            if(args.Contains("--welcometest"))return WelcomeChecks.Run(store,args);
            if(args.Contains("--welcome-preview")){using(var preview=new WelcomeForm(false))Application.Run(preview);return 0;}
            string key;using(var sha=SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(root.ToLowerInvariant()))).Replace("-","").Substring(0,24);
            string wakeName="Local\\AgentVoicePublicWake-"+key;
            bool created;using(var mutex=new Mutex(true,"Local\\AgentVoicePublic-"+key,out created)){
                if(!created){try{using(var wake=EventWaitHandle.OpenExisting(wakeName))wake.Set();}catch(WaitHandleCannotBeOpenedException){}return 0;}
                try{
                    if(!File.Exists(store.FilePath))store.Write(store.Read());
                    var welcomeState=new WelcomeState(store);var preference=welcomeState.Read();string choice="home";
                    if(!preference.seen)using(var welcome=new WelcomeForm(preference.reduceMotion)){
                        if(welcome.ShowDialog()!=DialogResult.OK)return 0;
                        choice=welcome.Choice;welcomeState.Choose(choice,welcome.ReduceMotion);
                    }
                    using(var wake=new EventWaitHandle(false,EventResetMode.AutoReset,wakeName))using(var form=new MainForm(store)){
                        string initialChoice=choice;form.Shown+=(s,e)=>form.ApplyWelcomeChoice(initialChoice);
                        var registration=ThreadPool.RegisterWaitForSingleObject(wake,(s,t)=>{if(form.IsHandleCreated&&!form.IsDisposed)form.BeginInvoke((Action)(()=>{form.Show();form.WindowState=FormWindowState.Normal;form.Activate();}));},null,Timeout.Infinite,false);
                        try{Application.Run(form);}finally{registration.Unregister(null);}
                    }return 0;
                }
                catch(Exception exc){Directory.CreateDirectory(Path.Combine(root,"data","logs"));File.AppendAllText(Path.Combine(root,"data","logs","ui-error.log"),DateTime.Now+" "+exc+Environment.NewLine);return 1;}
            }
        }
    }
}
