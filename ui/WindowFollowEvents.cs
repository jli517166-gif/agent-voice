using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AgentVoice {
    // Out-of-context notifications observe bounds/focus only. No input or hooks
    // are injected into Codex. Keep the delegate alive until all hooks detach.
    sealed class WindowFollowEvents:IDisposable {
        delegate void WinEvent(IntPtr hook,uint type,IntPtr window,int objectId,int childId,uint thread,uint time);
        [DllImport("user32.dll")]static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,WinEvent callback,uint process,uint thread,uint flags);
        [DllImport("user32.dll")]static extern bool UnhookWinEvent(IntPtr hook);
        readonly WinEvent callback;
        readonly List<IntPtr> hooks=new List<IntPtr>();
        bool disposed;
        public bool Listening{get{return hooks.Count==5;}}
        public WindowFollowEvents(Func<IntPtr> target,Action changed,bool includeOwnProcess=false){
            callback=(hook,type,window,objectId,childId,thread,time)=>{
                if(disposed)return;
                if(type==3||(window==target()&&objectId==0))changed();
            };
            uint flags=includeOwnProcess?0U:2U;
            foreach(var pair in new[]{new uint[]{3,3},new uint[]{0x800B,0x800B},new uint[]{0x16,0x17},new uint[]{0x8001,0x8003},new uint[]{0xA,0xB}}){
                IntPtr hook=SetWinEventHook(pair[0],pair[1],IntPtr.Zero,callback,0,0,flags);
                if(hook!=IntPtr.Zero)hooks.Add(hook);
            }
        }
        public void Dispose(){if(disposed)return;disposed=true;foreach(var hook in hooks)UnhookWinEvent(hook);hooks.Clear();}
    }
}
