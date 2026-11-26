"""Print the headquarters briefing and tabletop sheet from the playable Unity scene.

Run Steel and Ashes > Export headquarters paper survey first.
"""
import ast
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy.ndimage import distance_transform_edt, gaussian_filter

source = Path(__file__).resolve().parent
project = source.parents[3]
survey = json.loads((project / "Library/OsowiecBriefingSurvey.json").read_text(encoding="utf-8-sig"))
point = lambda p: (p["x"], p["y"])
marks = dict(zip(survey["names"], map(point, survey["marks"])))
starts = list(map(point, survey["deployment"]))
assert len(marks) == 11 and len(starts) == 6
routes = {}
for item in ast.parse((source / "make_osowiec_landscape.py").read_text(encoding="utf-8-sig")).body:
    if isinstance(item, ast.Assign) and isinstance(item.targets[0], ast.Name):
        name = item.targets[0].id
        if name in ("start", "marsh", "tunnel"):
            routes[name] = ast.literal_eval(item.value)

W, H = 1680, 1004
L, T, R, B = 49, 135, 1630, 828
scale = (R-L)/(survey["right"]-survey["left"])
top = 29.0
def at(x, z):
    return (round(L+(x-survey["left"])*scale), round(T+(top-z)*scale))

rng = np.random.default_rng(19)
paper = np.array([215., 205., 174.]) + rng.normal(0, 2.1, (H,W,1))
cloud = Image.fromarray(rng.normal(0, 2, (36,56)).astype(np.float32)).resize((W,H),Image.Resampling.BICUBIC)
paper += np.asarray(cloud)[:,:,None]
image = Image.fromarray(np.uint8(np.clip(paper,0,255)))
draw = ImageDraw.Draw(image)
ink=(61,58,42); red=(136,58,43); green=(57,78,59); river=(71,102,101)
light=(215,205,174); rule=(147,134,101)
captions=[]
def font(size, face="georgia"):
    return ImageFont.truetype(str(Path("C:/Windows/Fonts")/(face+".ttf")),size)
def type_at(pos, value, size, colour=ink, face="georgia", anchor="mm", halo=0):
    style=font(size,face)
    draw.text(pos,value,font=style,fill=colour,anchor=anchor,
              stroke_width=halo,stroke_fill=light)
    if T<pos[1]<B:
        x0,y0,x1,y1=draw.textbbox(pos,value,font=style,anchor=anchor,stroke_width=halo)
        captions.append(dict(x=x0-3,y=y0-3,w=x1-x0+6,h=y1-y0+6))
def line(points, colour=ink, width=2):
    draw.line(points,fill=colour,width=width,joint="curve")
def dashed(points, colour, width=4, dash=11, space=8):
    for a,b in zip(points,points[1:]):
        a,b=np.array(a),np.array(b);delta=b-a;length=np.linalg.norm(delta)
        if length<1:continue
        for i in np.arange(0,length,dash+space):
            line([tuple(a+delta*i/length),tuple(a+delta*min(length,i+dash)/length)],colour,width)

heights=np.asarray(survey["elevation"],np.float32).reshape(survey["rows"],survey["columns"])
missing=heights<-80
assert missing.mean()<.03
near=distance_transform_edt(missing,return_distances=False,return_indices=True)
heights[missing]=heights[tuple(near[:,missing])]
row=lambda z:(survey["top"]-z)/(survey["top"]-survey["bottom"])*(survey["rows"]-1)
terrain=Image.fromarray(heights).transform((R-L,B-T),Image.Transform.EXTENT,
    (0,row(top),survey["columns"]-1,row(top-(B-T)/scale)),Image.Resampling.BICUBIC)
terrain=gaussian_filter(np.array(terrain),1.05)
slope=np.gradient(terrain)
shade=np.clip(4+slope[1]*220-slope[0]*160,0,25)
bands=np.floor(terrain/.50)
edge=(bands[1:,1:]!=bands[:-1,1:])|(bands[1:,1:]!=bands[1:,:-1])
shade[1:,1:]+=edge*63
image.paste((103,102,72),(L,T,R,B),Image.fromarray(shade.astype(np.uint8)))

# Every water triangle is projected from a live river or oxbow mesh.
mask=Image.new("L",(R-L,B-T)); water=ImageDraw.Draw(mask)
points=[(x-L,y-T) for x,y in (at(*point(p)) for p in survey["water"])]
for i in range(0,len(points),3):
    water.polygon(points[i:i+3],fill=255)
wet=np.asarray(mask)>0
inshore=distance_transform_edt(wet)
y,x=np.mgrid[:B-T,:R-L]
tint=np.where(wet,.46,0.)+.35*(wet&(inshore<1.8))
tint+=.15*(wet&(((y+.7*np.sin(x*.04))%10)<1.35)&(inshore>3))
image.paste((97,137,133),(L,T,R,B),Image.fromarray(np.uint8(255*tint)))
for u in range(-55,56,10):
    x=at(u,0)[0];line([(x,T),(x,B)],(181,174,144),1)
for v in range(-20,30,10):
    y=at(0,v)[1];line([(L,y),(R,y)],(181,174,144),1)
footprints=Image.new("L",(R-L,B-T))
polygons=ImageDraw.Draw(footprints)
points=[(x-L,y-T) for x,y in (at(*point(p)) for p in survey["roofs"])]
for i in range(0,len(points),3):
    polygons.polygon(points[i:i+3],fill=255)
roof=np.asarray(footprints)>0
yy,xx=np.indices(roof.shape)
hatch=roof*(.18+.44*(distance_transform_edt(roof)<2)+.20*((xx+yy)%11<2))
image.paste((98,84,67),(L,T,R,B),Image.fromarray(np.uint8(hatch*255)))
draw.rectangle((L-2,T-2,R+1,B+1),outline=ink,width=3)
draw.rectangle((L-7,T-7,R+6,B+6),outline=rule,width=1)

for name in ("start","tunnel","marsh"):
    path=[at(*p) for p in routes[name]]
    if name=="marsh":
        dashed(path,red,5,15,10)
    else:
        line(path,ink,9);line(path,light,4)
def square(pos, colour, radius=5):
    x,y=pos; draw.rectangle((x-radius,y-radius,x+radius,y+radius),fill=colour)
def ring(pos,r,colour,fill=None,width=3):
    x,y=pos;draw.ellipse((x-r,y-r,x+r,y+r),outline=colour,width=width,fill=fill)
def note(key,caption,textpos,knee,align="lm",colour=ink):
    pos=at(*marks[key]);end=(textpos[0]-5 if align=="lm" else textpos[0]+5,textpos[1]+2)
    line([pos,knee,end],colour,2);ring(pos,6,colour,light,3)
    type_at(textpos,caption,32,colour,"georgiab",align,2)
def digit(pos,n):
    ring(pos,17,red,light,3);type_at((pos[0],pos[1]-1),str(n),24,red,"georgiab")

for index in range(2):
    cells=starts[3*index:3*index+3]
    xx=[p[0] for p in cells];zz=[p[1] for p in cells]
    x0,y0=at(min(xx)-1.15,max(zz)+1.0);x1,y1=at(max(xx)+1.15,min(zz)-1.0)
    dashed([(x0,y0),(x1,y0),(x1,y1),(x0,y1),(x0,y0)],green,3,8,5)
    for p in cells:square(at(*p),green,4)
    if index==0:
        line([(x0,y0),(x0-36,y0-42),(84,y0-42)],green,2)
        type_at((84,y0-54),"Сбор у лазарета",28,green,"georgiab","lb",2)
    else:
        line([(x0+6,y0),(x0-13,y0-37),(421,y0-37)],green,2)
        type_at((421,y0-46),"Сбор у низины",28,green,"georgiab","lb",2)
note("ward","Лазарет",(331,793),(282,791))
note("phone","Телефон · развилка",(735,474),(699,474))
note("lowland","Низина",(783,254),(851,254))
note("casemate","Каземат",(881,762),(850,762))
note("junction","Стык ходов",(1073,574),(1126,574),"rm")
note("yard","Двор батареи",(1198,269),(1327,269),colour=red)
type_at(at(-48.8,6.6),"Бебжа",34,river,"georgiai","mm",1)
# Two separate terminals and two portals connected below the gallery.
n,s=at(*marks["entryNorth"]),at(*marks["entrySouth"])
dashed([n,s],ink,3,9,7)
for key,numeral in [("north",1),("south",2),("entrySouth",3),("entryNorth",3),("gallery",4)]:
    digit(at(*marks[key]),numeral)
# Survey north and a scale in game metres.
cx,cy=at(-56,22)
line([(cx,cy+20),(cx,cy-17)],ink,2)
draw.polygon([(cx,cy-21),(cx-7,cy+5),(cx,cy),(cx+7,cy+5)],fill=ink)
type_at((cx,cy-24),"С",24,ink,"georgiab","mb")
type_at((48,60),"ОСОВЕЦЪ",65,ink,"georgiab","lm")
type_at((454,55),"Подходы к батарее",44,ink,"georgia","lm")
type_at((1630,62),"ПОЛЕВОЙ ШТАБ",26,red,"georgiab","rm")
line([(48,109),(1630,109)],ink,2)
for px,n,caption in [(67,1,"Северный ввод"),(464,2,"Южный ввод"),(846,3,"Нижний ход"),(1232,4,"Верхняя галерея")]:
    digit((px,880),n);type_at((px+24,880),caption,32,ink,"georgiab","lm")
line([(48,921),(1630,921)],rule,1)
line([(49,958),(111,958)],ink,8);line([(49,958),(111,958)],light,3)
type_at((127,959),"Траншея и каземат",27,ink,anchor="lm")
dashed([(467,958),(533,958)],red,4,13,8)
type_at((549,959),"Открытая низина",27,ink,anchor="lm")
dashed([(852,958),(913,958)],ink,3,7,6)
type_at((927,959),"Подземный переход",27,ink,anchor="lm")
bx=1432;by=956
for i in range(2):
    right=round(bx+(i+1)*scale*5)
    draw.rectangle((round(bx+i*scale*5),by,right,by+6),fill=ink if i==0 else light,outline=ink)
type_at((1574,958),"10 м",21,ink,anchor="lm")
image.save(source.parent/"SurveySheet.png",optimize=True)
layout=dict(width=W,height=H,plot=dict(x=L,y=T,w=R-L,h=B-T),west=survey["left"],
            east=survey["right"],north=top,south=top-(B-T)/scale,captions=captions)
(project/"Assets/Resources/OsowiecJournal/survey-layout.json").write_text(
    json.dumps(layout,indent=2)+"\n",encoding="utf-8")
