"""Bake tileable maps for the torn jute and the exposed insides of damaged bricks."""
from pathlib import Path

import numpy as np
from PIL import Image
from woven_field_props import write_woven


output = Path(__file__).resolve().parent.parent / "Fort1916" / "Surfaces"
size = 512
rng = np.random.default_rng(1716)
y, x = np.mgrid[:size, :size] / size


def noise(resolution):
    field = rng.uniform(0, 1, (resolution, resolution))
    tx, ty = x * resolution, y * resolution
    ix, iy = tx.astype(int), ty.astype(int)
    fx, fy = tx - ix, ty - iy
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    return ((1 - fx) * ((1 - fy) * field[iy, ix] + fy * field[(iy + 1) % resolution, ix])
            + fx * ((1 - fy) * field[iy, (ix + 1) % resolution]
                    + fy * field[(iy + 1) % resolution, (ix + 1) % resolution]))


def bake(name, albedo, relief, gloss, strength):
    def write(suffix, array):
        Image.fromarray(np.uint8(np.clip(array, 0, 255))).save(output / (name + suffix))
    write("_colour.jpg", albedo)
    dx = (np.roll(relief, -1, axis=1) - np.roll(relief, 1, axis=1)) * strength
    dy = (np.roll(relief, -1, axis=0) - np.roll(relief, 1, axis=0)) * strength
    normals = np.stack((-dx, dy, np.ones_like(dx)), axis=-1)
    normals /= np.linalg.norm(normals, axis=-1, keepdims=True)
    write("_normal.jpg", (normals * .5 + .5) * 255)
    write("_mask.png", np.stack((x * 0, x * 0 + 255, x * 0, gloss), axis=-1))


write_woven("Repair flax")

# Preserve the production brick grain that followed the original flax noise draws.
for resolution in (8, 10, 6, 128, 8):
    rng.uniform(0, 1, (resolution, resolution))
grain = noise(64) * .44 + noise(24) * .28 + noise(6) * .28
inclusions = np.clip((noise(105) - .57) * 4, 0, 1) * noise(16)
clay = np.stack((86 + 72 * grain + inclusions * 28, 49 + 47 * grain + inclusions * 35,
                37 + 37 * grain + inclusions * 34), axis=-1)
bake("Brick core", clay, grain + inclusions * .36, 38 + grain * 22, 3.3)
