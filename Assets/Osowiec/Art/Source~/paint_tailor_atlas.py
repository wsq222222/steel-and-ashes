"""Original printed and embroidered field issue marks. Atlas with four independent cells."""
from pathlib import Path
from math import pi
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

out=Path(__file__).resolve().parents[1]/"Cast"/"Textures"
n=512
atlas=Image.new("RGB",(2*n,2*n))
relief=Image.new("L",atlas.size)
rng=np.random.default_rng(17)
specs=[((91,77,54),(182,148,92),"ПНЕВМОТЯГА","М.07","БАТАРЕЯ · РЕМОНТ",0),
       ((54,67,58),(174,160,106),"ДАЛЬНОМЕР","№ 24","ОПТИЧЕСКАЯ МАСТЕРСКАЯ",1),
       ((106,88,56),(176,154,104),"СВЯЗНОЙ","III","ПОЧТА · ОПЕРАТИВНО",2),
       ((48,54,52),(137,147,127),"WACHPOSTEN","B.17","FESTUNG · ABTEILUNG",3)]
font_root=Path("C:/Windows/Fonts")


def font(size,bold=False):
    for face in ("consolab.ttf" if bold else "consola.ttf","DejaVuSansMono.ttf"):
        file=font_root/face
        try:return ImageFont.truetype(str(file) if file.exists() else face,size)
        except OSError:pass
    return ImageFont.load_default(size=size)


def center(draw,x,y,s,f,fill):
    draw.text((x,y),s,font=f,fill=fill,anchor="mm",stroke_width=0)


for base,ink,title,no,sub,i in specs:
    y,x=np.mgrid[:n,:n]
    noise=rng.normal(0,3.8,(n,n))
    thread=np.cos(x*2.1+y*.21)*3+np.sin(y*2.4)*3.8
    blot=np.asarray(Image.fromarray(rng.integers(30,202,(14,14),dtype=np.uint8)).resize((n,n),Image.Resampling.BICUBIC),dtype=float)/13-8
    weave=np.clip(np.array(base)+((thread+noise+blot)[:,:,None]),0,255).astype("uint8")
    cloth=Image.fromarray(weave);mask=Image.new("L",(n,n));d=ImageDraw.Draw(mask)
    d.rounded_rectangle((20,28,492,482),radius=22,outline=170,width=9)
    for xx in range(37,481,19):
        d.line([(xx,42),(xx+10,43)],fill=215,width=3);d.line([(xx,469),(xx+10,468)],fill=189,width=3)
    for yy in range(57,467,18):
        for xx in (34,478):d.line([(xx,yy),(xx+1,yy+9)],fill=195,width=3)
    center(d,n/2,101,title,font(42,True),238)
    d.line([(64,140),(448,140)],fill=190,width=4)
    if i==0:
        d.arc((79,169,273,350),188,350,fill=240,width=12)
        for a in np.linspace(np.pi*1.12,np.pi*1.86,7):
            d.line([(176+np.cos(a)*76,260+np.sin(a)*73),(176+np.cos(a)*88,260+np.sin(a)*84)],fill=207,width=4)
        d.line([(174,264),(209,207)],fill=245,width=7);d.ellipse((161,251,184,274),fill=222)
        center(d,341,254,no,font(44,True),230)
    elif i==1:
        d.ellipse((80,161,260,342),outline=211,width=6);d.ellipse((125,207,214,295),outline=211,width=4)
        d.line([(68,251),(268,251)],fill=220,width=6);d.line([(171,150),(171,351)],fill=230,width=5)
        center(d,355,253,no,font(42,True),230)
    elif i==2:
        for j in range(3):
            x0=85+j*48;d.polygon([(x0,319),(x0+17,306),(x0+58,176),(x0+39,189)],fill=232)
        center(d,340,252,no,font(74,True),230)
    else:
        d.polygon([(159,165),(234,194),(225,271),(159,318),(97,274),(89,197)],outline=226,width=7)
        d.line([(109,221),(150,251),(210,196)],fill=210,width=9)
        center(d,347,254,no,font(48,True),223)
    d.line([(62,362),(450,362)],fill=165,width=4)
    center(d,n/2,402,sub,font(24,True),203)
    chips=(rng.random((n,n))>.975) | ((noise<-3)&(blot>3))
    pat=np.asarray(mask).astype(float)*(1-chips*.87)
    layer=Image.fromarray(pat.astype("uint8"))
    cloth=Image.composite(Image.new("RGB",(n,n),ink),cloth,layer)
    offset=(n*(i%2),n*(i//2))
    atlas.paste(cloth,offset)
    relief.paste(Image.fromarray(np.clip(117+pat*.36+thread+noise,0,255).astype("uint8")),offset)
atlas.save(out/"FieldTailorAtlas.png")
height=np.asarray(relief,dtype=float)/255
dy,dx=np.gradient(height)
normal=np.stack((-dx*.53,-dy*.53,np.ones_like(dx)),axis=-1)
normal/=np.linalg.norm(normal,axis=-1,keepdims=True)
Image.fromarray(((normal*.5+.5)*255).astype("uint8")).save(out/"FieldTailorAtlas_N.png")
mask=np.zeros((n*2,n*2,4),dtype=np.uint8)
mask[:,:,3]=(49+22*height).astype("uint8")
Image.fromarray(mask).save(out/"FieldTailorAtlas_MS.png")

# Each deployed coat has its own lower wetting and dried-silt pattern. These
# cells use panel UVs, so stains remain on the garment when legs and hips move.
n=768
dy,dx=np.mgrid[:n,:n];u=dx/n;v=1-dy/n
colors=Image.new("RGB",(n*2,n*2));normals=Image.new("RGB",colors.size);finishes=Image.new("RGBA",colors.size)
for i,base in enumerate(((88,85,62),(105,94,69),(75,82,65),(75,83,83))):
    def field(sz):
        values=rng.normal(128,39,(sz,sz)).clip(0,255).astype("uint8")
        return (np.asarray(Image.fromarray(values).resize((n,n),Image.Resampling.BICUBIC),dtype=float)-128)/55
    broad,fleck=field(14),field(84)
    grain=rng.normal(0,1,(n,n))
    nap=np.sin(dx*2.18+dy*.33)*.23+np.cos((dx-dy)*1.47)*.24+grain*.28
    streak=np.sin(2*pi*(4*u+.16*i))+.42*np.sin(2*pi*(11*u-.14*i))
    waterline=.14+.022*streak+.025*broad
    damp=np.clip((waterline-v)/.095+.43,0,1)
    silt=np.clip((.085+.017*streak+.034*fleck-v)/.047,0,1)
    mottling=1+.066*broad+.027*fleck+.028*nap
    pigment=np.array(base,dtype=float)[None,None,:]*mottling[:,:,None]
    pigment*=1-.30*damp[:,:,None]
    pigment+=silt[:,:,None]*np.array((19,14,6))
    # Tiny pale splashes above the band and the brighter band of a worn facing.
    grits=(grain>1.15)*(fleck>.10)*np.clip((waterline+.05-v)/.20,0,1)
    pigment+=grits[:,:,None]*np.array((18,14,8))
    seams=np.exp(-(np.sin(2*pi*(u+.011))/.048)**2)
    pigment+=seams[:,:,None]*np.array((6,4,2))
    h=nap*.15+.13*fleck+.18*grits
    sy,sx=np.gradient(h)
    nm=np.stack((-sx,-sy,np.ones_like(sx)),axis=-1)
    nm/=np.linalg.norm(nm,axis=-1,keepdims=True)
    mask=np.zeros((n,n,4),dtype=np.uint8)
    mask[:,:,3]=np.clip(54+26*damp+5*fleck+2*grain,20,108)
    pos=(i%2*n,i//2*n)
    colors.paste(Image.fromarray(np.uint8(pigment.clip(0,255))),pos)
    normals.paste(Image.fromarray(np.uint8((.5+.5*nm)*255)),pos)
    finishes.paste(Image.fromarray(mask),pos)
colors.save(out/"FieldGreatcoat.png")
normals.save(out/"FieldGreatcoat_N.png")
finishes.save(out/"FieldGreatcoat_MS.png")
