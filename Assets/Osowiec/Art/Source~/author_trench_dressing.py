"""Open Osowiec_FortI_1916.blend and run in Blender. Survey units are metres, Z up.

Outputs a standalone linked asset library and an independent, collision-free FBX.
The cutaway fort, door hinge and underlying walkable deck remain in the base file.
"""
from collections import defaultdict
from math import cos, sin, pi, floor, atan2
from pathlib import Path
import random

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree


source = Path(__file__).resolve().parent
art = source.parent / "Fort1916"
rng = random.Random(561916)
collection = bpy.data.collections.new("Fort I · local repairs")
bpy.context.scene.collection.children.link(collection)
tau = 2 * pi
Z = Vector((0, 0, 1))


def map_material(name):
    material = bpy.data.materials.get(name)
    if material is not None:
        return material
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    pbr = nodes.get("Principled BSDF")
    for suffix, colour in [("_colour.jpg", True), ("_normal.jpg", False)]:
        path = art / "Surfaces" / (name + suffix)
        texture = nodes.new("ShaderNodeTexImage")
        texture.image = bpy.data.images.load(str(path), check_existing=True)
        texture.image.filepath = bpy.path.relpath(str(path))
        texture.image.colorspace_settings.name = "sRGB" if colour else "Non-Color"
        if colour:
            links.new(texture.outputs["Color"], pbr.inputs["Base Color"])
        else:
            normal = nodes.new("ShaderNodeNormalMap")
            normal.inputs["Strength"].default_value = .85
            links.new(texture.outputs["Color"], normal.inputs["Color"])
            links.new(normal.outputs[0], pbr.inputs["Normal"])
    pbr.inputs["Roughness"].default_value = .82
    return material


class Batch:
    def __init__(self):
        self.vertices, self.faces, self.shading = [], [], []

    def add(self, material, vertices, faces, smooth=False):
        offset = len(self.vertices)
        self.vertices.extend(tuple(v) for v in vertices)
        self.faces.extend(tuple(offset + i for i in face) for face in faces)
        self.shading.extend([(material, smooth)] * len(faces))


batches = defaultdict(Batch)
soil_objects = [obj for obj in bpy.context.scene.objects if obj.name.startswith("Ground_")]
if not soil_objects:
    raise RuntimeError("Open the survey scene before running this script.")
deps = bpy.context.evaluated_depsgraph_get()
ground = [(obj.bound_box[0][0], obj.bound_box[6][0], obj.bound_box[0][1], obj.bound_box[6][1],
           BVHTree.FromObject(obj, deps)) for obj in soil_objects]


def soil(x, y):
    for left, right, bottom, top, tree in ground:
        if left - .001 <= x <= right + .001 and bottom - .001 <= y <= top + .001:
            point = tree.ray_cast((x, y, 15), (0, 0, -1), 30)[0]
            if point is not None:
                return point.z
    raise ValueError(f"Point outside survey: {(x,y)}")


def tube(g, m, a, b, radius, tip=None, sides=8):
    a, b = Vector(a), Vector(b)
    axis = (b - a).normalized()
    right = axis.cross(Z if abs(axis.z) < .9 else Vector((0, 1, 0))).normalized()
    up = axis.cross(right)
    verts = [p + r * (cos(i * tau / sides) * right + sin(i * tau / sides) * up)
             for p, r in [(a, radius), (b, radius if tip is None else tip)] for i in range(sides)]
    faces = [tuple(reversed(range(sides))), tuple(range(sides, 2 * sides))]
    faces.extend((i, (i + 1) % sides, sides + (i + 1) % sides, sides + i) for i in range(sides))
    batches[g].add(m, verts, faces)


def cord(g, m, points, radius, sides=6):
    for a, b in zip(points, points[1:]):
        tube(g, m, a, b, radius, sides=sides)


def plank(g, m, centre, axis, side, length, width, thickness, damaged=False):
    # Cut a chamfer into each corner; a broken end uses an asymmetric grain-aligned split.
    x, y = length / 2, width / 2
    c = min(.024, width * .1)
    end = [(x-c,-y),(x,-y+c),(x,y-c),(x-c,y)]
    if damaged and m == "Brick core":
        end = [(x-c,-y),(x,-y+c),(x,y*.13),(x-c*.92,y*.51),(x-3.1*c,y)]
    elif damaged:
        end = [(x,-y),(x-.11*length,-y*.62),(x-.038*length,-y*.50),
               (x-.28*length,-y*.21),(x-.046*length,y*.08),(x-.21*length,y*.27),
               (x-.12*length,y*.49),(x-.22*length,y*.64),(x-.17*length,y)]
    outline = [(-x+c,-y)] + end + [(-x+c,y),(-x,y-c),(-x,-y+c)]
    centre, axis, side = Vector(centre), Vector(axis).normalized(), Vector(side).normalized()
    normal = axis.cross(side).normalized()
    n = len(outline)
    verts = [centre + axis*a + side*b + normal*z for z in (-thickness/2,thickness/2) for a,b in outline]
    faces = [tuple(reversed(range(n))),tuple(range(n,2*n))]
    faces += [(i,(i+1)%n,n+(i+1)%n,n+i) for i in range(n)]
    batches[g].add(m, verts, faces)


def blob(g, m, centre, radii, seed, rows=6, sides=10, angular=False, turn=0):
    local = random.Random(seed)
    p, (rx,ry,rz) = Vector(centre), radii
    vertices = [p + Vector((0,0,-rz))]
    for j in range(1,rows):
        lat = j*pi/rows-pi/2
        for i in range(sides):
            ang=tau*i/sides
            distortion=local.uniform(.78,1.19) if angular else local.uniform(.93,1.04)
            x,y=rx*cos(lat)*cos(ang)*distortion, ry*cos(lat)*sin(ang)*distortion
            vertices.append(p+Vector((x*cos(turn)-y*sin(turn),x*sin(turn)+y*cos(turn),rz*sin(lat)*distortion)))
    top=len(vertices);vertices.append(p+Vector((0,0,rz)))
    faces=[]
    for i in range(sides):
        j=(i+1)%sides
        faces.append((0,1+j,1+i))
        for k in range(rows-2):
            a=1+k*sides
            faces.append((a+i,a+j,a+sides+j,a+sides+i))
        a=top-sides
        faces.append((a+i,a+j,top))
    batches[g].add(m,vertices,faces,not angular)


def sack(g,centre,angle,seed):
    # Top seam, scalloped corners and gathered ears distinguish stitched sacks from rocks.
    x,y,z=centre; longitudinal=Vector((cos(angle),sin(angle),0)); lateral=Z.cross(longitudinal)
    c=Vector(centre); verts=[]; faces=[]; segments=16
    for along,w,h,drop in [(-.48,.025,.03,-.045),(-.39,.188,.115,-.018),(-.28,.211,.13,.014),
                            (.02,.225,.153,.023),(.29,.206,.132,0),(.41,.163,.095,-.017),(.49,.027,.032,-.04)]:
        for i in range(segments):
            a=i*tau/segments
            side=(abs(cos(a))**.76)*(1 if cos(a)>=0 else -1)
            up=(abs(sin(a))**.78)*(1 if sin(a)>=0 else -1)
            wrinkle=1+.062*cos(a*4+along*28+seed)
            verts.append(c+longitudinal*along+lateral*(side*w*wrinkle)+Z*(up*h+drop))
    for j in range(6):
        for i in range(segments):
            q=j*segments+i; k=j*segments+(i+1)%segments
            faces.append((q,k,k+segments,q+segments))
    faces.extend([tuple(reversed(range(segments))),tuple(range(6*segments,7*segments))])
    batches[g].add("Repair flax",verts,faces,True)
    points=[c+longitudinal*t+lateral*.093+Z*(.145-.36*abs(t)**2+.008*cos(15*t+seed)) for t in (-.34,-.23,-.1,.04,.17,.28,.35)]
    cord(g,"Canvas",points,.009,5)
    for direction in (-1,1):
        tie=c+longitudinal*(direction*.455)-Z*.025
        tube(g,"Canvas",tie-lateral*.047,tie+lateral*.047,.014,sides=6)


def ground_seam(g, a, b, offset, side, seed):
    local=random.Random(seed)
    a,b=Vector(a),Vector(b); span=b-a; length=span.length; forward=span/length; lateral=Vector((-forward.y,forward.x))
    # Exposed bands of loose damp clay cling to the foot of the timbered banks.
    for i in range(int(length/.16)):
        dist=(i+local.random())*.16
        density=.53+.36*sin(dist*.89+seed)+.17*cos(dist*1.55-seed)
        if local.random()>density:
            continue
        p=a+dist*forward+(offset+local.uniform(-.24,.19))*side*lateral
        h=soil(*p); scale=local.uniform(.084,.33)*(1+.24*cos(dist*1.5))
        blob(g,"Earth",(*p,h+.008),(scale,scale*local.uniform(.45,.80),scale*local.uniform(.19,.39)),i+seed,4,9,True)
        if i%4==0:
            q=p-side*lateral*local.uniform(.14,.68)
            blob(g,"Concrete" if i%12==0 else "Earth",(*q,soil(*q)+.019),(.085,.043,.037),seed-i,4,6,True)
        if i%6==0:
            q=p+side*lateral*.22
            points=[(*q,soil(*q)+.033),(*(q+forward*.19),soil(*(q+forward*.19))+.07),
                    (*(q+forward*.32-lateral*.08),soil(*(q+forward*.32-lateral*.08))+.015)]
            cord(g,"Bark",points,.015)
        if i%7==0:
            # Silt tongues and embedded fragments break up the long reflective channel beside the boards.
            q=a+dist*forward+local.uniform(.98,1.19)*side*lateral
            blob(g,"Earth",(*q,.017),(local.uniform(.11,.42),local.uniform(.07,.18),.028),
                 i+5*seed,4,10,turn=atan2(forward.y,forward.x))
            q2=q+forward*.17
            blob(g,"Earth",(*q2,.025),(.054,.036,.025),i+8,4,8)


def start_work():
    g="Start_bank_repairs"
    for idx,(a,b) in enumerate([((-46.8,-16),(-34,-16)),((-34,-16),(-34,-9)),
                                 ((-34,-9),(-25,-9)),((-25,-9),(-25,-3)),((-25,-3),(-16,-3))]):
        for s in (-1,1):
            ground_seam(g,a,b,2.38,s,191+idx*73+s)
    # Plank noses overlap the old deck only at existing slats. Replace worn edge pieces and nail heads.
    for x in [-45.8,-45.17,-44.27,-43.65,-42.50,-41.29,-40.65,-39.82,-38.86,-36.15]:
        side=rng.choice((-1,1));dy=side*(.71+rng.uniform(-.04,.12))
        p=(x,-16+dy,.111)
        plank(g,"Timber",p,(0,side,0),(-side,0,0),.46,.14,.025,True)
        for sx in (-.044,.044):
            tube(g,"Oxide",(x+sx,-16+side*.60,.11),(x+sx,-16+side*.60,.129),.012,sides=8)
    for i in range(8):
        x=-40.6+.68*i;y=-18.82+.13*sin(i*2.4)
        h=soil(x,y)
        sack(g,(x,y,h+.105),rng.uniform(-.28,.19),i)
        if i in (1,2,3,4,5):
            sack(g,(x+.28,y+.025,h+.32),.18*sin(i),91+i)
    # Revetment on the inner bend has been patched with sister timbers and structural brackets.
    for i in range(3):
        x=-38.65+i*1.14;y=-18.53
        a=Vector((x-.23,y,.03));b=Vector((x+.22,y-.12,1.08+i*.08))
        plank(g,"Timber",(a+b)/2,b-a,(0,1,0),(b-a).length,.12,.129,True)
        for j in (.21,.75):
            p=a.lerp(b,j)
            tube(g,"Oxide",p+Vector((0,.045,0)),p+Vector((0,.098,0)),.034,sides=9)
    for i in range(5):
        x,y=-35.67+i*.12,-18.0
        plank(g,"Bark",(x,y,.12+i*.115),(.1,1,.03),(1,-.1,0),1.52-rng.random()*.31,.16,.08,True)


def reel(g, centre, radius=.57):
    # Two annular timber flanges, a dark helix of cable and peened iron axle.
    c=Vector(centre); ax=Vector((.45,.893,0)).normalized(); side=Z.cross(ax)
    tube(g,"Timber",c-ax*.24,c+ax*.24,radius*.56,sides=36)
    for sign in (-1,1):
        p=c+ax*.259*sign
        for i in range(16):
            a=i*tau/16+.012;b=(i+1)*tau/16-.012
            ring=[]
            for sense in (-1,1):
                for r,t in [(radius*.18,a),(radius,a),(radius,b),(radius*.18,b)]:
                    ring.append(p+ax*.027*sense+side*(r*cos(t))+Z*(r*sin(t)))
            batches[g].add("Timber",ring,[(3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)])
        tube(g,"Oxide",p-ax*.04,p+ax*.04,radius*.19,sides=20)
        for j in range(6):
            a=j*tau/6
            q=p+side*.34*cos(a)+Z*.34*sin(a)
            tube(g,"Oxide",q-ax*.038,q+ax*.038,.022,sides=7)
    line=[]
    for i in range(11*24+1):
        a=i*tau/24
        line.append(c+ax*(-.237+.474*i/(11*24))+side*(radius*.69*cos(a))+Z*(radius*.69*sin(a)))
    cord(g,"Tar",line,.024,6)
    tube(g,"Oxide",c-ax*.37,c+ax*.37,.062,sides=14)


def station_work():
    g="Signal_party_workbench"
    # West-facing fascia for the approach along the duckboard walk.
    plank(g,"Timber",(-15.545,-4.80,1.79),(0,1,0),(0,0,1),1.20,.205,.044)
    for y in (-5.35,-4.25):
        for z in (1.732,1.848):
            tube(g,"Oxide",(-15.561,y,z),(-15.576,y,z),.012,sides=7)
    for i in range(5):
        x,y=-18.92+i*.54,-5.3-.11*sin(i)
        sack(g,(x,y,soil(x,y)+.12),-.2+rng.uniform(-.22,.19),103+i)
    x,y=-17.75,-4.96; z=soil(x,y)
    reel(g,(x,y,z+.55))
    # Sagging cable between the reel trolley and cups on the field shelter; stays along the bank.
    for i in range(2):
        cable=[]
        for j in range(22):
            t=j/21
            cable.append((-17.65+2.2*t,-4.81+.08*cos(t*pi)+i*.049,z+.51+1.06*t-.42*sin(pi*t)))
        cord(g,"Tar",cable,.014,7)
    for x in (-16.41,-15.44):
        z=soil(x,-5.06)
        tube(g,"Bark",(x,-5.12,z-.05),(x-.03,-5.1,1.96),.064,.044,8)
        for h in (1.62,1.76):
            tube(g,"Oxide",(x,-5.12,h),(x,-4.95,h),.022,sides=7)
            for t,r in [(0,.039),(.026,.061),(.049,.035)]:
                tube(g,"Concrete",(x,-4.95,h+t),(x,-4.95,h+t+.021),r,sides=12)
    # Heavy patched canvas over the telephone party's work area, opening toward the path.
    x0,x1=-18.98,-16.97;y0,y1=-5.66,-4.68
    for x in [x0,x1]:
        tube(g,"Bark",(x,y1,soil(x,y1)-.05),(x+.025,y1,1.64),.042,.031,8)
    cols,rows=19,11; v=[];f=[]
    for j in range(rows):
        t=j/(rows-1)
        for i in range(cols):
            u=i/(cols-1)
            z=2.01-.41*t-.20*sin(pi*u)*sin(pi*t*.75)+.043*sin(5*pi*u+.62*t)*(.25+t)
            if j==rows-1:z-=.04+.047*(.5+.5*sin(u*10*pi+1))
            v.append((x0+u*(x1-x0),y0+t*(y1-y0),z))
            if i and j:
                k=j*cols+i; f.append((k-cols-1,k-cols,k,k-1))
    batches[g].add("Repair flax",v,f,True)
    for border in (v[:cols],v[-cols:],v[::cols],v[cols-1::cols]):
        cord(g,"Canvas",border,.014)
    for t in (.22,.34,.73):
        q=[v[round(t*(cols-1))+j*cols] for j in range(rows)]
        cord(g,"Canvas",q,.010,5)


def portal_work():
    g="Engineers_portal_masonry"
    # The west face is at x=-10.305. A stepped patch shows exposed cores under the smooth cap.
    for s in (-1,1):
        for row in range(12):
            outer=4.12-.075*row
            inner=2.12+.085*row+.12*sin(row*.82+s)
            offset=(row%2)*.145
            n=0
            while inner+offset+n*.304<outer:
                u=inner+offset+n*.304
                y=-12+s*u; z=.13+row*.175
                if rng.random()<.13:n+=1;continue
                depth=rng.uniform(.029,.057);width=rng.uniform(.251,.292)
                p=(-10.312-depth*.5,y,z+rng.uniform(-.008,.008))
                plank(g,"Brick core",p,(0,1,0),(0,0,1),width,.145,depth,damaged=(n==0 and row%3==1))
                n+=1
        for i in range(75):
            x=-10.48-rng.random()**1.7*1.75
            y=-12+s*rng.uniform(2.03,3.55)
            h=soil(x,y)
            size=rng.uniform(.038,.22)
            blob(g,"Brick core" if i%4 else "Concrete",(x,y,h+size*.13),
                 (size,size*rng.uniform(.41,.71),size*rng.uniform(.23,.59)),1237+i+s,4,7,True,rng.random()*pi)
    # Slender water pipe and fittings on the left shoulder of the portal, lifted from the masonry.
    y=-9.5;x=-10.47
    for lo,hi in [(.22,.93),(.94,1.86),(1.87,2.76)]:
        tube(g,"Oxide",(x,y,lo),(x,y,hi),.058,sides=16)
        tube(g,"Oxide",(x,y,lo+.025),(x,y,lo+.108),.078,sides=16)
    curve=[(x,y,.26),(x-.035,y,.195),(x-.10,y,.139),(x-.24,y,.138)]
    cord(g,"Oxide",curve,.061,14)
    for h in (.63,1.63,2.53):
        tube(g,"Oxide",(-10.315,y,h),(x,y,h),.025,sides=8)
        tube(g,"Oxide",(x,y,h-.025),(x,y,h+.025),.077,sides=16)
    # Board repairs beside the entry, not across the floor or the arch.
    for j in range(3):
        y=-14.32-j*.187
        plank(g,"Timber",(-10.98,y,soil(-10.98,y)+.078),(1,.11,0),(-.11,1,0),
              1.19-j*.12,.139,.084,damaged=j!=1)

def close_sod_ends():
    # The original roof survey is an upward-facing, single shell. Join the exposed
    # end profiles to the real stone arch so the landscape reads as earth from below.
    roof=bpy.data.objects["Roof_sod"]
    survey=[roof.matrix_world@v.co for v in roof.data.vertices]
    name="Turf_section_over_vault"
    for side,x in [(-1,min(p.x for p in survey)),(1,max(p.x for p in survey))]:
        section=sorted((p.y,p.z) for p in survey if abs(p.x-x)<.01)
        samples=[]
        for index,((ay,az),(by,bz)) in enumerate(zip(section,section[1:])):
            count=max(2,round((by-ay)/.105))
            for k in range(count+(index==len(section)-2)):
                t=k/count
                y=ay+(by-ay)*t
                z=az+(bz-az)*t
                dy=abs(y+12)
                bottom=soil(x+side*.22,y)-.07
                if dy<1.82:
                    bottom=max(bottom,1.84+(1.82**2-dy**2)**.5)
                elif dy<2.10:
                    bottom=max(bottom,1.84*(1-(dy-1.82)/.28))
                bottom=min(z-.025,bottom)
                span=z-bottom
                lip=.19+.044*sin(y*3.4)
                samples.append([(x,y,z),
                    (x+side*lip,y,z-min(.065,span*.11)),
                    (x+side*(lip+.061),y,z-min(.228,span*.36)),
                    (x+side*(lip+.017),y,bottom+span*.39),
                    (x+side*(lip-.017),y,bottom)])
        for left,right in zip(samples,samples[1:]):
            for k,material in enumerate(("Sod","Sod","Earth","Earth")):
                verts=[left[k],right[k],right[k+1],left[k+1]]
                face=(0,1,2,3) if side<0 else (3,2,1,0)
                batches[name].add(material,verts,[face])


def publish():
    scales={"Earth":1.8,"Brick core":.44,"Canvas":1.1,"Repair flax":.56,"Bark":1.55,
            "Concrete":2.6,"Timber":1.62,"Oxide":1.4,"Tar":.65,"Sod":2.9}
    objects=[]
    for name,batch in batches.items():
        mesh=bpy.data.meshes.new(name);mesh.from_pydata(batch.vertices,[],batch.faces);mesh.update()
        obj=bpy.data.objects.new(name,mesh);collection.objects.link(obj);objects.append(obj)
        names=list(dict.fromkeys(m for m,_ in batch.shading))
        for m in names:mesh.materials.append(map_material(m))
        uv=mesh.uv_layers.new(name="Metric")
        packed=[]
        for p,(m,smooth) in zip(mesh.polygons,batch.shading):
            p.material_index=names.index(m);p.use_smooth=smooth
            nx,ny,nz=map(abs,p.normal); step=scales[m]
            for index in p.vertices:
                x,y,z=batch.vertices[index]
                if nz>=max(nx,ny):a,b=x,y
                elif nx>ny:a,b=y,z
                else:a,b=x,z
                packed.extend((a/step,b/step))
        uv.data.foreach_set("uv",packed)
    # A small editable scene opens directly, with texture references relative to the blend.
    edit=bpy.data.scenes.new("Trench repairs and west portal")
    edit.collection.children.link(collection)
    bpy_path=source/"Osowiec_TrenchDressing.blend"
    bpy.data.libraries.write(str(bpy_path),{edit},path_remap="RELATIVE_ALL",fake_user=True,compress=True)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.export_scene.fbx(filepath=str(art/"TrenchDressing.fbx"),axis_forward="-Z",axis_up="Y",
        apply_scale_options="FBX_SCALE_UNITS",use_selection=True,bake_anim=False,mesh_smooth_type="FACE",
        object_types={"MESH"},use_custom_props=False,path_mode="STRIP")
    print("TRENCH_DRESSING",len(objects),"mesh batches,",sum(len(b.faces) for b in batches.values()),"faces")


start_work()
station_work()
portal_work()
close_sod_ends()
publish()
