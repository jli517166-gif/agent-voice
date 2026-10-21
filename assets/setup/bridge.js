(() => {
  const pending=new Map(), listeners=new Set();let sequence=0;
  const session='s'+Math.random().toString(36).slice(2,10);
  const native=window.chrome?.webview;
  window.setupBridge={
    request(action,args={}){
      if(!native)return Promise.reject(new Error('请从 Agent Voice 程序打开此配置界面。'));
      const id=session+'_'+(++sequence);
      return new Promise((resolve,reject)=>{
        const timeout=setTimeout(()=>{pending.delete(id);reject(new Error('操作仍未返回，请查看过程或返回软件检查任务；未标记成功。'));},20*60*1000);
        pending.set(id,{resolve,reject,timeout});native.postMessage({id,action,args});
      });
    },
    subscribe(fn){listeners.add(fn);return()=>listeners.delete(fn);}
  };
  native?.addEventListener('message',event=>{
    const message=event.data;if(!message||typeof message!=='object')return;
    if(message.type==='result'){
      const operation=pending.get(message.id);if(!operation)return;
      pending.delete(message.id);clearTimeout(operation.timeout);
      if(message.ok)operation.resolve(message.result);else{
        const error=new Error(message.error||'操作未完成。');error.state=message.state;operation.reject(error);
      }
    }else for(const listener of listeners)listener(message);
  });
})();
