"""Original, deterministic interface cues. No samples, voices or dependencies."""
import math,random,struct,wave
from pathlib import Path

RATE=32000
def cue(index):
    rng=random.Random(1024+index)
    notes=[(130.81,196.0),(164.81,246.94),(146.83,220.0),(196.0,293.66),(261.63,392.0),(220.0,329.63),(174.61,261.63),(196.0,261.63),(130.81,261.63)][index]
    data=[]
    for n in range(int(RATE*.38)):
        t=n/RATE;value=0.0
        for beat,frequency in enumerate(notes):
            local=t-beat*.095
            if local<0:continue
            envelope=min(1,local/.008)*math.exp(-local*19)
            phase=2*math.pi*(frequency*local+frequency*.12*(1-math.exp(-local*15))/15)
            tone=math.sin(phase)+.20*math.sin(3*phase)+.06*math.sin(5*phase)
            value+=tone*envelope*.052
        value+=(rng.random()*2-1)*math.exp(-t*65)*.014
        value*=min(1,max(0,(.38-t)/.035))
        data.append(int(max(-.13,min(.13,value))*32767))
    return struct.pack('<'+'h'*len(data),*data)

def main():
    root=Path(__file__).resolve().parents[1];destination=root/'assets/sfx';destination.mkdir(parents=True,exist_ok=True)
    for index in range(9):
        with wave.open(str(destination/f'step-{index}.wav'),'wb') as output:
            output.setparams((1,2,RATE,0,'NONE','not compressed'));output.writeframes(cue(index))
    evidence=root/'acceptance/keynote-cyber';evidence.mkdir(parents=True,exist_ok=True)
    with wave.open(str(evidence/'interface-sound-demo.wav'),'wb') as output:
        output.setparams((1,2,RATE,0,'NONE','not compressed'))
        for index in range(9):output.writeframes(cue(index)+bytes(int(RATE*.2)*2))
    print('9 original low-level electronic cues generated; no user audio included.')
if __name__=='__main__':main()
