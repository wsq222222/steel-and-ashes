"""Fetch the CC0 source maps and pack smoothness for Unity's metallic workflow. Run using desktop Python."""
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import hashlib
import io
import time

import numpy as np
from PIL import Image, ImageOps
import requests

out = Path(__file__).resolve().parents[1] / "Fort1916" / "Surfaces"
out.mkdir(parents=True, exist_ok=True)
sets = {"Till":"brown_mud_rocks_01","Earth":"mud_forest", "Brick":"red_brick_03", "Timber":"wood_planks_dirt",
        "Concrete":"chipped_concrete", "Sod":"withered_grass","Bark":"bark_willow",
        "Oxide":"rust_coarse_01"}

def fetch(url):
    for i in range(6):
        try:
            response = requests.get(url, timeout=18, headers={"User-Agent":"SteelAndAshes student art production research"})
            response.raise_for_status()
            return response
        except requests.RequestException:
            if i == 5: raise
            time.sleep(0.7 * (i+1))

def acquire_ground(kind, slug, manifest):
    # Reuse the same UV-congruent scan maps in the fort material and its triplanar soil shader.
    size = "2k"
    cache = out.parents[4] / "Logs" / "MarshScans"
    cache.mkdir(parents=True, exist_ok=True)
    def raw(channel, fmt):
        entry = manifest[channel][size][fmt]
        local = cache / (slug + "-" + channel + "-" + size + "." + fmt)
        if local.exists() and hashlib.md5(local.read_bytes()).hexdigest() == entry["md5"]:
            return local
        blob = fetch(entry["url"]).content
        if hashlib.md5(blob).hexdigest() != entry["md5"]:
            raise ValueError("Scan checksum mismatch: " + entry["url"])
        local.write_bytes(blob)
        return local

    def level(path):
        with Image.open(path) as image:
            arr = np.asarray(image)
            if arr.ndim == 3:
                arr = arr[...,0]
            maximum = 65535 if arr.max() > 255 else 255
            return arr.astype(np.float32) / maximum

    colour = raw("Diffuse", "jpg")
    normal = raw("nor_gl", "jpg")
    rough = level(raw("Rough", "png"))
    height = level(raw("Displacement", "png"))
    low, high = np.percentile(height, [1,99])
    relief = np.clip((height-low)/max(.001,high-low),0,1)
    ao = level(raw("AO", "png")) if "AO" in manifest else np.ones_like(rough)
    rgba=np.zeros((*relief.shape,4), dtype=np.uint8)
    rgba[...,1]=np.rint(relief*255).astype(np.uint8)
    rgba[...,2]=np.rint(ao*255).clip(0,255).astype(np.uint8)
    rgba[...,3]=np.rint((1-rough)*255).clip(0,255).astype(np.uint8)
    output_mask=io.BytesIO()
    Image.fromarray(rgba).save(output_mask,format="PNG",compress_level=6)
    for suffix,blob in [("_colour.jpg",colour.read_bytes()),("_normal.jpg",normal.read_bytes()),("_mask.png",output_mask.getvalue())]:
        target=out/(kind+suffix)
        if not target.exists() or target.read_bytes()!=blob:
            work=cache/(kind+suffix+".ready")
            work.write_bytes(blob)
            work.replace(target)


def acquire(pair):
    kind, slug = pair
    if kind in ("Brick", "Timber", "Concrete"):
        from fetch_architecture_scans import bake
        return bake(pair)
    manifest = fetch("https://api.polyhaven.com/files/" + slug).json()
    if kind in ("Earth","Sod","Till"):
        return acquire_ground(kind,slug,manifest)
    for source, suffix in (("Diffuse","colour"),("nor_gl","normal"),("Rough","mask")):
        path = out / (kind + "_" + suffix + (".png" if suffix == "mask" else ".jpg"))
        if path.exists(): continue
        entry = manifest[source]["1k"]["jpg"]
        blob = fetch(entry["url"]).content
        assert hashlib.md5(blob).hexdigest() == entry["md5"]
        if suffix == "mask":
            rough = Image.open(io.BytesIO(blob)).convert("L")
            metal = Image.new("L", rough.size, 68 if kind == "Oxide" else 0)
            zero = Image.new("L", rough.size)
            smooth = ImageOps.invert(rough)
            Image.merge("RGBA", (metal, zero, zero, smooth)).save(path, optimize=True)
        else: path.write_bytes(blob)
    print(kind, slug, flush=True)

if __name__ == "__main__":
    with ThreadPoolExecutor(max_workers=6) as workers: list(workers.map(acquire, sets.items()))
    sky = out / "OvercastSoil.hdr"
    if not sky.exists():
        entry = fetch("https://api.polyhaven.com/files/overcast_soil_puresky").json()["hdri"]["2k"]["hdr"]
        blob = fetch(entry["url"]).content
        assert hashlib.md5(blob).hexdigest() == entry["md5"]
        sky.write_bytes(blob)
