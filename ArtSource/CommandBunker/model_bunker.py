"""Build the editable Osowiec environment, then export only its architecture for Unity.

Run bunker_surfaces.py with desktop Python first. Run this file using blender -b -P.
All coordinates are metres; Y points from the entry to the map table and Z points up.
"""
from pathlib import Path
from math import pi, sin, cos, sqrt
import math
import random
import re
import uuid
import sys
import bpy
import bmesh
from mathutils import Vector


SOURCE = Path(__file__).resolve().parent
ROOT = SOURCE.parents[1]
ART = ROOT / "Assets/Osowiec/HQ/Art"
SURF = ROOT / "Assets/Osowiec/Art/Fort1916/Surfaces"
PAL = ART / "Bunker"
rng = random.Random(91681)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene
scene.unit_settings.system = "METRIC"
scene.unit_settings.scale_length = 1
materials = {}
texture_family = dict(Limewash="Limewash", Brick="Brick", FiredClay="Brick core",
                      Masonry="Till", WetStone="Till", Oak="Timber", SoakedOak="Timber",
                      Heartwood="Timber", Iron="Oxide", Soil="Earth", Cloth="Canvas",
                      Paper="Canvas", Porcelain="Concrete", Wool="Felt", WoolSeam="Felt")
for label, family in texture_family.items():
    name = "Bunker_" + label
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    materials[label] = mat
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bs = nodes.get("Principled BSDF")
    bs.location = (250, 50)
    path = PAL if family in ("Limewash","Felt") else SURF
    raw = (PAL / (name + ".mat")).read_text()
    channel = re.search(r"_BaseColor: \{r: ([\d.]+), g: ([\d.]+), b: ([\d.]+)", raw)
    tint = [float(x) for x in channel.groups()]
    mat.diffuse_color = (*tint, 1)
    def image_tex(file, pos, data):
        node = nodes.new("ShaderNodeTexImage")
        node.image = bpy.data.images.load(str(path / file), check_existing=True)
        if data:
            node.image.colorspace_settings.name = "Non-Color"
        node.location = pos
        return node
    col = image_tex(family + "_colour.jpg", (-730, 300), False)
    multiply = nodes.new("ShaderNodeMixRGB")
    multiply.blend_type = "MULTIPLY"; multiply.inputs[0].default_value = 1
    multiply.inputs[2].default_value = (*[v**2.2 for v in tint], 1)
    multiply.location = (-150, 300)
    links.new(col.outputs["Color"], multiply.inputs[1])
    links.new(multiply.outputs["Color"], bs.inputs["Base Color"])
    normal = image_tex(family + "_normal.jpg", (-730, -50), True)
    bump = nodes.new("ShaderNodeNormalMap"); bump.location = (-150, -50)
    bump.inputs["Strength"].default_value = float(re.search(r"_BumpScale: ([\d.]+)", raw)[1])
    links.new(normal.outputs["Color"], bump.inputs["Color"])
    links.new(bump.outputs["Normal"], bs.inputs["Normal"])
    if label in ("WetStone","SoakedOak"):
        path=PAL
        mask=image_tex(label+"_mask.png",(-730,-400),True)
    else:
        mask=image_tex(family+"_mask.png",(-730,-400),True)
    roughness = nodes.new("ShaderNodeMath"); roughness.operation = "MULTIPLY_ADD"
    roughness.inputs[1].default_value = -float(re.search(r"_Smoothness: ([\d.]+)", raw)[1])
    roughness.inputs[2].default_value = 1
    roughness.location = (-150,-400)
    links.new(mask.outputs["Alpha"], roughness.inputs[0])
    links.new(roughness.outputs[0], bs.inputs["Roughness"])


def collection(name):
    result = bpy.data.collections.new(name)
    scene.collection.children.link(result)
    return result


entry = collection("01 | Stair and covered approach")
walls = collection("02 | Masonry bays and vault")
timber = collection("03 | Shoring and joinery")
floor = collection("04 | Floor and weathering")
interior = collection("05 | Signal equipment and furnishings")
registration = collection("06 | Engine registration")
preview = collection("07 | Staged furniture and lighting - Blender only")
modules = []
wood = {"Oak", "SoakedOak", "Heartwood"}


class Part:
    def __init__(self, name, owner):
        self.name, self.owner = name, owner
        self.vertices, self.faces, self.uv, self.slots, self.face_slots = [], [], [], [], []
        modules.append(self)

    def face(self, pts, material, coords=None, grain=1, offset=(0,0), scale=1):
        pts = [tuple(p) for p in pts]
        n = (Vector(pts[1])-Vector(pts[0])).cross(Vector(pts[2])-Vector(pts[0]))
        if n.length < 1e-9:
            return
        if coords is None:
            axis = max(range(3), key=lambda i: abs(n[i]))
            a,b = [(1,2),(0,2),(0,1)][axis]
            if material in wood:
                if a == grain: a,b = b,a
                coords = [(p[a]*.85 + offset[0], p[b]*.46 + offset[1]) for p in pts]
            else:
                factor = .73 if material in ("Brick","Limewash") else 1.26
                coords = [(p[a]*factor*scale + offset[0], p[b]*factor*scale + offset[1]) for p in pts]
        i = len(self.vertices)
        self.vertices.extend(pts)
        self.faces.append(tuple(range(i, i+len(pts))))
        self.uv.extend(coords)
        if material not in self.slots: self.slots.append(material)
        self.face_slots.append(self.slots.index(material))

    def build(self):
        mesh = bpy.data.meshes.new(self.name)
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.materials.clear()
        for label in self.slots:
            mesh.materials.append(materials[label])
        for face,slot in zip(mesh.polygons, self.face_slots):
            face.material_index=slot
        uv = mesh.uv_layers.new(name="Surface metres")
        uv.data.foreach_set("uv", [v for pair in self.uv for v in pair])
        mesh.update()
        if self.name.startswith("Greatcoat"):
            tailored=bmesh.new()
            tailored.from_mesh(mesh)
            bmesh.ops.remove_doubles(tailored,verts=tailored.verts[:],dist=.00012)
            for face in tailored.faces:
                face.smooth=True
            tailored.to_mesh(mesh);tailored.free()
        obj = bpy.data.objects.new(self.name, mesh)
        self.owner.objects.link(obj)
        return obj


def box(part, center, size, mat, bevel=.012, grain=None, yaw=0, chipped=False):
    """A cut, eased board with a slightly broken corner and a real bevel on its lip."""
    x,y,z = center; w,l,h = size; w/=2; l/=2; h/=2
    c = min(bevel, .41*w, .41*l, .42*h)
    grain = max(range(3), key=lambda j: size[j]) if grain is None else grain
    chip = min(c*(3.6 if chipped else 1.2), w*.45, l*.45)
    corners = [(-w+chip,-l), (w-c,-l), (w,-l+c),(w,l-c),(w-c,l),(-w+c,l),(-w,l-c),(-w,-l+chip)]
    def rim(shrink, zz):
        return [(x+cos(yaw)*(a*(w-shrink)/w)-sin(yaw)*(b*(l-shrink)/l),
                 y+sin(yaw)*(a*(w-shrink)/w)+cos(yaw)*(b*(l-shrink)/l), z+zz) for a,b in corners]
    rings = [rim(c*.62,-h),rim(0,-h+c*.7),rim(0,h-c*.7),rim(c*.62,h)]
    uv = (rng.random(),rng.random()) if mat in wood else (0,0)
    part.face(list(reversed(rings[0])), mat, grain=grain, offset=uv)
    for ra,rb in zip(rings,rings[1:]):
        for k in range(8):
            j=(k+1)%8
            part.face([ra[k],ra[j],rb[j],rb[k]], mat, grain=grain, offset=uv)
    part.face(rings[-1],mat,grain=grain,offset=uv)


def tube(part, pts, radius, mat, seg=9):
    pts = [Vector(p) for p in pts]
    rings = []
    for i,p in enumerate(pts):
        direction = (pts[min(i+1,len(pts)-1)]-pts[max(0,i-1)]).normalized()
        ref = Vector((1,0,0)) if abs(direction.x)<.89 else Vector((0,1,0))
        a = direction.cross(ref).normalized()
        b = direction.cross(a)
        rr = radius[i] if isinstance(radius,list) else radius
        rings.append([p+rr*(a*cos(2*pi*k/seg)+b*sin(2*pi*k/seg)) for k in range(seg)])
    part.face(rings[0][::-1], mat); part.face(rings[-1],mat)
    for i in range(len(rings)-1):
        for k in range(seg):
            j=(k+1)%seg
            part.face([rings[i][k],rings[i][j],rings[i+1][j],rings[i+1][k]],mat)


def vault(part, y0, y1, width, spring, rise, face, segments, phase=0, dy=0):
    """Continuous inner face; UV follows the actual arc to preserve mortar spacing at the crown."""
    arc=[]
    for i in range(segments+1):
        theta=pi*i/segments
        arc.append((width*cos(theta),spring+rise*sin(theta)))
    length=0
    for a,b in zip(arc,arc[1:]):
        next_length=length+math.dist(a,b)
        part.face([(a[0],y1,a[1]+dy),(b[0],y1,b[1]+dy),(b[0],y0,b[1]),(a[0],y0,a[1])][::-1],face,
                  [(phase+length*.73,y1*.73),(phase+next_length*.73,y1*.73),
                   (phase+next_length*.73,y0*.73),(phase+length*.73,y0*.73)][::-1])
        length=next_length


def rib(part, y, width, spring, rise, depth, count, mat):
    for i in range(count):
        rings=[]
        for a in [pi*(i+.018)/count,pi*(i+.982)/count]:
            rings.append([(width*cos(a), y-depth*.5, spring+rise*sin(a)),
                          ((width+.095)*cos(a), y-depth*.5, spring+(rise+.09)*sin(a)),
                          ((width+.095)*cos(a), y+depth*.5, spring+(rise+.09)*sin(a)),
                          (width*cos(a), y+depth*.5, spring+rise*sin(a))])
        for k in range(4):
            j=(k+1)%4
            part.face([rings[0][k],rings[0][j],rings[1][j],rings[1][k]],mat)
        part.face(rings[0][::-1],mat);part.face(rings[1],mat)


def roof_height(x):
    return 2.27+1.15*sqrt(max(0,1-(x/3.42)**2))


# The cellar is not a rectangular box. The ends follow the arch; the entry and
# the side recess are open below their lintels.
for label,y,entry_gap in [("South portal",-2.60,True),("Map wall",3.65,False)]:
    p=Part("Solid_"+label,walls)
    breaks = sorted(set([-3.42+6.84*i/44 for i in range(45)]+[-1.2,1.2]))
    for a,b in zip(breaks,breaks[1:]):
        in_entry = entry_gap and abs((a+b)*.5)<1.2
        def base(t):
            return 2.18+.47*sqrt(max(0,1-(t/1.2)**2)) if in_entry else -.10
        coords=[(a,y,base(a)),(b,y,base(b)),(b,y,roof_height(b)+.07),(a,y,roof_height(a)+.07)]
        p.face(coords if y>0 else coords[::-1],"Limewash")
        if entry_gap:
            p.face([(x,y-.12,z) for x,y,z in coords],"Brick")
            if in_entry:
                # Intrados and jambs join two correctly facing sides of one
                # masonry wall. The approach lining stops at the outer face.
                p.face([(a,y,base(a)),(b,y,base(b)),(b,y-.12,base(b)),(a,y-.12,base(a))],"Brick")
    if entry_gap:
        for side in [-1,1]:
            x=side*1.2
            pts=[(x,y-.12,-.12),(x,y,-.12),(x,y,2.18),(x,y-.12,2.18)]
            p.face(pts if side<0 else pts[::-1],"FiredClay")
        rib(p,y+.065,1.17,2.18,.45,.20,21,"FiredClay")

for side in (-1,1):
    ranges = [(-2.60,.18),(2.32,3.65)] if side<0 else [(-2.60,3.65)]
    for index,(a,b) in enumerate(ranges):
        p=Part("Solid_Wall_"+str(side)+"_"+str(index),walls)
        box(p,(side*3.51,(a+b)/2,1.115),(.18,b-a,2.33),"Limewash",.009)
    if side<0:
        p=Part("Solid_Signal alcove",walls)
        box(p,(-4.34,1.25,1.07),(.16,2.25,2.22),"Brick")
        box(p,(-3.88,.18,1.07),(.98,.13,2.22),"Brick")
        box(p,(-3.88,2.32,1.07),(.98,.13,2.22),"Brick")
        box(p,(-3.83,1.25,2.135),(.95,2.16,.16),"Heartwood")
        box(p,(-3.51,1.25,2.24),(.18,2.20,.24),"Limewash")

for i,(a,b) in enumerate([(-2.63,-.57),(-.57,1.47),(1.47,3.68)]):
    p=Part("Vault | Bay "+str(i+1),walls)
    vault(p,a,b,3.42,2.27,1.15,"Limewash",56,i*.21)
for i,y in enumerate([-2.50,-.54,1.52,3.55]):
    p=Part("Vault | Brick arch band "+str(i+1),walls)
    rib(p,y,3.35,2.27,1.105,.13,47,"FiredClay")
    for sign in [-1,1]:
        # Shoring abuts the shoulders of the vault. The left niche has an uninterrupted opening.
        if sign<0 and .18<y<2.32: continue
        box(p,(sign*3.35,y,1.15),(.105,.16,2.30),"FiredClay")

for side in (-1,1):
    for y in ([-2.45,.09,2.39,3.54] if side<0 else [-2.44,-.57,2.48,3.54]):
        p=Part("Shoring | Post %+.1f %+.1f"%(side,y),timber)
        box(p,(side*3.295,y,1.08),(.185,.214,2.16),"Heartwood",.024,2,chipped=True)
        box(p,(side*3.29,y,.10),(.216,.244,.20),"Masonry",.018)
        box(p,(side*3.29,y,2.12),(.205,.27,.20),"Oak",.02)
        for z in [.27,1.81]:
            tube(p,[(side*3.193,y,z),(side*3.184,y,z)],.017,"Iron",8)
    p=Part("Shoring | Wall plate "+str(side),timber)
    box(p,(side*3.32,.51,2.17),(.175,6.20,.15),"Heartwood",grain=1)
    for a,b in ([(-2.5,.12),(2.39,3.54)] if side<0 else [(-2.5,3.54)]):
        # Low retaining planks along the damp footing.
        for j in range(2):
            box(p,(side*3.385,(a+b)/2,.12+.205*j),(.065,b-a,.195),"SoakedOak",.012,1,chipped=True)

p=Part("Walk_Oak floor",floor)
floor_width = 6.84
for row in range(29):
    pitch = floor_width/29
    x=-3.42+pitch*(row+.5)
    start=-2.62
    while start<3.64:
        end=min(3.66,start+rng.uniform(1.35,2.50))
        if 3.66-end<.39:end=3.66
        wet=abs(x)<.88 and start<-1.1 and rng.random()<.80
        box(p,(x,(start+end)/2,-.044+rng.uniform(-.002,.001)),(pitch-.004,end-start-.005,.085),
            "SoakedOak" if wet else "Oak",.007,1,chipped=True)
        for y in (start+.064,end-.064):
            for offset in [-.079,.079]:
                tube(p,[(x+offset,y,-.003),(x+offset,y,.001)],.0055,"Iron",7)
        start=end

p=Part("Walk_Signal recess boards",floor)
for i in range(11):
    y=.15+(i+.5)*.20
    box(p,(-3.84,y,-.045),(.89,.196,.087),"SoakedOak",.007,0,chipped=True)

p=Part("Walk_Covered passage boards",entry)
for i in range(14):
    y=-5.5+(i+.5)*(2.90/14)
    box(p,(0,y,-.044),(2.44,2.90/14-.004,.086),"SoakedOak",.009,0, chipped=True)
    for x in (-.92,.92):
        tube(p,[(x,y,-.003),(x,y,.001)],.009,"Iron",8)

p=Part("Walk_Stone treads and landing",entry)
for k in range(6):
    y=-5.5-(k+.5)*.38
    z=.15*(k+1)
    split=rng.uniform(-.47,.42)
    for x0,x1 in [(-1.21,split-.002),(split+.002,1.21)]:
        box(p,((x0+x1)/2,y,z-.099),(x1-x0,.383,.195),"WetStone",.017,chipped=True)
box(p,(0,-8.28,.788),(2.48,1.01,.218),"WetStone",.022)
for side in (-1,1):
    p=Part("Solid_Entry revetment "+str(side),entry)
    # A single continuous inside face per pitch. No buried coplanar wall behind
    # the descending masonry, or distant shots show flickering black rectangles.
    start=-7.78 if side>0 else -8.80
    breaks=sorted(set([start,-7.78,-5.50,-2.72]))
    for a,b in zip(breaks,breaks[1:]):
        def spring(y): return 2.195+min(.9,max(0,(-5.50-y)*.15/.38))
        outline=[(a,-.16),(b,-.16),(b,spring(b)),(a,spring(a))]
        inner=[(side*1.20,y,z) for y,z in outline]
        outer=[(side*1.44,y,z) for y,z in outline]
        p.face(inner if side<0 else inner[::-1],"Brick")
        p.face(outer if side>0 else outer[::-1],"Brick")
        for k in (0,1,2,3):
            j=(k+1)%4
            cap=[inner[k],outer[k],outer[j],inner[j]]
            p.face(cap if side<0 else cap[::-1],"Brick")
    for i,y in enumerate([-7.68,-5.50,-4.10,-2.73]):
        z=max(0,(-5.5-y)*.15/.38)
        # Saturation is limited to the foot. The end of the dry main upright is
        # wedged into its masonry springer rather than left as a shiny flat top.
        box(p,(side*1.14,y,z+1.09),(.135,.16,2.18),"Heartwood",.011,2,chipped=True)
        box(p,(side*1.14,y,z+.105),(.143,.168,.21),"SoakedOak",.007,2)
        box(p,(side*1.145,y,z+2.194),(.18,.227,.105),"Heartwood",.011,1)
        box(p,(side*1.155,y,z+2.264),(.20,.243,.060),"FiredClay",.010)
        for height in [.22,1.99]:
            box(p,(side*1.14,y,z+height),(.145,.17,.038),"Iron",.003)
            tube(p,[(side*1.068,y,z+height),(side*1.062,y,z+height)],.010,"Iron",7)
    # Narrow lower sill; runs parallel to the steps.
    tube(p,[(side*1.10,-7.64,1.64),(side*1.10,-5.50,.80),(side*1.10,-5.14,.80)],.032,"Iron",12)
    for y in [-7.44,-6.30,-5.30]:
        z=.80+max(0,(-5.5-y)*.15/.38)
        tube(p,[(side*1.18,y,z-.12),(side*1.103,y,z-.105),(side*1.10,y,z)],.019,"Iron")

for i,(a,b,spring,delta) in enumerate([(-8.81,-7.78,3.08,0),(-7.78,-5.50,3.08,-.9),(-5.50,-2.72,2.18,0)]):
    p=Part("Passage | Barrel "+str(i),entry)
    vault(p,a,b,1.21,spring,.47,"Brick",32,0,delta)
for i,y in enumerate([-7.7,-5.49,-4.07,-2.70]):
    p=Part("Passage | Portal hoop "+str(i),entry)
    rib(p,y,1.15,2.18+max(0,(-5.5-y)*.15/.38),.435,.18,23,"FiredClay")

p=Part("Solid_Outer return and inner gate",entry)
# Birdsmouth at the landing. Both the backing wall and the roof of the
# quarter-turn service doorway extend over its soffit instead of ending below it.
box(p,(0,-8.94,2.12),(2.90,.27,3.26),"Limewash")
for x in [-.95,-.50,-.05,.40,.85]:
    box(p,(x,-8.76,1.91),(.415,.11,1.98),"SoakedOak",.015,2)
box(p,(0,-8.67,1.28),(2.05,.076,.12),"Heartwood",grain=0)
box(p,(0,-8.67,2.55),(2.05,.076,.12),"Heartwood",grain=0)
box(p,(2.12,-8.29,2.18),(.22,1.61,3.16),"Brick")
box(p,(1.67,-8.92,2.16),(1.07,.22,3.17),"Limewash")
box(p,(1.67,-7.64,2.16),(1.07,.22,3.17),"Brick")
box(p,(1.63,-8.28,3.48),(1.08,1.39,.25),"Brick")
box(p,(1.64,-8.28,.79),(.83,1.26,.213),"WetStone")
box(p,(1.21,-8.29,3.23),(.24,1.04,.42),"Heartwood",.013,1)
for i in range(4):
    box(p,(1.36+i*.055,-8.60+i*.165,1.94),(.078,.18,2.08),"SoakedOak",.010,2,yaw=-.27)
for z in [1.25,2.55]:
    box(p,(1.39,-8.33,z),(.07,.67,.071),"Heartwood",.006,1,yaw=-.27)
    for k in range(3):
        yy=-8.53+.21*k;xx=1.29+.06*k
        tube(p,[(xx,yy,z),(xx-.010,yy,z)],.013,"Iron",8)
# Bevelled decking intentionally has edge gaps. A dark joist bed is visible
# through those gaps down to the last step rather than a view out of the world.
p=Part("Construction | Compacted earth under structure",floor)
box(p,(-.37,.44,-.18),(8.15,7.10,.18),"Soil",.012)
box(p,(0,-4.30,-.21),(2.97,3.49,.23),"Soil",.012)
box(p,(.30,-7.30,-.22),(3.95,3.32,.24),"Soil",.012)
box(p,(.04,-8.95,.17),(4.5,.44,.71),"Soil",.012)
p=Part("Earth | Cut bank over stair",entry)
for side in [-1,1]:
    bounds = [-8.78,-7.78,-6.65,-5.50,-4.12,-2.60]
    for a,b in zip(bounds,bounds[1:]):
        def level(y): return 2.74+min(.90,max(0,(-5.50-y)*.15/.38))
        for inn,out,rise0,rise1 in [(0,1.20,.40,.24),(1.20,2.03,.24,-.15),(2.03,3.02,-.15,-1.18)]:
            p.face([(side*inn,a,level(a)+rise0),(side*out,a,level(a)+rise1),
                    (side*out,b,level(b)+rise1),(side*inn,b,level(b)+rise0)][::side],"Soil")

p=Part("Solid_Signal writing shelf",interior)
for i in range(3):
    box(p,(-3.84+i*.19,1.25,.985),(.19,1.84,.073),"Oak",.009,1,chipped=True)
for y in [.50,2.01]:
    box(p,(-3.91,y,.894),(.66,.095,.11),"Heartwood",grain=0)
    tube(p,[(-4.15,y,.54),(-3.64,y,.91)],.034,"Iron")
for h in [1.43,1.80]:
    box(p,(-4.04,1.25,h),(.42,1.84,.057),"Oak",.005,1)
    for y in [.54,1.99]:
        tube(p,[(-4.23,y,h-.21),(-3.86,y,h-.031)],.018,"Iron",8)

p=Part("Signal stores | Books and line reels",interior)
for i in range(7):
    height=rng.uniform(.18,.27); y=.51+i*.092
    box(p,(-4.04,y,1.459+height/2),(.22,.065,height-.008),"Paper",.004)
    for sign in [-1,1]:
        box(p,(-4.04,y+sign*.036,1.459+height/2),(.233,.007,height),"Cloth",.002)
    box(p,(-3.914,y,1.459+height/2),(.018,.076,height),"Heartwood",.003)
for i in range(4):
    y=1.41+i*.125; z=1.56
    for yy in [y-.052,y+.052]:
        tube(p,[(-4.04,yy-.007,z),(-4.04,yy+.007,z)],.098,"Oak",20)
    tube(p,[(-4.04,y-.044,z),(-4.04,y+.044,z)],.065,"Iron",20)
for x,y in [(-3.73,.96),(-3.79,1.57)]:
    for j in range(4):
        box(p,(x,y,1.023+j*.0035),(.20,.31,.003),"Paper",.002,yaw=.04*j)
for i in range(3):
    x=-4.01; y=.62+i*.46; z=1.94
    tube(p,[(x,y-.15,z),(x,y+.15,z)],.104,"Cloth",19)
    for dy in [-.092,.092]:
        tube(p,[(x,y+dy-.008,z),(x,y+dy+.008,z)],.108,"Heartwood",18)

p=Part("Signal stores | Insulator pairs and loose cloth cable",interior)
for y in [-2.04,-.82,.39,1.68,2.90]:
    for h in [1.80,1.985]:
        # The cable jumps over the alcove and returns to wall-mounted insulators.
        x=-3.37 if y<.18 or y>2.32 else -3.92
        tube(p,[(x-.045,y,h),(x+.035,y,h)],.016,"Iron",10)
        tube(p,[(x+.032,y,h),(x+.053,y,h),(x+.060,y,h),(x+.069,y,h)],
             [.043,.048,.027,.037],"Porcelain",14)
for h in [1.80,1.985]:
    runs=[(-3.30,-2.04),(-3.30,-.82),(-3.85,.39),(-3.85,1.68),(-3.30,2.90)]
    points=[]
    for (x0,a),(x1,b) in zip(runs,runs[1:]):
        for i in range(10):
            t=i/10
            points.append((x0+(x1-x0)*t,a+(b-a)*t,h-.068*sin(pi*t)))
    points.append((*runs[-1],h))
    tube(p,points,.0065,"Iron",7)

p=Part("Solid_Entry bench",interior)
for i in range(2):
    box(p,(2.85+i*.22,-1.31,.463),(.211,1.34,.077),"Oak",.011,1,chipped=True)
for y in [-1.84,-.79]:
    for x in [2.80,3.10]:
        box(p,(x,y,.217),(.075,.106,.427),"Heartwood",.008,2)
    box(p,(2.96,y,.362),(.44,.11,.13),"Heartwood",.01,0)
box(p,(2.96,-1.30,.164),(.063,1.09,.084),"Oak",.006,1)
p=Part("Entry bench | Forged hook rail",interior)
box(p,(3.355,-1.25,1.87),(.062,1.14,.116),"Oak",.012,1)
for y in [-1.67,-1.26,-.85]:
    box(p,(3.317,y,1.864),(.014,.045,.091),"Iron",.004)
    # Flattened shank with a rounded turned-up tip; two fasteners bear its load.
    tube(p,[(3.311,y,1.835),(3.268,y,1.82),(3.211,y,1.83),(3.192,y,1.856),(3.192,y,1.886)],
         [.013,.012,.012,.010,.015],"Iron",12)
    for z in [1.83,1.895]:
        tube(p,[(3.311,y,z),(3.303,y,z)],.008,"Iron",10)
for yy in [-1.77,-.73]:
    tube(p,[(3.326,yy,1.87),(3.319,yy,1.87)],.011,"Iron",11)

# The coat hangs by a bent wooden shoulder hanger. The front is open at the
# throat and hem; the sleeves, lapels and pocket welts are tailored pieces.
tube(p,[(3.204,-1.26,1.868),(3.186,-1.26,1.887),(3.167,-1.26,1.865),
        (3.174,-1.26,1.819),(3.184,-1.26,1.791)],.006,"Iron",10)
tube(p,[(3.207,-1.476,1.668),(3.201,-1.40,1.711),(3.188,-1.29,1.766),
        (3.184,-1.25,1.776),(3.193,-1.14,1.738),(3.204,-1.047,1.676)],
     [.017,.020,.021,.021,.020,.017],"Oak",12)
for yy in [-1.40,-1.12]:
    tube(p,[(3.20,yy,1.716),(3.20,-1.26,1.69)],.008,"Oak",10)


def interp(t,keys):
    for (a,v),(b,w) in zip(keys,keys[1:]):
        if t<=b:
            blend=max(0,(t-a)/(b-a))
            return v+(w-v)*blend
    return keys[-1][1]


def half_width(t):
    return interp(t,[(0,.241),(.09,.254),(.37,.205),(.55,.166),(.75,.19),
                     (.88,.207),(.925,.228),(.959,.196),(1,.073)])


def coat_pos(h,t,front=True,offset=0):
    """h traverses the physical pattern from left to right across the chest."""
    width=half_width(t)
    h=max(-1,min(1,h)); rim=abs(h)
    yy=-1.26+.024*sin(t*pi*1.65)+h*width
    zz=.643+1.129*t+.028*sin(h*6+1.5)*((1-t)**4)+h*.027*(t**2)
    pocket_weight=.022*math.exp(-((t-.47)/.12)**2)*max(h,0)
    zz-=pocket_weight
    folds=.016*sin(12*h+1.24*t+.56)*(.40+.60*(1-t))+.009*cos(23*h-2.5*t)
    # The cloth is mostly flat where the hanger holds it and folds below.
    if front:
        xx=3.216-(.115+.022*sin(pi*t))*(1-.76*rim**2)-folds
        xx-=.012*max(0,1-abs(t-.76)/.15)*(1-rim)
        xx-=offset
    else:
        xx=3.224+.023*(1-rim**2)+folds*.4+offset
    return (xx,yy,zz)


panels=Part("Greatcoat | Tailored front and back panels",interior)
trim=Part("Greatcoat | Turned collar and pocket flaps",interior)
sleeves=Part("Greatcoat | Bent sleeves and turned cuffs",interior)
details=Part("Greatcoat | Stitched edges and horn buttons",interior)

def inner_edge(t,side):
    if t>.75:return side*.33*((t-.75)/.25)**1.25
    if t<.49:return side*.18*((.49-t)/.49)**1.35
    return -.028 if side>0 else .055


def joined_grid(part, rows, mat, reverse=False):
    for lower,upper in zip(rows,rows[1:]):
        for j in range(len(lower)-1):
            quad=[lower[j],lower[j+1],upper[j+1],upper[j]]
            part.face(quad[::-1] if reverse else quad,mat)


levels=sorted(set([i/34 for i in range(35)]+[.88,.925,.959]))
back=[[coat_pos(-1+2*i/36,t,False) for i in range(37)] for t in levels]
joined_grid(panels,back,"Wool")
for side in (-1,1):
    grid=[]
    for t in levels:
        edge=inner_edge(t,side)
        grid.append([coat_pos(edge+(side-edge)*i/21,t,True,.004 if side>0 else 0) for i in range(22)])
    joined_grid(panels,grid,"Wool",reverse=side>0)
    tube(details,[row[0] for row in grid],.0028,"WoolSeam",8)
    tube(details,grid[0],.0035,"WoolSeam",9)
    for t0,t1 in zip(levels,levels[1:]):
        pts=[coat_pos(side,t0,False),coat_pos(side,t0),coat_pos(side,t1),coat_pos(side,t1,False)]
        panels.face(pts if side>0 else pts[::-1],"WoolSeam")
    # Turned, doubled lapel. Top of the flap intersects the shoulder seam;
    # its lower point comes forward from the breast and catches sidelight.
    lapel=[(side*.34,.993,.008),(side*.76,.947,.009),(side*.62,.912,.023),
           (side*.77,.902,.020),(side*.16,.743,.006),(side*.27,.902,.025)]
    points=[coat_pos(h,t,True,k) for h,t,k in lapel]
    center=tuple(sum(v[i] for v in points)/len(points) for i in range(3))
    for j in range(len(points)):
        tri=[center,points[j],points[(j+1)%len(points)]]
        if (Vector(tri[1])-Vector(tri[0])).cross(Vector(tri[2])-Vector(tri[0])).x>0:tri.reverse()
        trim.face(tri,"Wool")
    tube(details,points+[points[0]],.0036,"WoolSeam",9)
    collar=[coat_pos(side*.12,.998,True,.009),coat_pos(side*.62,.976,True,.023),
            coat_pos(side*.70,.944,True,.026),coat_pos(side*.36,.919,True,.015)]
    trim.face(collar if side<0 else collar[::-1],"WoolSeam")
    tube(details,collar[:3],.0037,"Wool",8)
    welt=[(side*.32,.486),(side*.83,.519),(side*.84,.490),(side*.73,.471),(side*.36,.46)]
    pocket=[coat_pos(h,t,True,.009) for h,t in welt]
    trim.face(pocket if side>0 else pocket[::-1],"WoolSeam")
    tube(details,pocket[:2],.004,"Wool",8)

    # Elliptic sleeves sag and flatten at their empty elbows and wrists.
    keys=[(-.18,(.112,.963,.049,.030,.02)),(-.12,(.145,.947,.078,.050,.040)),
          (-.055,(.173,.93,.091,.061,.049)),(0,(.184,.911,.098,.067,.052)),(.13,(.231,.87,.091,.061,.048)),
          (.42,(.271,.733,.097,.057,.042)),(.52,(.278,.68,.084,.054,.033)),
          (.75,(.268,.59,.118,.050,.037)),(1,(.246,.495,.149,.045,.033))]
    def sleeve_section(f,extra=0):
        vals=[interp(f,[(k,v[n]) for k,v in keys]) for n in range(5)]
        yy,ht,depth,ry,rx=vals
        rings=[]
        for i in range(29):
            a=2*pi*i/28
            crease=.0035*cos(5*a+f*4)+.0035*sin(14*f+.7)*sin(a)
            crossing=.056*max(0,(f-.46)/.54) if side<0 else 0
            rings.append((3.217-depth-.045*crossing/.056+(rx+extra+crease)*cos(a),
                          -1.26+side*(yy+.016*sin(pi*f*1.2)-crossing)+(.012 if side<0 else 0)*f+(ry+extra+crease)*sin(a),
                          .643+1.129*ht+.020*side*f+.008*sin(a+f)*(f**2)))
        return rings
    cuff=[sleeve_section(f,.005) for f in [.913,.93,.978,1.0]]
    arm=[sleeve_section(f) for f in [-.18,-.145,-.12,-.092,-.055,-.026]+[i/23 for i in range(24)]]
    joined_grid(sleeves,arm,"Wool",reverse=True)
    joined_grid(sleeves,cuff,"WoolSeam",reverse=True)
    tube(details,cuff[-1],.004,"Wool",8)
    tube(details,[row[3] for row in arm],.002,"WoolSeam",7)
    # Looking into the opening reveals the folded lining behind the cuff.
    wrist=cuff[-1]; cp=Vector(tuple(sum(c[k] for c in wrist[:-1])/28 for k in range(3)))
    inner=[cp+(Vector(v)-cp)*.82+Vector((0,0,.024)) for v in wrist]
    joined_grid(sleeves,[wrist,inner],"WoolSeam")
    sleeves.face(inner[:-1],"WoolSeam")
for t in [.514,.588,.66,.733]:
    for side in [-1,1]:
        x,y,z=coat_pos(side*.24,t,True,.008)
        tube(details,[(x+.002,y,z),(x-.004,y,z),(x-.006,y,z)],[.010,.0107,.009],"Iron",14)
        tube(details,[(x-.0065,y-.003,z),(x-.0065,y+.003,z+.001)],.0012,"WoolSeam",5)

def ground_z(y):
    if y>=-5.5:return 0
    return min(.9,.15*(1+int((-y-5.5)/.38)))

p=Part("Weather | Tracked silt and gravel",floor)
for i,y in enumerate([-8.31,-7.88,-7.37,-6.93,-6.49,-6.13,-5.77,-5.20,-4.64,-4.06,-3.51,-3.00,-2.43,-1.84]):
    x=.16*(-1)**i+.14*sin(i*.56)
    yaw=rng.uniform(-.21,.21)
    # Separate dull heel, ball and broken tread lugs, richer at the wet portal.
    for side in [-1,1]:
        for j in range(6):
            if i>8 and rng.random()<.40: continue
            local_x=side*(.033+.005*sin(j*.8))
            local_y=[-.101,-.088,-.007,.026,.057,.087][j]
            points=[]
            for k in range(10):
                a=2*pi*k/10; size=rng.uniform(.80,1.15)
                dx=local_x+.024*cos(a)*size;dy=local_y+.012*sin(a)*size
                xx=x+dx*cos(yaw)-dy*sin(yaw); yy=y+dx*sin(yaw)+dy*cos(yaw)
                points.append((xx,yy,ground_z(yy)+.0024))
            p.face(points,"Soil")
for i in range(119):
    y=rng.uniform(-8.70,-3.0); x=rng.choice([-1,1])*rng.uniform(.81,1.12)
    r=rng.uniform(.009,.041)
    zz=ground_z(y)
    pts=[(x+r*cos(2*pi*j/6)*rng.uniform(.7,1.25),y+r*sin(2*pi*j/6),zz+.003) for j in range(6)]
    for j in range(6):
        p.face([pts[j],pts[(j+1)%6],(x+.001,y-.002,zz+r*.6)],"Soil" if i%3 else "WetStone")

for obj in modules:
    obj.build()
for name,position in [("SurveyOrigin",(0,0,0)),("SurveyNorth",(0,1,0)),("SurveyUp",(0,0,1))]:
    obj=bpy.data.objects.new(name,None);registration.objects.link(obj)
    obj.empty_display_size=.1;obj.location=position
shell = list(scene.objects)
fbx = ART / "CommandBunker.fbx"
asset_guid = uuid.uuid5(uuid.NAMESPACE_URL, "osowiec/CommandBunker.fbx").hex
fbx_meta = Path(str(fbx)+".meta")
meta_src=(ART/"CommandTable.fbx.meta").read_text()
entries=[]
for mat in materials.values():
    mat_guid=re.search(r"^guid: (\w+)",(PAL/(mat.name+".mat.meta")).read_text(),re.M)[1]
    entries.append("  - first:\n      type: UnityEngine:Material\n      assembly: UnityEngine.CoreModule\n      name: "+mat.name+
                   "\n    second: {fileID: 2100000, guid: "+mat_guid+", type: 2}")
meta_src=re.sub(r"^guid: \w+","guid: "+asset_guid,meta_src,1,flags=re.M)
meta_src=meta_src.replace("  externalObjects: {}","  externalObjects:\n"+"\n".join(entries))
if "--preview-only" not in sys.argv:
    fbx_meta.write_text(meta_src)
    bpy.ops.export_scene.fbx(filepath=str(fbx), axis_forward="-Z", axis_up="Y",
                             apply_unit_scale=True,apply_scale_options="FBX_SCALE_UNITS",bake_anim=False,
                             object_types={"MESH","EMPTY"},path_mode="STRIP",use_mesh_modifiers=True)

# The source includes the actual table, telephone and fixtures as editable staged objects.
# Unity installs its interactive copies at the same anchors, hence they are not exported twice.
def stage(name,position,yaw=0,scale=1):
    sub=bpy.data.collections.new(name+" | editable furniture")
    preview.children.link(sub)
    anchor=bpy.data.objects.new(name+" anchor",None);sub.objects.link(anchor)
    anchor.location=position;anchor.rotation_euler.z=-math.radians(yaw);anchor.scale=(scale,)*3
    with bpy.data.libraries.load(str(ART/"Source~"/(name+".blend")),link=False) as (src,dst):
        dst.objects=[k for k in src.objects if not k.startswith("SurveyOrigin")]
    for obj in dst.objects:
        if obj and obj.type in ("MESH","CURVE","EMPTY"):
            sub.objects.link(obj)
            if obj.parent is None:obj.parent=anchor

stage("CommandTable",(0,1.10,0))
stage("SurveyBoard",(-.45,3.51,1.77),scale=1.12)
stage("FieldGramophone",(1.95,2.82,0),-25)
stage("StormLantern",(.98,1.60,1.123),scale=1.08)
stage("StormLantern",(1.95,3.46,1.80),scale=.84)
shelf=Part("Lantern wall bracket",preview)
box(shelf,(1.95,3.49,1.785),(.33,.31,.03),"Iron",.005)
tube(shelf,[(1.95,3.61,1.56),(1.95,3.35,1.77)],.016,"Iron")
shelf.build()
for mat in bpy.data.materials:
    if not mat.name.startswith("HQ_") or not mat.use_nodes:continue
    bs=mat.node_tree.nodes.get("Principled BSDF")
    if bs is None:continue
    slot=mat.name.split(".")[0].removeprefix("HQ_")
    if slot=="Map":
        paper=mat.node_tree.nodes.new("ShaderNodeTexImage")
        paper.image=bpy.data.images.load(str(ROOT/"Assets/Osowiec/Art/SurveySheet.png"),check_existing=True)
        mat.node_tree.links.new(paper.outputs["Color"],bs.inputs["Base Color"])
    if slot in ("Flame","Glass"):
        bs.inputs["Emission Color"].default_value=(1,.47,.12,1)
        bs.inputs["Emission Strength"].default_value=5.5 if slot=="Flame" else .42

def area(name, pos, target, power, color, size):
    lamp=bpy.data.lights.new(name,"AREA");lamp.energy=power;lamp.color=color;lamp.shape="DISK";lamp.size=size
    obj=bpy.data.objects.new(name,lamp);preview.objects.link(obj);obj.location=pos
    obj.rotation_euler=(Vector(target)-obj.location).to_track_quat("-Z","Y").to_euler()

area("Cold overcast at the stair",(0,-7.77,3.09),(0,-2.0,.35),350,(.65,.77,1),2.05)
area("Threshold bounce",(-.7,-1.99,2.14),(0,1.65,1.13),68,(.76,.81,1),2.35)
area("Kerosene over the map",(.84,1.09,2.32),(0,1.49,1.11),145,(1,.65,.35),1.3)
area("Lamp at the music stand",(1.89,3.17,2.24),(1.95,3.64,1.79),50,(1,.56,.26),.68)
area("Cartographic board wash",(-1.21,2.03,2.53),(-.1,3.5,1.68),100,(1,.70,.44),1.3)
world = bpy.data.worlds.new("Cloudy exterior");world.use_nodes=True
world.node_tree.nodes["Background"].inputs[0].default_value=(.29,.37,.48,1)
world.node_tree.nodes["Background"].inputs[1].default_value=.26;scene.world=world
for name,pos,aim,lens in [
        ("First person | threshold",(0,-2.92,1.63),(0,1.12,1.40),24),
        ("First person | upper landing",(0,-8.07,2.53),(0,-.45,1.17),27),
        ("First person | map and telephone",(-.79,-.51,1.62),(.14,1.19,1.08),29)]:
    camera=bpy.data.cameras.new(name);camera.lens=lens
    obj=bpy.data.objects.new(name,camera);preview.objects.link(obj)
    obj.location=pos;obj.rotation_euler=(Vector(aim)-obj.location).to_track_quat("-Z","Y").to_euler()
    if name.endswith("threshold"):scene.camera=obj
scene.render.resolution_x=1280;scene.render.resolution_y=720;scene.render.resolution_percentage=100
scene.render.engine="CYCLES";scene.cycles.samples=16;scene.cycles.use_denoising=True
for screen in bpy.data.screens:
    for area_ in screen.areas:
        if area_.type=="VIEW_3D":
            area_.spaces.active.region_3d.view_perspective="CAMERA"
            area_.spaces.active.shading.color_type="MATERIAL"
            area_.spaces.active.overlay.show_overlays=False
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/"CommandBunker.blend"),compress=True)
print("BUNKER_OK",len(shell),"export nodes",sum(len(o.data.polygons) for o in shell if o.type=="MESH"),"surface polygons",
      "entry 2.40m / 6x 0.15m / room crown 3.42m",flush=True)
