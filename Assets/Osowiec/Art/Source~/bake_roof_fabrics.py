"""Extract one evenly dyed repeat from the existing fort linen scans for the hospital rain fly."""
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

surfaces = Path(__file__).resolve().parents[1] / "Fort1916" / "Surfaces"
for source, output, repeats, pigment in (
    ("Canvas", "Shelter waxed linen", 4, (112, 114, 101)),
    ("Repair flax", "Shelter field patch", 2, (132, 120, 99)),
):
    with Image.open(surfaces / (source + "_colour.jpg")) as original:
        size = original.width // repeats
        # Unity's UV origin is the lower left. All photographed yarn channels use this repeat.
        tile = original.crop((0, original.height - size, size, original.height)).convert("L")
    grey = np.asarray(tile).astype(np.float32)
    padded = np.pad(grey, 40, mode="wrap").astype(np.uint8)
    illumination = np.asarray(Image.fromarray(padded).filter(ImageFilter.GaussianBlur(11)))[40:-40, 40:-40]
    thread = np.clip(grey / np.maximum(1, illumination), .65, 1.41)
    rgb = np.rint(np.asarray(pigment)[None, None, :] * (1 + .73 * (thread[:, :, None] - 1)))
    Image.fromarray(rgb.clip(0, 255).astype(np.uint8)).save(surfaces / (output + ".jpg"), quality=94, subsampling=0)
