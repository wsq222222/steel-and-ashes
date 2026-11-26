"""Bake registered CC0 colour, GL normals, smoothness and measured relief for the fort."""
from concurrent.futures import ThreadPoolExecutor
import hashlib
import io
from pathlib import Path

import numpy as np
from PIL import Image
from fetch_marsh_surfaces import fetch, out

def bake(item):
    material, slug = item
    manifest = fetch("https://api.polyhaven.com/files/" + slug).json()
    cache = out.parents[4] / "Logs" / "ArchitectureScans"
    cache.mkdir(exist_ok=True, parents=True)
    def read(channel, fmt):
        entry = manifest[channel]["2k"][fmt]
        local = cache / (slug + "_" + channel + "." + fmt)
        if not local.exists() or hashlib.md5(local.read_bytes()).hexdigest() != entry["md5"]:
            data = fetch(entry["url"]).content
            if hashlib.md5(data).hexdigest() != entry["md5"]:
                raise ValueError("Bad download: " + entry["url"])
            local.write_bytes(data)
        return local
    normal = read("nor_gl", "jpg").read_bytes()
    colour = read("Diffuse", "jpg").read_bytes()
    def gray(channel):
        source = np.asarray(Image.open(read(channel, "png")))
        source = source if source.ndim == 2 else source[...,0]
        return source.astype("float32") / (65535. if source.max()>255 else 255.)
    depth = gray("Displacement")
    rough = gray("Rough")
    lo, hi = np.percentile(depth, [1,99])
    mask = np.zeros((*depth.shape,4), dtype="uint8")
    mask[...,1] = np.rint(np.clip((depth-lo)/(hi-lo),0,1)*255)
    mask[...,3] = np.rint((1.-rough)*255)
    image = io.BytesIO()
    Image.fromarray(mask).save(image,format="PNG",compress_level=5)
    for suffix, blob in [("_colour.jpg", colour),("_normal.jpg",normal),("_mask.png",image.getvalue())]:
        path = out / (material+suffix)
        if not path.exists() or path.read_bytes()!=blob:
            temp = cache / (material+suffix+".ready")
            temp.write_bytes(blob)
            temp.replace(path)
    print(material, depth.shape, "height calibrated", float(lo), float(hi), flush=True)

if __name__=="__main__":
    with ThreadPoolExecutor(3) as executor:
        list(executor.map(bake, [("Brick","red_brick_03"), ("Concrete","chipped_concrete"), ("Timber","wood_planks_dirt")]))
