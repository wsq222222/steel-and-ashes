"""Original bellows, air valve and loose bearing for the two-stroke cart drive."""
import numpy as np
from scipy import signal
from render_audio import RATE, write


def render():
    beats, period = 32, 0.252
    n = round(RATE * beats * period)
    out = np.zeros(n)
    random = np.random.default_rng(23191)
    for i in range(beats):
        t = np.arange(round(RATE*.33))/RATE
        local = random.standard_normal(len(t))
        hiss = signal.sosfilt(signal.butter(2, [410, 2510], fs=RATE, btype="bandpass", output="sos"), local)
        body = signal.sosfilt(signal.butter(2, [92, 380], fs=RATE, btype="bandpass", output="sos"), local)
        intake = np.sin(np.pi*np.minimum(1, t/.175))**1.9
        housing = sum(g*np.sin(2*np.pi*f*t)*np.exp(-t/d) for f,g,d in [(118,.20,.031),(291,.09,.020),(572,.072,.012)])
        hit = housing*(1-np.exp(-t/.0018)) + body*.49*np.exp(-t/.091)+hiss*.40*intake
        start = round((i + .018*np.sin(i*3.7))*period*RATE)
        # Circular addition preserves pressure across the loop seam.
        np.add.at(out, (start+np.arange(len(hit)))%n, hit*(1.0 if i%2==0 else .65))
    write("CartAirMotor", out, .53, loop=True)


if __name__ == "__main__":
    render()
