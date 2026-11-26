# Generate three original short admissions with the Russian Windows narrator and master them at 48 kHz.
$ErrorActionPreference = 'Stop'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ("osowiec-ward-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$voice = New-Object -ComObject SAPI.SpVoice
$russian = @($voice.GetVoices() | Where-Object { $_.GetAttribute('Language') -eq '419' })[0]
if (-not $russian) { throw 'A Russian SAPI voice is required for these Russian lines.' }
$voice.Voice = $russian
$voice.Rate = -1
$lines = @('Осторожно. Первая койка у стены.', 'Носилки сюда. Место готово.', 'Третьего приняли. Передайте в штаб.')
for ($i = 0; $i -lt $lines.Count; $i++) {
    $stream = New-Object -ComObject SAPI.SpFileStream
    $stream.Format.Type = 22
    $stream.Open((Join-Path $scratch ("WardCall" + ($i+1) + '.wav')),3,$false)
    $voice.AudioOutputStream = $stream
    [void]$voice.Speak($lines[$i])
    $stream.Close()
}
$bank = Join-Path $PSScriptRoot '../Resources/OsowiecSound'
@'
from pathlib import Path
import numpy as np, sys, wave
from scipy.signal import resample_poly, butter, sosfilt
from scipy.io import wavfile
from math import gcd
for src in Path(sys.argv[1]).glob('*.wav'):
    rate,data=wavfile.read(src)
    x=data.astype(float); x-=x.mean()
    factor=gcd(48000,rate)
    x=resample_poly(x,48000//factor,rate//factor)
    x=sosfilt(butter(2,[105,5600],fs=48000,btype='bandpass',output='sos'),x)
    wet=np.pad(x,(0,3456))
    for i,f in [(709,.07),(1824,.039),(3100,.019)]:
        wet[i:i+len(x)] += f*x
    wet -= np.mean(wet)
    val=np.int32(np.clip(wet/max(np.abs(wet))*.64,-1,1)*8388607)
    pcm=np.stack((val&255,(val>>8)&255,(val>>16)&255),axis=1).astype('uint8')
    with wave.open(str(Path(sys.argv[2])/src.name),'wb') as out:
        out.setparams((1,3,48000,0,'NONE','not compressed'));out.writeframes(pcm.tobytes())
    print(src.stem,round(len(val)/48000,2),'seconds')
'@ | python - $scratch $bank
if ($LASTEXITCODE -ne 0) { throw 'Voice mastering failed.' }
