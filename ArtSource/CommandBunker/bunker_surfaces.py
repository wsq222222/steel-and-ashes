"""Build the bunker material palette from the game's own PBR sources. Run with Python + numpy/scipy/Pillow."""
from pathlib import Path
import re
import uuid

import numpy as np
from PIL import Image
from scipy.ndimage import map_coordinates


ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / "Assets/Osowiec/HQ/Art"
FORT = ROOT / "Assets/Osowiec/Art/Fort1916"
SOURCE = FORT / "Surfaces"
DEST = ART / "Bunker"


def guid(path):
    return re.search(r"^guid: (\w+)", Path(str(path) + ".meta").read_text(), re.M)[1]


def meta(path, template):
    dest = Path(str(path) + ".meta")
    if not dest.exists():
        asset_guid = uuid.uuid5(uuid.NAMESPACE_URL, "osowiec/" + path.relative_to(ROOT).as_posix()).hex
        dest.write_text(re.sub(r"^guid: \w+", "guid: " + asset_guid, template.read_text(), count=1, flags=re.M))


def noise(rng, shape, cols, rows):
    grid = rng.uniform(-1, 1, (rows, cols)).astype("f")
    y, x = np.mgrid[:shape[0], :shape[1]].astype("f")
    return map_coordinates(grid, [y * rows / shape[0], x * cols / shape[1]], order=3, mode="grid-wrap")


def limewash():
    scale = (1536, 1536)
    base = np.asarray(Image.open(SOURCE / "Brick_colour.jpg").resize(scale), "f")
    normal = np.asarray(Image.open(SOURCE / "Brick_normal.jpg").resize(scale), "f") / 127.5 - 1
    rng = np.random.default_rng(191509)
    n = lambda x, y: noise(rng, scale, x, y)
    large, medium, grit = n(4, 5), n(19, 18), n(92, 80)
    mineral = large + .56 * medium + .36 * n(40, 38) + .22 * grit + .12 * n(235, 220)
    mineral += rng.normal(0, .06, scale).astype("f")
    pores = rng.uniform(-1, 1, scale).astype("f")
    residue = np.clip((mineral + .01) * 4.8, 0, 1)
    pigment = 155 + 12 * large + 7 * grit + 5 * pores
    chalk = np.stack((pigment + 13, pigment + 7, pigment - 7), -1)
    bleach = residue[:, :, None] * .82
    damp = np.clip((n(29, 2) + .34 * medium - .36) * 1.18, 0, 1)
    rgb = (base * (1 - bleach) + chalk * bleach) * (1 - damp[:, :, None] * .24)
    out = DEST / "Limewash_colour.jpg"
    Image.fromarray(np.uint8(np.clip(rgb, 0, 255))).save(out, quality=94, subsampling=0)
    meta(out, SOURCE / "Brick_colour.jpg.meta")
    dy, dx = np.gradient(residue * .25 + grit * .14 + .04 * pores)
    normal[:, :, 0] = normal[:, :, 0] * (1 - .34 * residue) - dx
    normal[:, :, 1] = normal[:, :, 1] * (1 - .34 * residue) + dy
    normal /= np.maximum(.001, np.linalg.norm(normal, axis=2, keepdims=True))
    out = DEST / "Limewash_normal.jpg"
    Image.fromarray(np.uint8(np.clip(normal * 127.5 + 127.5, 0, 255))).save(out, quality=93)
    meta(out, SOURCE / "Brick_normal.jpg.meta")
    mask = np.zeros((*scale, 4), np.uint8)
    mask[:, :, 1] = 255
    mask[:, :, 3] = np.clip(55 + 18 * grit - 20 * residue + 78 * damp, 0, 255)
    out = DEST / "Limewash_mask.png"
    Image.fromarray(mask).save(out)
    meta(out, SOURCE / "Brick_mask.png.meta")


def wet_film():
    for label,family in [("WetStone","Till"),("SoakedOak","Timber")]:
        src=np.array(Image.open(SOURCE / (family+"_mask.png")).resize((1024,1024)))
        rng=np.random.default_rng(1327 if label=="WetStone" else 1381)
        shape=(1024,1024)
        flow=noise(rng,shape,15 if label=="SoakedOak" else 7,4)
        pores=src[:,:,3].astype("f")/255
        # Only the low grain and small damp pockets hold a coherent reflection.
        # A high baseline on the entire board turned each plank into a mirror.
        if label=="SoakedOak":
            pockets=flow*.52+noise(rng,shape,22,9)*.25+noise(rng,shape,3,3)*.23
            wet=np.clip((pockets-.07)*1.65,0,1)
            src[:,:,3]=np.clip(40+86*wet+38*pores,0,178)
        else:
            wet=np.clip((flow-.03)*1.21,0,1)
            src[:,:,3]=np.clip(65+79*wet+48*pores,0,195)
        target=DEST/(label+"_mask.png")
        Image.fromarray(src).save(target)
        meta(target,SOURCE/(family+"_mask.png.meta"))


def coat_felt():
    # Matt, fulled wool has short heathered fibres. Open-weave sacking reads as
    # a coarse regular grid when the player stands directly in front of a sleeve.
    shape=(1024,1024)
    rng=np.random.default_rng(1457)
    fleck=noise(rng,shape,489,303)
    nap=noise(rng,shape,187,527)
    fibre=.46*fleck+.31*nap+.23*rng.normal(0,.62,shape).astype("f")
    weather=3.6*noise(rng,shape,5,7)+2.7*noise(rng,shape,26,22)
    grain=fibre*10+weather
    colour=np.stack([107+grain,104+grain*.95,85+grain*.84],-1)
    target=DEST/"Felt_colour.jpg"
    Image.fromarray(np.uint8(np.clip(colour,0,255))).save(target,quality=93)
    meta(target,SOURCE/"Canvas_colour.jpg.meta")
    dy,dx=np.gradient(fibre*.21)
    normal=np.stack([-dx,dy,np.ones(shape,"f")],-1)
    normal/=np.linalg.norm(normal,axis=-1,keepdims=True)
    target=DEST/"Felt_normal.jpg"
    Image.fromarray(np.uint8(normal*127.5+127.5)).save(target,quality=91)
    meta(target,SOURCE/"Canvas_normal.jpg.meta")
    mask=np.zeros((*shape,4),"uint8");mask[:,:,1]=255
    mask[:,:,3]=np.clip(42+fibre*21+weather,0,255)
    target=DEST/"Felt_mask.png"
    Image.fromarray(mask).save(target);meta(target,SOURCE/"Canvas_mask.png.meta")


# Material, source texture family, display tint (sRGB), normal strength, smoothness multiplier.
PALETTE = [
    ("Limewash", "Limewash", (1,.99,.96), .92, .85),
    ("Brick", "Brick", (.92,.84,.78), .8, .70),
    ("FiredClay", "Brick core", (.89,.72,.65), .73, .73),
    ("Masonry", "Till", (.74,.72,.66), .55, .68),
    ("WetStone", "Till", (.66,.69,.67), .63, 1),
    ("Oak", "Timber", (.88,.81,.71), .47, .65),
    ("SoakedOak", "Timber", (.79,.76,.69), .45, 1),
    ("Heartwood", "Timber", (.68,.63,.54), .57, .60),
    ("Iron", "Oxide", (.41,.42,.42), .53, .81),
    ("Soil", "Earth", (.79,.78,.73), .67, .83),
    ("Cloth", "Canvas", (.49,.52,.46), .46, .61),
    ("Wool", "Felt", (.89,.90,.87), .43, .46),
    ("WoolSeam", "Felt", (.75,.78,.71), .40, .40),
    ("Paper", "Canvas", (1,.92,.72), .09, .27),
    ("Porcelain", "Concrete", (1,.99,.92), .08, 1),
]


def write_material(name, family, tint, strength, gloss):
    reference = (FORT / "Brick.mat").read_text()
    # This interior uses the same URP Lit lighting and baked GI path as its
    # furnishings. The exterior fort shader's rain area is in world coordinates.
    lit=re.search(r"m_Shader: \{[^}]+\}",(ART/"HQ_QuarterSawn.mat").read_text())[0]
    reference=re.sub(r"m_Shader: \{[^}]+\}",lit,reference,count=1)
    directory = DEST if family in ("Limewash","Felt") else SOURCE
    mat_name = "Bunker_" + name
    rgb = ", ".join(k + ": " + str(round(v, 4)) for k, v in zip("rgb", tint))
    reference = re.sub(r"m_Name: Brick\b", "m_Name: " + mat_name, reference, count=1)
    for suffix in ["colour.jpg", "normal.jpg", "mask.png"]:
        target=directory / (family+"_"+suffix)
        if suffix=="mask.png" and name in ("WetStone","SoakedOak"):
            target=DEST/(name+"_mask.png")
        reference = reference.replace(guid(SOURCE / ("Brick_" + suffix)), guid(target))
    reference = re.sub(r"(_(?:BaseColor|Color): )\{[^}]+\}", r"\g<1>{" + rgb + ", a: 1}", reference)
    reference = re.sub(r"(_Smoothness: )[\d.]+", r"\g<1>" + str(gloss), reference)
    reference = re.sub(r"(_BumpScale: )[\d.]+", r"\g<1>" + str(strength), reference)
    target = DEST / (mat_name + ".mat")
    target.write_text(reference)
    meta(target, FORT / "Brick.mat.meta")


if __name__ == "__main__":
    DEST.mkdir(parents=True, exist_ok=True)
    meta(DEST, ART / "Textures.meta")
    limewash()
    wet_film()
    coat_felt()
    for spec in PALETTE:
        write_material(*spec)
