"""Paint the crossed-card foliage atlas for the Osowiec floodplain, RGBA with bleed."""
from pathlib import Path
import math
import random
from PIL import Image,ImageDraw,ImageFilter
from scipy.ndimage import distance_transform_edt
import numpy as np

side=512; oversample=2; size=side*oversample
surface=Path(__file__).resolve().parents[1]/"Fort1916"/"Surfaces"
scan=np.array(Image.open(surface/"Sod_colour.jpg").convert("RGB").resize((64,64)))
atlas=Image.new("RGBA",(side*2,side*2))
for tile in range(4):
    rnd=random.Random(6091+tile)
    canvas=Image.new("RGBA",(size,size)); d=ImageDraw.Draw(canvas)
    def p(x,y):return (round(x*size),round((1-y)*size))
    for blade in range([130,97,78,84][tile]):
        bx=rnd.gauss(.49,.105); by=rnd.uniform(.015,.085)
        h=rnd.uniform(.21,.84)*(1.08 if tile==2 else 1)
        lean=rnd.gauss(0,.21)*(.55+h*.65); bend=rnd.uniform(.033,.26)
        width=rnd.uniform(.003,.014)*(1.26 if tile==3 else 1)
        tone=scan[rnd.randrange(64),rnd.randrange(64)].astype(float)
        tint=([.67,.83,.54],[.85,.85,.54],[.77,.77,.51],[.73,.81,.58])[tile]
        col=np.clip((tone*.36+np.array([85,80,66])*.87)*np.array(tint)*rnd.uniform(.78,1.34),15,195)
        curve=[];left=[];right=[]
        for n in range(13):
            t=n/12; x=bx+lean*t**1.38; y=by+h*t-bend*t**3.2*abs(lean)*2
            thick=width*(.38+.62*math.sin(math.pi*t)**.65)*(1-t**2)
            curve.append(p(x,y));left.append(p(x-thick*.52,y));right.append(p(x+thick*.52,y))
        d.polygon(left+right[::-1],fill=(*col.astype(int),255))
        d.polygon(left+curve[::-1],fill=(*np.clip(col*1.23,0,235).astype(int),255))
        if tile==2 and blade%7==0:
            for n in range(6):
                t=.76+n*.035; x=bx+lean*t**1.38;y=by+h*t-bend*t**3.2*abs(lean)*2
                for direction in (-1,1):
                    q=p(x+.014*direction,y+.049*(1-t*.6))
                    d.line([p(x,y),q],fill=(134,116,69,246),width=round(.0037*size))
    # Transparent pixels carry the colour of the closest blade through DXT and mipmapping.
    plant=canvas.resize((side,side),Image.Resampling.LANCZOS)
    pix=np.array(plant)
    ii=distance_transform_edt(pix[:,:,3]<28,return_distances=False,return_indices=True)
    blank=pix[:,:,3]<28; pix[blank,:3]=pix[ii[0][blank],ii[1][blank],:3]
    atlas.paste(Image.fromarray(pix),(tile%2*side,tile//2*side))
atlas.save(surface/"Floodplain_sedges.png",optimize=True)
