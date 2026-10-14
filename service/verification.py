"""Ephemeral, source-correlated end-to-end checks; never export verification."""
import copy
import secrets
import threading
import time

class Verification:
    def __init__(self,jobs,clock=time.monotonic):
        self.jobs=jobs; self.clock=clock; self.lock=threading.RLock(); self.checks={}

    def start(self,source):
        with self.lock:
            token='agent-voice-verify-'+secrets.token_hex(3)
            text='语音接入验证 '+secrets.token_hex(3) if source=='airi' else token
            prompt='Reply with exactly this line and nothing else:\n'+text
            item={'id':secrets.token_hex(8),'source':source,'text':text,'prompt':prompt,
                  'state':'waiting','jobId':None,'expires':self.clock()+900,'error':None}
            self.checks[source]=item
            return self.snapshot(source)

    def receipt(self,source,text,result):
        with self.lock:
            item=self.checks.get(source)
            if item and item['state']=='waiting' and self.clock()<item['expires'] and text.strip()==item['text']:
                if result.get('jobId') and not result.get('duplicate'):
                    item['jobId']=result['jobId'];item['state']='playing'
                elif not result.get('ok'):item.update(state='failed',error=result.get('error'))

    def snapshot(self,source):
        with self.lock:
            item=self.checks.get(source)
            if not item:return {'source':source,'state':'unverified'}
            if item['jobId'] and item['state']=='playing':
                try:job=self.jobs.get(item['jobId'])
                except KeyError:item.update(state='expired',error='验证任务已过期')
                else:
                    if job['state']=='completed':
                        result=job.get('result') or {}
                        ok=result.get('played') and (source!='airi' or result.get('output')=='airi')
                        item.update(state='verified' if ok else 'failed',error=None if ok else '未通过目标播放链路')
                    elif job['state'] in ('failed','cancelled'):item.update(state='failed',error=job.get('error'))
            if self.clock()>=item['expires']:item.update(state='expired',error='验证已过期，请重新验证')
            return {k:copy.deepcopy(v) for k,v in item.items() if k!='expires'}

    def snapshot_all(self):
        with self.lock:return {source:self.snapshot(source) for source in self.checks}

    def clear(self):
        with self.lock:self.checks.clear()
