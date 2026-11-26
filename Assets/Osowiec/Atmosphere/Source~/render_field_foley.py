"""Boot recordings for the fort and four sample-aligned layers of the Osowiec waltz.

Run with numpy, scipy and soundfile. Input recording licences are in StepRecordings.
The field arrangement quotes the opening A-C-F and E-D-C phrase of the HQ gramophone.
"""
from pathlib import Path
from math import gcd
import numpy as np
import soundfile as sf
from scipy import signal
from render_audio import RATE, write, place

SOURCES = Path(__file__).parent / 'StepRecordings'
RNG = np.random.default_rng(191610)
BEAT = .51
BARS = 16
TAU = 2*np.pi


def sample(folder, filename):
    data, rate = sf.read(SOURCES / folder / filename, always_2d=True)
    mono = np.mean(data, axis=1)
    div = gcd(rate, RATE)
    mono = signal.resample_poly(mono, RATE//div, rate//div)
    mono = signal.sosfilt(signal.butter(2, 48, fs=RATE, btype='highpass', output='sos'), mono)
    start = np.flatnonzero(np.abs(mono) > np.max(np.abs(mono))*.035)[0]
    mono = mono[max(0, start-int(.004*RATE)):]
    mono /= np.max(np.abs(mono))+.000001
    return mono


def filt(data, hz, kind='lowpass'):
    return signal.sosfilt(signal.butter(2, hz, fs=RATE, btype=kind, output='sos'), data)


def add_layer(buf, clip, at, gain):
    place(buf, clip, at, gain)


def boots():
    for i in range(8):
        timber_take=sample('wood',f'{i}.ogg') if i%2==0 else sample('TinyWorlds',f'wood0{1+(i//2)%3}.ogg')
        wood=signal.resample_poly(timber_take,94+i*2,100)
        heel = sample('boots',f'{i}.ogg')
        grit = sample('gravel',f'{i}.ogg')
        splash=sample('water',f'{i%5}.ogg')
        wet=sample('rubberduck',f'mud_{1+3*i:02d}.ogg')
        peat = sample('TinyWorlds','mud02.ogg')
        fibre = sample('TinyWorlds',f'wood0{1+i%3}.ogg')
        for family in ('Dugout','Timber','Mud','Brick','Water'):
            out=np.zeros(round(RATE*.78))
            if family=='Dugout':
                add_layer(out,wood,.014,.84)
                add_layer(out,filt(heel,300),.017,.25)
                if i in (1,6): add_layer(out,filt(fibre,1750),.102,.12)
            elif family=='Timber':
                add_layer(out,filt(wood,2400+i*120),.013,.84)
                add_layer(out,filt(heel,410),.019,.32)
                add_layer(out,filt(splash,1900),.059,.125)
                if i==5: add_layer(out,filt(fibre,2200),.124,.145)
            elif family=='Water':
                add_layer(out,signal.resample_poly(splash,95+i*2,100),.014,.70)
                add_layer(out,filt(wet,2100),.04,.33)
                add_layer(out,filt(grit,620),.025,.28)
                add_layer(out,filt(peat,1200),.125,.15)
            elif family=='Mud':
                add_layer(out,filt(grit,1800),.017,.57)
                add_layer(out,filt(peat,1350),.060+i*.006,.48)
                add_layer(out,filt(wet,1200),.127,.21)
                add_layer(out,filt(splash,830),.040,.22)
            else:
                add_layer(out,heel,.014,.87)
                add_layer(out,grit,.032,.14)
                add_layer(out,filt(heel,1600),.054,.13)
            cloth=RNG.normal(0,1,round(RATE*.24))
            age=np.arange(len(cloth))/RATE
            add_layer(out,filt(cloth,[420,1680],'bandpass')*np.sin(np.pi*age/.24)**3,.045,.035)
            out[-750:]*=np.linspace(1,0,750)
            write(f'Step{family}{i+1}',out,.52+.012*(i%4))


def instrument(midi, beats, kind):
    t=np.arange(round(beats*BEAT*RATE))/RATE
    pitch=440*2**((midi-69)/12)
    phase=TAU*pitch*t + .12*np.sin(TAU*t*(4.2 if kind=='bow' else 3.7))
    env=np.sin(np.pi*(np.arange(len(t))+.5)/len(t))**(1.4 if kind=='bow' else 2.4)
    if kind=='bow':
        harmonics=sum(np.sin(i*phase+.16*i)*np.exp(-i*.15)/i for i in range(1,12))
        grain=filt(RNG.standard_normal(len(t)),[600,2100],'bandpass')
        return (.73*harmonics+.055*grain)*env
    return (np.sin(phase)+.55*np.sin(phase*2)+.23*np.sin(phase*3)+.10*np.sin(phase*5))*env


def score():
    n=round(BEAT*BARS*3*RATE)
    stems={name:np.zeros(n) for name in ('Bows','Brass','Iron','Air')}
    notes=[38,36,43,45,38,41,43,45,38,34,36,43,41,43,45,38]
    for bar,root in enumerate(notes):
        when=bar*3*BEAT
        place(stems['Bows'],instrument(root,2.82,'bow'),when+.024,.58)
        if bar%4 in (1,3):
            place(stems['Bows'],instrument(root+7,1.52,'bow'),when+1.44*BEAT,.17)
        if bar in (0,8): phrase=[57,60,65]
        elif bar in (1,9): phrase=[64,62,60]
        elif bar in (5,13): phrase=[53,57,60]
        elif bar in (7,15): phrase=[61,57,50]
        else: phrase=[]
        for j, note in enumerate(phrase):
            place(stems['Brass'],instrument(note,.92,'brass'),when+j*BEAT,.72)
        for beat in ([0,2.32] if bar%4==2 else [.64] if bar%2 else [0]):
            t=np.arange(round(RATE*.30))/RATE
            ring=sum(g*np.sin(TAU*f*t)*np.exp(-t/d) for f,g,d in [(92,.64,.053),(247,.38,.09),(681,.18,.139),(1169,.084,.06)])
            ring+=filt(RNG.standard_normal(len(t)),[520,1900],'bandpass')*np.exp(-t/.019)*.14
            place(stems['Iron'],ring*(1-np.exp(-t/.002)),when+beat*BEAT,.43 if beat else .66)
        breath=instrument(root+19,2.98,'bow')
        air=filt(RNG.standard_normal(len(breath)),[250,1450],'bandpass')
        env=np.sin(np.linspace(0,np.pi,len(air)))**1.5
        place(stems['Air'],breath*.09+env*air*.34,when,1)
    for name,data in stems.items(): write('Field'+name,data,.55,loop=True)
    # A descending, filtered tonal/air cue whose rhythm differs from ordinary rain.
    t=np.arange(round(RATE*1.35))/RATE
    whistle=np.sin(TAU*(610*t+80*t*t))*np.sin(np.linspace(0,np.pi,len(t)))**1.7
    grit=filt(RNG.standard_normal(len(t)),[580,2700],'bandpass')
    write('ShellWarning',whistle*.57+grit*np.sin(np.pi*t/1.35)**2*.32,.72)
    t=np.arange(round(RATE*2.7))/RATE
    rumble=filt(RNG.standard_normal(len(t)),[42,155],'bandpass')
    soil=filt(RNG.standard_normal(len(t)),[210,2700],'bandpass')
    blast=.88*rumble*(1-np.exp(-t/.025))*np.exp(-t/.50)+soil*.72*np.exp(-t/.075)
    for when in (.18,.29,.41,.64,.93):
        u=np.maximum(0,t-when);blast+=soil*.15*(1-np.exp(-u/.002))*np.exp(-u/.045)
    write('Earthfall',blast,.78)
    for i,name in enumerate(('Harness','OpticCase','Gaiter')):
        t=np.arange(round(.44*RATE))/RATE
        dry=filt(RNG.standard_normal(len(t)),[370,2500],'bandpass')
        gear=np.zeros_like(t)
        for when in (.031,.082+i*.014,.19):
            u=np.maximum(0,t-when)
            gear+=(dry*.4+np.sin(TAU*(394+i*157)*u)*.19)*(1-np.exp(-u/.002))*np.exp(-u/.031)
        write('Kit'+name,gear,.44)
        axle_t=np.arange(round(.71*RATE))/RATE
        grain=filt(RNG.standard_normal(len(axle_t)),[270,2200],'bandpass')
        phase=TAU*np.cumsum((121+i*17)+16*np.sin(3.7*axle_t))/RATE
        axle=(.35*np.sin(phase)+.11*np.sin(phase*3)+grain*.42)*np.sin(np.pi*axle_t/.71)**2
        write(f'CartAxle{i+1}',axle,.48)


def personnel():
    # Leather, wool and the weight of a dropped pack. Short, original field cues.
    rng=np.random.default_rng(281916)
    for name,duration,hits in [('FieldInjury',.38,[(.014,1.,112),(.065,.24,265)]),
                               ('FieldFall',.88,[(.032,.28,154),(.142,1.,72),(.257,.39,115),(.393,.17,390)])]:
        out=np.zeros(round(duration*RATE))
        for at,gain,hz in hits:
            t=np.arange(round((duration-at)*RATE))/RATE
            cloth=filt(rng.normal(size=len(t)),[240,1950],'bandpass')*np.exp(-t/.069)
            body=np.sin(TAU*(hz*t+2*(1-np.exp(-t/.025))))*np.exp(-t/.059)
            layer=(body*.67+cloth*.52)*(1-np.exp(-t/.005))
            add_layer(out,layer,at,gain)
        write(name,out,.57 if name=='FieldInjury' else .63)
    for name,marks in [('SignalCall',[(0,660),(.13,660)]),('SignalReturn',[(0,475),(.095,710)]),('SignalLost',[(0,460),(.12,300)])]:
        out=np.zeros(round(.38*RATE));t=np.arange(round(.18*RATE))/RATE
        for at,hz in marks:
            strike=(.69*np.sin(TAU*hz*t)+.26*np.sin(TAU*hz*2.04*t)+.13*np.sin(TAU*hz*3.19*t))
            add_layer(out,strike*np.exp(-t/.031)*(1-np.exp(-t/.0025)),at+.009,1)
        write(name,out,.43)


def render():
    boots()
    score()
    personnel()


if __name__=='__main__':
    render()
