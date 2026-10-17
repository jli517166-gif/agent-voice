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
            if(args.Contains("--bundletest"))return UiChecks.Bundle(store,args);
            string key;using(var sha=SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(root.ToLowerInvariant()))).Replace("-","").Substring(0,24);
            string wakeName="Local\\AgentVoicePublicWake-"+key;
            bool created;using(var mutex=new Mutex(true,"Local\\AgentVoicePublic-"+key,out created)){
                if(!created){try{using(var wake=EventWaitHandle.OpenExisting(wakeName))wake.Set();}catch(WaitHandleCannotBeOpenedException){}return 0;}
                try{if(!File.Exists(store.FilePath))store.Write(new Settings());using(var wake=new EventWaitHandle(false,EventResetMode.AutoReset,wakeName))using(var form=new MainForm(store)){var registration=ThreadPool.RegisterWaitForSingleObject(wake,(s,t)=>{if(form.IsHandleCreated&&!form.IsDisposed)form.BeginInvoke((Action)(()=>{form.Show();form.WindowState=FormWindowState.Normal;form.Activate();}));},null,Timeout.Infinite,false);try{Application.Run(form);}finally{registration.Unregister(null);}}return 0;}
                catch(Exception exc){Directory.CreateDirectory(Path.Combine(root,"data","logs"));File.AppendAllText(Path.Combine(root,"data","logs","ui-error.log"),DateTime.Now+" "+exc+Environment.NewLine);return 1;}
            }
        }
    }
}
