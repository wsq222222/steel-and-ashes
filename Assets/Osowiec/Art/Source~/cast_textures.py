"""Generate tiling cloth, leather and finish maps; project the project's portrait reference onto the two sculpted heads."""
from pathlib import Path
import numpy as np
import sys
from PIL import Image, ImageFilter

ART = Path(__file__).resolve().parents[1]
ROOT = ART.parents[3]
OUT = ART / "Cast" / "Textures"
OUT.mkdir(parents=True, exist_ok=True)

def bake_uncovered_faces():
    # The photo projection ends under the steel brim. Give forehead UVs (V up
    # to 1.065) a padded scalp, sampled from the same finish as the bare crown.
    skin = Image.open(OUT / "Skin.png").convert("RGB")
    for name in ("FaceAssault", "FaceScout"):
        photo = Image.open(OUT / (name + ".png")).convert("RGB")
        width, height = photo.size
        extra = height // 8
        y, x = np.mgrid[-extra:height, :width]
        face = np.asarray(photo)[np.clip(y, 0, height - 1), x]
        grain = np.asarray(skin)[(y + skin.height) % skin.height, x % skin.width]
        side = np.minimum(1, np.abs(2 * x / width - 1))
        hairline = .145 - .045 * side ** 2
        mix = np.clip((hairline - y / height) / .13, 0, 1)
        mix = (mix ** 2 * (3 - 2 * mix))[..., None]
        result = np.uint8(np.rint(face * (1 - mix) + grain * mix))
        Image.fromarray(result).resize(photo.size, Image.Resampling.LANCZOS).save(OUT / (name + "Uncovered.png"))

if "--bare-faces" in sys.argv:
    bake_uncovered_faces()
    sys.exit(0)
if "--atlas" in sys.argv:
    atlas = Image.new("RGB", (960, 384), (48, 47, 39))
    for index, role in enumerate(("Assault", "Scout", "Support")):
        portrait = Image.open(OUT.parent / (role+"Portrait.png")).convert("RGBA")
        atlas.paste(portrait, (index*320, 0), portrait)
    atlas.save(OUT.parent / "FieldPortraitAtlas.png")
    sys.exit(0)
rng = np.random.default_rng(1916)
size = 384
yy, xx = np.mgrid[:size, :size]
palette = {
    "Wool": ((91, 87, 61), 0, .32), "RifleWool": ((98, 86, 64), 0, .29),
    "HeavyWool": ((77, 81, 65), 0, .31), "SlateWool": ((77, 84, 80), 0, .30),
    "Canvas": ((124, 111, 78), 0, .24), "Leather": ((68, 49, 36), 0, .42),
    "Boot": ((47, 43, 37), 0, .43), "Steel": ((78, 80, 69), .70, .43),
    "FieldPaint": ((81, 84, 63), .46, .4), "BlueSteel": ((66, 71, 76), .72, .44),
    "Brass": ((134, 112, 65), .72, .54), "Walnut": ((101, 67, 43), 0, .42),
    "Rubber": ((57, 60, 51), 0, .3), "Lens": ((69, 85, 79), .48, .79),
    "Skin": ((135, 101, 77), 0, .31), "Piping": ((140, 125, 88), 0, .36)
}

def cloudy(n):
    # Wrap the coarse lattice so both the interpolation and derivative are periodic.
    grid = rng.normal(0, 1, (n, n))
    row = xx * n / size
    col = yy * n / size
    i, j = row.astype(int), col.astype(int)
    u, v = row % 1, col % 1
    u, v = u*u*(3-2*u), v*v*(3-2*v)
    return ((1-v)*((1-u)*grid[j,i] + u*grid[j,(i+1)%n]) +
            v*((1-u)*grid[(j+1)%n,i] + u*grid[(j+1)%n,(i+1)%n]))

def save(name, arr):
    Image.fromarray(np.uint8(np.clip(arr, 0, 255))).save(OUT / (name+".png"))

for name, (base, metal, gloss) in palette.items():
    grain = rng.normal(0,1,(size,size))
    field = cloudy(6)*.45 + cloudy(18)*.25 + cloudy(48)*.15
    if "Wool" in name or name in ("Canvas", "Piping"):
        warp = .3*np.cos(2*np.pi*xx/4) + .22*np.cos(2*np.pi*(yy+xx)/8)
        relief = .36*grain + field + warp
    elif name == "Walnut":
        relief = np.sin(2*np.pi*xx/48 + 2.1*cloudy(4))*.55 + field + grain*.16
    elif name in ("Leather","Boot","Rubber","Skin"):
        relief = field + .37*cloudy(96) + .17*grain
    else:
        relief = field + grain*.27 + (grain>2.2)*.85
    save(name, np.asarray(base)[None,None,:] * np.maximum(.45,1+relief[...,None]*.19))
    nx=(np.roll(relief,-1,1)-np.roll(relief,1,1))*.32
    ny=(np.roll(relief,-1,0)-np.roll(relief,1,0))*.32
    normals=np.stack([-nx,ny,np.ones_like(nx)],axis=2)
    normals/=np.linalg.norm(normals,axis=2,keepdims=True)
    save(name+"_N", (normals*.5+.5)*255)
    mask=np.full((size,size,4),255.)
    mask[:,:,0]=metal*255
    mask[:,:,1]=0
    mask[:,:,2]=0
    mask[:,:,3]=np.clip(gloss+field*.12+grain*.023,.08,.92)*255
    save(name+"_MS",mask)

sheet = Image.open(ROOT / "references" / "mission-osowiec" / "portraits-v2.png").convert("RGB")
for name,box in [("FaceAssault",(216,156,499,439)), ("FaceScout",(959,125,1253,421))]:
    crop = sheet.crop(box).resize((512,512), Image.Resampling.LANCZOS)
    # Reduce baked large-scale shadow; retain skin, brows, stubble and local values.
    arr = np.asarray(crop, dtype=float)
    broad = np.asarray(crop.filter(ImageFilter.GaussianBlur(28)), dtype=float).mean(axis=2)
    gain = np.clip(1.28*(94/(broad+1))**.24, .85, 1.65)
    save(name, arr * gain[...,None])

bake_uncovered_faces()
