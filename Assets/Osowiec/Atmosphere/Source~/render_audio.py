"""Render the original Osowiec field recording sound bank. No source samples required.
Python 3, numpy and scipy. All curves and instrument designs are editable below.
Usage: python render_audio.py. Output: signed 24-bit 48 kHz PCM masters for Unity.
"""
from pathlib import Path
import wave
import numpy as np
from scipy import signal

RATE = 48000
OUT = Path(__file__).resolve().parents[1] / 'Resources' / 'OsowiecSound'
RNG = np.random.default_rng(191608)
TAU = np.pi * 2


def clock(duration):
    return np.arange(int(round(duration * RATE))) / RATE


def noise(t, low, high):
    b = signal.butter(2, [low, high], btype='bandpass', fs=RATE, output='sos')
    x = signal.sosfilt(b, RNG.standard_normal(len(t)))
    return x / max(.001, np.sqrt(np.mean(x*x)))


def burst(t, start, attack, decay):
    age = np.maximum(0, t-start)
    return (1 - np.exp(-age / attack)) * np.exp(-age / decay)


def bump(t, start, duration):
    phase = np.clip((t-start)/duration, 0, 1)
    return np.sin(np.pi * phase)**2


def tone(t, hz):
    return np.sin(TAU * hz * t)


def knock(t, start, base, modes, decay):
    u = np.maximum(0, t-start)
    return sum(g*tone(u, base*ratio)*burst(t, start, .0011, decay/(.8+i*.24))
               for i, (ratio, g) in enumerate(modes))


def place(buf, clip, when, gain=1):
    i = int(when * RATE)
    count = min(len(buf)-i, len(clip))
    if count > 0:
        buf[i:i+count] += clip[:count] * gain


def reflections(x, taps):
    y = x.copy()
    for delay, gain in taps:
        i = round(delay * RATE)
        if i < len(x):
            y[i:] += x[:-i] * gain
    return y


def seam(x, seconds):
    overlap = int(seconds*RATE)
    a = np.linspace(0, np.pi/2, overlap)
    # Sequence: middle, then tail crossfaded into the beginning; wrap to middle.
    wrap = np.cos(a)*x[-overlap:] + np.sin(a)*x[:overlap]
    return np.concatenate([x[overlap:-overlap], wrap])


def write(name, samples, peak=.68, loop=False):
    x = np.array(samples, dtype=np.float64)
    x -= np.mean(x, axis=0)
    if not loop:
        edge = int(.005*RATE)
        ramp = np.linspace(0, 1, edge)
        if x.ndim == 2: ramp = ramp[:, None]
        x[:edge] *= ramp
        x[-edge:] *= ramp[::-1]
    x *= peak/max(.000001, np.max(np.abs(x)))
    encoded = np.rint(x.reshape(-1)*8388607).astype('<i4').view(np.uint8).reshape(-1, 4)
    with wave.open(str(OUT / (name+'.wav')), 'wb') as out:
        out.setparams((x.shape[1] if x.ndim == 2 else 1, 3, RATE, 0, 'NONE', 'not compressed'))
        out.writeframes(encoded[:, :3].copy().tobytes())


def step(surface, variant):
    t = clock(.72)
    speed = RNG.uniform(.87, 1.13)
    t = t * speed
    heel = RNG.uniform(.013, .027)
    toe = RNG.uniform(.075, .13)
    body = knock(t, heel, RNG.uniform(57, 76), [(1,.64),(1.43,.25),(2.7,.12)], .051)
    leather = noise(t, 380, 1750)*(.085*burst(t, heel, .003, .045) + .046*burst(t, toe, .009, .07))
    if surface == 'Mud':
        puddle = noise(t, 175, 850)*(.38*burst(t, heel, .012, .085) + .18*burst(t, toe, .009, .047))
        suck = .18*bump(t, .17, .16)*(noise(t, 300, 1080) + .3*tone(t, 100+t*260))
        return body*.77 + puddle + suck + leather*.48
    if surface == 'Timber':
        wood = knock(t, heel, RNG.uniform(134, 162), [(1,.53),(1.76,.24),(2.56,.19),(3.9,.12)], .081)
        wood += knock(t, toe, 128+variant*4, [(1,.2),(2.21,.12)], .052)
        creak = bump(t, toe+.015, .24)*(.12*tone(t, 391+14*np.sin(t*36))+.064*noise(t, 710, 2300))
        return wood + creak + body + leather
    gravel = noise(t, 1100, 5200)*(.37*burst(t, heel, .002, .03)+.13*burst(t, toe, .002, .05))
    scuff = .11*noise(t, 440, 2750)*bump(t, .14, .10)
    return reflections(body + gravel + leather + scuff, [(.034,.22), (.067,-.12), (.107,.09)])


def drop(duration=.34):
    t = clock(duration)
    f = RNG.uniform(480, 1340)
    return knock(t, .005, f, [(1,.62),(1.83,.2),(2.76,.09)], RNG.uniform(.028,.067)) + noise(t, 900, 4500)*burst(t,.004,.001,.01)*.10


def rain(t):
    delta = RNG.uniform(0, TAU)
    wind = .80+.14*np.sin(t*.57+delta)+.05*np.sin(t*1.71+delta)
    x = wind*(.18*noise(t, 360, 4100)+.055*noise(t, 3700, 9100)) + .06*noise(t, 74, 320)
    for _ in range(int(t[-1]*8)):
        place(x, drop(.13), RNG.uniform(0, max(.01,t[-1]-.15)), RNG.uniform(.022,.11))
    return seam(x,.65)


def room(t):
    x = .028*noise(t, 65, 230)+.012*noise(t, 240, 730)
    for when in [1.15,3.82,4.05,7.6,9.61,13.08,13.7,17.23,19.56,22.09,25.53,27.45,27.8,30.24]:
        place(x, reflections(drop(.65),[(.043,.47),(.079,-.23),(.145,.17),(.237,.1)]), when+RNG.uniform(-.2,.2), RNG.uniform(.03,.11))
    return seam(x, .65)


def breaths():
    t = clock(6.8)
    x = np.zeros_like(t)
    for at, length in [(.13,1.07),(3.34,1.23)]:
        x += .28*noise(t, 170, 740)*bump(t, at, length)
        x += .10*noise(t, 610, 1370)*bump(t, at+.17, length*.66)
        x += .27*noise(t, 95, 450)*bump(t, at+length+.19, 1.39)
        x += knock(t, at+length+.15, 91, [(1,.075),(2.43,.023)], .039)
    return x


def metal(t, at, size):
    return knock(t, at, size, [(1,.33),(2.36,.23),(4.04,.18),(6.19,.12)], .083) + .21*noise(t, 390, 4200)*burst(t,at,.0012,.026)


def door():
    t = clock(1.64)
    rub = (tone(t, 96+np.sin(t*19)*.55)+.4*tone(t, 287+1.4*np.sin(t*17)))
    x = .25*rub*bump(t,.14,.99)*(.5+.3*np.sin(t*48)) + .19*noise(t,150,610)*bump(t,.21,.78)
    x += metal(t, .04, 207) + .64*metal(t, .125, 194)
    x += knock(t, 1.17, 67, [(1,1.2),(2.46,.45)], .137) + .72*metal(t, 1.2, 165)
    return reflections(x,[(.047,.26),(.086,-.17),(.159,.08)])


def shot(interior, variant):
    t = clock(1.69 if interior else 1.25)
    start = .009
    crack = noise(t, 800, 8200)*burst(t,start,.0009,.012)
    wash = noise(t, 122, 1700)*burst(t,start,.002,.050)
    bass = knock(t,start, 62+variant*4, [(1,.85),(1.77,.42),(3.25,.3)], .102)
    x = .75*crack + .84*wash + bass
    if interior:
        echo = reflections(x,[(.026,.69),(.045,-.46),(.074,.34),(.103,.25),(.142,.19),(.189,-.16),(.233,.12),(.331,.072)])
        return echo + .49*noise(t,155,1460)*burst(t, .045, .009, .22)
    return reflections(x,[(.146,.15),(.312,-.085),(.441,.04)]) + .15*noise(t,90,750)*burst(t,.104,.03,.195)


def reload_cue():
    t = clock(1.42)
    x = .29*noise(t, 370, 1500)*(bump(t,.05,.26)+bump(t,.52,.22))
    for at, pitch, level in [(.11,455,.58),(.16,302,.38),(.32,596,.33),(.69,260,.70),(.91,173,.90),(.96,430,.44)]:
        x += level*metal(t,at,pitch)
    return x


def motif(win):
    t = clock(4.80)
    notes = [146.83, 220, 246.94, 293.66] if win else [146.83, 220, 174.61, 146.83]
    x = np.zeros_like(t)
    for j, hz in enumerate(notes):
        at = [.08,.58,1.22,1.98][j]
        age = np.maximum(0,t-at)
        ex = burst(t,at,.07,.58+j*.17)
        brass = sum((.68/i)*tone(age, hz*i) * np.exp(-age*.28*i) for i in range(1,6))
        x += ex*(brass+.032*noise(t, 300, 900)) + .33*ex*tone(age,hz*.5)
    return reflections(x,[(.091,.19),(.182,.12),(.359,.055)])


def shellac_waltz():
    beat = .51
    result = np.zeros(int(16*3*beat*RATE))
    # Original sixteen-bar parlor waltz. Numbers are MIDI pitches, three crotchets to a bar.
    tune = [(69,72,77),(76,74,72),(70,74,77),(76,74,70),
            (69,72,76),(74,72,69),(67,71,74),(73,69,67),
            (69,74,77),(81,79,77),(76,72,69),(70,72,74),
            (77,76,72),(74,70,67),(69,73,76),(74,69,62)]
    harmony = [(50,57,65),(48,55,64),(43,50,58),(45,52,61),
               (50,57,65),(41,48,57),(43,50,59),(45,52,61),
               (50,57,65),(46,53,62),(48,55,64),(43,50,58),
               (41,48,57),(43,50,58),(45,52,61),(50,57,65)]
    def instrument(midi, length, bow=False):
        age=clock(length)
        f=440*2**((midi-69)/12)
        wobble = (.047*np.sin(TAU*5.3*age)) if bow else 0
        phase=TAU*f*age+wobble
        if bow:
            y=sum(np.sin(i*phase)*np.exp(-i*.32)/i for i in range(1,9))
            env=bump(age,0,length)*.8 + .22*burst(age,.006,.043,.18)
        else:
            y=sum(np.sin(i*phase)*np.exp(-age*(1.8+i*1.1))/i for i in range(1,6))
            env=burst(age,0,.008,.25)
        return y*env
    for bar,(notes,chord) in enumerate(zip(tune,harmony)):
        at=bar*3*beat
        place(result,instrument(chord[0],.48),at,.44)
        for b,midi in enumerate(notes):
            place(result,instrument(midi,beat*.95,True),at+b*beat+.012,.45)
            if b:
                for pitch in chord[1:]:
                    place(result,instrument(pitch,.37),at+b*beat,.29)
    # Mild turntable wow, restricted acoustic-horn bandwidth and shellac surface ticks.
    t=np.arange(len(result))/RATE
    pos=np.arange(len(result))+12*np.sin(TAU*1.3*t)+3.5*np.sin(TAU*5.2*t)
    result=np.interp(np.mod(pos,len(result)),np.arange(len(result)),result)
    result=signal.sosfilt(signal.butter(3,[230,3150],btype='bandpass',fs=RATE,output='sos'),result)
    result+=.011*noise(t,730,3900)
    for at in np.arange(.07,t[-1]-.05,.77):
        place(result, noise(clock(.018),1300,4100)*burst(clock(.018),0,.0008,.0024),at,RNG.uniform(.008,.037))
    fade=int(.024*RATE)
    result[:fade]*=np.linspace(0,1,fade)
    result[-fade:]*=np.linspace(1,0,fade)
    return result


def render():
    OUT.mkdir(parents=True, exist_ok=True)
    for ground in ['Mud','Timber','Brick']:
        for i in range(5):
            write(f'Step{ground}{i+1}', step(ground,i), RNG.uniform(.54,.68))
    # Bed duration after overlap is 32 or 22 seconds. Independent left/right panoramas.
    write('Rain', np.column_stack([rain(clock(22.65)),rain(clock(22.65))]), .54, True)
    write('Room', np.column_stack([room(clock(32.65)),room(clock(32.65))]), .45, True)
    t = clock(2.9)
    write('Distant', .48*noise(t,34,150)*burst(t,.10,.07,.58)+.17*noise(t,165,480)*burst(t,.17,.14,.28), .48)
    write('Door', door(), .75)
    write('Breathing', breaths(), .48)
    t = clock(.67)
    for i in range(3):
        write(f'Drop{i+1}', reflections(drop(.6),[(.047,.19),(.085,.09)]), .42)
        for inside in (True,False):
            write(('VaultShot' if inside else 'FieldShot')+str(i+1), shot(inside,i), .86)
    write('Reload', reload_cue(), .65)
    write('Cycle', .64*metal(t,.023,302)+.52*metal(t,.133,429)+.29*noise(t,330,1460)*bump(t,.04,.13), .54)
    write('MaskStrap', .32*noise(t,340,2300)*bump(t,.04,.18)+.32*metal(t,.24,397), .46)
    write('MapPaper', noise(t,880,5200)*(.32*bump(t,.03,.15)+.29*bump(t,.16,.18)+.12*bump(t,.31,.11)), .43)
    write('Pencil', noise(t,1350,6200)*(.34*bump(t,.04,.19)+.14*bump(t,.24,.09)), .40)
    write('BrickChip', noise(t,1200,5700)*burst(t,.02,.001,.033) + .85*metal(t,.044,290) + .28*metal(t,.13,830), .50)
    write('PlankChip', knock(t,.013,172,[(1,.9),(1.68,.5),(3.18,.2)],.10) + .28*noise(t,820,4500)*burst(t,.016,.001,.03), .54)
    write('MudHit', noise(t,85,580)*burst(t,.012,.004,.065) + .37*noise(t,640,1370)*burst(t,.025,.015,.057), .46)
    write('Marker', knock(t,.02,262,[(1,.72),(2.77,.26)],.069)+.11*noise(t,800,3500)*burst(t,.02,.002,.03), .58)
    write('Order', .73*metal(t,.025,312)+.42*metal(t,.111,462), .51)
    write('Victory', motif(True), .68)
    write('Defeat', motif(False), .64)
    write('ShellacWaltz',shellac_waltz(),.65)
    t=clock(1.65)
    write('Pneumatic', .77*metal(t,.027,194)+.55*noise(t,380,3500)*burst(t,.085,.005,.19)
        +.42*knock(t,.20,92,[(1,1),(2.6,.24)],.10)+.30*metal(t,.46,267),.67)
    write('Optics', .51*metal(t,.035,726)+.24*metal(t,.12,602)
        +.17*noise(t,780,2770)*bump(t,.10,.23)+.43*metal(t,.33,434),.51)
    t=clock(3.2)
    slide=noise(t,58,390)*bump(t,.07,1.60)*.46+noise(t,600,2840)*bump(t,.14,1.23)*.19
    for i in range(26):
        at=.11+RNG.beta(1.25,2.8)*2.1
        slide+=knock(t,at,RNG.uniform(56,208),[(1,.34),(1.71,.15)],RNG.uniform(.04,.15))*RNG.uniform(.22,1)
    write('Earthfall',slide,.76)
    t = clock(1.6)
    write('Capture', knock(t,.04,392,[(1,.69),(2.62,.22)],.29)+knock(t,.45,587.33,[(1,.67),(2.65,.17)],.37), .53)
    print('Rendered', len(list(OUT.glob('*.wav'))), 'PCM24 masters to', OUT)


if __name__ == '__main__':
    render()
    from render_field_foley import render as render_field_revision
    render_field_revision()
