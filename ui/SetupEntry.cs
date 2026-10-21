using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace AgentVoice {
    internal static class SetupEntry {
        internal static Form Create(SettingsStore store){
            try{
                // Probe before constructing a type whose async methods require the SDK.
                foreach(string name in new[]{"Microsoft.Web.WebView2.Core","Microsoft.Web.WebView2.WinForms"})Assembly.Load(name);
                return CreateEmbedded(store);
            }
            catch(FileNotFoundException){return new SetupWizard(store);}
            catch(FileLoadException){return new SetupWizard(store);}
            catch(BadImageFormatException){return new SetupWizard(store);}
            catch(TypeLoadException){return new SetupWizard(store);}
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static Form CreateEmbedded(SettingsStore store){return new EmbeddedSetupForm(store);}
    }
}
