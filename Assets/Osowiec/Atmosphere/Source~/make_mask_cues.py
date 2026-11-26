"""Render an original, short, low-dynamic-range muffled pressure transient and a respirator lens rub."""
from pathlib import Path
import wave
import numpy as np

out=Path(__file__).resolve().parent.parent/"Resources"/"OsowiecSound"
out.mkdir(parents=True,exist_ok=True)
rate=48000
rng=np.random.default_rng(6051)

def emit(name, signal):
    signal[:120]*=np.linspace(0,1,120)
    signal[-240:]*=np.linspace(1,0,240)
    taper=np.hanning(len(signal))
    signal-=np.sum(signal)*taper/np.sum(taper)
    signal*=.72/max(abs(signal))
    values=(signal*8388607).astype("<i4").view(np.uint8).reshape(-1,4)[:,:3].copy()
    with wave.open(str(out/(name+".wav")),"wb") as f:
        f.setparams((1,3,rate,len(signal),"NONE",""))
        f.writeframes(values.tobytes())

t=np.arange(int(rate*.56))/rate
grain=np.convolve(rng.normal(0,1,len(t)),np.ones(38)/38,"same")
heartbeat=np.sin(2*np.pi*(48*t+.7*(1-np.exp(-t*21))))*(1-np.exp(-t*300))*np.exp(-t*12)
tap=np.maximum(0,t-.102)
heartbeat+=.28*np.sin(2*np.pi*58*tap)*(1-np.exp(-tap*230))*np.exp(-tap*18)
emit("PressurePulse",heartbeat+.10*grain*np.exp(-t*15))

t=np.arange(int(rate*.49))/rate
raw=rng.normal(0,1,len(t))
grain=np.convolve(raw,np.ones(9)/9,"same")-.55*np.convolve(raw,np.ones(24)/24,"same")
envelope=np.sin(np.pi*t/t[-1])**1.3*(.64+.34*np.cos(t*24))
emit("MaskWipe",grain*envelope)
