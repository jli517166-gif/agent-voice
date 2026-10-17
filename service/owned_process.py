"""Bounded termination of a Popen-owned worker and its Windows venv redirector child."""
import os,subprocess

def terminate(process):
    if process is None or process.poll() is not None:return
    if os.name=='nt':
        # A Windows venv python.exe may only be a redirector. Killing it alone
        # leaves its inference child (and GPU allocations) running.
        try:
            subprocess.run(['taskkill.exe','/PID',str(process.pid),'/T','/F'],stdin=subprocess.DEVNULL,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=5,creationflags=subprocess.CREATE_NO_WINDOW)
        except (OSError,subprocess.TimeoutExpired):pass
    if process.poll() is None:
        process.terminate()
        try:process.wait(timeout=3)
        except subprocess.TimeoutExpired:process.kill();process.wait(timeout=3)
    else:process.wait(timeout=3)
