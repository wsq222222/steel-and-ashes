"""Build the original Osowiec terrain, fortification and props. Source units are metres.
Coordinates supplied to this authoring script are (east, north, elevation).
Run through Blender --background --python path/to/this_script.py
"""
from collections import defaultdict
from functools import lru_cache
from math import sin, cos, pi, atan2, sqrt, floor, ceil, exp
from pathlib import Path
import random
import sys
import bpy
from mathutils import Vector

source = Path(__file__).resolve().parent
sys.path.insert(0, str(source))
import western_ward
import western_bank
import biebrza_valley as valley
output = source.parent / "Fort1916"
output.mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version=0
rng = random.Random(1916)

# One batch per authored object/sector rather than one draw call per brick or reed.
pal = {"Earth": (.29,.23,.16), "Sod": (.25,.25,.15), "Brick": (.39,.21,.14),
       "Concrete": (.4,.39,.34), "Timber": (.24,.18,.13), "Bark": (.25,.22,.17),
       "Oxide": (.19,.18,.15), "Water": (.15,.17,.15), "Canvas": (.37,.32,.22),
       "Reed": (.34,.31,.16), "Grass": (.19,.23,.13), "Glass": (1,.6,.2),
       "Brass": (.47,.37,.17), "Tar": (.12,.13,.12), "Flag": (.47,.23,.16),
       "Sedge": (.31,.35,.16), "Seed": (.39,.31,.17), "River": (.13,.22,.23)}
mats = {}
for name, col in pal.items():
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*col, 1)
    mats[name] = m

class Geometry:
    def __init__(self):
        self.verts, self.faces, self.paint = [], [], []
    def add(self, verts, faces, mat):
        start = len(self.verts)
        self.verts.extend(verts)
        self.faces.extend([tuple(start+i for i in face) for face in faces])
        self.paint.extend(mat if isinstance(mat,list) else [mat]*len(faces))

groups = defaultdict(Geometry)
cube_faces = [(3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
def box(g,m,x,y,z,dx,dy,dz,angle=0,jag=0):
    co,si=cos(angle),sin(angle)
    pts=[]
    for iz in (-1,1):
        for ix,iy in ((-1,-1),(1,-1),(1,1),(-1,1)):
            a,b=dx*ix/2,dy*iy/2
            pts.append((x+a*co-b*si,y+a*si+b*co,z+dz*iz/2+(rng.uniform(-jag,jag) if jag else 0)))
    groups[g].add(pts,cube_faces,m)
def pole(g,m,a,b,r,tip=None,n=8):
    a,b=Vector(a),Vector(b)
    v=b-a
    if v.length < .0001: return
    v.normalize()
    h=v.cross(Vector((0,0,1)) if abs(v.z)<.91 else Vector((1,0,0))).normalized()
    k=v.cross(h)
    if tip is None: tip=r
    verts=[tuple(cen+rad*(cos(i*2*pi/n)*h+sin(i*2*pi/n)*k)) for cen,rad in ((a,r),(b,tip)) for i in range(n)]
    faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]
    faces += [(i,(i+1)%n,n+(i+1)%n,n+i) for i in range(n)]
    groups[g].add(verts,faces,m)
def ring(g,m,cx,cy,z,ra,rb,top,n=40):
    pts=[(cx+r*cos(i*2*pi/n),cy+r*sin(i*2*pi/n),h) for r,h in ((ra,z),(rb,z),(ra,top),(rb,top)) for i in range(n)]
    faces=[]
    for i in range(n):
        j=(i+1)%n
        faces.extend([(i,j,2*n+j,2*n+i),(n+j,n+i,3*n+i,3*n+j),(2*n+i,2*n+j,3*n+j,3*n+i)])
    groups[g].add(pts,faces,m)

start=[(-48,-16),(-34,-16),(-34,-9),(-25,-9),(-25,-3),(-15,-3)]
marsh=[(-15,-3),(-15,6),(-9,12),(0,14),(10,11),(18,6),(25,3),(33,0)]
tunnel=[(-15,-3),(-13,-12),(-9,-12),(10,-12),(16,-12),(20,-4),(25,0),(33,0)]
craters=[(-21,9,3.1),(-8,17.5,4),(1,10,2.5),(7,16,2.9),(13.5,5.8,2.6),
         (-38,6.5,3.6),(-4,23,3.6),(17,18.5,4),(10,-21,4),(29,-21,3.2)]
event_sites=[(-4,13),(11,11)]
def smooth(lo,hi,v):
    t=max(0,min(1,(v-lo)/(hi-lo)))
    return t*t*(3-2*t)
def pathdist(x,y,path):
    least=1e9
    for (ax,ay),(bx,by) in zip(path,path[1:]):
        ux,uy=bx-ax,by-ay
        t=max(0,min(1,((x-ax)*ux+(y-ay)*uy)/(ux*ux+uy*uy)))
        least=min(least,(x-ax-t*ux)**2+(y-ay-t*uy)**2)
    return sqrt(least)
def n2(x,y):
    return (sin(x*.46+y*.21)+.45*cos(y*1.03+x*.37)+.23*sin(x*2.63-y*.87))*.55
@lru_cache(maxsize=380000)
def height(x,y):
    outside=max(abs(x)-72,abs(y)-45,0)
    if outside>=7:
        return valley.grade(x,y,valley.far_ground(x,y),0)
    d0=pathdist(x,y,start); d1=pathdist(x,y,marsh); d2=pathdist(x,y,tunnel)
    h=1.84+.58*n2(x*.32,y*.32)+.28*n2(x,y)
    h+=2.65*exp(-((x-3)/17)**4-((y-1)/8)**4)
    h+=1.1*exp(-((x+30)/12)**4-((y+6)/8)**4)
    yard=((x-33)/13.8)**2+((y-.4)/13.0)**2
    h*=smooth(.76,1.52,yard)
    h*=smooth(2.28,3.58,d0+.14*n2(x,y))
    h*=smooth(2.24,6.25,d1)
    h*=smooth(2.06,3.72,d2)
    h+=.019*n2(x*1.2,y*1.2)
    for cx,cy,r in craters:
        d=sqrt((x-cx)**2+((y-cy)*1.15)**2)/r
        if d<1.62:
            rim=d+.075*sin((x-cx)*1.28+.43*(y-cy))+.062*n2(x,y)
            depth=.51+.185*r
            lip=.22+.17*(1+sin(.7*x-.5*y))
            h+=(-depth*(1-smooth(.18,1,rim))+lip*exp(-((rim-.96)/.19)**2))*smooth(1.05,2.42,min(d0,d1,d2))
    if -48<x<-35 and -18.6<y<-16.2:
        cut=((x+41.5)/4.9)**2+((y+17.25-.12*sin(x*1.14))/.71)**2
        h-=.16*max(0,1-cut)**2
    outer=smooth(0,7,outside)
    h=h*(1-outer)+valley.far_ground(x,y)*outer
    distance=min(d0,d1,d2)
    shaped=valley.grade(x,y,h,smooth(2.6,5.6,distance))
    # Preserve the board beds, full cart lanes and the foot of their revetments.
    fade=smooth(2.05,3.65,distance)
    return western_ward.grade(x,y,h+fade*(shaped-h))

river_depths=defaultdict(list)

def terrain():
    # Chunked geometry also keeps 16-bit Unity mesh indices and culling useful.
    for i in range(8):
        for j in range(5):
            x0,y0=-72+18*i,-45+18*j
            count=36
            verts=[(x0+x*.5,y0+y*.5,height(x0+x*.5,y0+y*.5)) for y in range(count+1) for x in range(count+1)]
            faces=[]; tags=[]
            for y in range(count):
                for x in range(count):
                    k=y*(count+1)+x
                    wx,wy,h=verts[k]
                    if any(abs(wx+.25-cx)<2 and abs(wy+.25-cy)<2 for cx,cy in event_sites):continue
                    faces.extend([(k,k+1,k+count+2),(k,k+count+2,k+count+1)])
                    for tri in faces[-2:]:
                        valley.water_triangle(groups,river_depths,[verts[t] for t in tri])
                    d=min(pathdist(wx,wy,start),pathdist(wx,wy,tunnel),pathdist(wx,wy,marsh))
                    lush=h>1.4+.54*n2(wx*.8,wy*.8) and d>3
                    tags.extend(["Sod" if lush else "Earth"]*2)
            groups[f"Ground_{i}_{j}"].add(verts,faces,tags)
    # Eight graded outer sectors stitch to the half-metre survey exactly at its border.
    for i,xs in enumerate(valley.outer_axes(72)):
        for j,ys in enumerate(valley.outer_axes(45)):
            if (i,j)==(1,1):continue
            verts=[(x,y,height(x,y)) for y in ys for x in xs]
            faces=[]; width=len(xs)
            for row in range(len(ys)-1):
                for col in range(width-1):
                    k=row*width+col
                    faces.extend(((k,k+1,k+width+1),(k,k+width+1,k+width)))
                    for tri in faces[-2:]:
                        valley.water_triangle(groups,river_depths,[verts[t] for t in tri])
            groups[f"Flood_meadow_{i}_{j}"].add(verts,faces,"Sod")
    # Cut-out squares share the exact boundary heights of their surrounding terrain.
    for idx,(cx,cy) in enumerate(event_sites):
        for state in ("Plate","Hollow"):
            verts=[]; faces=[]; count=16
            for j in range(count+1):
                for i in range(count+1):
                    x=cx-2+i*.25;y=cy-2+j*.25;h=height(x,y)
                    r=sqrt((x-cx)**2+((y-cy)*1.07)**2)
                    if state=="Hollow" and r<1.94:
                        warped=r+.12*sin((x-cx)*3.8+(y-cy)*1.2)+.05*sin(y*6.3)
                        h+=(-.68*(1-smooth(.18,1.54,warped))+.19*exp(-((warped-1.37)/.25)**2))*(1-smooth(1.56,1.94,r))
                    verts.append((x,y,h))
                    if i and j:
                        k=j*17+i;faces.append((k-18,k-17,k,k-1))
            groups[f"Event_{state}_{idx}"].add(verts,faces,"Earth")
    for idx,(x,y) in enumerate([(-12.8,8.8),(15.0,7.7)]):
        g=f"WireSnag_{idx}"
        for d in (-1.0,1.0):
            h=height(x,y+d)
            pole(g,"Bark",(x,y+d,h-.05),(x+.06,y+d,h+.6),.038,.03,n=7)
        prev=None
        for j in range(90):
            t=j/89; a=t*2*pi*8
            q=(x+.19*cos(a),y-1.06+2.12*t,height(x,y-1.06+2.12*t)+.31+.18*sin(a))
            if prev:pole(g,"Oxide",prev,q,.017,n=5)
            prev=q

def deck():
    # Individual slats and rails are embedded in the bank. Centre lanes stay clear.
    for idx,(a,b) in enumerate(zip(start,start[1:])):
        ax,ay=a; dx,dy=b[0]-ax,b[1]-ay
        L=sqrt(dx*dx+dy*dy); ux,uy=dx/L,dy/L; vx,vy=-uy,ux; ang=atan2(uy,ux)
        g=f"Walkway_{idx}"
        for d in (-.64,.64):
            box(g,"Timber",ax+dx*.5+vx*d,ay+dy*.5+vy*d,.015,L,.12,.045,ang)
        for n in range(ceil(L/.30)):
            t=(n+.5)*.30
            if t>L-.02:continue
            shift=.16*sin(t*.4+idx)
            box(g,"Timber",ax+ux*t+vx*shift,ay+uy*t+vy*shift,.063,
                .26+rng.random()*.03,1.37+rng.random()*.33,.075,ang+rng.uniform(-.023,.023),.007)
        for side in (-1,1):
            step=2.1
            for n in range(ceil(L/step)):
                t=(n+.5)*step
                pos=(ax+ux*t+vx*2.67*side,ay+uy*t+vy*2.67*side)
                if pathdist(*pos,start)<2.32 or min(pathdist(*pos,marsh),pathdist(*pos,tunnel))<2.4:continue
                length=min(step-.09,2*(L-t)) if t>L-step*.5 else step-.09
                if length<.28:continue
                g=f"Trench_revetment_{idx}"
                for row in range(rng.randrange(3,6)):
                    off=row*.09
                    box(g,"Timber",pos[0]+vx*side*off,pos[1]+vy*side*off,.19+row*.245,
                        length,.13,.215,ang+rng.uniform(-.01,.01),.035)
                x,y=pos[0]-ux*length*.40-vx*side*.12,pos[1]-uy*length*.40-vy*side*.12
                pole(g,"Bark",(x,y,-.04),(x+vx*side*.18,y+vy*side*.18,1.46),.09,.071,8)
    for x,y in [(-44,-18.8),(-34.7,-12.3),(-25.8,-6.2),(-15.5,-5.8),(-9.7,-13.6),
                (-3,-13.7),(3,-10.28),(9.1,-13.7),(21.4,-5.3),(31.7,1.2)]:
        lamp(x,y,1.48)

def lamp(x,y,z):
    g="Lanterns_forged_cages"
    pole(g,"Oxide",(x,y,z-.15),(x,y,z+.03),.109,.109,12)
    pole(g,"Glass",(x,y,z),(x,y,z+.20),.074,.085,10)
    pole(g,"Oxide",(x,y,z+.19),(x,y,z+.28),.135,.061,12)
    for i in range(4):
        a=i*pi/2; px=x+.089*cos(a); py=y+.089*sin(a)
        pole(g,"Oxide",(px,py,z-.02),(px,py,z+.22),.012,n=5)
    pole(g,"Iron" if "Iron" in mats else "Oxide",(x-.14,y,z-.15),(x+.14,y,z-.15),.025,n=6)
    if height(x,y)<z-.4:
        pole(g,"Timber",(x+.17,y,0),(x+.17,y,z+.38),.071,.052,7)

def arcade(g,xleft,xright,ybase,foot,spring,rad,depth,mat,axis="x",rib=False):
    # A curved barrel or voussoir band built around an open volume.
    N=20; verts=[]; faces=[]
    for i in range(N):
        a,b=i*pi/N,(i+1)*pi/N
        inset=.008 if rib else 0
        a+=inset;b-=inset
        for xv in (xleft,xright):
            for r,phi in ((rad,a),(rad,b),(rad+depth,b),(rad+depth,a)):
                pt=(xv,ybase+r*cos(phi),foot+spring+r*sin(phi))
                verts.append(pt if axis=="x" else (pt[1],pt[0],pt[2]))
        k=i*8
        # Recalculate outward after the axis swap where required.
        faces.extend([tuple(k+n for n in reversed(f)) for f in cube_faces])
    groups[g].add(verts,faces,mat)

def casemate():
    for s in (-1,1):
        y=-12+s*1.94
        box("Solid_Casemate_walls","Brick",.25,y,1.0,19.9,.48,2.0)
        box("Solid_Casemate_walls","Concrete",.25,y,.24,20.0,.58,.46)
        box("Solid_Casemate_walls","Concrete",.25,y,1.86,20.13,.54,.17)
    arcade("Roof_vault",-9.72,10.25,-12,0,1.83,1.72,.34,"Brick")
    for x in [-9.62,-6.15,-2.7,.8,4.25,7.68,10.15]:
        arcade("Roof_ribs",x-.12,x+.12,-12,0,1.80,1.655,.21,"Concrete",rib=True)
        for s in (-1,1):
            box("Solid_Casemate_walls","Brick",x,-12+s*1.72,.90,.30,.17,1.80)
    box("Brickfloor","Brick",.25,-12,.029,20.18,3.56,.058)
    # Individual earth-covered shoulders plus roof mass, all cut away on the tactical map.
    verts=[]; faces=[]
    for i in range(33):
        x=-9.68+20.01*i/32
        for j,(w,h) in enumerate([(-6.2,.16),(-4.5,1.9),(-2.6,3.63),(-1.6,4.19),(0,4.37),(1.6,4.18),(2.6,3.68),(4.9,1.48),(6.5,.16)]):
            verts.append((x,-12+w,h+.14*n2(x,w)))
            if i and j:
                k=i*9+j; faces.append((k-10,k-1,k,k-9))
    groups["Roof_sod"].add(verts,faces,"Sod")
    for x in (-9.9,10.4):
        arcade("Roof_portals",x-.26,x+.26,-12,0,1.85,1.75,.50,"Brick",rib=True)
        for s in (-1,1):
            box("Solid_Casemate_walls","Brick",x,-12+s*3.12,1.48,.81,2.55,2.96)
            box("Solid_Casemate_walls","Concrete",x-.02,-12+s*3.12,2.97,1.01,2.72,.22)
            box("Solid_Casemate_walls","Concrete",x-.03,-12+s*2.1,.37,1.07,.66,.74)
    # Maintenance fittings, conduit and brackets establish the scale seen in first person.
    for side in (-1,1):
        y=-12+1.62*side
        pole("Service_lines","Oxide",(-9.4,y,1.72),(10.1,y,1.72),.037,n=9)
        for i in range(14):
            x=-9.1+i*1.45
            box("Service_lines","Oxide",x,y,1.72,.045,.105,.13)
    for x,y in [(-8.1,-13.42),(-3.35,-10.61),(7.8,-13.44)]:
        crate(x,y,0,.62,.48,.46,0)
    casemate_gate_frame()
    for i in range(13):
        box("Doorleaf","Timber",0,-13.50+.115+i*.212,1.18,.17,.207,2.34,0,.009)
    for h in (.28,.67,1.81):
        box("Doorleaf","Oxide",-.105,-12.0,h,.05,2.69,.104)
        for j in range(7):
            pole("Doorleaf","Oxide",(-.15,-13.30+j*.397,h),(-.12,-13.30+j*.397,h),.019,n=6)
    box("Doorleaf","Brass",-.13,-10.89,1.06,.10,.18,.11)

def casemate_gate_frame():
    # The leaf sits in a transverse masonry partition. The jambs extend back into the
    # vault sidewalls. The head masonry rises from the lintel into the existing arch.
    piers = "Solid_Casemate_gate_piers"
    head = "Solid_Casemate_gate_head"
    left, right = -13.50, -10.72
    top = 2.35
    for outer,inner in [(-13.90,left),(right,-10.10)]:
        width = inner-outer
        centre = (inner+outer)*.5
        box(piers,"Brick",0,centre,top*.5,.52,width,top)
        for course in range(8):
            thickness = .28 if course in (0,7) else .285
            for facing in (-1,1):
                box(piers,"Concrete" if course in (0,7) else "Brick",
                    facing*(.266+.007*(course%3)),centre,.155+course*.292,
                    .065,width+.018*(course%2),thickness)
    # Solid rock behind the joints. At the rim the top section overlaps the old vault.
    def half_width(z):
        return sqrt(max(0.0,1.84**2-(z-1.83)**2))
    for course in range(13):
        z0=top+.106*course
        z1=z0+.106
        w0,w1=half_width(z0),half_width(z1)
        if w0<.01: break
        verts=[(x,-12+y,z) for z,b in ((z0,w0),(z1,w1))
               for x,y in ((-.26,-b),(.26,-b),(.26,b),(-.26,b))]
        groups[head].add(verts,cube_faces,"Brick")
    for face in (-1,1):
        box(head,"Concrete",face*.281,(left+right)*.5,2.47,.115,3.20,.26)
        box(head,"Brick",face*.327,(left+right)*.5,2.624,.072,3.03,.07)
        box(head,"Concrete",face*.335,(left+right)*.5,2.477,.07,.245,.30)
        for gate_y in (left,right):
            box(piers,"Timber",face*.29,gate_y,1.18,.13,.085,2.36)
    # Fixed pintles, retaining plates and a worn keeper fasten into the masonry.
    for z in (.31,1.83):
        box(piers,"Oxide",-.329,left-.055,z,.055,.16,.135)
        pole(piers,"Oxide",(-.20,left+.015,z-.09),(-.20,left+.015,z+.09),.037,n=12)
    box(piers,"Oxide",-.333,right+.075,1.06,.045,.15,.18)

def crate(x,y,h,w,d,z,ang):
    g="Supply_crates"; co,si=cos(ang),sin(ang)
    def local(m,u,v,l,sx,sy,sz):
        box(g,m,x+co*u-si*v,y+si*u+co*v,h+l,sx,sy,sz,ang)
    local("Timber",0,0,z*.5,w,d,z)
    for v in (-d*.51,d*.51):
        for u in (-w*.42,w*.42): local("Timber",u,v,z*.5,.092,.05,z+.025)
    for u in (-w*.38,w*.38): local("Oxide",u,0,z+.012,.042,d+.04,.016)
    for i in range(4):local("Timber",0,-d/2+d*(i+.5)/4,z+.026,w-.01,d/4-.009,.023)

def telephones_and_markers():
    x,y=-15.0,-4.8
    for sx in (-.47,.47):
        for sy in (-.30,.30):
            box("Field_station","Timber",x+sx,y+sy,.99,.105,.096,1.98)
    for i in range(8):
        box("Field_station","Timber",x-.51+.145*i,y-.31,1.18,.141,.082,1.51,0,.018)
    for i in range(7):
        box("Field_station","Timber",x-.50+i*.16,y,1.98,.185,.91,.082,0,.018)
    box("Field_station","Timber",x,y,.94,1.02,.64,.065)
    box("Field_station","Oxide",x,y,1.1,.57,.36,.25)
    pole("Field_station","Tar",(x-.20,y+.06,1.28),(x+.19,y+.06,1.28),.034,n=8)
    for s in (-1,1):pole("Field_station","Tar",(x+s*.20,y+.06,1.26),(x+s*.23,y+.08,1.32),.063,n=9)
    prev=None
    for i in range(27):
        pt=(x+.24+.025*cos(i*1.6),y+.14+.025*sin(i*1.6),1.22-i*.018)
        if prev:pole("Field_station","Tar",prev,pt,.008,n=4)
        prev=pt
    for x,y in [(-15.6,15.1),(.2,16.6),(24,2.0),(33,1.85),(-9.9,-14.1)]:
        pole("Signals","Timber",(x,y,0),(x+.06,y,3.42),.075,.041,9)
        if x==33:
            box("Signals","Oxide",x-.52,y,.73,.91,.49,1.40)
            box("Signals","Brass",x-.52,y-.255,.91,.59,.024,.42)
            for i in range(3):pole("Signals","Tar",(x-.72+i*.19,y-.30,.98),(x-.72+i*.19,y-.28,.98),.051,n=8)
        vs=[]; fs=[]
        for i in range(9):
            for j in range(3):
                vs.append((x+.04+i*.118,y+.10*sin(i*.92+j*.59),3.38-.20*j-.05*sin(i*.73)))
                if i and j:
                    k=i*3+j;fs.append((k-4,k-1,k,k-3)); fs.append((k-3,k,k-1,k-4))
        groups["Signals"].add(vs,fs,"Flag")
    for cx,cy in [(30,6),(37,-5)]:
        # Offset the physical cabinet from the interaction point so a player can stand there.
        x,y=cx+.65,cy+.44
        g="Battery_line_boxes"
        for sx in (-.31,.31):
            box(g,"Timber",x+sx,y, .82,.102,.12,1.68)
        box(g,"Timber",x,y,1.36,.88,.42,.96)
        box(g,"Oxide",x,y-.22,1.37,.75,.045,.78)
        box(g,"Brass",x,y-.25,1.58,.48,.023,.14)
        box(g,"Oxide",x,y,1.88,1.02,.60,.08)
        for i in range(3):
            xx=x-.20+.20*i
            pole(g,"Concrete",(xx,y-.29,1.41),(xx,y-.30,1.50),.045,n=10)
            pole(g,"Brass",(xx,y-.28,1.29),(xx,y-.32,1.37),.023,n=8)
        pole(g,"Timber",(x-.31,y-.03,.55),(x+.31,y-.03,.55),.17,n=16)
        for q in range(10):
            xx=x-.22+q*.048
            pole(g,"Tar",(xx-.013,y-.03,.55),(xx+.017,y-.03,.55),.182,n=16)
        for xx in (x-.29,x+.29):
            pole(g,"Timber",(xx-.025,y-.03,.55),(xx+.025,y-.03,.55),.229,n=16)

def artillery(cx,cy,ang):
    g="Battery_statuary"
    co,si=cos(ang),sin(ang)
    def local(x,y,z):return (cx+x*co-y*si,cy+x*si+y*co,z)
    def tube(m,a,b,r,t=None,n=16):pole(g,m,local(*a),local(*b),r,t,n)
    def cratepart(m,x,y,z,dx,dy,dz):box(g,m,*local(x,y,z),dx,dy,dz,ang)
    ring(g,"Concrete",cx,cy,.02,3.5,2.8,.28)
    ring(g,"Oxide",cx,cy,.28,2.85,2.76,.35)
    tube("Concrete",(0,0,.04),(0,0,.30),2.83,2.83,44)
    tube("Oxide",(0,0,.30),(0,0,.58),1.12,1.03,24)
    for s in (-1,1):
        cratepart("Oxide",-.08,s*.72,1.06,2.35,.27,1.02)
        tube("Oxide",(-.72,s*.74,.72),(.86,s*.74,1.82),.139,.17)
        tube("Oxide",(-.48,s*.73,1.22),(-2.3,s*1.21,.42),.19,.09)
        for i in range(7):
            tube("Brass",(-.91+i*.3,s*.865,.96),(-.91+i*.3,s*.883,.96),.03,.03,8)
    tube("Oxide",(-1.72,0,1.51),(.47,0,1.95),.40,.34)
    tube("Oxide",(.35,0,1.93),(4.43,0,2.70),.31,.179)
    for xx,r in [(-1.6,.44),(-.7,.405),(.39,.35),(1.18,.305),(4.38,.202)]:
        zz=1.85+.19*xx;tube("Oxide",(xx-.055,0,zz-.012),(xx+.055,0,zz+.012),r,r)
    tube("Tar",(4.476,0,2.708),(4.486,0,2.710),.134,.134)
    # A visible raised carriage, a rounded breech block and an operating wheel.
    cratepart("Oxide",-1.57,0,1.52,.51,.68,.55)
    for s in (-1,1):
        a=local(-.6,s*.90,1.18)
        for i in range(16):
            a1=i*pi/8;a2=(i+1)*pi/8
            tube("Oxide",(-.6+.31*cos(a1),s*.94,1.18+.31*sin(a1)),(-.6+.31*cos(a2),s*.94,1.18+.31*sin(a2)),.025,n=6)
        for i in range(4):
            aa=i*pi/2;tube("Oxide",(-.60,s*.94,1.18),(-.60+.28*cos(aa),s*.94,1.18+.28*sin(aa)),.021,n=6)

def crown_profile(heightwall):
    return [(-.2,heightwall+.18),(.8,heightwall+.50),(2.2,heightwall+.68),
            (4.6,heightwall+.18),(7.5,1.16),(9.5,.86)]

def fort_front(x0,x1,y,heightwall):
    # Cadence of brick pier / recessed opening / stone sill seen in the Fort I photograph.
    g="Fort_barracks"
    w=x1-x0
    box(g,"Brick",(x0+x1)/2,y,heightwall*.5,w,1.28,heightwall)
    box(g,"Concrete",(x0+x1)/2,y-.04,.35,w+.2,1.45,.70)
    box(g,"Concrete",(x0+x1)/2,y-.10,heightwall+.11,w+.35,1.74,.25)
    for i in range(ceil(w/3.12)):
        x=x0+1.62+i*3.12
        if x+.72>x1:break
        box(g,"Tar",x,y-.666,1.67,1.28,.036,1.70)
        for s in (-1,1):
            box(g,"Concrete",x+s*.68,y-.70,1.68,.15,.14,1.91)
            box(g,"Timber",x+s*.78,y-.72,1.62,.19,.071,1.56,0,.023)
        for z in (.81,2.51):
            box(g,"Concrete",x,y-.75,z,1.61,.25,.13)
        for p in (-.29,0,.29):box(g,"Oxide",x+p,y-.70,1.66,.027,.04,1.53)
        for z in (1.27,1.96):box(g,"Oxide",x,y-.70,z,1.12,.04,.034)
        box(g,"Brick",x-1.39,y-.69,heightwall*.51,.46,.35,heightwall)
        for n in range(6):box(g,"Brick",x-1.55+.53*n,y-.74,heightwall-.15,.22,.20,.24)
    # Turf over a long rising embankment instead of a freestanding facade.
    n=max(2,int(w/.80)); verts=[]; faces=[]
    for i in range(n+1):
        x=x0+w*i/n
        for j,(ofs,h) in enumerate(crown_profile(heightwall)):
            verts.append((x,y+ofs,h+.16*n2(x*.8,ofs)))
            if i and j:
                k=i*6+j;faces.append((k-7,k-1,k,k-6))
    groups["Fort_crown"].add(verts,faces,"Sod")
    for end in (0,n):
        crown=verts[end*6:end*6+6]
        bank=crown+[(p[0],p[1],-.62) for p in reversed(crown)]
        groups["Fort_end_walls"].add(bank,[tuple(range(12)) if end==n else tuple(reversed(range(12)))],"Brick")

foliage_uv={}
def vegetation():
    grove=random.Random(7516)
    def branch(batch,points,radii,facets,phase=0):
        pts=[Vector(q) for q in points]; verts=[];faces=[]
        u=None
        for k,(p,radius) in enumerate(zip(pts,radii)):
            axis=(pts[min(k+1,len(pts)-1)]-pts[max(0,k-1)]).normalized()
            if u is None:
                u=axis.orthogonal().normalized()
            else:
                u=(u-axis*u.dot(axis)).normalized()
            v=axis.cross(u)
            for j in range(facets):
                ag=2*pi*j/facets+phase
                ridged=radius*(1+.12*sin(ag*3+.35*k)+.055*sin(ag*5+k*.31))
                verts.append(tuple(p+ridged*(u*cos(ag)+v*sin(ag))))
                if k:faces.append(((k-1)*facets+j,(k-1)*facets+(j+1)%facets,k*facets+(j+1)%facets,k*facets+j))
        faces.append(tuple((len(pts)-1)*facets+j for j in range(facets)))
        groups[batch].add(verts,faces,"Bark")
    treepoints=[(-52,-6),(-43,-4),(-39,4),(-38,-25),(-30,1),(-29,12),(-23,18),
                (-22.7,12.35),(-13,24),(-5,20),(8,22),(14,17),(21,20),(-9,-26),(16,-25)]
    treepoints += [(grove.uniform(-80,80),grove.uniform(30,52)) for _ in range(57)]
    for index,(x,y) in enumerate(treepoints):
        if valley.channel(x,y)[0]<.9:continue
        z=height(x,y); lay=grove.uniform(0,2*pi); dx,dy=cos(lay),sin(lay)
        r=grove.uniform(.17,.41); tall=grove.uniform(3.4,6.9)
        if index%5==0:tall*=.56
        batch=f"Willows_{index//10}"
        geometry=groups[batch]
        begin_verts,begin_faces=len(geometry.verts),len(geometry.faces)
        def wood(t):
            drift=r*(1.43*sin(t*4.13+index%3)+t*2.9)
            return Vector((x+dx*drift+dy*.19*sin(t*5.2),y+dy*drift-dx*.38*t*t,z+t*tall))
        rings=(-.04,.07,.28,.53,.77,1)
        trunk=[wood(t) for t in rings]
        def trunk_at(t):
            for i in range(1,len(rings)):
                if t<=rings[i]:
                    return trunk[i-1].lerp(trunk[i],(t-rings[i-1])/(rings[i]-rings[i-1]))
            return trunk[-1]
        branch(batch,trunk,[r*1.7,r*1.29,r*1.01,r*.92,r*.66,r*.30],11,lay)
        if index<15:
            for j in range(6):
                a=lay+j*1.04+grove.uniform(-.25,.25); extent=grove.uniform(.63,1.61)
                base=trunk[0]; xx=base.x+cos(a)*extent;yy=base.y+sin(a)*extent
                branch(batch,[(wood(.09).x,wood(.09).y,z+.35),(base.x+cos(a)*.43,base.y+sin(a)*.43,z+.12),(xx,yy,height(xx,yy)-.045)],
                       [r*.57,r*.30,.022],7,a)
        # Splintered wood and an off-centre fork replace the blunt flat pole tip.
        for j in range(3):
            a=lay+j*2.20; stub=trunk[-1]+Vector((cos(a)*r*.12,sin(a)*r*.12,-.065))
            q=stub+Vector((cos(a)*r*.33,sin(a)*r*.33,grove.uniform(.12,.61)))
            branch(batch,[stub,q],[r*.17,.011],5,a)
        for j in range(3 if index%5==0 else 5):
            t=.31+(j+.2)*.108+grove.uniform(-.06,.05)
            v0=trunk_at(t);ag=lay+j*2.4+.7; arm=grove.uniform(.86,2.65)*(1.16-t*.23)
            d=Vector((cos(ag),sin(ag),0)); e=Vector((-sin(ag),cos(ag),0))
            shoulder=v0+d*arm*.33+Vector((0,0,arm*.26))
            elbow=v0+d*arm*.72+e*arm*.24+Vector((0,0,arm*.66))
            tip=v0+d*arm+e*arm*.16+Vector((0,0,arm*grove.uniform(.72,1.43)))
            branch(batch,[v0,shoulder,elbow,tip],[r*(.62-.22*t),r*.35,r*.15,.018],8,ag)
            if index%5==0:continue
            for k in range(2 if index>14 else 4):
                # Bud from the actual bent limb, including its offset elbow.
                limb_t=.34+k*.40
                base=shoulder.lerp(elbow,limb_t) if limb_t<=1 else elbow.lerp(tip,limb_t-1)
                side=(-1 if k%2 else 1)
                end=base+d*.33+e*side*grove.uniform(.42,.98)+Vector((0,0,grove.uniform(.15,.75)))
                mid=base.lerp(end,.57)+Vector((0,0,.12))
                branch(batch,[base,mid,end],[.048,.027,.009],6)
                if index<15 or k==0:
                    branch(batch,[mid,end+e*side*.12+Vector((0,0,.35))],[.020,.005],5)
        # Test the complete crown, including the outer twigs, against both buildings.
        # Keeping the RNG stream unchanged also keeps neighbouring trees and marsh plants stable.
        points=geometry.verts[begin_verts:]
        lo_x=min(p[0] for p in points); hi_x=max(p[0] for p in points)
        lo_y=min(p[1] for p in points); hi_y=max(p[1] for p in points)
        fronts=[(19,53,16.1),(-25,15,26.7)]
        if any(hi_x>x0-.65 and lo_x<x1+.65 and hi_y>y0-1.1 and lo_y<y0+9.75
               for x0,x1,y0 in fronts):
            del geometry.verts[begin_verts:]
            del geometry.faces[begin_faces:]
            del geometry.paint[begin_faces:]
    # Textured bent cards sit on the sampled ground at each foot, including on slopes.
    for i in range(19700):
        x,y=grove.uniform(-61,59),grove.uniform(-32,31)
        if western_ward.in_ward(x,y):continue
        dst=min(pathdist(x,y,start),pathdist(x,y,tunnel),pathdist(x,y,marsh))
        h=height(x,y)
        if h<water_cells.get((floor(x*2),floor(y*2)),-100)+.04 or valley.channel(x,y)[0]<.2:continue
        if dst<2.53 or ((x-33)/13)**2+(y/11)**2<1 or abs(y+12)<3.65 and -10<x<11:continue
        clump=.50+.28*sin(x*.75+sin(y*.55))+.20*cos(y*1.27-x*.4)
        if grove.random()>clump+.18:continue
        g=f"Marsh_cover_{floor((x+64)/16)}_{floor((y+32)/16)}"
        tall=grove.uniform(.095,.31) * (.75+.52*clump)
        tile=grove.choices(range(4),weights=(5,4,1,2))[0]
        if tile==2:tall*=1.42
        span=tall*grove.uniform(1.21,2.19)
        verts=[];faces=[];coords=[]
        ang=grove.uniform(0,pi)
        for j in range(3):
            a=ang+j*pi/3+grove.uniform(-.20,.20);u,v=cos(a),sin(a)
            o=len(verts);ht=tall*grove.uniform(.78,1.13)
            for row,(wide,off,up) in enumerate(((.78,0,0),(1,.07,.52),(1.12,.23,1))):
                for side in (-1,1):
                    xx=x+side*u*span*wide*.5-v*ht*off;yy=y+side*v*span*wide*.5+u*ht*off
                    z=height(xx,yy) if row==0 else (h+height(xx,yy))*.5
                    verts.append((xx,yy,z-.023+up*ht))
                    uu=(tile%2 + (.016 if side<0 else .984))*.5
                    vv=((1-tile//2) + .015+.97*up)*.5
                    coords.append((uu,vv))
            faces.extend([(o,o+1,o+3,o+2),(o+2,o+3,o+5,o+4)])
        groups[g].add(verts,faces,"Reed" if tile==2 else "Grass")
        foliage_uv.setdefault(g,[]).extend(coords)
    field_fences()

def field_fences():
    name="Field_wire"
    cuts={6,15}
    def crown_ground(x,y):
        offset=y-26.7
        for (a,h),(b,k) in zip(crown_profile(3.7),crown_profile(3.7)[1:]):
            if a<=offset<=b:
                t=(offset-a)/(b-a)
                return (h+.16*n2(x*.8,a))*(1-t)+(k+.16*n2(x*.8,b))*t
        return height(x,y)
    for chain in range(3):
        posts=[]
        for i in range(21):
            x=-58+i*4.8
            if chain==0:
                # Between the two turns of the north road the fence follows the turf above
                # the barracks. Three separately tensioned runs leave both end accesses open.
                yy=(23.9+.88*sin(i*.73) if i<=6 else
                    27.18+.055*sin(i*.76) if i<=15 else 28.8+.42*sin(i*.76))
            elif chain==1:
                yy=-24.3+.92*sin(i*.76+1)
            else:
                # The rear fence sits behind the bank and towers, clear of the front road.
                yy=38.8+1.05*sin(i*.76+2)
            on_crown=chain==0 and 7<=i<=15
            ground=crown_ground if on_crown else height
            h=ground(x,yy)
            lean_x=rng.uniform(-.28,.23); lean_y=rng.uniform(-.24,.27); rise=rng.uniform(.91,1.48)
            top=(x+lean_x,yy+lean_y*(.28 if on_crown else 1),h+rise)
            if valley.channel(x,yy)[0]<.36:
                posts.append(None)
                continue
            pole(name,"Bark",(x,yy,h-.08),top,.068,.053,7)
            posts.append(top)
            if chain==0 and i in (6,7,15,16):
                # Brace away from the opening, and fasten the two strands separately.
                dx=-.86 if i in (6,15) else .86
                pole(name,"Bark",(x+dx,yy,ground(x+dx,yy)-.035),
                     (top[0],top[1],top[2]-.19),.055,.038,7)
                for drop in (.08,.39):
                    pole(name,"Oxide",(top[0],top[1],top[2]-drop-.017),
                         (top[0],top[1],top[2]-drop+.017),.061,n=10)
        for i,(u,v) in enumerate(zip(posts,posts[1:])):
            if not u or not v or chain==0 and i in cuts:continue
            if valley.channel((u[0]+v[0])*.5,(u[1]+v[1])*.5)[0]<.12:continue
            for drop in (.08,.39):
                prev=(u[0],u[1],u[2]-drop)
                for s in range(1,8):
                    t=s/7
                    q=(u[0]+(v[0]-u[0])*t,u[1]+(v[1]-u[1])*t,
                       u[2]+(v[2]-u[2])*t-drop-.20*sin(pi*t))
                    pole(name,"Oxide",prev,q,.011,n=4);prev=q

def close_bank_sedges():
    # Folded live leaves and branched seed heads break the grass-card silhouette along the approaches.
    sedges=random.Random(6103)
    for i in range(2000):
        x=sedges.uniform(-47,19)
        y=sedges.uniform(-21,23)
        distance=min(pathdist(x,y,start),pathdist(x,y,marsh),pathdist(x,y,tunnel))
        if not 2.35<distance<4.1 or (abs(y+12)<4 and -11<x<12):
            continue
        z=height(x,y)
        if z<water_cells.get((floor(x*2),floor(y*2)),-100)+.09 or valley.channel(x,y)[0]<.1:
            continue
        batch=f"Folded_sedge_{floor((x+50)/9)}_{floor((y+22)/9)}"
        scale=sedges.uniform(.26,.69)
        for j in range(sedges.randint(5,10)):
            a=sedges.uniform(0,2*pi)
            d=Vector((cos(a),sin(a),0))
            s=Vector((-sin(a),cos(a),0))
            h=scale*sedges.uniform(.67,1.16)
            lean=sedges.uniform(.25,1.03)
            w=sedges.uniform(.008,.022)
            origin=Vector((x,y,z-.018))+d*sedges.uniform(.015,.075)
            verts=[]; faces=[]; coords=[]
            for k,t in enumerate((0,.23,.52,.78,1)):
                half=w*(.02+sin((.18+.82*t)*pi)**.65)
                mid=origin+d*(lean*h*t*t)+s*(.03*sin(t*2.3))+Vector((0,0,h*(1.6*t-.75*t*t-.26*lean*t**3)))
                verts.extend((tuple(mid-s*half),tuple(mid+d*half*.58),tuple(mid+s*half)))
                coords.extend(((0,t),(.5,t),(1,t)))
                if k:
                    n=3*k
                    faces.extend(((n-3,n-2,n+1,n),(n-2,n-1,n+2,n+1)))
            groups[batch].add(verts,faces,"Sedge")
            foliage_uv.setdefault(batch,[]).extend(coords)
        if sedges.random()<.53:
            # Seeds in a separate opaque batch with real thickness in side light.
            head=batch.replace("Folded_sedge","Seedheads")
            foot=Vector((x,y,z))
            top=foot+Vector((.09,-.045,scale*1.36))
            pole(head,"Seed",foot,top,.009,.003,5)
            for j in range(7):
                stem=foot.lerp(top,.62+.045*j)
                tip=stem+Vector((.04*sin(j*2.3),.047*cos(j*2.3),.078))
                pole(head,"Seed",stem,tip,.0028,.002,4)
                pole(head,"Seed",tip,tip+Vector((.012,0,-.045)),.006,.002,5)

def shoreline_plants():
    """Reed islands taper out into the open water and leave clear cuts through the bank."""
    reeds=random.Random(1848)
    for i in range(18000):
        x,y=reeds.uniform(-93,83),reeds.uniform(-46,64)
        d,r,lake=valley.channel(x,y)
        if not -1.05<d<3.9:continue
        patch=.53+.31*sin(.57*x+.29*y)+.27*sin(.37*y-.19*x)+.19*cos(x*.99-y*.44)
        if patch<.34 or abs(x+34)<3.6 and -8<y<1:continue
        g=f"Marsh_cover_river_{floor(x/19)}_{floor(y/19)}"
        rhizome=reeds.uniform(.62,1.38)*(.76+.37*patch)
        if lake:rhizome*=.77
        for j in range(reeds.randint(2,5)):
            a=reeds.uniform(0,2*pi);rr=reeds.uniform(0,.62)
            px,py=x+rr*cos(a),y+rr*sin(a);pz=height(px,py)
            if pz<valley.WATER_LEVEL-.36:continue
            h=rhizome*reeds.uniform(.76,1.24);w=h*reeds.uniform(.15,.26)
            for yaw in [a,a+1.46]:
                xx,yy=cos(yaw),sin(yaw)
                verts=[];coords=[]
                for spread,lean,t in ((.84,0,0.),(1,.045,.52),(.84,.18,1.)):
                    for sign in (-1,1):
                        verts.append((px+sign*w*spread*xx+lean*h,py+sign*w*spread*yy-.03*t,pz-.025+h*t))
                        coords.append(((.016 if sign<0 else .984)*.5,(.016+.968*t)*.5))
                groups[g].add(verts,[(0,1,3,2),(2,3,5,4)],"Reed")
                foliage_uv.setdefault(g,[]).extend(coords)
            if i%8==0:
                stem=f"Reed_panicles_{floor(x/30)}_{floor(y/30)}"
                tip=(px+.17*h,py-.04,pz+h*1.11)
                pole(stem,"Seed",(px,py,pz),tip,.010,.005,5)
                for k in range(3):
                    q=(tip[0]+.022*k,tip[1]+.013*k,tip[2]+.044*k)
                    pole(stem,"Seed",q,(q[0]+.05,q[1]+.015,q[2]+.10),.021-.003*k,.003,6)

def north_fort_skyline():
    """Upper masonry of the battery, behind the traversable yard and northern road."""
    group = "Fort_upper_battery"
    for cx, cy, radius, base, top in [(34.8, 20.1, 3.7, 4.42, 7.12), (-2.5, 30.4, 2.9, 3.88, 6.37)]:
        pole(group, "Brick", (cx,cy,base-.56), (cx,cy,top-.52), radius, n=48)
        ring(group, "Concrete", cx,cy,top-.63,radius+.16,radius-.28,top-.48,48)
        # Separate brick piers reveal the recess behind each viewing slot.
        pole(group, "Tar", (cx,cy,top-.53), (cx,cy,top-.06), radius-.31, n=48)
        for j in range(16):
            angle = 2*pi*j/16
            box(group,"Brick",cx+(radius-.13)*cos(angle),cy+(radius-.13)*sin(angle),
                top-.27,.30,1.09 if j%3 else .67,.53,angle)
        pole(group, "Concrete", (cx,cy,top-.06), (cx,cy,top+.14), radius+.22, radius+.12,48)
        pole(group, "Sod", (cx,cy,top+.14), (cx,cy,top+.40), radius+.09, radius-.75,48)
        ring(group, "Oxide", cx,cy,top-.06,radius+.23,radius+.18,top+.008,48)
    # Parapet capstones and irregular steel railing follow the crown above the facade.
    for start_x,end_x,y,z in [(20.1,30.2,17.1,4.88),(39.2,51.9,17.1,4.88),(-22,-7,27.75,4.31)]:
        span=end_x-start_x
        for j in range(ceil(span/.91)):
            width=min(.91,span-j*.91)
            box(group,"Concrete",start_x+j*.91+width/2,y,z,width-.014,.57,.25,0,.018)
        stations=[start_x+span*j/6 for j in range(7)]
        for j,x in enumerate(stations):
            lean=.07*sin(x)
            pole(group,"Oxide",(x,y,z+.06),(x+lean,y,z+.94),.036,n=7)
            if j and j!=4:
                prev=stations[j-1]
                for h in (.55,.89):
                    pole(group,"Oxide",(prev+.07*sin(prev),y,z+h),(x+lean,y,z+h),.023,n=7)
    # The narrow survey mast gives the same landmark to the trench and courtyard cameras.
    cx,cy=28.7,20.6
    for side in (-1,1):
        for depth in (-1,1):
            pole(group,"Oxide",(cx+.69*side,cy+.69*depth,4.55),(cx+.19*side,cy+.19*depth,10.36),.064,.043,8)
    for j in range(4):
        lo=4.83+j*1.33
        half=.66-j*.117
        for side in (-1,1):
            pole(group,"Oxide",(cx-half,cy+half*side,lo),(cx+half-.112,cy+(half-.112)*side,lo+1.33),.028,n=6)
            pole(group,"Oxide",(cx+half*side,cy+half,lo),(cx+(half-.112)*side,cy-half+.112,lo+1.33),.028,n=6)
    box(group,"Timber",cx,cy,10.40,1.82,.12,.13)
    for x in (-.71,-.43,.43,.71):
        pole(group,"Concrete",(cx+x,cy,10.46),(cx+x,cy,10.65),.057,n=8)

def yard():
    artillery(32,9,1.19);artillery(40,-8,2.1)
    fort_front(19,53,16.1,4.3);fort_front(-25,15,26.7,3.7)
    for x,y,a in [(22,-11,-.15),(37,13,.24),(43,-4,-.4),(29,11,0),(34,-10,.4),(-16,-7,.43)]:
        crate(x,y,0,1.0,.68,.67,a);crate(x+.36,y+.68,.02,.70,.57,.48,a+.18)
    for x,y,a in [(-18,7,2.4),(6,16,-.14),(16,9,1.21),(28,5,-.29),(37,3,.81),(20,-9,1.92)]:
        # Worn sacks have rounded irregular shoulders and sagging seams.
        for row in range(2):
            for i in range(4-row):
                t=(i-1.4+row*.4)*.66; cx=x+t*cos(a);cy=y+t*sin(a);vs=[]; fs=[]; h=height(cx,cy)+row*.23
                for j,(k,z) in enumerate([(.63,0),(.97,.075),(1,.16),(.78,.245)]):
                    for j2 in range(12):
                        theta=j2*2*pi/12; dx=.37*k*abs(cos(theta))**.52*(1 if cos(theta)>=0 else -1);dy=.21*k*abs(sin(theta))**.65*(1 if sin(theta)>=0 else -1)
                        vs.append((cx+dx*cos(a)-dy*sin(a),cy+dx*sin(a)+dy*cos(a),h+z+.018*sin(3*theta+i)))
                for j in range(3):
                    for n in range(12):fs.append((j*12+n,j*12+(n+1)%12,(j+1)*12+(n+1)%12,(j+1)*12+n))
                fs.append(tuple(range(36,48)))
                groups["Solid_Sandbags"].add(vs,fs,"Canvas")
    for i in range(550):
        x,y=rng.uniform(-22,48),rng.uniform(-22,22)
        if min(pathdist(x,y,tunnel),pathdist(x,y,start))<2.1 or y>-8 and x<5:continue
        if y<14 and x>18 and rng.random()<.6: continue
        r=rng.uniform(.065,.24);box("Masonry_fragments","Brick" if i%3 else "Concrete",x,y,height(x,y)+r*.18,r*2,r,r*.78,rng.random()*pi,r*.18)

def pools():
    # Water fills connected pockets in the triangulated half-metre survey. A small
    # priority flood measures the lowest spill elevation before generating the shore.
    from heapq import heappop, heappush
    wet_vertices=[]
    for cx,cy,r in craters + [(-41.5,-17.25,5.)]:
        n=ceil(r*2.6)
        ox,oy=round(cx*2)/2,round(cy*2)/2
        cells={(i,j):(ox+i*.5,oy+j*.5,height(ox+i*.5,oy+j*.5))
               for i in range(-n,n+1) for j in range(-n,n+1)}
        seed=min((q for q in cells if abs(q[0])<=max(2,r*.9) and abs(q[1])<=max(2,r*.9)),
                 key=lambda q:cells[q][2])
        low=cells[seed][2]
        costs={seed:low}; queue=[(low,seed)]
        while queue:
            cost,q=heappop(queue)
            if costs[q]!=cost:continue
            if n in (abs(q[0]),abs(q[1])):break
            for dx,dy in ((1,0),(-1,0),(0,1),(0,-1),(1,1),(-1,-1)):
                v=q[0]+dx,q[1]+dy
                new=max(cost,cells[v][2])
                if new<costs.get(v,1e6):
                    costs[v]=new;heappush(queue,(new,v))
        level=min(low+(.095 if cy<-16 and cx<-40 else .32),cost-.036)
        if level-low<.028:continue
        flooded={q for q in costs if costs[q]<level}
        count=0
        for i in range(-n,n):
            for j in range(-n,n):
                if any(abs(ox+(i+.5)*.5-ex)<2 and abs(oy+(j+.5)*.5-ey)<2 for ex,ey in event_sites):continue
                a,b,c,d=(i,j),(i+1,j),(i+1,j+1),(i,j+1)
                for triangle in ((a,b,c),(a,c,d)):
                    if all(v not in flooded for v in triangle):continue
                    verts=[]; depths=[]
                    for prev,cur in zip(triangle[-1:]+triangle[:-1],triangle):
                        a0,b0=cells[prev],cells[cur]
                        if (a0[2]<level)!=(b0[2]<level):
                            t=(level-a0[2])/(b0[2]-a0[2])
                            verts.append((a0[0]+(b0[0]-a0[0])*t,a0[1]+(b0[1]-a0[1])*t,level+.001))
                            depths.append(0.)
                        if b0[2]<level:
                            verts.append((b0[0],b0[1],level+.001));depths.append(min(1,(level-b0[2])/.25))
                    if len(verts)>=3:
                        groups["Runoff_pools"].add(verts,[tuple(range(len(verts)))],"Water")
                        water_cells[round(ox*2)+i,round(oy*2)+j]=level
                        wet_vertices.extend(depths);count+=1
        print("Surveyed pool",cx,cy,"depth",round(level-low,3),"wet triangles",count)
    return wet_vertices


def wetland_repairs():
    # A maintenance crossing ties the two banks together above the player's first bend.
    # Its rails frame the brighter phone station at normal eye height.
    bridge="Roof_Trench_maintenance_bridge"
    cx,cy=-37.7,-16
    for side in (-1,1):
        y=cy+2.80*side
        for edge in (-1,1):
            x=cx+.72*edge
            pole(bridge,"Bark",(x+.09*edge,y+.34*side,-.15),(x,y,2.94),.135,.105,10)
            pole(bridge,"Timber",(x,y+.06*side,1.62),(x,cy+1.75*side,2.73),.086,.071,8)
        box(bridge,"Timber",cx,y,2.72,1.93,.25,.22,0,.023)
    for off in (-.66,.66):
        box(bridge,"Timber",cx+off,cy,2.86,.175,6.81,.21,0,.021)
    for i in range(28):
        y=cy-3.31+i*.245
        width=1.60+.094*sin(i*1.62)
        box(bridge,"Timber",cx+.045*sin(i*1.3),y,3.001+.016*cos(i*.63),
            width,.231+.009*cos(i*2),.12,.012*sin(i*1.53),.021)
    for side in (-1,1):
        x=cx+side*.78
        rail=[]
        for i,y in enumerate((cy-3.15,cy-1.34,cy+.33,cy+1.71,cy+3.18)):
            top=(x+side*(.048+.023*i),y+.062*sin(i),3.63+.17*sin(i*2+side*.3))
            pole(bridge,"Bark",(x,y,2.82),top,.051,.036,9);rail.append(top)
            # Lashing tied around the post where the handrail crosses.
            for loop in range(3):
                z=top[2]-.071-.028*loop
                pole(bridge,"Tar",(top[0],top[1],z-.008),(top[0],top[1],z+.008),.052,.052,10)
        for i,(a,b) in enumerate(zip(rail,rail[1:])):
            pole(bridge,"Timber",a,b,.052,.043,9)
            if i<3 or side>0:
                pole(bridge,"Bark",(a[0],a[1],a[2]-.45),(b[0],b[1],b[2]-.29),.037,.021,7)
    # A folded length of damp duck canvas, lashed to the near rail. It never hangs into headroom.
    verts=[];faces=[]
    for i in range(15):
        for j in range(7):
            t=i/14;s=j/6;y=cy+.38+t*1.23
            x=cx-.85-.091*sin(s*pi)-.033*cos(i*.85+s*4)
            z=3.55+.045*sin(2*t*pi)-s*(.46+.17*sin(pi*t))+.019*cos(i*1.89+s*3)
            verts.append((x,y,z))
            if i and j:
                k=i*7+j;faces.extend([(k-8,k-7,k,k-1),(k-1,k,k-7,k-8)])
    groups[bridge].add(verts,faces,"Canvas")
    for bx,by in [(cx-.8,cy+2.35),(cx+.74,cy-2.45)]:
        lamp(bx,by,2.28)
        pole(bridge,"Oxide",(bx,by,2.80),(bx,by,2.56),.012,.012,8)
    # Woven fascine baskets: splits weave alternately over and under live stakes.
    g="Floodbank_wattles"
    prng=random.Random(7471)
    for x,y,r,h in [(-42,-13.85,.59,1.19),(-40.93,-13.88,.56,1.08),(-39.89,-13.85,.51,.91),
                    (-26.6,-10.77,.48,.82),(-17.97,-.89,.53,1.05),(-16.95,-.92,.53,1.19)]:
        base=height(x,y)-.16
        stakes=13
        def edge(a,t,radius):
            return x+(radius+.025*sin(a*3+t))*cos(a)+.035*t,y+radius*sin(a)
        for s in range(stakes):
            a=2*pi*s/stakes;bx,by=edge(a,0,r*.84);ex,ey=edge(a,1,r*1.045)
            pole(g,"Bark",(bx,by,base),(ex,ey,base+h+prng.uniform(.025,.11)),.025,.015,7)
        for row in range(int(h/.053)):
            pts=[]
            for k in range(stakes*3+1):
                a=k*2*pi/(stakes*3); t=(row+.5+k/(stakes*3))*.050/h
                radius=r*(.84+.19*t)+.029*cos(k*pi/3+row*pi)
                bx,by=edge(a,t,radius)
                pts.append((bx,by,base+t*h+.008*sin(a*3+row*.33)))
            for v,u in zip(pts,pts[1:]):
                pole(g,"Bark" if row%4 else "Timber",v,u,.026,.022,5)
        vs=[(x,y,base+h+.05)]
        for k in range(stakes*2):
            a=k*pi/stakes;bx,by=edge(a,1,r*.97);vs.append((bx,by,base+h*.95+.025*sin(3*a)))
        groups[g].add(vs,[(0,k+1,(k+1)%(2*stakes)+1) for k in range(2*stakes)],"Earth")
    # Roots and sod clods breaking up the lip of the first large crater.
    for cx,cy,ra in craters:
        for i in range(10):
            a=prng.uniform(0,2*pi);rr=ra*prng.uniform(.84,1.16); x=cx+cos(a)*rr;y=cy+sin(a)*rr*.86
            if min(pathdist(x,y,path) for path in (start,tunnel,marsh))<2.15:continue
            z=height(x,y);g="Torn_peat_and_roots"
            vs=[(x,y,z+prng.uniform(.045,.095))]
            for k in range(8):
                ang=k*pi*.25;rad=prng.uniform(.12,.30)
                xx=x+cos(ang)*rad;yy=y+sin(ang)*rad*.71
                vs.append((xx,yy,height(xx,yy)-.018))
            groups[g].add(vs,[(0,k+1,(k+1)%8+1) for k in range(8)],"Sod")
            if i%3==0:
                prev=(x,y,z+.04)
                for k in range(1,5):
                    xx=x+cos(a+pi)*k*.13; yy=y+sin(a+pi)*k*.10+.03*sin(k)
                    point=(xx,yy,height(xx,yy)+.035)
                    pole(g,"Bark",prev,point,.023-.003*k,.019-.003*k,5);prev=point

def utility_silhouettes():
    # Telegraph poles and the maintenance gantry frame the route from normal eye height.
    stakes=[(-49,-20),(-39,-20),(-31,-13),(-21.8,-7.3),(-17,-6),(-11,-16),(4,-19),(18,-16),
            (26,-12),(41,-14),(49,-6)]
    cross=[]
    for i,(x,y) in enumerate(stakes):
        z=height(x,y)
        top=(x+.15*sin(i),y+.09*cos(i),z+4.25+.33*sin(i*.9))
        pole("Telegraph","Bark",(x,y,z-.17),top,.126,.086,10)
        box("Telegraph","Timber",top[0],top[1],top[2]-.24,.145,1.03,.12)
        for side in (-1,1):
            pole("Telegraph","Concrete",(top[0],top[1]+side*.39,top[2]-.17),(top[0],top[1]+side*.39,top[2]-.04),.043,n=8)
        cross.append(top)
    for u,v in zip(cross,cross[1:]):
        for off in (-.38,.38):
            prev=(u[0],u[1]+off,u[2]-.075)
            for j in range(1,15):
                t=j/14
                q=(u[0]*(1-t)+v[0]*t,u[1]*(1-t)+v[1]*t+off,u[2]*(1-t)+v[2]*t-.08-.54*sin(pi*t))
                pole("Telegraph","Tar",prev,q,.013,n=5);prev=q
    gx,gy=37,12
    for px,py in [(gx-2,gy),(gx+2,gy),(gx-2,gy+1.35),(gx+2,gy+1.35)]:
        pole("Lifting_frame","Bark",(px,py,0),(px,py,4.45),.117,.105,9)
        pole("Lifting_frame","Timber",(px,py,2.97),(px+(.76 if px<gx else -.76),py,4.22),.077,n=7)
    for py in (gy,gy+1.35):box("Lifting_frame","Timber",gx,py,4.40,4.7,.22,.25)
    box("Lifting_frame","Timber",gx-.35,gy+.7,4.53,.21,1.78,.19)
    pole("Lifting_frame","Oxide",(gx-.35,gy+.71,4.45),(gx-.35,gy+.71,2.47),.026,n=7)
    pole("Lifting_frame","Oxide",(gx-.35,gy+.71,2.47),(gx-.18,gy+.71,2.34),.039,n=7)
    pole("Lifting_frame","Oxide",(gx-.18,gy+.71,2.34),(gx-.03,gy+.71,2.49),.033,n=7)

def vault_decay():
    stone=random.Random(6527)
    def fragment(x,y,z,r,mat,group):
        n=stone.randint(5,8); length=stone.uniform(1,1.84); depth=r*stone.uniform(.35,.79)
        roll=stone.uniform(0,2*pi); vs=[]; face=[]
        outline=[(2*pi*k/n+stone.uniform(-.16,.16),r*stone.uniform(.72,1.2)) for k in range(n)]
        for j in range(3):
            width=(.77,1,.61)[j]
            for a,v in outline:
                u=v*cos(a)*length*width; w=v*sin(a)*width
                vs.append((x+u*cos(roll)-w*sin(roll),y+w*cos(roll)+u*sin(roll),z+(-.025,.13*depth,depth)[j]+stone.uniform(-.09,.09)*depth))
        for j in (0,1):
            for k in range(n):
                a=j*n+k;b=j*n+(k+1)%n;face.append((a,b,b+n,a+n))
        face.append(tuple(2*n+k for k in range(n)));groups[group].add(vs,face,mat)
    # Coarse brick breakage, splashed fines and fragments collect together at wall feet.
    for side in (-1,1):
        centres=[(-12.8,2.20),(-11.22,1.85),(-10.45,1.68),(-9.14,1.48),(-7.33,1.54),(-4.4,1.51),(-2.35,1.55)]
        for ci,(cx,w) in enumerate(centres):
            for k in range(65 if ci<4 else 32):
                x=stone.gauss(cx,.56 if ci<4 else .34)
                offset=stone.gauss(w,.26 if ci<4 else .10)
                if x>-9.8:offset=min(1.59,max(1.18,offset))
                y=-12+side*offset
                z=max(.058 if -9.82<x<10.31 and offset<1.78 else -.02,height(x,y))
                size=stone.uniform(.018,.052) if k>14 else stone.uniform(.053,.14) if k>3 else stone.uniform(.11,.24)
                mat=stone.choices(("Brick","Earth","Concrete"),weights=(5,4,2))[0]
                fragment(x,y,z,size,mat,"Casemate_chipped_brick_and_scree")
        # Crumbling limestone deposits and irregular damp edges on the interior side wall.
        for k in range(72):
            x=stone.uniform(-9.2,-.6)
            if any(abs(x-r)<.34 for r in (-9.62,-6.15,-2.7)):continue
            z=stone.uniform(.54,1.53); width=stone.uniform(.032,.13); length=stone.uniform(.05,.31)
            y=-12+side*1.695
            pts=[(x-width,y,z),(x-width*.47,y,z+length*.25),(x+width*.52,y,z+length*.17),(x+width,y,z),
                 (x+width*.14,y,z-length*.32),(x-width*.14,y,z-length),(x-width*.64,y,z-length*.39)]
            face=tuple(range(7)) if side<0 else tuple(reversed(range(7)))
            groups["Casemate_flaking_lime"].add(pts,[face],"Concrete")
    # Small earth slips, embedded stones and exposed fine roots follow the two shoulders.
    # Height is reevaluated for each vertex so neither edge hangs over the bank.
    for bank in (-1,1):
        for k in range(34):
            cx=stone.uniform(-47.8,-34.6);cy=-16+bank*stone.uniform(2.48,3.56)
            span=stone.uniform(.14,.66);wide=stone.uniform(.09,.33);points=[]
            skew=stone.uniform(-.72,.72)
            for v in range(3):
                for u in range(5):
                    along=(u-2)/2;across=(v-1)
                    x=cx+along*span+skew*across*wide*.30
                    y=cy+across*wide*(1-.46*abs(along))+.047*sin(u*1.76+k)
                    raised=stone.uniform(.02,.072)*(1-abs(along)*.44)*(1 if v==1 else .095)
                    points.append((x,y,height(x,y)+raised))
            fs=[(a,a+1,a+6,a+5) for a in (0,1,2,3,5,6,7,8)]
            groups["Trench_exposed_peat_shelves"].add(points,fs,"Earth" if k%4 else "Sod")
            if k%3==0:
                x,y,z=points[3];q=(x+.14,y-bank*.20,height(x+.14,y-bank*.20)+.032)
                tip=(x+.27,y-bank*.41,height(x+.27,y-bank*.41)+.016)
                pole("Trench_exposed_peat_shelves","Bark",(x,y,z),q,.021,.015,n=6)
                pole("Trench_exposed_peat_shelves","Bark",q,tip,.014,.005,n=5)
        for j in range(74):
            x=stone.uniform(-47.5,-35.2);y=-16+bank*stone.uniform(.97,2.36)
            if height(x,y)<water_cells.get((floor(x*2),floor(y*2)),-100)+.03:continue
            z=height(x,y)
            size=stone.uniform(.021,.066) if j>12 else stone.uniform(.065,.13)
            fragment(x,y,z,size,stone.choice(("Earth","Earth","Concrete","Bark")),"Groundside_loose_peat")
    # Individual weathered facing stones follow the western portal arch. Inset corners reveal mortar.
    for k in range(26):
        a0=.012+pi*k/26+.005; a1=.012+pi*(k+1)/26-.006
        rmin=1.776+stone.uniform(-.013,.013); rmax=2.135+stone.uniform(-.027,.028)
        x=-10.172-stone.uniform(.012,.085); vv=[]
        cont=[(a0,rmin+.026),(a0+.015,rmin),(a1-.009,rmin+.005),(a1,rmin+.040),
              (a1-.004,rmax-.024),(a1-.014,rmax),(a0+.012,rmax-.009),(a0,rmax-.038)]
        for d in (x+.065,x):
            for a,r in cont:
                vv.append((d+stone.uniform(-.007,.007),-12+r*cos(a),1.85+r*sin(a)))
        faces=[tuple(reversed(range(8))),tuple(range(8,16))]
        faces.extend((j,(j+1)%8,(j+1)%8+8,j+8) for j in range(8))
        groups["Roof_west_portal_chipped_arch"].add(vv,faces,"Concrete" if k%7==4 else "Brick")
    # Weather-beaten storm drain runs down the reveal and turns beneath the debris.
    g="Casemate_drain_and_clamps";x=-9.24;y=-13.49
    for a,b in [((x-.10,y,2.78),(x,y,2.49)),((x,y,2.49),(x,y,.49)),((x,y,.49),(x-.13,y+.04,.32))]:
        pole(g,"Oxide",a,b,.064,n=12)
    for z in (.73,1.45,2.17):
        pole(g,"Oxide",(x,y,z-.023),(x,y,z+.023),.077,n=12)
        box(g,"Oxide",x,y-.082,z,.196,.095,.041)
        for d in (-.078,.078):pole(g,"Oxide",(x+d,y+.011,z),(x+d,y+.031,z),.014,n=6)

def export():
    scales={"Earth":2.2,"Sod":2.9,"Brick":1.8,"Concrete":2.8,"Bark":1.7,"Timber":2.5,"Oxide":2.0,"Water":2.0}
    for name,data in groups.items():
        mesh=bpy.data.meshes.new(name)
        mesh.from_pydata(data.verts,[],data.faces)
        mesh.update()
        obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj)
        matlist=list(dict.fromkeys(data.paint))
        for m in matlist:mesh.materials.append(mats[m])
        mesh.polygons.foreach_set("material_index",[matlist.index(m) for m in data.paint])
        if name.startswith(("Ground_","Flood_meadow_","Event_")):
            attr=mesh.color_attributes.new(name="BankGrowth",type="FLOAT_COLOR",domain="POINT")
            weights=[]
            for x,y,z in data.verts:
                if name.startswith("Flood_meadow_"):
                    shore=valley.channel(x,y)[0]
                    w=.52+.29*sin(x*.13+y*.11)+.15*sin(y*.30)
                    weights.extend((w*smooth(.3,2.8,shore),)*3+(1,));continue
                slope=max(abs(height(x+.26,y)-height(x-.26,y)),abs(height(x,y+.26)-height(x,y-.26)))/.52
                w=smooth(.32,1.78,z-.38*n2(x*.76,y*.76))*(1-.77*smooth(.55,1.28,slope))
                w*=smooth(.0,1.8,valley.channel(x,y)[0])
                weights.extend((w,w,w,1))
            attr.data.foreach_set("color",weights)
        if name=="Runoff_pools":
            attr=mesh.color_attributes.new(name="FloodDepth",type="FLOAT_COLOR",domain="POINT")
            assert len(pool_depths)==len(data.verts)
            attr.data.foreach_set("color",[c for d in pool_depths for c in (d,d,d,1)])
        if name in river_depths:
            attr=mesh.color_attributes.new(name="FloodDepth",type="FLOAT_COLOR",domain="POINT")
            current=0. if name.startswith("Stillwater") else 1.
            attr.data.foreach_set("color",[c for d in river_depths[name] for c in (d,current,1,1)])
        if name.startswith("Marsh_cover_"):
            attr=mesh.color_attributes.new(name="BladeMask",type="FLOAT_COLOR",domain="POINT")
            levels=(0.,0.,.52,.52,1.,1.)
            attr.data.foreach_set("color",[c for i in range(len(data.verts)) for c in (levels[i%6],1,1,1)])
        if name.startswith("Folded_sedge_"):
            attr=mesh.color_attributes.new(name="BladeMask",type="FLOAT_COLOR",domain="POINT")
            attr.data.foreach_set("color",[c for _,t in foliage_uv[name] for c in (t,1,1,1)])
        uv=mesh.uv_layers.new(name="Metric UV")
        packed=[]
        for p,m in zip(mesh.polygons,data.paint):
            nx,ny,nz=map(abs,p.normal)
            s=scales.get(m,1.)
            if name.startswith(("Ground_","Flood_meadow_","Event_","Willows_")) or name in ("Signals","Solid_Sandbags"):p.use_smooth=True
            for vid in p.vertices:
                x,y,z=data.verts[vid]
                if name in foliage_uv:
                    packed.extend(foliage_uv[name][vid]);continue
                if name in wood_uv:
                    packed.extend(wood_uv[name][vid]);continue
                if name.startswith(("Roof_vault","Roof_ribs","Roof_portals")) and nx<.78:
                    packed.extend((x/1.42,1.73*atan2(z-1.83,y+12)/1.42));continue
                if nz>=max(nx,ny):u,v=x,y
                elif nx>ny:u,v=y,z
                else:u,v=x,z
                packed.extend((u/s,v/s))
        uv.data.foreach_set("uv",packed)
    for name,where in [("Survey_origin",(0,0,0)),("Survey_east",(1,0,0)),("Survey_north",(0,1,0)),("Survey_up",(0,0,1))]:
        ob=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(ob);ob.location=where
    bpy.ops.export_scene.fbx(filepath=str(output/"MarshFort.fbx"),axis_forward="-Z",axis_up="Y",
                         apply_scale_options="FBX_SCALE_UNITS",bake_anim=False,mesh_smooth_type="FACE",
                         object_types={"MESH","EMPTY"},use_custom_props=False)
    for visible in wood_uv:
        proxy=bpy.data.objects[visible.removeprefix("Weathered_")]
        proxy.hide_render=True
        proxy.hide_set(True)
    bpy.ops.wm.save_as_mainfile(filepath=str(source/"Osowiec_FortI_1916.blend"),compress=True)
    print("Fort I:",len(groups),"mesh batches,",sum(len(d.faces) for d in groups.values()),"faces")

terrain()
western_ward.build(box, pole, groups)
deck()
casemate()
telephones_and_markers()
water_cells={}
pool_depths=pools()
vegetation()
close_bank_sedges()
shoreline_plants()
yard()
wetland_repairs()
utility_silhouettes()
vault_decay()
north_fort_skyline()
wood_uv=western_bank.build(groups,pole,start,height,pathdist,foliage_uv)
export()
