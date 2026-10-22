using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace AgentVoice {
    internal static class PrivacyChecks {
        internal static int Run(string[] args){
            string directory=Path.GetFullPath(args[1]);Directory.CreateDirectory(directory);
            try{
                var store=new SettingsStore(Path.Combine(directory,"new-"+Guid.NewGuid().ToString("N")));var config=store.Read();
                if(config.harness.Values.Any(x=>x)||config.privacy.retainText||config.cleanup.keepLatest!=0)throw new Exception("Fresh privacy defaults unsafe");
                store.Write(config);
                var raw=Json.Object(File.ReadAllText(store.FilePath));raw.Remove("privacy");((Dictionary<string,object>)raw["cleanup"]).Remove("keepLatestBeyondAge");((Dictionary<string,object>)raw["cleanup"])["keepLatest"]=3;((Dictionary<string,object>)raw["harness"])["codex"]=true;
                File.WriteAllText(store.FilePath,Json.Encode(raw));config=store.Read();
                if(!config.cleanup.keepLatestBeyondAge||!config.harness["codex"])throw new Exception("Migration lost an existing opt-in");
                using(var form=new MainForm(store,true)){form.Show();Application.DoEvents();form.VerifyPrivacy(directory);form.Close();}
                File.WriteAllText(Path.Combine(directory,"privacy-ui.json"),"{\"ok\":true,\"newDefaultsOff\":true,\"existingOptInPreserved\":true}");return 0;
            }catch(Exception error){File.WriteAllText(Path.Combine(directory,"privacy-ui-error.txt"),error.ToString());return 1;}
        }
    }
    public sealed partial class MainForm {
        internal void VerifyPrivacy(string directory){
            ShowPage("privacy");Application.DoEvents();
            if(readingChoices.Count!=3||checks["retainText"].Checked||!checks["keepBeyondAge"].Checked)throw new Exception("Privacy controls did not bind to migrated settings");
            if(!pages.ContainsKey("privacy")||PageName("privacy")!="隐私与连接")throw new Exception("Privacy navigation missing");
            foreach(var size in new[]{new Size(1200,850),new Size(1700,1000)}){Size=size;Application.DoEvents();using(var image=new Bitmap(Width,Height)){DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(directory,"privacy-"+size.Width+".png"));}}
            ShowPage("home");Application.DoEvents();using(var image=new Bitmap(Width,Height)){DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(directory,"home-ai-notice.png"));}
        }
    }
}
