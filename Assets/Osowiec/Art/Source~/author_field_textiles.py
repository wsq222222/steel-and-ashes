"""Bake the scanned canvas and the original flag cloth."""
from pathlib import Path
import numpy as np
from PIL import Image
from woven_field_props import write_woven

out=Path(__file__).resolve().parents[1]/"Fort1916"/"Surfaces"
out.mkdir(parents=True,exist_ok=True)
rng=np.random.default_rng(1916)
n=512
y,x=np.indices((n,n))
write_woven("Canvas")
# The archived flag seed follows the original canvas noise draw.
rng.normal(0,.07,(n,n))
for kind,base in [("Flag",(118,63,42))]:
    u=2*np.pi*x/n; v=2*np.pi*y/n
    yarn=(np.sin(59*u+.46*np.sin(5*v))+.88*np.cos(61*v+.36*np.sin(3*u)))*.39
    patches=.55*np.cos(3*u+1.4*np.cos(2*v))+.40*np.sin(v+3*u)+.09*np.cos(7*v)
    noise=rng.normal(0,.07,(n,n))
    h=.48+yarn*.36+.026*patches+.10*noise
    cols=np.clip(np.array(base)[None,None,:]*(1+.21*yarn[...,None]+.14*patches[...,None]+noise[...,None]),0,255).astype("uint8")
    Image.fromarray(cols).save(out/(kind+"_colour.jpg"),quality=92)
    dx=np.roll(h,-1,1)-np.roll(h,1,1);dy=np.roll(h,-1,0)-np.roll(h,1,0)
    normal=np.stack((-1.07*dx,1.07*dy,np.ones_like(h)),axis=2)
    normal/=np.linalg.norm(normal,axis=2,keepdims=True)
    Image.fromarray(((normal+.99999)*127.5).clip(0,255).astype("uint8")).save(out/(kind+"_normal.jpg"),quality=94)
    smooth=np.zeros((n,n,4),dtype="uint8")
    smooth[:,:,3]=(46+18*yarn-3*patches).clip(0,255).astype("uint8")
    Image.fromarray(smooth).save(out/(kind+"_mask.png"),optimize=True)
