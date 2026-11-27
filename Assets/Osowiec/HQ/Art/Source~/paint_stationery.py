"""Paint the original field-office textures. Python, numpy, scipy and Pillow."""
from pathlib import Path
import sys
import numpy as np
from scipy.ndimage import gaussian_filter
from PIL import Image, ImageDraw, ImageFont

OUT = Path(__file__).resolve().parents[1] / 'Textures'
OUT.mkdir(exist_ok=True)
rng = np.random.default_rng(1916)
N = 768
Y,X = np.mgrid[:N,:N]


def unit(a):
    return (a - np.mean(a)) / max(.00001, np.std(a))


def gray(s, shape=(N,N)):
    return unit(gaussian_filter(rng.normal(size=shape), s, mode='wrap'))


def wood():
    bend = gray((12,88))*.77 + 9*np.sin(2*np.pi*X/N) + 4*np.cos(4*np.pi*X/N+Y/157)
    knots = np.zeros_like(X, dtype=float)
    for cx,cy,rx,ry in [(184,144,51,8),(566,531,74,11),(373,659,30,6),(102,398,23,5)]:
        r=((X-cx)/rx)**2+((Y-cy)/ry)**2
        bend += np.exp(-r*.29)*(Y-cy)*.87
        knots += np.exp(-r*.65) * (12+15*(.5+.5*np.sin(9*np.sqrt(r))))
    woodgrain = .5+.5*np.sin((Y+bend)*2*np.pi/11 + .84*gray((4,70)))
    base = 136 + 13*gray((3,57)) + 5*gray((.55,28)) + 6*woodgrain - knots + 5*gray((.45,.7))
    pores = np.maximum(gray((.42,13))-1.15, 0)
    base -= 13*pores
    colors = np.stack([base, base*.75-1, base*.47+5],axis=2)
    Image.fromarray(np.uint8(np.clip(colors,0,255))).save(OUT/'QuarterSawn.png')
    detail = gaussian_filter(base,.63,mode='wrap')
    dy,dx = np.gradient(detail*.016)
    normal = np.stack([-dx,-dy,np.ones_like(dx)],axis=2)
    normal /= np.linalg.norm(normal,axis=2)[:,:,None]
    Image.fromarray(np.uint8((normal*.5+.5)*255)).save(OUT/'QuarterSawn_N.png')


def paper(w,h):
    grain = 2.6*gray(.45,(h,w)) + 3.4*gray(12,(h,w))
    yy,xx=np.mgrid[:h,:w]
    edge=np.minimum(np.minimum(yy,h-yy),np.minimum(xx,w-xx))
    stain=13*np.exp(-edge/19)
    rgb=np.array([200,182,143])[None,None,:]+(grain-stain)[:,:,None]
    return Image.fromarray(np.uint8(np.clip(rgb,0,255)))


def font(size, bold=False):
    f = Path('C:/Windows/Fonts') / ('timesbd.ttf' if bold else 'times.ttf')
    return ImageFont.truetype(str(f),size) if f.exists() else ImageFont.load_default(size=size)


english = "--en" in sys.argv
labels_en = {
    'КРЕПОСТЬ ОСОВЕЦ':'OSOWIEC FORTRESS','ШТАБНАЯ КАНЦЕЛЯРИЯ':'HEADQUARTERS OFFICE',
    'ЖУРНАЛ ДОНЕСЕНИЙ':'DISPATCH REGISTER','Август 1916 г.                       Лист № 12':'August 1916                       Sheet No. 12',
    'Время':'Time','Откуда доставлено':'Sent from','Получил':'Received by','Западный двор':'Western yard',
    'Телефонная станция':'Telephone exchange','Орудийный парк':'Artillery park','Приказ принят':'Orders received',
    'ШТАБ':'HQ','Поперечные профили грунта и каземата':'Earthwork and casemate profiles',
    'I.   Земляной вал':'I.   Earthen rampart','II.   Свод в разрезе':'II.   Vault section',
    'Условный чертёж    •    1916':'Section drawing    •    1916','ЖУРНАЛ':'REGISTER','ПОЛЕВОЙ СВЯЗИ':'FIELD SIGNALS',
}
def translated(value):
    return labels_en.get(value,value) if english else value
def image_name(name):
    return name + ('-en.png' if english else '.png')

def text(d, xy, msg, size=22, bold=False, color=(59,48,37)):
    d.text(xy,translated(msg),font=font(size,bold), fill=color)


def forms():
    im=paper(640,896)
    d=ImageDraw.Draw(im)
    br=(92,74,51)
    d.rectangle((36,40,603,849),outline=br,width=3)
    text(d,(66,56),'КРЕПОСТЬ ОСОВЕЦ',38,True)
    text(d,(67,114),'ШТАБНАЯ КАНЦЕЛЯРИЯ',23)
    d.line((65,176,572,176),fill=br,width=2)
    text(d,(65,196),'ЖУРНАЛ ДОНЕСЕНИЙ',32,True)
    text(d,(64,251),'Август 1916 г.                       Лист № 12',21)
    labels=['Время','Откуда доставлено','Получил']
    for i in range(14):d.line((64,329+i*31,570,329+i*31),fill=(132,114,83),width=1)
    for i,(label,x) in enumerate(zip(labels,[69,196,429])): text(d,(x,300),label,18)
    for x in (177,410): d.line((x,297,x,731),fill=br,width=1)
    for j,(tm,item) in enumerate([('4.20','Западный двор'),('4.45','Телефонная станция'),('5.15','Орудийный парк'),('5.30','Приказ принят')]):
        text(d,(72,331+j*31),tm,17);text(d,(190,331+j*31),item,17)
    d.ellipse((413,698,547,818),outline=(132,78,58),width=5)
    d.ellipse((419,704,541,812),outline=(132,78,58),width=2)
    text(d,(435,745),'ШТАБ',20,True,(133,80,53))
    im.save(OUT/image_name('Dispatch'))
    im=paper(1024,640);d=ImageDraw.Draw(im)
    text(d,(38,25),'КРЕПОСТЬ ОСОВЕЦ',34,True);text(d,(38,73),'Поперечные профили грунта и каземата',24)
    for y in range(126,577,23):d.line((42,y,975,y),fill=(168,153,122))
    for x in range(42,975,28):d.line((x,126,x,570),fill=(175,160,130))
    def profile(y, offset):
        points=[(50,y),(130,y-4),(215,y-50),(312,y-55),(346,y-46),(384,y-4),(490,y-2),(614,y-91),(777,y-92),(867,y-47),(963,y-39)]
        d.line(points,fill=(67,68,55),width=5)
        for k in range(42):
            x=91+k*19
            for i in range(len(points)-1):
                a,b=points[i],points[i+1]
                if a[0]<=x<b[0]:
                    z=a[1]+(b[1]-a[1])*(x-a[0])/(b[0]-a[0])
                    d.line((x,z+3,x-15,z+23),fill=(103,92,67),width=2)
        d.arc((654,y-67,730,y+4),180,360,fill=(83,52,40),width=5)
        d.line((654,y-31,654,y+8,730,y+8,730,y-31),fill=(82,57,41),width=3)
    profile(269,0);profile(484,36)
    text(d,(64,333),'I.   Земляной вал',20)
    text(d,(62,538),'II.   Свод в разрезе',20)
    text(d,(667,580),'Условный чертёж    •    1916',20)
    im.save(OUT/image_name('Profiles'))


def ledger():
    im=paper(512,352); d=ImageDraw.Draw(im)
    for pad,width in [(11,3),(18,1)]:
        d.rectangle((pad,pad,511-pad,351-pad),outline=(102,81,54),width=width)
    for y,copy,size,heavy in [(63,'КРЕПОСТЬ ОСОВЕЦ',29,False),(159,'ЖУРНАЛ',55,True),(224,'ПОЛЕВОЙ СВЯЗИ',24,True),(292,'1916',22,False)]:
        d.text((256,y),translated(copy),anchor='mm',font=font(size,heavy),fill=(75,58,39))
    d.line((133,99,379,99),fill=(128,96,63),width=2)
    im.save(OUT/image_name('LedgerCover'))


if __name__ == '__main__':
    if "--forms" not in sys.argv: wood()
    forms()
    ledger()
