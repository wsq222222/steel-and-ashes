"""Bake the surveyed tangent and downstream distance (metres) for the river shader."""
from pathlib import Path
import numpy as np
from PIL import Image
from biebrza_valley import bins


def bake():
    width, height = 512, 256
    x, z = np.meshgrid(((np.arange(width) + .5) / width - .5) * 580,
                       (.5 - (np.arange(height) + .5) / height) * 250)
    nearest = np.full_like(x, np.inf)
    result = np.zeros((height, width, 4), dtype=np.uint8)
    segments = {s[:2]: s for entries in bins.values() for s in entries if not s[-1]}
    ordered = sorted(segments.values(), key=lambda s: s[0])
    # Follow the original continuous path through the two bends where x reverses.
    cursor = ordered[0]
    distance = 0.
    for index in range(len(segments)):
        sx, sz, _, dx, dz, _, inv, _ = cursor
        length = np.hypot(dx, dz)
        t = np.clip(((x - sx) * dx + (z - sz) * dz) * inv, 0, 1)
        error = (x - sx - t * dx) ** 2 + (z - sz - t * dz) ** 2
        mask = error < nearest
        nearest[mask] = error[mask]
        result[mask, 0] = round((dx / length * .5 + .5) * 255)
        result[mask, 1] = round((dz / length * .5 + .5) * 255)
        arc = np.rint((distance + t[mask] * length) * 64).astype(np.uint16)
        result[mask, 2] = arc >> 8
        result[mask, 3] = arc & 255
        distance += length
        if index != len(segments) - 1:
            cursor = min(segments.values(), key=lambda s: (s[0]-sx-dx)**2 + (s[1]-sz-dz)**2)
    assert 600 < distance < 1024
    target = Path(__file__).resolve().parents[1] / "Fort1916" / "Surfaces" / "Biebrza channel flow.png"
    Image.fromarray(result).save(target)


if __name__ == '__main__':
    bake()
