"""Validate authored masters before Unity import. Run with Python, numpy and scipy."""
from pathlib import Path
import wave
import numpy as np
from scipy.io import wavfile


bank = Path(__file__).resolve().parents[1] / 'Resources' / 'OsowiecSound'
outcomes = {}
for path in sorted(bank.glob('*.wav')):
    with wave.open(str(path)) as source:
        assert source.getsampwidth() == 3 and source.getframerate() == 48000, path
        stereo = path.stem in ('Rain', 'Room')
        assert source.getnchannels() == (2 if stereo else 1), path
    rate, raw = wavfile.read(path)
    signal = raw.astype(np.float64) / 2**31
    peak = np.max(np.abs(signal))
    assert .32 < peak < .89 and abs(np.mean(signal)) < .001, path
    if stereo or path.stem in ('ShellacWaltz','FieldBows','FieldIron','FieldAir','FieldBrass'):
        seam = np.max(np.abs(signal[-1] - signal[0]))
        assert seam < np.percentile(np.abs(np.diff(signal, axis=0)), 99.9), path
    outcomes[path.stem] = (round(len(signal) / rate, 2), round(20 * np.log10(peak), 2))

for surface in ('Dugout', 'Timber', 'Mud', 'Brick', 'Water'):
    variations = [wavfile.read(bank / f'Step{surface}{i}.wav')[1] for i in range(1, 9)]
    # Time alignment is preserved so copied/retuned-identical emissions fail the check.
    assert max(abs(np.corrcoef(variations)[~np.eye(8, dtype=bool)])) < .95, surface

mixer = (bank / 'FieldMix.mixer').read_text(encoding='utf8')
for bus in ('Open marsh', 'Casemate', 'Exterior', 'Interior', 'Foley', 'World', 'UI', 'Music'):
    assert 'm_Name: ' + bus in mixer, bus
assert {'MaskWipe', 'PressurePulse'} <= outcomes.keys()
stems=('FieldBows','FieldIron','FieldAir','FieldBrass')
assert all(outcomes[k][0]==24.48 for k in stems)
assert all(k in outcomes for k in ('ShellWarning','CartAxle1','CartAxle2','CartAxle3','KitHarness','KitGaiter','KitOpticCase'))
assert all(k in outcomes for k in ('FieldInjury','FieldFall','SignalCall','SignalReturn','SignalLost'))
print(f'{len(outcomes)} stereo/mono PCM24 masters, 40 footsteps, four 24.48s stems, 48 kHz, peak/DC, loop/variation tests passed.')
for name in ('Rain', 'Room', 'Breathing', 'ShellacWaltz', 'Pneumatic', 'Optics', 'Earthfall', 'Victory', 'Defeat'):
    print(f'{name:15s} {outcomes[name][0]:6.2f} s  {outcomes[name][1]:6.2f} dBFS')
