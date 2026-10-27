using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace AgentVoice {
 public sealed class DownloadJob:IDisposable {
  public readonly string Token=Guid.NewGuid().ToString("N");
  readonly string prefix;readonly Action ended;bool disposed;
  public DownloadJob(SettingsStore store,Action onEnd){prefix=store.Resolve("data/download-control/"+Token);Directory.CreateDirectory(Path.GetDirectoryName(prefix));ended=onEnd;}
  public bool Paused {get{return File.Exists(prefix+".pause");}}
  public bool Cancelled {get{return File.Exists(prefix+".cancel");}}
  public void Pause(){if(disposed)throw new InvalidOperationException("下载任务已结束。");File.WriteAllText(prefix+".pause","");}
  public void Resume(){File.Delete(prefix+".pause");}
  public void Cancel(){File.WriteAllText(prefix+".cancel","");}
  public object Snapshot(){
   var data=ReadState();string status=Json.Text(data,"state");
   return new{active=!disposed,paused=Paused,cancelled=Cancelled,status=Paused?(status=="paused"?"paused":"pausing"):"running",message=Paused?(status=="paused"?"下载已暂停，点击继续下载":"正在暂停，等待当前数据块或安装步骤结束"):(status=="paused"?"正在继续下载":Json.Text(data,"message"))};
  }
  Dictionary<string,object> ReadState(){try{using(var file=new FileStream(prefix+".json",FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(file,Encoding.UTF8))return Json.Object(reader.ReadToEnd());}catch(IOException){return new Dictionary<string,object>();}catch(ArgumentException){return new Dictionary<string,object>();}}
  void Report(string message,long count=0,long total=0,string state="running"){
   string temp=prefix+"."+Guid.NewGuid().ToString("N")+".tmp";
   File.WriteAllText(temp,Json.Encode(new{message=message,downloadedBytes=count,totalBytes=total,state=state}),new UTF8Encoding(false));
   if(File.Exists(prefix+".json"))File.Replace(temp,prefix+".json",null);else File.Move(temp,prefix+".json");
  }
  public async Task Check(){
   bool waited=false;
   while(Paused){if(Cancelled)throw new OperationCanceledException("下载已停止；已完成文件和可续传断点已保留。");if(!waited){var previous=ReadState();long count=0,total=0;long.TryParse(Json.Text(previous,"downloadedBytes"),out count);long.TryParse(Json.Text(previous,"totalBytes"),out total);Report("下载已暂停",count,total,"paused");waited=true;}await Task.Delay(80);}
   if(Cancelled)throw new OperationCanceledException("下载已停止；已完成文件和可续传断点已保留。");
  }
  async Task<T> Network<T>(Task<T> pending,HttpWebRequest request,int timeout){
   var end=DateTime.UtcNow.AddMilliseconds(timeout);
   while(!pending.IsCompleted){
    if(Cancelled||DateTime.UtcNow>=end){request.Abort();pending.ContinueWith(task=>{var observed=task.Exception;},TaskContinuationOptions.OnlyOnFaulted);if(Cancelled)throw new OperationCanceledException("下载已停止；已完成文件和可续传断点已保留。");throw new WebException("下载网络响应超时，断点已保留。",WebExceptionStatus.Timeout);}
    await Task.WhenAny(pending,Task.Delay(100));
   }
   return await pending;
  }
  public async Task Copy(string url,string target,Action<string> progress){
   string partial=target+".partial";int failures=0;
   while(true){
    await Check();long offset=File.Exists(partial)?new FileInfo(partial).Length:0;
    var request=(HttpWebRequest)WebRequest.Create(url);request.ReadWriteTimeout=15000;request.Timeout=20000;
    if(offset>0)request.AddRange(offset);
    try{
     using(var response=(HttpWebResponse)await Network(request.GetResponseAsync(),request,20000))using(var source=response.GetResponseStream()){
      if(response.StatusCode==HttpStatusCode.PartialContent){string expected="bytes "+offset+"-";if(!(response.Headers["Content-Range"]??"").StartsWith(expected))throw new InvalidDataException("下载范围不匹配。");}
      else offset=0;
      long total=response.ContentLength>0?offset+response.ContentLength:0,count=offset;
      using(var output=new FileStream(partial,offset>0?FileMode.Append:FileMode.Create,FileAccess.Write,FileShare.Read)){
       byte[] block=new byte[65536];int bytes;DateTime last=DateTime.MinValue;
       while(true){await Check();bytes=await Network(source.ReadAsync(block,0,block.Length),request,15000);if(bytes==0)break;await output.WriteAsync(block,0,bytes);count+=bytes;if(count>128L*1024*1024)throw new InvalidDataException("引导文件过大。");if((DateTime.UtcNow-last).TotalMilliseconds>500){Report("正在下载主服务 Python",count,total);progress("正在下载主服务 Python · "+(count/1048576d).ToString("F1")+" MB");last=DateTime.UtcNow;}}
      }
      if(total>0&&count!=total)throw new IOException("下载连接提前结束。");
     }
     await Check();if(File.Exists(target))File.Delete(target);File.Move(partial,target);return;
    }catch(WebException error){if(error.Response is HttpWebResponse&&((HttpWebResponse)error.Response).StatusCode==HttpStatusCode.RequestedRangeNotSatisfiable){error.Response.Dispose();File.Delete(partial);}if(++failures>=4)throw;}
     catch(IOException){if(++failures>=4)throw;}
    await Check();await Task.Delay(300);
   }
  }
  public void Dispose(){if(disposed)return;disposed=true;ended();foreach(string suffix in new[]{".pause",".cancel",".json"})try{File.Delete(prefix+suffix);}catch(IOException){} }
 }
}
