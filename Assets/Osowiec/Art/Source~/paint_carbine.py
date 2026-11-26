"""Paint the display prop's registered pigment, relief and Unity metal/smoothness atlas."""
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = Path(__file__).resolve().parent.parent / 'Cast' / 'Textures'
SIZE = 2048
# Rows run bottom to top in Blender UV space. Keep a gutter inside each row.
ROWS = {'walnut': (0, 620), 'oiled': (620, 895), 'steel': (895, 1235),
        'pewter': (1235, 1390), 'leather': (1390, 1650), 'web': (1650, 1830),
        'brass': (1830, 1930), 'dark': (1930, 1990), 'thread': (1990, 2048)}


def paint():
    rng = np.random.default_rng(191604)
    colour = np.empty((SIZE, SIZE, 3), np.uint8)
    normal = np.empty_like(colour)
    masks = np.zeros((SIZE, SIZE, 4), np.uint8)
    for kind, (low, high) in ROWS.items():
        h = high - low
        y, x = np.mgrid[:h, :SIZE].astype(np.float32)

        def noise(nx, ny):
            grid = np.uint8(rng.random((ny, nx)) * 255)
            tile = Image.fromarray(grid).resize((SIZE, h), Image.Resampling.BICUBIC)
            return np.asarray(tile, dtype=np.float32) / 127.5 - 1

        fine = rng.normal(0, 1, (h, SIZE)).astype(np.float32)
        cloud = noise(22, 7) * .60 + noise(77, 18) * .33
        marks = Image.new('L', (SIZE, h))
        pen = ImageDraw.Draw(marks)
        for _ in range(int(h * 1.1)):
            px, py = rng.uniform([0, 0], [SIZE, h]); length = rng.uniform(3, 49)
            pen.line([(px, py), (px + length*.4, py + 1), (px + length, py + rng.uniform(-5, 7))],
                     fill=int(rng.uniform(38, 212)), width=int(rng.choice([1, 1, 1, 2])))
        scratches = np.asarray(marks.filter(ImageFilter.GaussianBlur(.46)), dtype=float)/255
        # Wear accumulates around the grip, exposed corners and the forward band.
        # Broad rubbed areas read at arm's length. Smaller defects share a mask across PBR channels.
        u, v = x / SIZE, y / h
        rubbed = np.zeros_like(x)
        for cx, cy, width, height in ((.14,.23,.14,.21),(.58,.72,.18,.26),
                                      (.83,.37,.10,.18),(.96,.89,.055,.2),(.29,.94,.2,.16)):
            rubbed = np.maximum(rubbed, np.exp(-((u-cx)/width)**2 - ((v-cy)/height)**2))
        rub = np.clip((rubbed + .34*cloud + .14*noise(96,32) - .39)*2.05, 0, 1)
        chip_mask = Image.new('L', (SIZE, h))
        chip_draw = ImageDraw.Draw(chip_mask)
        for _ in range(int(h * .50)):
            px,py = rng.integers(0,SIZE), rng.integers(0,h)
            if rubbed[py,px] < .22: continue
            width, depth = rng.uniform(5,31), rng.uniform(1.5,5.7)
            chip_draw.polygon([(px,py),(px+width*.19,py-depth),(px+width,py-depth*.35),
                               (px+width*.68,py+depth*.62),(px+width*.35,py+depth)],fill=int(rng.integers(100,245)))
        chips = np.asarray(chip_mask.filter(ImageFilter.GaussianBlur(.65)), dtype=np.float32)/255
        color_shift = np.zeros((h, SIZE, 3), np.float32)
        if kind in ('walnut', 'oiled'):
            warp = 13 * noise(5, 4) + 4 * noise(13, 11) + 4*np.sin(x/295 + y/53)
            phase = y + warp
            rings = np.sin(phase*.258 + np.sin(phase*.079))
            pores = np.maximum(0, noise(81, max(10, h//2)) - .30)**1.5
            fibers = noise(34, h) * .62 + noise(220, h) * .32
            grain = .32*rings + .30*np.sin(phase*.092) + .45*fibers - 1.9*pores
            tone = grain*.28 + cloud*.23 + scratches*.33 + .16*noise(4, 3)
            base = np.array((142, 87, 48) if kind == 'walnut' else (120, 74, 42))
            relief = 1.0*grain + .45*fibers - 1.2*scratches + .11*fine
            # Exposed fibres turn grey honey; old working surfaces retain a darker oil stain.
            color_shift += rub[...,None]*np.array([17,22,23]) + chips[...,None]*np.array([42,42,31])
            relief -= chips*3.4 + rub*pores*.85
            metal = 0
            gloss = .40 + .15*cloud + .07*rings - .16*pores + .12*rub - .30*chips
        elif kind in ('steel', 'pewter', 'brass'):
            base = np.array({'steel': (78, 86, 90), 'pewter': (133, 137, 131), 'brass': (155, 117, 66)}[kind])
            scour = noise(50, max(12, h//2))
            freckles = np.maximum(0, noise(390, max(12, h//4)) - .48)
            tone = cloud*.22 + scour*.13 + scratches*.8 - freckles*.38
            relief = .42*scour - scratches*1.4 - .8*freckles + .09*fine
            patina = np.clip((.24-cloud + .27*noise(190,39))*1.35,0,1) * (1-rub*.83)
            bare = np.clip(chips*.94 + rub*.48 + scratches*.24,0,1)
            color_shift += bare[...,None]*np.array([53,53,45]) + patina[...,None]*np.array([9,-1,-10])
            relief -= chips*1.7 + patina*freckles*1.15
            metal = np.clip((.79 if kind == 'steel' else .84) + bare*.16 - .48*patina,0,1)
            gloss = .48 + .12*cloud + scour*.05 + .25*bare - .14*patina - freckles*.24
        elif kind in ('leather', 'dark'):
            base = np.array((89, 57, 37) if kind == 'leather' else (40, 42, 39))
            hide = noise(470, max(12, h//3)) + .4*noise(94, max(12, h//8))
            crack = np.exp(-np.abs(hide)*9)
            tone = cloud*.27 + hide*.11 - crack*.21 + scratches*.38
            relief = .62*hide - .85*crack - scratches*.8
            metal = 0 if kind == 'leather' else .38
            color_shift += rub[...,None]*np.array([16,13,9]) + chips[...,None]*np.array([31,23,16])
            relief -= chips*1.9
            gloss = .35 + .17*cloud - crack*.11 + .13*rub - .18*chips
        else:
            base = np.array((116, 97, 68) if kind == 'web' else (163, 136, 94))
            weave = .50*np.cos(x*1.52 + np.sin(y*.83)) + .56*np.cos(y*1.31 + np.sin(x*.73))
            tone = cloud*.22 + .20*weave + .04*fine
            relief = .72*weave + .18*fine
            metal = 0
            gloss = .24 + .09*cloud
        # Uneven beads brighten the varnish and metal at grazing light. Pigment stays dark underneath.
        if kind in ('walnut', 'oiled', 'steel', 'pewter', 'leather'):
            beads = np.zeros((h, SIZE), float)
            for _ in range(int(h * 1.3)):
                px = rng.integers(8, SIZE-8); py = rng.integers(8, h-8)
                a = rng.uniform(1.1, 4.7); b = a*rng.uniform(.55, .96)
                ry, rx = np.mgrid[-7:8,-7:8]
                lens = np.maximum(0, 1 - (rx/a)**2 - (ry/b)**2)**.68
                reg = beads[py-7:py+8, px-7:px+8]
                np.maximum(reg, lens, out=reg)
            relief += 2.9*beads
            gloss += .38*beads
            tone -= beads*.09
        block = slice(SIZE-high, SIZE-low)
        colour[block] = np.uint8(np.clip(base*(1+tone[..., None])+color_shift, 0, 255))
        dy, dx = np.gradient(relief)
        vec = np.stack((-dx*.59, dy*.59, np.ones_like(dx)), -1)
        vec /= np.linalg.norm(vec, axis=-1)[...,None]
        normal[block] = np.uint8((vec*.5+.5)*255+.5)
        masks[block,:,0] = np.uint8(np.clip(metal*255, 0, 255))
        masks[block,:,3] = np.uint8(np.clip(gloss*255, 8, 250))
    for pixels, suffix in [(colour,''),(normal,'_N'),(masks,'_MS')]:
        Image.fromarray(pixels).save(OUT / ('WorkshopFinish' + suffix + '.png'))


if __name__ == '__main__':
    paint()
