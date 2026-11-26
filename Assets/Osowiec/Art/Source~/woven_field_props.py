"""Build metric, weathered fort textiles from photographed CC0 hessian, aligned across all channels."""
from pathlib import Path
import hashlib
from concurrent.futures import ThreadPoolExecutor
from io import BytesIO

import numpy as np
from PIL import Image
import requests


def write_woven(kind):
    source = Path(__file__).resolve().parent
    output = source.parent / "Fort1916" / "Surfaces"
    cache = source.parents[3] / "Logs" / "TextileScans"
    cache.mkdir(parents=True, exist_ok=True)
    options={
        "Canvas":("hessian_380",4,(96,89,68),.23),
        "Repair flax":("hessian_230",2,(130,110,76),.28),
    }
    slug, repeats, tint, wet_sheen=options[kind]
    headers={"User-Agent": "Steel and Ashes / development art"}
    manifest=requests.get("https://api.polyhaven.com/files/"+slug,headers=headers,timeout=25)
    manifest.raise_for_status()
    maps=manifest.json()
    def get(channel):
        fmt="jpg" if channel=="Diffuse" else "png"
        entry=maps[channel]["1k"][fmt]
        local=cache/(slug+"-"+channel+"-1k."+fmt)
        if not local.exists() or hashlib.md5(local.read_bytes()).hexdigest()!=entry["md5"]:
            r=requests.get(entry["url"],headers=headers,timeout=40)
            r.raise_for_status()
            if hashlib.md5(r.content).hexdigest()!=entry["md5"]: raise ValueError("Source checksum mismatch for "+channel)
            local.write_bytes(r.content)
        with Image.open(local) as src:
            if channel in ("nor_gl","Diffuse"): img=src.convert("RGB")
            else:
                values=np.array(src)
                if values.ndim==3: values=values[...,0]
                if values.max()>255: values=np.rint(values.astype(np.float32)/257)
                img=Image.fromarray(values.astype("uint8"))
            return np.asarray(img.resize((2048//repeats,2048//repeats),Image.Resampling.LANCZOS)).astype(np.float32)/255
    with ThreadPoolExecutor(max_workers=3) as workers:
        diffuse,normal,rough,ao=workers.map(get,("Diffuse","nor_gl","Rough","AO"))
    n=2048
    y,x=np.ogrid[:n,:n]
    u,v=x*(2*np.pi/n),y*(2*np.pi/n)
    # A seamless broad field of mud and damp breaks up repetitions across a whole prop.
    patches=.49*np.sin(2*u+1.1*np.sin(v)) + .30*np.cos(3*v+.66*np.sin(2*u)) + .21*np.sin(u+v+.7*np.cos(4*v))
    damp=np.clip((patches+.28)/1.15,0,1).astype(np.float32)
    damp=damp*damp*(3-2*damp)
    def tile(im):
        return np.tile(im,(repeats,repeats,1) if im.ndim==3 else (repeats,repeats))
    dye=np.asarray(tint)/(255*np.mean(diffuse,axis=(0,1)))
    rgb=tile(diffuse)*dye[None,None,:]*(1-.31*damp[...,None])
    tangent=normal*2-1
    tangent/=np.linalg.norm(tangent,axis=-1,keepdims=True).clip(.001)
    norm=tile(tangent*.5+.5)
    matte=tile(rough)
    open_weave=tile(ao)
    mask=np.zeros((n,n,4),np.float32)
    mask[...,1]=open_weave
    mask[...,3]=np.clip(1-matte + matte*wet_sheen*damp*(.55+.45*open_weave),0,.75)
    for name,data in [("_colour.jpg",rgb),("_normal.jpg",norm),("_mask.png",mask)]:
        image=Image.fromarray(np.rint(np.clip(data,0,1)*255).astype(np.uint8))
        file=output/(kind+name)
        content=BytesIO()
        if name.endswith(".jpg"):
            image.save(content,format="JPEG",quality=95,subsampling=0)
        else:
            image.save(content,format="PNG",compress_level=6)
        if not file.exists() or file.read_bytes()!=content.getvalue():
            pending=cache/(kind+name+".ready")
            pending.write_bytes(content.getvalue())
            pending.replace(file)


if __name__=="__main__":
    for kind in ("Canvas","Repair flax"): write_woven(kind)
