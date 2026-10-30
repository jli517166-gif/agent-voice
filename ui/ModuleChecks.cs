using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace AgentVoice {
    static class ModuleChecks {
        static string Sha(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        internal static string Package(string directory,string name,Dictionary<string,string> input,string[] components,Dictionary<string,string> defaults,Action<ModuleManifest> mutate=null,string kind="environment",string engine="all"){
            string file=Path.Combine(directory,name+(kind=="engine"?".ttsengine":kind=="singing-engine"?".singengine":kind=="feature"?".avmodule":".ttsenvironment"));var files=input.Select(pair=>new ModuleFile{path=pair.Key,size=Encoding.UTF8.GetByteCount(pair.Value),sha256=Sha(Encoding.UTF8.GetBytes(pair.Value))}).ToArray();
            var manifest=new ModuleManifest{format="agent-voice-module",version=1,kind=kind,engine=engine,name="test",platform="windows-x64",profile="fixture",minimumDriver=kind=="environment"?"0":"",components=components,defaults=defaults,files=files};if(mutate!=null)mutate(manifest);
            using(var zip=ZipFile.Open(file,ZipArchiveMode.Create)){using(var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open(),new UTF8Encoding(false)))writer.Write(Json.Encode(manifest));foreach(var item in input)using(var writer=new StreamWriter(zip.CreateEntry("payload/"+item.Key).Open(),new UTF8Encoding(false)))writer.Write(item.Value);}
            return file;
        }
        static object Install(SettingsStore store,string file){return ModulePackages.Install(store,file,text=>{},CancellationToken.None);}
        public static int Run(SettingsStore ignored,string[] args){
            string directory=Path.GetFullPath(args.Length>1?args[1]:ignored.Resolve("acceptance/modules"));Directory.CreateDirectory(directory);string run=Path.Combine(directory,"run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(run);var errors=new List<string>();int checks=0;
            try{
                var store=new SettingsStore(Path.Combine(run,"bare app (2)"));var cfg=store.Read();store.Write(cfg);string before=File.ReadAllText(store.FilePath);var core=new Dictionary<string,string>{{"runtime/core/python.exe","fixture interpreter"}};var components=new[]{"runtime/core"};var defaults=new Dictionary<string,string>{{"pythonService","runtime/core/python.exe"}};
                try{ModulePackages.Rtx50Requirement(new ModuleManifest{kind="environment",minimumDriver="0"});errors.Add("Legacy environment without RTX 50 runtime was accepted");}catch(InvalidOperationException error){if(!error.Message.Contains("RTX 50"))errors.Add("Legacy RTX 50 rejection was not actionable");}checks++;
                var compatible=new ModuleManifest{kind="environment",gpuCompatibility=new Dictionary<string,GpuRequirement>{{"rtx50",new GpuRequirement{cuda="12.8",minimumDriver="570.26",torch="2.7.1"}}}};
                if(ModulePackages.Rtx50Requirement(compatible).cuda!="12.8")errors.Add("RTX 50 environment profile was not recognized");checks++;
                string package=Package(run,"core",core,components,defaults);Directory.CreateDirectory(store.Resolve("data/module-install"));
                var progress=new List<string>();using(var staleTemp=new FileStream(store.Resolve("data/module-install/pending.json.tmp"),FileMode.Create,FileAccess.ReadWrite,FileShare.None))ModulePackages.Install(store,package,text=>progress.Add(text),CancellationToken.None);checks++;
                if(!progress.Any(text=>text.Contains("100.0%")&&text.Contains("B /")))errors.Add("Module import did not expose byte progress");checks++;
                if(!File.Exists(store.Resolve("runtime/core/python.exe"))||store.Read().token!=cfg.token||store.Read().voice.reference!=cfg.voice.reference)errors.Add("Bare native install changed voice or credentials");
                if(!Json.Encode(Install(store,package)).Contains("\"reused\":true"))errors.Add("Repeated import was not reused");checks++;
                string bad=Package(run,"bad-hash",core,components,defaults,m=>m.files[0].sha256=new string('0',64));string prior=File.ReadAllText(store.FilePath);
                try{Install(store,bad);errors.Add("Corrupt package accepted");}catch(InvalidDataException){}if(File.ReadAllText(store.FilePath)!=prior||File.ReadAllText(store.Resolve("runtime/core/python.exe"))!="fixture interpreter")errors.Add("Corrupt archive changed installation");checks++;
                bad=Package(run,"traversal",core,components,defaults,m=>m.files[0].path="../outside");try{Install(store,bad);errors.Add("Traversal accepted");}catch(InvalidDataException){}checks++;
                bad=Package(run,"credentials",core,components,new Dictionary<string,string>{{"token","evil"}});try{Install(store,bad);errors.Add("Credentials patch accepted");}catch(InvalidDataException){}checks++;
                using(var cancel=new CancellationTokenSource()){cancel.Cancel();try{ModulePackages.Install(store,package,text=>{},cancel.Token);errors.Add("Cancelled extraction accepted");}catch(OperationCanceledException){}}checks++;
                string extra=Package(run,"extra",core,components,defaults);using(var zip=ZipFile.Open(extra,ZipArchiveMode.Update))zip.CreateEntry("unregistered.exe");try{Install(store,extra);errors.Add("Unregistered payload accepted");}catch(InvalidDataException){}checks++;
                var changed=new Dictionary<string,string>{{"runtime/core/python.exe","new interpreter"},{"tools/ffmpeg.exe","new ffmpeg"}};string rollback=Package(run,"rollback",changed,new[]{"runtime/core","tools/ffmpeg.exe"},defaults);Directory.CreateDirectory(store.Resolve("tools"));File.WriteAllText(store.Resolve("tools/ffmpeg.exe"),"old ffmpeg");
                using(var hold=new FileStream(store.Resolve("tools/ffmpeg.exe"),FileMode.Open,FileAccess.Read,FileShare.None))try{Install(store,rollback);errors.Add("Locked commit unexpectedly succeeded");}catch(IOException){}
                if(File.ReadAllText(store.Resolve("runtime/core/python.exe"))!="fixture interpreter"||File.ReadAllText(store.Resolve("tools/ffmpeg.exe"))!="old ffmpeg"||File.Exists(ModulePackages.Pending(store)))errors.Add("Failed commit did not roll back");checks++;
                var env=new Dictionary<string,string>{{"runtime/core/python.exe","fixture interpreter"},{"tts-engines/gpt-sovits/runtime/pyvenv.cfg","home = Z:/old\nexecutable = Z:/old/python.exe\ninclude-system-site-packages = true\n"},{"tts-engines/gpt-sovits/runtime/gpu-packages/placeholder","gpu fixture"},{"tts-engines/gpt-sovits/runtime/Lib/site-packages/000_candidate_gpu.pth","old"},{"tts-engines/gpt-sovits/runtime/Scripts/python.exe","redirector"}};
                string gpt=Package(run,"gpt",env,new[]{"runtime/core","tts-engines/gpt-sovits/runtime"},new Dictionary<string,string>{{"pythonService","runtime/core/python.exe"},{"pythonGpt","tts-engines/gpt-sovits/runtime/Scripts/python.exe"}});Install(store,gpt);
                if(!File.ReadAllText(store.Resolve("tts-engines/gpt-sovits/runtime/pyvenv.cfg")).Contains(store.Resolve("runtime/core")))errors.Add("Bundled environment was not relocated");
                if(!Json.Encode(Install(store,gpt)).Contains("\"reused\":true"))errors.Add("Relocated environment cannot be reused");checks++;
                // Emulate interruption after an original directory was moved aside.
                string id=Guid.NewGuid().ToString("N"),stage="data/module-install/stage-"+id,backup="data/module-backups/"+id;Directory.CreateDirectory(store.Resolve(stage));Directory.CreateDirectory(store.Resolve(backup+"/runtime"));File.Copy(store.FilePath,store.Resolve(backup+"/settings.json"));Directory.Move(store.Resolve("runtime/core"),store.Resolve(backup+"/runtime/core"));Directory.CreateDirectory(store.Resolve("runtime/core"));File.WriteAllText(store.Resolve("runtime/core/python.exe"),"interrupted");
                File.WriteAllText(ModulePackages.Pending(store),Json.Encode(new ModuleJournal{stage=stage,backup=backup,hadSettings=true,moves=new List<ModuleMove>{new ModuleMove{target="runtime/core",backup=backup+"/runtime/core",existed=true}}}));ModulePackages.Recover(store);
                if(File.ReadAllText(store.Resolve("runtime/core/python.exe"))!="fixture interpreter"||File.Exists(ModulePackages.Pending(store)))errors.Add("Interrupted install recovery failed");checks++;
                var engineFiles=new Dictionary<string,string>{{"tts-engines/cosyvoice/source/cosyvoice/cli/cosyvoice.py","source"},{"tts-engines/cosyvoice/models/model/llm.pt","base weights"}};string engine=Package(run,"engine",engineFiles,new[]{"tts-engines/cosyvoice/source","tts-engines/cosyvoice/models/model"},new Dictionary<string,string>{{"cosyRepo","tts-engines/cosyvoice/source"},{"cosyModel","tts-engines/cosyvoice/models/model"}},null,"engine","cosyvoice");Install(store,engine);if(store.Read().engines.cosyModel!="tts-engines/cosyvoice/models/model")errors.Add("Engine defaults not matched");checks++;
                if(store.Read().token!=cfg.token||Json.Encode(store.Read().voice)!=Json.Encode(cfg.voice))errors.Add("Module installation changed voice settings");
                string incomplete=Package(run,"incomplete",new Dictionary<string,string>{{"runtime/core/readme.txt","missing interpreter"}},components,defaults);try{Install(store,incomplete);errors.Add("Environment without Python entry accepted");}catch(InvalidDataException){}checks++;
                string futureDriver=Package(run,"future-driver",core,components,defaults,m=>m.minimumDriver="999.0");try{Install(store,futureDriver);errors.Add("Environment requiring a newer NVIDIA driver was accepted");}catch(InvalidOperationException error){if(!error.Message.Contains("驱动"))errors.Add("Driver rejection was not actionable");}checks++;
                string unconfigured=Package(run,"unconfigured-engine",engineFiles,new[]{"tts-engines/cosyvoice/source","tts-engines/cosyvoice/models/model"},new Dictionary<string,string>(),null,"engine","cosyvoice");try{Install(store,unconfigured);errors.Add("Engine without path defaults accepted");}catch(InvalidDataException){}checks++;
                var longStore=new SettingsStore(Path.Combine(run,new string('a',95),new string('b',95)));Install(longStore,package);if(longStore.Resolve("runtime/core/python.exe").Length<=260||!File.Exists(longStore.Resolve("runtime/core/python.exe")))errors.Add("Long-path installation failed");checks++;
                var featureFiles=new Dictionary<string,string>{{"modules/example/module.exe","fixture executable"},{"modules/example/backend/action.py","fixture code"}};
                Action<ModuleManifest> feature=m=>{m.id="example";m.apiVersion=1;m.moduleVersion="1.0.0";m.entrypoint="modules/example/module.exe";};
                string featurePackage=Package(run,"feature",featureFiles,new[]{"modules/example"},new Dictionary<string,string>(),feature,"feature","");
                Install(store,featurePackage);if(ModulePackages.InstalledFeature(store,"example").apiVersion!=1||ModulePackages.FeatureIds(store).Length!=1)errors.Add("Feature module unavailable after install");checks++;
                if(!Json.Encode(Install(store,featurePackage)).Contains("\"reused\":true"))errors.Add("Feature repeated import was not reused");checks++;
                File.WriteAllText(store.Resolve("modules/example/backend/action.py"),"tampered");try{ModulePackages.InstalledFeature(store,"example");errors.Add("Tampered feature remained launchable");}catch(InvalidDataException){}checks++;
                Install(store,featurePackage);ModulePackages.InstalledFeature(store,"example");ModulePackages.RemoveFeature(store,"example");if(ModulePackages.FeatureIds(store).Length!=0||Directory.Exists(store.Resolve("modules/example")))errors.Add("Removed feature still discoverable");checks++;
                string incompatible=Package(run,"feature-version",featureFiles,new[]{"modules/example"},new Dictionary<string,string>(),m=>{feature(m);m.apiVersion=99;},"feature","");try{Install(store,incompatible);errors.Add("Incompatible feature API accepted");}catch(InvalidDataException){}checks++;
                var singingFiles=new Dictionary<string,string>{{"singing-engines/ace-step/portable/python.exe","fixture singer"},{"singing-engines/ace-step/启动歌声引擎.cmd","fixture launcher"}};
                string singing=Package(run,"ace-step",singingFiles,new[]{"singing-engines/ace-step"},new Dictionary<string,string>(),null,"singing-engine","ace-step");Install(store,singing);if(!File.Exists(store.Resolve("singing-engines/ace-step/portable/python.exe")))errors.Add("Singing engine was not installed independently");checks++;
                var audioFiles=new Dictionary<string,string>(featureFiles){{"modules/example/reference.wav","not allowed"}};string audio=Package(run,"feature-audio",audioFiles,new[]{"modules/example"},new Dictionary<string,string>(),feature,"feature","");try{Install(store,audio);errors.Add("Feature bundled audio accepted");}catch(InvalidDataException){}checks++;
                var overwriteFiles=new Dictionary<string,string>(featureFiles){{"service/tts_bundle.py","outside module"}};string overwrite=Package(run,"feature-outside",overwriteFiles,new[]{"modules/example"},new Dictionary<string,string>(),feature,"feature","");try{Install(store,overwrite);errors.Add("Feature overwrote core files");}catch(InvalidDataException){}checks++;
                if(store.Read().token!=cfg.token||Json.Encode(store.Read().voice)!=Json.Encode(cfg.voice))errors.Add("Feature lifecycle changed user voice or credentials");
            }catch(Exception e){errors.Add(e.ToString());}
            File.WriteAllText(Path.Combine(directory,"module-results.json"),Json.Encode(new{ok=errors.Count==0,checks=checks,errors=errors}));return errors.Count==0?0:1;
        }
    }
}
