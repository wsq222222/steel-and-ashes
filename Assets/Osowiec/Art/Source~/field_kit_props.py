"""Rebuild the exterior-only Osowiec field props, their shared game atlas, and equipment pictograms."""
from math import atan2, cos, pi, sin, sqrt
from pathlib import Path
import random

from PIL import Image, ImageDraw
import numpy as np


ASSETS = Path(__file__).resolve().parents[3]
KIT = ASSETS / "Resources" / "OsowiecKit"
MODELS = KIT / "Models"
MODELS.mkdir(exist_ok=True, parents=True)

# The shared atlas fits one draw call for each in-game prop.
PALETTE = {
    "iron": (61, 70, 68), "blued": (39, 51, 51), "steel": (146, 153, 143), "brass": (175, 139, 72),
    "olive": (104, 110, 78), "red": (147, 68, 47), "bone": (209, 197, 156), "black": (36, 38, 34),
}


def atlas():
    rng = np.random.default_rng(240)
    size = 256
    sheet = Image.new("RGB", (size * 4, size * 2))
    for index, (finish, color) in enumerate(PALETTE.items()):
        brush = rng.normal(0, 2, (size, 1))
        noise = rng.normal(0, 4, (size, size)) + brush
        cloudy = np.asarray(Image.fromarray(rng.integers(80, 170, size=(16, 16), dtype=np.uint8)).resize(
            (size, size), Image.Resampling.BICUBIC)).astype(float)
        noise += (cloudy - 127) * .20
        base = np.asarray(color)[None, None, :] + noise[:, :, None]
        tile = Image.fromarray(np.clip(base, 0, 255).astype(np.uint8))
        ink = ImageDraw.Draw(tile, "RGBA")
        for _ in range(98):
            x, y = rng.integers(0, size, 2)
            length = int(rng.integers(1, 11))
            ink.line((x, y, x + length, y + int(rng.integers(-2, 3))), fill=(229, 214, 166, 44))
        if finish in ("olive", "red"):
            for _ in range(22):
                x, y = rng.integers(0, size, 2)
                ink.polygon([(x,y), (x+4,y), (x+2,y+1), (x+1,y+3)], fill=(51,52,45,120))
        sheet.paste(tile, (index % 4 * size, (1 - index // 4) * size))
    sheet.save(MODELS / "FieldFinish.png", optimize=True)


class Prop:
    def __init__(self):
        self.positions, self.uvs, self.faces = [], [], []
        self.lows, self.highs = [], []

    def point(self, p):
        self.positions.append(tuple(p))
        return len(self.positions)

    def face(self, indices, finish, coords=None):
        tile = list(PALETTE).index(finish)
        coords = coords or [(0,0),(0,1),(1,1),(1,0)][:len(indices)]
        pairs = []
        for n, (u, v) in zip(indices, coords):
            self.uvs.append(((tile%4+.02+.96*u)/4,(tile//4+.02+.96*v)/2))
            pairs.append(f"{n}/{len(self.uvs)}")
        self.faces.append("f " + " ".join(pairs))

    def profile(self, rings, finish, sides=40, center=(0,0), angles=(0,2*pi), cap=True, shape=None):
        rows = []
        for height, radius in rings:
            row = []
            for j in range(sides+1):
                a = angles[0] + (angles[1]-angles[0])*j/sides
                r = radius * (shape(a) if shape else 1)
                row.append(self.point((center[0]+r*cos(a),height,center[1]+r*sin(a))))
            rows.append(row)
        for i in range(len(rows)-1):
            for j in range(sides):
                a, b = j/sides, (j+1)/sides
                c, d = i/(len(rows)-1), (i+1)/(len(rows)-1)
                self.face([rows[i][j],rows[i+1][j],rows[i+1][j+1],rows[i][j+1]],finish,[(a,c),(a,d),(b,d),(b,c)])
        if cap:
            for index, top in [(0,False),(-1,True)]:
                row = rows[index]
                mid = self.point((center[0],rings[index][0],center[1]))
                for j in range(sides):
                    self.face([mid,row[j+1],row[j]] if top else [mid,row[j],row[j+1]],finish)
        return rows

    def panel(self, rings, finish):
        rows=[[self.point(p) for p in row] for row in rings]
        for lower, upper in zip(rows, rows[1:]):
            for i in range(len(lower)):
                j=(i+1)%len(lower)
                self.face([lower[i],upper[i],upper[j],lower[j]],finish)
        for i in range(1,len(rows[0])-1):
            self.face([rows[0][0],rows[0][i],rows[0][i+1]],finish)
            self.face([rows[-1][0],rows[-1][i+1],rows[-1][i]],finish)

    def tube(self, points, radius, finish, sides=12):
        """Sweep a circular section along a ribbon / external ornament."""
        rings=[]
        for i,p in enumerate(points):
            axis=np.asarray(points[min(i+1,len(points)-1)])-np.asarray(points[max(0,i-1)])
            axis=axis / np.linalg.norm(axis)
            seed=(0,0,1) if abs(axis[2])<.86 else (0,1,0)
            b=np.cross(axis,seed); b=b/np.linalg.norm(b); a=np.cross(b,axis)
            rings.append([np.asarray(p)+radius*(a*cos(-2*pi*j/sides)+b*sin(-2*pi*j/sides)) for j in range(sides)])
        self.panel(rings, finish)

    def stripe(self, y0,y1,r,color):
        self.profile([(y0,r-.0004),(y0+.0005,r),(y1-.0005,r),(y1,r-.0004)],color,cap=False)

    def export(self,name):
        out=["# Osowiec cast exterior",f"o {name}"]
        out.extend("v %.6f %.6f %.6f" % p for p in self.positions)
        out.extend("vt %.6f %.6f" % uv for uv in self.uvs)
        out.extend(self.faces)
        (MODELS / (name+".obj")).write_text("\n".join(out)+"\n", encoding="ascii")


def grenade():
    p=Prop()
    rows=[(-.054,.017),(-.048,.031),(-.032,.043),(-.008,.048),(.017,.047),(.036,.039),(.048,.027),(.054,.019)]
    p.profile(rows,"blued")
    # Offset enamel scales give the silhouette a cast, seed-like pattern.
    for row in range(len(rows)-1):
        y0,r0=rows[row]; y1,r1=rows[row+1]
        for i in range(10):
            start=i*2*pi/10+.028
            p.profile([(y0+.001,r0+.0002),(y0+.003,r0+.0023),(y1-.003,r1+.0023),(y1-.001,r1+.0002)],
                      "olive" if row not in (4,5) else "red",5,angles=(start,start+2*pi/10-.056),cap=False)
    p.profile([(.051,.018),(.053,.020),(.059,.020),(.061,.017),(.070,.015),(.072,.012)],"brass")
    p.profile([(.071,.013),(.072,.014),(.079,.014),(.081,.010)],"iron",10)
    p.stripe(-.012,-.007,.051,"brass")
    # A raised field-mark is repeated on the two broad shoulders.
    for z in [-1,1]:
        for x in [-.007,.007]:
            p.tube([(x-.004,.018,.047*z),(x,.024,.048*z),(x+.004,.018,.047*z)],.0015,"bone",8)
    p.export("Grenade")
    ring=Prop()
    arc=[(.015*cos(i*2*pi/32),.014*sin(i*2*pi/32),0) for i in range(33)]
    ring.tube(arc,.0021,"brass")
    ring.export("KitRing")


def smoke(gas=False):
    p=Prop()
    p.profile([(-.066,.027),(-.064,.033),(-.059,.036),(-.051,.036),(-.048,.034),(.056,.034),
               (.059,.037),(.066,.037),(.069,.031)],"olive")
    for y in [-.059,-.053,.056,.064]:
        p.stripe(y,y+.003,.0374,"steel")
    if gas:
        for quarter in range(4):
            a=quarter*pi/2
            p.profile([(-.018,.0353),(.041,.0353)],"bone",12,angles=(a,a+pi/2),cap=False)
    else:
        p.stripe(.019,.038,.0348,"bone")
    p.stripe(-.023 if gas else .014,-.020 if gas else .017,.035,"red")
    for i in range(0 if gas else 6):
        start=i*pi/3+.12
        p.profile([(.022,.0350),(.035,.0350)],"red",4,angles=(start,start+.27),cap=False)
    p.profile([(.068,.028),(.071,.027),(.072,.021),(.076,.018),(.085,.017),(.088,.013)],"blued",24)
    p.stripe(.081,.085,.018,"brass")
    # Side seam and recessed inspection dots.
    p.tube([(.033,-.046,.008),(.034,.051,.008)],.0012,"iron")
    for i in range(12):
        a=i*pi/6
        p.profile([(.069,.0025),(.071,.0025)],"black",10,center=(.023*cos(a),.023*sin(a)))
    p.export("Gas" if gas else "Smoke")


def gas_print():
    image=Image.open(MODELS/"FieldFinish.png").convert("RGB")
    tile=Image.new("RGB",(512,512),(203,177,91))
    d=ImageDraw.Draw(tile)
    ink=(49,45,35)
    d.rectangle((9,9,502,502),outline=ink,width=8)
    d.ellipse((94,46,417,343),fill=ink)
    d.polygon([(134,270),(377,270),(357,345),(324,351),(316,424),(196,424),(188,351),(157,344)],fill=ink)
    yellow=(203,177,91)
    for x in (148,282): d.ellipse((x,207,x+81,285),fill=yellow)
    d.polygon([(256,268),(229,324),(281,324)],fill=yellow)
    for x in (219,252,285): d.rectangle((x,372,x+9,427),fill=yellow)
    w=image.width//4
    index=list(PALETTE).index("bone")
    image.paste(tile.resize((w,w),Image.Resampling.LANCZOS),((index%4)*w,(1-index//4)*w))
    image.save(MODELS/"FieldGasFinish.png",optimize=True)


def bayonet():
    p=Prop()
    def ring(y,x,wide,thick):
        return [(x+wide,y,0),(x,y,thick),(x-wide,y,0),(x,y,-thick)]
    p.profile([(0,.011),(.008,.015),(.021,.014),(.033,.013),(.04,.016)],"blued",16)
    p.stripe(.025,.034,.0155,"brass")
    p.panel([ring(.033,0,.022,.011),ring(.042,0,.023,.011),ring(.051,0,.016,.007)],"iron")
    rows=[(.050,0,.014,.006),(.064,0,.015,.006),(.158,.001,.013,.0055),
          (.224,.003,.010,.0044),(.264,.007,.006,.003),(.293,.009,.0006,.0005)]
    blades=[ring(*r) for r in rows]
    indexed=[[p.point(v) for v in row] for row in blades]
    for lower,upper in zip(indexed,indexed[1:]):
        for i in range(4):
            j=(i+1)%4
            p.face([lower[i],upper[i],upper[j],lower[j]],"steel" if i in (0,2) else "iron")
    for side in (-1,1):
        p.tube([(-.003,.067,.0052*side),(-.002,.149,.0048*side),(.002,.214,.0041*side)],.0009,"blued",6)
    p.export("Bayonet")


def icons():
    # Two inks and a narrow dark keyline keep the silhouettes readable on selected and idle wheel wedges.
    paper=(237,224,183,255); sand=(185,161,108,255); shadow=(60,57,42,255); light=(255,246,209,255)
    scale=4
    def draw(name, recipe):
        img=Image.new("RGBA",(128*scale,128*scale))
        d=ImageDraw.Draw(img)
        def pts(points): return [(round(x*scale),round(y*scale)) for x,y in points]
        def line(points, color=paper, width=3): d.line(pts(points),fill=color,width=round(width*scale),joint="curve")
        def poly(points, color=paper):
            d.polygon(pts(points),fill=color); line(points+[points[0]], shadow,1.4)
        def ellipse(box, color, outline=None, width=1):
            d.ellipse(tuple(round(v*scale) for v in box),fill=color,outline=outline,width=round(width*scale))
        recipe(line,poly,ellipse)
        img.resize((192,192),Image.Resampling.LANCZOS).save(KIT/(name+".png"),optimize=True)
    def rifle(l,p,e):
        p([(10,87),(24,72),(43,66),(48,59),(58,60),(99,35),(104,35),(115,28),(119,32),(108,40),
           (105,40),(65,66),(56,75),(48,74),(43,84),(36,84),(36,78),(28,83),(16,97)])
        l([(58,62),(103,36)],light,1.8);l([(67,66),(102,43)],sand,2);l([(18,86),(33,76)],sand,3)
        l([(45,72),(45,78),(51,78),(56,71)],sand,3);l([(91,40),(90,36),(94,33)],paper,2)
    def grenade(l,p,e):
        e((53,17,81,44),None,sand,5)
        p([(46,39),(48,30),(70,32),(73,43),(81,47),(90,60),(92,75),(88,87),(76,99),(62,105),
           (49,103),(35,95),(28,82),(28,65),(34,50)],paper)
        p([(48,34),(70,34),(77,46),(87,51),(84,56),(69,48),(63,43),(46,42)],sand)
        for line in [[(33,62),(55,65),(83,59)],[(29,76),(56,80),(90,72)],[(34,88),(60,93),(86,84)],
                     [(46,49),(40,68),(43,84),(52,99)],[(62,48),(58,64),(61,80),(66,101)],[(72,50),(76,65),(78,89)]]:
            l(line,shadow,2.8)
        l([(37,56),(40,50),(46,47)],light,2)
    def smoke(l,p,e):
        p([(49,35),(54,26),(72,30),(75,37),(80,41),(85,48),(67,102),(59,106),(32,98),(29,91),(46,39)])
        p([(51,39),(54,29),(70,32),(70,42)],sand);p([(42,55),(80,64),(77,73),(39,64)],sand)
        for x,y in [(48,58),(58,61),(68,64)]:l([(x-1,y+4),(x+3,y-2)],shadow,2)
        l([(32,91),(60,98),(70,97)],shadow,2.8);l([(46,43),(72,50),(83,48)],shadow,2.6)
        for pts_ in [[(78,34),(85,29),(83,22),(88,16)],[(85,40),(95,35),(98,27)]]:l(pts_,sand,2.5)
    def gas(l,p,e):
        p([(38,35),(48,29),(49,19),(77,19),(78,29),(91,36),(91,94),(85,102),(40,102),(34,94),(34,40)],sand)
        p([(37,44),(88,44),(88,87),(37,87)],shadow)
        e((44,46,82,74),paper)
        p([(49,65),(77,65),(73,80),(54,80)],paper)
        for x in (50,66): e((x,57,x+9,65),shadow)
        p([(63,63),(60,69),(66,69)],shadow)
        for x in (58,64,69): l([(x,75),(x,81)],shadow,1.8)
        l([(39,89),(86,89)],light,2)
    def bayonet(l,p,e):
        p([(21,97),(17,93),(35,75),(33,69),(37,65),(43,70),(71,45),(109,20),(87,49),
           (52,78),(56,84),(53,89),(46,84),(28,101)])
        l([(48,75),(89,41),(101,30)],shadow,2.5)
        for x,y in [(26,91),(30,87),(34,83)]:l([(x-2,y-2),(x+2,y+2)],sand,2)
        l([(32,70),(52,89)],sand,2.5)
    def leap(l,p,e):
        p([(19,92),(32,89),(43,80),(54,52),(60,42),(73,48),(65,59),(62,73),(75,80),(78,89),(66,91),
           (48,86),(43,94),(35,98),(20,98)])
        l([(47,82),(58,73),(66,79)],shadow,3)
        l([(28,73),(43,66)],sand,3);l([(24,65),(46,56)],sand,2.5)
        l([(75,59),(84,40),(100,28)],paper,3.5)
        p([(98,25),(87,25),(104,17),(105,35)],sand)
    def periscope(l,p,e):
        p([(35,97),(36,84),(46,85),(58,48),(55,45),(59,33),(70,29),(90,35),(83,54),(70,51),
           (59,89),(56,99)])
        p([(60,35),(70,32),(86,37),(82,49),(57,43)],sand)
        l([(63,38),(74,41),(78,39)],light,2);l([(51,80),(62,50)],light,3)
        l([(37,90),(57,95)],shadow,3);l([(90,31),(98,25)],sand,2.5);l([(92,40),(104,39)],sand,2.5)
    def rush(l,p,e):
        p([(25,78),(44,67),(56,55),(54,42),(66,37),(70,26),(77,30),(81,39),(93,42),(102,48),(99,54),
           (86,55),(82,62),(75,63),(64,74),(51,79),(36,95),(29,96),(39,79)])
        p([(69,37),(74,31),(78,41)],sand);e((84,44,88,48),shadow)
        l([(58,52),(66,58),(74,57)],shadow,2.4)
        l([(20,62),(41,56)],sand,3);l([(18,72),(39,66)],sand,2.8);l([(26,52),(45,48)],sand,2)
    for name,recipe in [("Rifle",rifle),("Grenade",grenade),("Smoke",smoke),("Gas",gas),("Bayonet",bayonet),
                        ("Leap",leap),("Periscope",periscope),("Rush",rush)]:
        draw(name,recipe)


if __name__=="__main__":
    atlas()
    grenade()
    smoke()
    smoke(True)
    gas_print()
    bayonet()
    icons()

