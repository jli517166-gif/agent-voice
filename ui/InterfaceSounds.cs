using System;
using System.IO;
using System.Media;
using System.Threading;

namespace AgentVoice {
    internal sealed class InterfaceSounds:IDisposable {
        readonly object gate=new object();SoundPlayer player;long epoch;bool disposed;
        internal static bool Allowed(bool enabled,bool testing,bool serviceListening){return enabled&&!testing&&!serviceListening;}
        public void Play(string root,int step){
            long version=Interlocked.Increment(ref epoch);string file=Path.Combine(root,"assets","sfx","step-"+Math.Max(0,Math.Min(8,step))+".wav");
            if(!File.Exists(file))return;
            ThreadPool.QueueUserWorkItem(_=>{SoundPlayer next=null;try{next=new SoundPlayer(file);next.Load();lock(gate){if(disposed||version!=epoch)return;if(player!=null){player.Stop();player.Dispose();}player=next;next=null;player.Play();}}catch(InvalidOperationException){}catch(IOException){}finally{if(next!=null)next.Dispose();}});
        }
        public void Stop(){Interlocked.Increment(ref epoch);lock(gate){if(player!=null){player.Stop();player.Dispose();player=null;}}}
        public void Dispose(){lock(gate){disposed=true;Stop();}}
    }
}
