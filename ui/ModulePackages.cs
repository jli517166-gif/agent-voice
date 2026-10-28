using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace AgentVoice {
    public sealed class ModuleFile {public string path,sha256;public long size;}
    public sealed class ModuleManifest {
        public string format,kind,name,engine,platform,profile,minimumDriver,id,entrypoint,moduleVersion;
        public int version,apiVersion;public string[] components;public Dictionary<string,string> defaults;public ModuleFile[] files;
    }
    public sealed class ModuleMove {public string target,backup;public bool existed;}
    public sealed class ModuleJournal {public string stage,backup;public bool hadSettings;public List<ModuleMove> moves=new List<ModuleMove>();}
    // No Python is required. A bootstrap environment can be installed on a bare machine.
    public static class ModulePackages {
        public static int ImportCommand(SettingsStore store,string[] args){
            if(args.Length!=2)return 2;string report=store.Resolve("data/module-install/cli-result.json");
            try{var result=Install(store,Path.GetFullPath(args[1]),message=>{},CancellationToken.None);Write(report,result);return 0;}
            catch(Exception error){Write(report,new{ok=false,error=error.Message});return 1;}
        }
        public static string Pending(SettingsStore store){return store.Resolve("data/module-install/pending.json");}
        static void Write(string file,object value){
            Directory.CreateDirectory(Path.GetDirectoryName(file));string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{
                File.WriteAllText(temp,Json.Encode(value),new UTF8Encoding(false));
                for(int attempt=0;;attempt++)try{if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);break;}
                catch(IOException error){int code=error.HResult&0xffff;if(attempt>=5||(code!=32&&code!=33))throw;Thread.Sleep(40*(attempt+1));}
            }finally{if(File.Exists(temp))File.Delete(temp);}
        }
        static T Read<T>(string text){return new JavaScriptSerializer{MaxJsonLength=32*1024*1024}.Deserialize<T>(text);}
        static string Hash(Stream stream){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        static string HashFile(string file){using(var stream=File.OpenRead(file))return Hash(stream);}
        static string Size(long value){string[] units={"B","KB","MB","GB","TB"};double size=value;int unit=0;while(size>=1024&&unit<units.Length-1){size/=1024;unit++;}return size.ToString(unit==0?"0":"0.0")+" "+units[unit];}
        static Version ParsedVersion(string value){Version result;if(!Version.TryParse(value??"",out result))throw new InvalidDataException("环境包的最低 NVIDIA 驱动版本无效");return result;}
        static string NvidiaDriver(){
            try{using(var process=Process.Start(new ProcessStartInfo("nvidia-smi","--query-gpu=driver_version --format=csv,noheader,nounits"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){if(!process.WaitForExit(15000)){try{process.Kill();}catch{}throw new InvalidOperationException("NVIDIA 驱动检测超时");}string output=process.StandardOutput.ReadToEnd().Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();return process.ExitCode==0?output:"";}}
            catch(InvalidOperationException){throw;}catch{return "";}
        }
        static void CheckCompatibility(ModuleManifest manifest){
            if(manifest.kind!="environment"||string.IsNullOrWhiteSpace(manifest.minimumDriver)||manifest.minimumDriver=="0")return;
            Version required=ParsedVersion(manifest.minimumDriver),current;string detected=NvidiaDriver();
            if(!Version.TryParse(detected,out current))throw new InvalidOperationException("未检测到可用的 NVIDIA 驱动；此环境包最低需要 "+required+"。请先安装或更新显卡驱动，再导入环境包。");
            if(current<required)throw new InvalidOperationException("当前 NVIDIA 驱动 "+current+" 低于环境包最低要求 "+required+"。请先更新显卡驱动，再导入环境包。");
        }
        static bool Relative(string path){return !string.IsNullOrEmpty(path)&&!path.Contains('\\')&&!path.Contains(':')&&!path.StartsWith("/")&&path.Split('/').All(p=>p.Length>0&&p!="."&&p!=".."&&!p.EndsWith(".")&&!p.EndsWith(" ")&&p.IndexOfAny(Path.GetInvalidFileNameChars())<0);}
        static bool Under(string path,string folder){return path==folder||path.StartsWith(folder+"/",StringComparison.Ordinal);}
        static string Inside(SettingsStore store,string relative){
            if(!Relative(relative))throw new InvalidDataException("模块路径无效："+relative);
            string path=store.Resolve(relative),root=store.Root.TrimEnd('\\')+"\\";
            if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("模块路径超出软件目录");
            for(string current=path;current!=null&&current.StartsWith(root,StringComparison.OrdinalIgnoreCase);current=Path.GetDirectoryName(current))
                if((Directory.Exists(current)||File.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("安装位置包含目录链接："+relative);
            return path;
        }
        static bool ComponentAllowed(ModuleManifest m,string path){
            if(m.kind=="feature")return System.Text.RegularExpressions.Regex.IsMatch(path??"","^modules/[a-z][a-z0-9-]{0,63}$")&&(m.id==null||path=="modules/"+m.id);
            if(m.kind=="engine"){
                string prefix="tts-engines/"+m.engine+"/";
                return path==prefix+"source"||(m.engine=="gpt-sovits"&&path==prefix+"nltk_data")||(path.StartsWith(prefix+"models/",StringComparison.Ordinal)&&path.Split('/').Length==4);
            }
            return path=="runtime/core"||path=="tools/ffmpeg.exe"||path=="tools/ffprobe.exe"||path=="tools/system-prerequisites"||path=="tts-engines/cosyvoice/runtime"||path=="tts-engines/gpt-sovits/runtime"||path=="data/environment-repair/wheels"||path=="data/environment-repair/downloads";
        }
        static void Validate(ModuleManifest m,string file){
            if(m==null||m.format!="agent-voice-module"||m.version!=1||m.platform!="windows-x64"||!new[]{"engine","environment","feature"}.Contains(m.kind))throw new InvalidDataException("不支持的模块格式、平台或版本");
            if(m.kind=="engine"&&!new[]{"cosyvoice","gpt-sovits"}.Contains(m.engine))throw new InvalidDataException("尚未适配的 TTS 引擎");
            if(!file.EndsWith(m.kind=="engine"?".ttsengine":m.kind=="feature"?".avmodule":".ttsenvironment",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("包后缀和模块类型不一致");
            if(m.files==null||m.files.Length==0||m.files.Length>100000||m.components==null||m.components.Length==0||m.defaults==null)throw new InvalidDataException("模块清单为空或超出限制");
            if(m.components.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=m.components.Length)throw new InvalidDataException("模块目标重复");
            if(m.kind=="environment"&&!m.components.Contains("runtime/core"))throw new InvalidDataException("环境包必须包含主服务运行时");
            foreach(string c in m.components){if(!Relative(c)||!ComponentAllowed(m,c)||m.components.Any(other=>other!=c&&(Under(c,other)||Under(other,c))))throw new InvalidDataException("模块目标不允许："+c);}
            long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var f in m.files){
                if(f==null||!Relative(f.path)||!names.Add(f.path)||!m.components.Any(c=>Under(f.path,c))||f.size<0||f.size>32L*1024*1024*1024||!System.Text.RegularExpressions.Regex.IsMatch(f.sha256??"","^[a-f0-9]{64}$"))throw new InvalidDataException("模块文件清单不合法");
                total=checked(total+f.size);
            }
            foreach(string path in names){string ancestor=path;while(ancestor.Contains('/')){ancestor=ancestor.Substring(0,ancestor.LastIndexOf('/'));if(names.Contains(ancestor))throw new InvalidDataException("文件和目录路径冲突");}}
            if(total>128L*1024*1024*1024)throw new InvalidDataException("模块解压体积超过限制");
            foreach(string c in m.components)if(!m.files.Any(f=>Under(f.path,c)))throw new InvalidDataException("模块目标没有文件："+c);
            if(m.kind=="feature"){
                if(!System.Text.RegularExpressions.Regex.IsMatch(m.id??"","^[a-z][a-z0-9-]{0,63}$")||m.apiVersion!=1||m.components.Length!=1||m.components[0]!="modules/"+m.id||m.entrypoint!="modules/"+m.id+"/module.exe"||string.IsNullOrWhiteSpace(m.name)||m.name.Length>80)throw new InvalidDataException("功能模块标识或主程序接口版本不兼容");
                if(m.defaults.Count!=0||total>128L*1024*1024||m.files.Any(f=>f.path=="modules/"+m.id+"/module.json"||!new[]{".exe",".dll",".py",".json",".md",".txt",".png",".svg",".gif"}.Contains(Path.GetExtension(f.path).ToLowerInvariant()))||!m.files.Any(f=>f.path==m.entrypoint&&f.size>0))throw new InvalidDataException("功能模块不能携带声音权重、录音、改写设置或缺少入口");
                return;
            }
            var allowed=new Dictionary<string,string>{{"pythonService","runtime/core/python.exe"},{"pythonTts","tts-engines/cosyvoice/runtime/tools/python.exe"},{"pythonGpt","tts-engines/gpt-sovits/runtime/Scripts/python.exe"},{"ffmpeg","tools/ffmpeg.exe"}};
            if(m.kind=="engine"){string key=m.engine=="cosyvoice"?"cosy":"gpt";allowed=new Dictionary<string,string>{{key+"Repo","tts-engines/"+m.engine+"/source"},{key+"Model",""}};}
            foreach(var entry in m.defaults){
                if(!allowed.ContainsKey(entry.Key)||!Relative(entry.Value))throw new InvalidDataException("模块不能写入该设置："+entry.Key);
                if(allowed[entry.Key]!=""&&entry.Value!=allowed[entry.Key])throw new InvalidDataException("模块默认路径不匹配");
                if(!m.components.Any(c=>Under(entry.Value,c)))throw new InvalidDataException("默认路径没有对应模块文件");
                if(allowed[entry.Key]==""&&!m.components.Contains(entry.Value))throw new InvalidDataException("基础模型路径不匹配");
            }
            if(m.kind=="engine"){
                string key=m.engine=="cosyvoice"?"cosy":"gpt";
                if(!m.defaults.ContainsKey(key+"Repo")||!m.defaults.ContainsKey(key+"Model"))throw new InvalidDataException("TTS 包必须声明源码与基础模型默认位置");
            }else{
                if(!m.defaults.ContainsKey("pythonService"))throw new InvalidDataException("环境包必须声明主服务 Python 位置");
                foreach(var executable in allowed.Values.Where(p=>m.components.Any(c=>Under(p,c))))
                    if(!m.files.Any(f=>f.path==executable&&f.size>0))throw new InvalidDataException("环境包缺少运行入口："+executable);
            }
        }
        public static ModuleManifest Inspect(string file){using(var zip=ZipFile.OpenRead(file)){var entry=zip.GetEntry("manifest.json");if(entry==null||entry.Length>32*1024*1024)throw new InvalidDataException("缺少模块清单或清单过大");using(var reader=new StreamReader(entry.Open(),Encoding.UTF8)){var m=Read<ModuleManifest>(reader.ReadToEnd());Validate(m,file);return m;}}}
        static FileStream Lock(SettingsStore store){Directory.CreateDirectory(store.Resolve("data"));var stream=new FileStream(store.Resolve("data/engine-operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite);try{if(stream.Length==0){stream.WriteByte(0);stream.Flush();}stream.Lock(0,1);return stream;}catch{stream.Dispose();throw new InvalidOperationException("服务或其他安装操作正在运行，请关闭服务后重试。");}}
        static void Remove(string path){if(Directory.Exists(path))Directory.Delete(path,true);else if(File.Exists(path))File.Delete(path);}
        static void Move(string from,string to){Directory.CreateDirectory(Path.GetDirectoryName(to));if(Directory.Exists(from))Directory.Move(from,to);else File.Move(from,to);}
        static void Restore(SettingsStore store,ModuleJournal journal){
            if(journal==null||!journal.stage.StartsWith("data/module-install/stage-",StringComparison.Ordinal)||!journal.backup.StartsWith("data/module-backups/",StringComparison.Ordinal))throw new InvalidDataException("安装恢复记录损坏，请保留备份并查看日志");
            foreach(var item in Enumerable.Reverse(journal.moves)){
                if(!new[]{new ModuleManifest{kind="environment"},new ModuleManifest{kind="engine",engine="cosyvoice"},new ModuleManifest{kind="engine",engine="gpt-sovits"},new ModuleManifest{kind="feature"}}.Any(m=>ComponentAllowed(m,item.target)))throw new InvalidDataException("恢复记录包含未批准目标");
                string target=Inside(store,item.target),backup=Inside(store,item.backup);
                if(!item.backup.StartsWith(journal.backup+"/",StringComparison.Ordinal))throw new InvalidDataException("安装备份路径异常");
                if(File.Exists(backup)||Directory.Exists(backup)){Remove(target);Move(backup,target);}
                else if(!item.existed)Remove(target);
            }
            string settings=Inside(store,journal.backup+"/settings.json");if(journal.hadSettings&&File.Exists(settings)){File.Copy(settings,store.FilePath,true);}else if(!journal.hadSettings&&File.Exists(store.FilePath))File.Delete(store.FilePath);
            Remove(Inside(store,journal.stage));File.Delete(Pending(store));
        }
        public static void Recover(SettingsStore store){if(!File.Exists(Pending(store)))return;using(var guard=Lock(store)){var journal=Read<ModuleJournal>(File.ReadAllText(Pending(store),Encoding.UTF8));Restore(store,journal);}}
        static void Relocate(SettingsStore container,SettingsStore destination){
            string runtime=container.Resolve("tts-engines/gpt-sovits/runtime"),cfg=Path.Combine(runtime,"pyvenv.cfg"),core=destination.Resolve("runtime/core");
            if(!Directory.Exists(Path.Combine(runtime,"gpu-packages"))||!File.Exists(cfg)||!File.Exists(container.Resolve("runtime/core/python.exe")))return;
            var lines=File.ReadAllLines(cfg).Select(line=>line.StartsWith("home =")?"home = "+core:line.StartsWith("executable =")?"executable = "+Path.Combine(core,"python.exe"):line.StartsWith("include-system-site-packages =")?"include-system-site-packages = false":line);
            File.WriteAllLines(cfg,lines,new UTF8Encoding(false));string pth=Path.Combine(runtime,"Lib/site-packages/000_candidate_gpu.pth");if(File.Exists(pth))File.WriteAllText(pth,"import sys,pathlib; sys.path.insert(0,str(pathlib.Path(sys.prefix)/'gpu-packages'))\n",new UTF8Encoding(false));
        }
        public static object Install(SettingsStore store,string file,Action<string> progress,CancellationToken cancellation){
            progress("正在读取并校验模块清单……");Recover(store);var m=Inspect(file);CheckCompatibility(m);string id=Guid.NewGuid().ToString("N"),stage="data/module-install/stage-"+id,backup="data/module-backups/"+id;
            using(var guard=Lock(store)){
                var journal=new ModuleJournal{stage=stage,backup=backup,hadSettings=File.Exists(store.FilePath)};bool committing=false;
                Directory.CreateDirectory(Inside(store,stage));
                try{
                    long total=m.files.Sum(f=>f.size),completed=0;var drive=new DriveInfo(Path.GetPathRoot(store.Root));if(drive.AvailableFreeSpace<total+128L*1024*1024)throw new IOException("安装空间不足：需要暂存整个模块，并保留原组件用于回滚。");
                    using(var zip=ZipFile.OpenRead(file)){
                        var expected=new HashSet<string>(m.files.Select(f=>"payload/"+f.path),StringComparer.Ordinal);expected.Add("manifest.json");var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var e in zip.Entries){if(!names.Add(e.FullName)||!expected.Contains(e.FullName))throw new InvalidDataException("模块包含未登记或重复文件："+e.FullName);if((e.ExternalAttributes>>16&0xF000)==0xA000)throw new InvalidDataException("模块不能包含链接");}
                        if(names.Count!=m.files.Length+1)throw new InvalidDataException("模块资源不完整");
                        for(int i=0;i<m.files.Length;i++){
                            cancellation.ThrowIfCancellationRequested();var f=m.files[i];var e=zip.GetEntry("payload/"+f.path);if(e==null||e.Length!=f.size)throw new InvalidDataException("模块长度不匹配："+f.path);
                            string target=Inside(store,stage+"/"+f.path);Directory.CreateDirectory(Path.GetDirectoryName(target));
                            using(var input=e.Open())using(var output=File.Create(target)){var buffer=new byte[1024*1024];long count=0;int got;while((got=input.Read(buffer,0,buffer.Length))>0){cancellation.ThrowIfCancellationRequested();count+=got;if(count>f.size)throw new InvalidDataException("模块解压大小异常");output.Write(buffer,0,got);}if(count!=f.size)throw new InvalidDataException("模块数据截断");}
                            if(HashFile(target)!=f.sha256)throw new InvalidDataException("模块校验失败："+f.path);
                            completed+=f.size;progress("校验并暂存 "+(i+1)+" / "+m.files.Length+" · "+Size(completed)+" / "+Size(total)+" · "+(total==0?100d:completed*100d/total).ToString("0.0")+"%");
                        }
                    }
                    if(m.components.Contains("tts-engines/gpt-sovits/runtime"))Relocate(new SettingsStore(Inside(store,stage)),store);
                    var settings=store.Read();bool same=true;
                    foreach(var f in m.files){string path=Inside(store,f.path);if(!File.Exists(path)||HashFile(path)!=HashFile(Inside(store,stage+"/"+f.path))){same=false;break;}}
                    foreach(var entry in m.defaults)if(Convert.ToString(typeof(Engines).GetField(entry.Key).GetValue(settings.engines))!=entry.Value)same=false;
                    if(m.kind=="feature"&&(!File.Exists(Inside(store,"modules/"+m.id+"/module.json"))||Json.Canonical(Json.Object(File.ReadAllText(Inside(store,"modules/"+m.id+"/module.json"),Encoding.UTF8)))!=Json.Canonical(m)))same=false;
                    if(same){Remove(Inside(store,stage));return new{ok=true,reused=true,kind=m.kind,name=m.name,verifiedUsable=false};}
                    cancellation.ThrowIfCancellationRequested();Directory.CreateDirectory(Inside(store,backup));if(journal.hadSettings)File.Copy(store.FilePath,Inside(store,backup+"/settings.json"));
                    Write(Pending(store),journal);committing=true;progress("正在应用模块；此阶段会完成安装或恢复原文件。");
                    foreach(string component in m.components){
                        string target=Inside(store,component),saved=Inside(store,backup+"/"+component),source=Inside(store,stage+"/"+component);bool exists=File.Exists(target)||Directory.Exists(target);
                        journal.moves.Add(new ModuleMove{target=component,backup=backup+"/"+component,existed=exists});Write(Pending(store),journal);
                        if(exists)Move(target,saved);Move(source,target);
                    }
                    foreach(var entry in m.defaults)typeof(Engines).GetField(entry.Key).SetValue(settings.engines,entry.Value);
                    if(m.kind=="feature")Write(Inside(store,"modules/"+m.id+"/module.json"),m);
                    Write(store.FilePath,settings);Write(store.Resolve("data/module-install/last.json"),new{ok=true,kind=m.kind,name=m.name,installedAt=DateTimeOffset.Now.ToString("o"),backup=backup,verifiedUsable=false,profile=m.profile});
                    // Removing the journal is the transaction commit point. No settings or
                    // component changes occur afterward; a leftover stage is disposable.
                    File.Delete(Pending(store));committing=false;try{Remove(Inside(store,stage));}catch(IOException){}
                    return new{ok=true,reused=false,kind=m.kind,name=m.name,backup=backup,verifiedUsable=false};
                }catch{if(committing)Restore(store,journal);else Remove(Inside(store,stage));throw;}
            }
        }
        public static string[] FeatureIds(SettingsStore store){string root=store.Resolve("modules");if(!Directory.Exists(root))return new string[0];return Directory.GetDirectories(root).Select(Path.GetFileName).Where(id=>System.Text.RegularExpressions.Regex.IsMatch(id,"^[a-z][a-z0-9-]{0,63}$")).ToArray();}
        public static ModuleManifest InstalledFeature(SettingsStore store,string id){
            if(!System.Text.RegularExpressions.Regex.IsMatch(id??"","^[a-z][a-z0-9-]{0,63}$"))throw new InvalidDataException("模块标识无效");
            string record=Inside(store,"modules/"+id+"/module.json");if(new FileInfo(record).Length>32L*1024*1024)throw new InvalidDataException("模块记录过大，请重新安装");
            var m=Read<ModuleManifest>(File.ReadAllText(record,Encoding.UTF8));Validate(m,id+".avmodule");if(m.id!=id)throw new InvalidDataException("模块目录与标识不符");
            foreach(var f in m.files){string path=Inside(store,f.path);if(!File.Exists(path)||new FileInfo(path).Length!=f.size||HashFile(path)!=f.sha256)throw new InvalidDataException("模块文件损坏，请重新导入："+f.path);}return m;
        }
        public static void RemoveFeature(SettingsStore store,string id){
            if(!System.Text.RegularExpressions.Regex.IsMatch(id??"","^[a-z][a-z0-9-]{0,63}$"))throw new InvalidDataException("模块标识无效");
            using(var guard=Lock(store)){string path=Inside(store,"modules/"+id);if(!Directory.Exists(path))throw new InvalidOperationException("模块尚未安装");Move(path,Inside(store,"data/module-backups/removed-"+id+"-"+Guid.NewGuid().ToString("N")));}
        }
    }
}
