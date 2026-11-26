"""Render two original mono Foley cues for the Osowiec equipment interactions."""
from pathlib import Path
import wave

import numpy as np
from scipy import signal


RATE = 48000
OUTPUT = Path(__file__).resolve().parent.parent / "Resources" / "OsowiecSound"
RNG = np.random.default_rng(260126)


def axis(seconds):
    return np.arange(round(seconds * RATE)) / RATE


def noise(t, lo, hi):
    white = RNG.normal(size=t.size)
    sos = signal.butter(3, [lo, hi], fs=RATE, btype="bandpass", output="sos")
    band = signal.sosfilt(sos, white)
    return band / (np.std(band) + 1e-8)


def env(t, attack, decay):
    return -np.expm1(-t / attack) * np.exp(-t / decay)


def place(mix, event, at, gain=1):
    i = round(at * RATE)
    count = min(len(event), len(mix) - i)
    mix[i:i+count] += gain * event[:count]


def resonance(t, base, sweep, fall):
    phase = 2*np.pi*(base*t + sweep*fall*(1 - np.exp(-t/fall)))
    return np.sin(phase)


def grenade():
    t = axis(1.94)
    snap = noise(t, 900, 5600) * env(t,.0018,.038)
    bloom = resonance(t, 52, 104, .052) * env(t,.009,.205)
    crunch = noise(t, 85, 680) * env(t,.007,.173)
    air = noise(t, 130, 1280) * env(t,.043,.30)
    mix = .63*bloom + .43*crunch + .37*snap + .15*air
    # The quieter returns and irregular grit give the drawn burst a sense of distance.
    for delay,gain in [(.083,.16),(.174,.12),(.284,.065)]:
        place(mix,.5*bloom+.13*crunch+.09*snap,delay,gain)
    for offset in [.14,.18,.25,.32,.41,.51,.64,.78,.93]:
        short = axis(RNG.uniform(.07,.14))
        tap = noise(short, 540, 3700)*env(short,.0017,.013)
        tick = resonance(short,RNG.uniform(380,660),210,.015)*env(short,.002,.023)
        place(mix,.44*tap+.47*tick,offset,RNG.uniform(.12,.26)*(1-offset*.54))
    return mix


def smoke():
    t = axis(2.38)
    # Latch, first puff, two broad overlapping breath bands and a soft granular tail.
    click = (.21*resonance(t,584,740,.021) + .29*noise(t,1200,4400))*env(t,.002,.026)
    puff = .28*noise(t,180,1040)*env(t,.008,.16)
    mix = click+puff
    airy = axis(2.24)
    shape = env(airy,.095,.83) * (.77 + .15*np.cos(airy*8.4) + .08*np.cos(airy*13.1))
    hiss = .19*noise(airy,930,5600) + .105*noise(airy,330,1750)
    place(mix,hiss*shape,.046)
    rattle=axis(.24)
    place(mix,.21*resonance(rattle,311,390,.016)*env(rattle,.002,.033),.07)
    for at in [.10,.22,.36,.52]:
        s=axis(.11)
        place(mix,noise(s,1300,3200)*env(s,.007,.020),at,.047)
    return mix


def write(name,data,peak):
    highpass=signal.butter(2,32,btype="highpass",fs=RATE,output="sos")
    data=signal.sosfilt(highpass,data)
    data = np.tanh(data*1.35)
    end = round(.09*RATE)
    data[-end:] *= np.linspace(1,0,end)**2
    data *= peak/max(abs(data))
    pcm = np.rint(data * 8388607).astype("<i4").view(np.uint8).reshape(-1,4)[:,:3]
    with wave.open(str(OUTPUT/(name+".wav")),"wb") as handle:
        handle.setparams((1,3,RATE,len(data),"NONE","not compressed"))
        handle.writeframes(pcm.tobytes())
    print(f"{name}: {len(data)/RATE:.2f} s at {20*np.log10(peak):.1f} dBFS, RMS {20*np.log10(np.sqrt(np.mean(data**2))):.1f} dBFS")


if __name__=="__main__":
    write("GrenadeBurst",grenade(),.88)
    write("SmokeBloom",smoke(),.67)

