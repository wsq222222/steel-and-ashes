"""Build original commissioned Osowiec cast, generic deformation rigs, actions and first-person assets.
Run Blender in background with --python this_file. -- portraits renders the casting sheet only.
Blender coordinates: X right, -Y forward, Z up. Dimensions in metres.
"""
from pathlib import Path
import sys, math
import bpy
from mathutils import Vector
from math import cos, sin, pi, exp, sqrt, radians

SRC = Path(__file__).resolve().parent
sys.path.insert(0,str(SRC))
import field_tailoring
import field_gait
OUT = SRC.parent / "Cast"
OUT.mkdir(exist_ok=True)
TEX = OUT / "Textures"
ROLES = ["Assault", "Scout", "Support", "Sentry"]
WOOL = ["Wool", "RifleWool", "HeavyWool", "SlateWool"]

def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0

def material(name):
    old = bpy.data.materials.get(name)
    if old: return old
    m = bpy.data.materials.new(name)
    m.use_nodes=True
    bs=m.node_tree.nodes.get("Principled BSDF")
    im=bpy.data.images.load(str(TEX/(name+".png")),check_existing=True)
    node=m.node_tree.nodes.new("ShaderNodeTexImage"); node.image=im
    m.node_tree.links.new(node.outputs["Color"],bs.inputs["Base Color"])
    normal=TEX/(name+"_N.png")
    if normal.exists():
        node=m.node_tree.nodes.new("ShaderNodeTexImage")
        node.image=bpy.data.images.load(str(normal),check_existing=True); node.image.colorspace_settings.name="Non-Color"
        nm=m.node_tree.nodes.new("ShaderNodeNormalMap"); nm.inputs["Strength"].default_value=.48
        m.node_tree.links.new(node.outputs["Color"],nm.inputs["Color"]); m.node_tree.links.new(nm.outputs["Normal"],bs.inputs["Normal"])
    bs.inputs["Roughness"].default_value=.25 if name=="Lens" else .57 if name in ("Steel","BlueSteel","Brass","FieldPaint") else .80
    bs.inputs["Metallic"].default_value=.62 if name in ("Steel","BlueSteel","Brass") else .26 if name in ("FieldPaint","Lens") else 0
    return m

class Mesh:
    def __init__(self):
        self.v=[]; self.faces=[]; self.mats=[]; self.ids=[]; self.skin=[]; self.uv={}
    def vertex(self,co,bone):
        self.v.append(tuple(co)); self.skin.append(bone); return len(self.v)-1
    def face(self,verts,mat):
        self.faces.append(tuple(verts))
        if mat not in self.mats: self.mats.append(mat)
        self.ids.append(self.mats.index(mat))
    def grid(self,rings,mat,cap=True):
        n=len(rings[0])
        for lo,hi in zip(rings,rings[1:]):
            for k in range(n):
                j=(k+1)%n
                self.face((lo[k],lo[j],hi[j],hi[k]),mat)
        if cap:
            self.face(list(reversed(rings[0])),mat); self.face(rings[-1],mat)
    def loft(self,levels,mat,bone,n=32,fold=0):
        rr=[]
        for i,(x,y,z,rx,ry) in enumerate(levels):
            rr.append([])
            for k in range(n):
                a=2*pi*k/n
                w=1+fold*(.60*cos(9*a+.24*i)+.35*cos(15*a-.18*i)+.16*sin(5*a+i*.14))
                rr[-1].append(self.vertex((x+rx*w*cos(a),y+ry*w*sin(a),z),bone))
        self.grid(rr,mat)
    def oval(self,c,r,mat,bone,n=20,h=12):
        x,y,z=c; a,b,d=r
        levels=[]
        for i in range(h+1):
            ang=-pi/2+pi*(.005+.99*i/h)
            levels.append((x,y,z+d*sin(ang),a*cos(ang),b*cos(ang)))
        self.loft(levels,mat,bone,n)
    def tube(self,pts,radii,mat,bone,n=12,fold=0):
        ring=[]; prev=None
        for k,(p,rad) in enumerate(zip(pts,radii)):
            p=Vector(p)
            t=(Vector(pts[min(len(pts)-1,k+1)])-Vector(pts[max(0,k-1)])).normalized()
            guide=Vector((1,0,0)) if abs(t.x)<.92 else Vector((0,1,0))
            u=(guide-t*t.dot(guide)).normalized() if prev is None else (prev-t*t.dot(prev)).normalized()
            if u.length<.5: u=(guide-t*t.dot(guide)).normalized()
            prev=u
            v=t.cross(u)
            rx,ry=(rad,rad) if isinstance(rad,(float,int)) else rad
            ring.append([])
            for j in range(n):
                a=2*pi*j/n; f=1+fold*cos(a*5+k*.42)
                ring[-1].append(self.vertex(p+(rx*cos(a)*u+ry*sin(a)*v)*f,bone))
        self.grid(ring,mat)
    def box(self,c,dim,mat,bone,bevel=.1):
        x,y,z=c; a,b,h=(v*.5 for v in dim)
        s=min(a,b,h)*bevel; rr=[]
        for height,inset in [(z-h,s),(z-h+s,0),(z+h-s,0),(z+h,s)]:
            A,B=a-inset,b-inset; cham=min(A,B)*.18
            outline=[(-A+cham,-B),(A-cham,-B),(A,-B+cham),(A,B-cham),(A-cham,B),(-A+cham,B),(-A,B-cham),(-A,-B+cham)]
            rr.append([self.vertex((x+xx,y+yy,height),bone) for xx,yy in outline])
        self.grid(rr,mat)
    def panel(self,pts,mat,bone):
        ids=[self.vertex(p,bone) for p in pts]
        self.face(ids,mat)
        self.face(tuple(reversed(ids)),mat)
    def finish(self,name):
        data=bpy.data.meshes.new(name); data.from_pydata(self.v,[],self.faces); data.update()
        ob=bpy.data.objects.new(name,data); bpy.context.collection.objects.link(ob)
        for mat in self.mats: data.materials.append(material(mat))
        uv=data.uv_layers.new(name="TailoredUV")
        groups={}
        for i,b in enumerate(self.skin):
            if b:
                influences=((b,1.0),) if isinstance(b,str) else b
                for name,weight in influences:
                    if name not in groups: groups[name]=ob.vertex_groups.new(name=name)
                    groups[name].add([i],weight,"REPLACE")
        for f,mi in zip(data.polygons,self.ids):
            f.material_index=mi; f.use_smooth=len(f.vertices)<=4
            n=f.normal; choose=max(range(3),key=lambda i:abs(n[i]))
            for li in f.loop_indices:
                vi=data.loops[li].vertex_index; x,y,z=self.v[vi]
                coord=(y,z) if choose==0 else (x,z) if choose==1 else (x,y)
                uv.data[li].uv=self.uv.get(vi,(coord[0]*3,coord[1]*3))
        if getattr(self,"helmet_vertices",None):
            ob.vertex_groups.new(name="DetachableShell").add(list(self.helmet_vertices),1.0,"REPLACE")
        return ob

def separate_helmet(actor):
    """Keep the authored shell and its rivets on the same animated skeleton."""
    import bmesh
    group=actor.vertex_groups.get("DetachableShell")
    if not group: return []
    group_index=group.index
    shell=actor.copy(); shell.data=actor.data.copy(); shell.name="IssuedHelmet"
    bpy.context.collection.objects.link(shell)
    for obj,keep_shell in [(actor,False),(shell,True)]:
        remove=[v.index for v in obj.data.vertices if any(g.group==group_index for g in v.groups)!=keep_shell]
        bm=bmesh.new(); bm.from_mesh(obj.data); bm.verts.ensure_lookup_table()
        bmesh.ops.delete(bm,geom=[bm.verts[i] for i in remove],context="VERTS")
        bm.to_mesh(obj.data); bm.free(); obj.data.update()
        obj.vertex_groups.remove(obj.vertex_groups["DetachableShell"])
    assert len(shell.data.vertices)>500 and len(shell.data.vertices)<2000
    return [shell]

def rig_spec():
    b={"Pelvis":((0,.015,.87),(0,.018,1.05),None),"Chest":((0,.018,1.05),(0,.01,1.45),"Pelvis"),
       "Neck":((0,.012,1.45),(0,.008,1.585),"Chest"), "Head":((0,.008,1.585),(0,0,1.805),"Neck")}
    for side,s in [("L",-1),("R",1)]:
        sh=(s*.205,0,1.415); el=(s*.275,-.045 if s==1 else -.17,1.16)
        hand=(.115,-.245,1.22) if s==1 else (.09,-.49,1.236)
        hip=(s*.102,.015,.92); knee=(s*.115,-.026,.53); foot=(s*.118,.02,.15)
        b.update({side+"Arm":(sh,el,"Chest"),side+"Forearm":(el,hand,side+"Arm"),side+"Hand":(hand,(hand[0],hand[1]-.065,hand[2]+.01),side+"Forearm"),
                  side+"Thigh":(hip,knee,"Pelvis"),side+"Shin":(knee,foot,side+"Thigh"),side+"Foot":(foot,(s*.118,-.18,.09),side+"Shin")})
    return b

def lerp(a,b,t): return Vector(a).lerp(Vector(b),t)

def body(role):
    m=Mesh(); wool=WOOL[role]; b=rig_spec()
    armholes=field_tailoring.coat(m,role,wool)
    for s in [-1,1]:
        m.panel([(s*.071,-.041,1.465),(s*.129,-.091,1.408),(s*.112,-.117,1.34),(s*.018,-.133,1.26)],"Canvas" if role==1 else wool,"Chest")
        m.tube([(s*.071,-.043,1.466),(s*.130,-.093,1.409),(s*.113,-.119,1.34),(s*.018,-.136,1.26)],[.004,.004,.003,.0015],"Piping","Chest")
        m.box((s*.13,-.113,1.21),(.095,.025,.076),wool,"Chest")
        m.box((s*.13,-.134,1.239),(.103,.013,.026),"Canvas","Chest")
        m.oval((s*.13,-.145,1.24),(.007,.004,.007),"Brass","Chest",12,7)
        # Shoulder webbing traverses the shoulders and rear canvas pack.
        m.tube([(s*.116,.16,1.07),(s*.145,.108,1.30),(s*.133,.060,1.435),(s*.131,.012,1.449),
                (s*.131,-.035,1.441),(s*.132,-.091,1.39),(s*.130,-.105,1.345),(s*.092,-.133,1.067)],
          [(.018,.007),(.020,.007),(.019,.006),(.019,.006),(.019,.006),(.020,.007),(.020,.007),(.020,.007)],"Leather","Chest")
        m.box((s*.135,-.138,1.305),(.054,.014,.042),"Brass","Chest")
        m.box((s*.135,-.148,1.305),(.038,.005,.025),"Leather","Chest")
    m.loft([(0,.01,1.033,.177,.129),(0,.01,1.038,.179,.131),(0,.01,1.087,.176,.13),(0,.01,1.092,.174,.128)],"Leather","Chest",48)
    m.box((0,-.131,1.064),(.066,.017,.048),"Brass","Chest")
    m.box((0,-.143,1.064),(.046,.009,.032),"Leather","Chest")
    for i in range(5):
        z=1.12+i*.06
        m.oval((.025,-(.128 if i<2 else .137),z),(.008,.006,.008),"Brass","Chest",12,7)
    for s in [-1,1]:
        for j in range(2 if s<0 and role==1 else 1):
            x=s*(.085+j*.071); y=-.12+j*.022
            m.box((x,y,1.014),(.065,.06,.098),"Leather","Chest",.26)
            m.box((x,y-.026,1.043),(.067,.025,.044),"Boot","Chest",.25)
            m.oval((x,y-.04,1.029),(.004,.003,.006),"Brass","Chest",10,7)
    if role!=0:
        width=.25 if role==2 else .33
        m.box((0,.159,1.256),(width,.098 if role==2 else .145,.222),"Canvas","Chest",.7)
        m.box((0,.198,1.327),(width,.05,.076),wool,"Chest",.48)
        for s in [-1,1]: m.box((s*.094,.209,1.256),(.024,.012,.18),"Leather","Chest")
    if role in (1,3):
        m.tube([(-.204,.18,1.415),(-.19,.187,1.417),(0,.188,1.417),(.19,.187,1.417),(.204,.18,1.414)],[.051,.062,.065,.062,.051],wool,"Chest",28,.05)
        for s in [-1,1]:
            m.tube([(s*.145,.184,1.417),(s*.16,.184,1.417)],[.067,.067],"Leather","Chest",24)
    if role in (0,1):
        field_tailoring.folded_neckcloth(m,wool)
    else:
        field_tailoring.folded_neckcloth(m,"Canvas" if role==3 else "RifleWool")
    uniform_limbs(m,b,armholes,role,wool)
    head(m,role,uniform=True)
    specialist_kit(m,role)
    field_tailoring.kit(m,role,wool)
    fit_head(m)
    return m.finish("Uniform_and_equipment")

def uniform_limbs(m,b,armholes,role,wool):
    for side,s in [("L",-1),("R",1)]:
        sh,el,_=b[side+"Arm"]; _,hand,_=b[side+"Forearm"]; hip,knee,_=b[side+"Thigh"]; _,foot,_=b[side+"Shin"]
        # Overlap soft sleeves and breeches across joints so skin stays closed in locomotion.
        field_tailoring.sleeve(m,armholes[s],sh,el,side,role,wool)
        m.tube([lerp(el,hand,t) for t in [-.12,.06,.30,.48,.66,.82,1.04]],[(.054,.059),(.066,.068),(.069,.063),(.060,.057),(.059,.056),(.05,.053),(.043,.049)],wool,side+"Forearm",28,.063)
        m.tube([lerp(el,hand,t) for t in [.78,.80,.95,.97]], [.053,.058,.053,.048],wool,side+"Forearm",24)
        glove(m,Vector(hand),s,side+"Hand")
        m.tube([lerp(hip,knee,t) for t in [-.12,.05,.22,.55,.8,.96,1.08]],[.083,.098,.108,.097,.085,.065,.058],wool,side+"Thigh",30,.11)
        m.tube([lerp(knee,foot,t) for t in [-.15,.05,.24,.49,.81,1.06]],[.052,.077,.074,.066,.054,.047],"Canvas",side+"Shin",28,.05)
        field_tailoring.puttee(m,knee,foot,side)
        x=s*.118
        m.loft([(x,-.048,.018,.067,.14),(x,-.05,.033,.072,.151),(x,-.054,.055,.071,.15),(x,-.052,.089,.068,.144),(x,-.023,.116,.061,.108),(x,.017,.14,.055,.067),(x,.018,.187,.051,.058)],"Boot",side+"Foot",32)
        m.loft([(x,-.055,.013,.071,.151),(x,-.055,.031,.073,.153)],"Rubber",side+"Foot",36)
        for j in range(4):
            y=-.095+j*.011; z=.103+j*.006
            m.tube([(x-.026,y,z),(x+.027,y+.013,z+.003)],[.003,.003],"Leather",side+"Foot",8)

def glove(m,c,s,bone,view=False):
    # Individual articulated fingers wrap the wrist around the prop.
    x,y,z=c
    m.oval((x,y-.018,z),(.043,.047,.039),"Leather",bone,22,12)
    for j in range(4):
        dx=(j-1.5)*.015
        p=Vector((x+dx,y-.041,z+.009))
        m.tube([p,p+Vector((.002,-.023,.002)),p+Vector((-.003,-.032,-.014)),p+Vector((-.004,-.025,-.03))],[.009,.009,.008,.006],"Leather",bone,12)
    m.tube([(x-s*.026,y-.004,z+.014),(x-s*.042,y-.019,z+.012),(x-s*.041,y-.040,z-.007)],[.016,.013,.009],"Leather",bone,14)

def fit_head(m):
    # Translate the entire cranium, ear straps, cheek guards and headwear together.
    for i,(p,b) in enumerate(zip(m.v,m.skin)):
        if b=="Head":
            x,y,z=p
            m.v[i]=(x,y-.013,z+.036)


def head(m,role,uniform=False):
    wool=WOOL[role]
    if uniform:
        # Nape rises into the occiput. A pair of shallow sternomastoid ridges runs
        # from behind the jaw to the medial ends of the clavicles.
        rows=[]
        for t,rx,ry,cy in ((0,.081,.069,.013),(.16,.066,.060,.014),(.33,.056,.054,.016),
                            (.52,.054,.052,.016),(.72,.056,.056,.014),(.88,.061,.061,.012),(1,.065,.066,.012)):
            row=[]
            for j in range(56):
                a=2*pi*j/56
                front=cos(a)
                angle=a if a<pi else a-2*pi
                line=.006*exp(-((abs(angle)-(.38+1.62*t))/.23)**2)*sin(pi*t)
                throat=.004*exp(-(angle/.29)**2-((t-.58)/.16)**2)
                z=1.440+.145*t+.029*t*(1-front)*.5
                blend=max(0,(t-.55)/.45)*.63
                bone=(("Neck",1-blend),("Head",blend)) if blend>.0001 else "Neck"
                row.append(m.vertex(((rx+line)*sin(a),cy-(ry+line+throat)*front,z),bone))
            rows.append(row)
        m.grid(rows,"Rubber" if role==3 else "Skin")
    else:
        m.oval((0,.012,1.525),(.069,.07,.075),"Skin","Neck")
    # Explicit front projection over a convex head with sculpted cheek, brow, nose and jaw.
    zlo,zhi=1.505,1.765
    ring=[]
    profile=[(.0,.038,.049),(.08,.061,.072),(.19,.079,.081),(.38,.093,.089),(.56,.100,.096),(.72,.103,.097),(.86,.098,.092),(1,.071,.073)]
    for i in range(39):
        t=i/38; z=zlo+(zhi-zlo)*t
        k=next((j for j in range(len(profile)-1) if t<=profile[j+1][0]),len(profile)-2)
        q=(t-profile[k][0])/(profile[k+1][0]-profile[k][0])
        rx=profile[k][1]*(1-q)+profile[k+1][1]*q; ry=profile[k][2]*(1-q)+profile[k+1][2]*q
        row=[]
        for j in range(80):
            a=2*pi*j/80; x=rx*sin(a); y=.008-ry*cos(a)
            if cos(a)>0:
                g=lambda xx,zz,wx,wz:exp(-((x-xx)/wx)**2-((t-zz)/wz)**2)
                nose=.02*g(0,.47,.019,.17)+.040*g(0,.40,.024,.065)
                lips=.010*g(0,.258,.033,.032)
                cheeks=.010*(g(.060,.41,.031,.11)+g(-.060,.41,.031,.11))
                eyes=-.008*(g(.044,.57,.025,.04)+g(-.044,.57,.025,.04))
                brow=.010*(g(.04,.62,.036,.045)+g(-.04,.62,.036,.045))
                y-=(nose+lips+cheeks+eyes+brow)*cos(a)
            jaw=max(0,1-t/.31)**1.6 if uniform else 0
            y-=.020*jaw*max(0,cos(a))**2
            vi=m.vertex((x,y,z+.025*jaw*(1-cos(a))*.5),"Head"); row.append(vi)
            # Image interval maps the visible facial features to the facial relief.
            m.uv[vi]=(.5+x/.212,.105+.96*t)
        ring.append(row)
    m.face(ring[0][::-1],"Rubber" if role==3 else "Skin")
    for i in range(38):
        for j in range(80):
            k=(j+1)%80; front=cos(2*pi*(j+.5)/80)
            mat=("FaceScout" if role==1 else "FaceAssault") if front>.10 and role!=3 else ("Skin" if role!=3 else "Rubber")
            m.face((ring[i][j],ring[i][k],ring[i+1][k],ring[i+1][j]),mat)
    # The helmet used to hide an open edge. Close the scalp so the bare head can be seen from above.
    crown_material="Rubber" if role==3 else "Skin"
    previous=ring[-1]
    for radius,height in ((.78,.011),(.46,.020),(.17,.024)):
        row=[m.vertex((.071*radius*sin(j*2*pi/80),.008-.073*radius*cos(j*2*pi/80),zhi+height),"Head") for j in range(80)]
        for j in range(80):
            k=(j+1)%80
            m.face((previous[j],previous[k],row[k],row[j]),crown_material)
        previous=row
    m.face(previous,crown_material)
    for s in [-1,1]:
        m.oval((s*.092,.008,1.628),(.019,.028,.038),"Skin" if role!=3 else "Rubber","Head")
        if uniform and role in (0,1):
            field_tailoring.fitted_chinstrap(m,s)
        else:
            m.tube([(s*.104,-.006,1.715),(s*.107,-.013,1.654),(s*.087,-.043,1.56),(s*.05,-.058,1.519),(0,-.061,1.507)],[.008,.008,.007,.007,.006],"Leather","Head",10)
    if role==3:
        m.loft([(0,-.035,1.51,.049,.077),(0,-.034,1.535,.066,.080),(0,-.012,1.586,.089,.105),(0,-.002,1.63,.098,.1),(0,.002,1.676,.099,.086),(0,.005,1.71,.09,.088)],"Rubber","Head",48,.027)
        for s in [-1,1]:
            p=Vector((s*.047,-.093,1.665)); axis=Vector((s*.2,-.96,.13)).normalized()
            for a,b,rr,mat in [(-.004,.010,.043,"Leather"),(.007,.017,.042,"Brass"),(.016,.021,.036,"Lens")]:
                m.tube([p+axis*a,p+axis*b],[rr,rr*.98],mat,"Head",40)
            m.tube([(s*.083,-.070,1.672),(s*.092,-.02,1.69),(s*.095,.047,1.67)],[.009,.01,.008],"Canvas","Head",12)
        if role==2:
            pts=[(0,-.112,1.581),(.001,-.157,1.555),(.009,-.158,1.494),(.043,-.167,1.405),(.08,-.162,1.33)]
            for a,b in zip(pts,pts[1:]):
                m.tube([a,b],[.026,.026],"Rubber","Chest",22)
                for k in range(4):
                    p=lerp(a,b,k/4); q=lerp(a,b,(k+.55)/4)
                    m.tube([p,q],[.030,.030],"Leather","Chest",18)
            m.box((.088,-.157,1.274),(.126,.079,.112),"FieldPaint","Chest",.6)
        else:
            p=Vector((0,-.105,1.578)); axis=Vector((0,-.90,-.40)).normalized()
            for a,b,r,mat in [(0,.019,.035,"Brass"),(.016,.069,.045,"FieldPaint"),(.066,.072,.040,"BlueSteel")]:
                m.tube([p+axis*a,p+axis*b],[r,r],mat,"Head",28)
    shell_begin=len(m.v)
    # A spun head shell and thin turned brim. Each pattern defines a genuinely different crown.
    steel=("BlueSteel","FieldPaint","Steel","SlateWool")[role] if role!=3 else "BlueSteel"
    brim=[.128,.165,.13,.14][role]; deep=[.143,.168,.148,.166][role]
    if role==2:
        brim=.113
        deep=.127
        steel="RifleWool"
    rr=[]
    for j,(fac,z) in enumerate([(1,1.686),(1.01,1.69),(.975,1.697),(.88 if role!=1 else .70,1.716),(.83 if role!=1 else .67,1.76),(.64 if role!=1 else .53,1.798),(.42 if role!=1 else .39,1.823),(.08,1.84)]):
        row=[]
        for k in range(64):
            a=k*2*pi/64; back=sin(a)
            hh=z+(.010*cos(2*a)-.009*back if j<4 else 0)
            if role==2:
                hh=1.69+(hh-1.69)*.63+.008*cos(a)
            if role==3 and j<4: hh-=.045*max(back+.35,0)
            row.append(m.vertex((brim*fac*cos(a),.008+deep*fac*sin(a),hh),"Head"))
        rr.append(row)
    m.grid(rr,steel)
    if role==0:
        m.tube([(0,-.112,1.741),(0,-.075,1.798),(0,-.011,1.851),(0,.048,1.832),(0,.108,1.766)],[(.007,.007),(.015,.014),(.014,.019),(.012,.014),(.006,.006)],"BlueSteel","Head",12)
    for s in [-1,1]:
        width = brim*(.694 if role==1 else .854)
        if role!=2: m.oval((s*width,.013,1.736),(.004,.009,.009),"Brass","Head",12,8)

    if role != 2: m.helmet_vertices=range(shell_begin,len(m.v))


def dial(m,center,axis,r,bone,lens=False):
    c=Vector(center); d=Vector(axis).normalized()
    u=(Vector((1,0,0))-d*d.x).normalized()
    v=d.cross(u); v=v if v.z>0 else -v
    m.tube([c-d*.012,c+d*.006],[r*.92,r],"BlueSteel",bone,40)
    m.tube([c+d*.005,c+d*.011],[r,r],"Brass",bone,40)
    m.tube([c+d*.011,c+d*.013],[r*.85,r*.85],"Lens" if lens else "Piping",bone,42)
    if lens:
        for offset,width in [(.2,.51),(.41,.25)]:
            p=c+d*.014+v*(offset*r)-u*.13*r
            m.tube([p-u*(width*r),p+u*(width*r)],[.0018,.0014],"Steel",bone,7)
        return
    for j in range(13):
        a=radians(-62+j*25); spoke=u*cos(a)+v*sin(a)
        p=c+d*.014
        m.tube([p+spoke*r*.64,p+spoke*r*.78],[r*.018,r*.018],"Boot",bone,6)
    p=c+d*.017
    m.tube([p-.12*r*u-.1*r*v,p+.39*r*u+.42*r*v],[.0029,.0013],"BlueSteel",bone,8)
    m.oval(p,(.003,.003,.003),"Brass",bone,9,6)

def spring(m,a,b,r,wire,turns,bone):
    a,b=Vector(a),Vector(b); axis=(b-a).normalized()
    u=axis.cross(Vector((0,1,0))).normalized(); v=axis.cross(u)
    total=turns*9
    pts=[a.lerp(b,i/total)+r*(u*cos(2*pi*i/9)+v*sin(2*pi*i/9)) for i in range(total+1)]
    m.tube(pts,[wire]*len(pts),"Steel",bone,7)

def rivet(m,p,r,bone):
    m.oval(p,(r,.55*r,r),"Brass",bone,10,7)

def specialist_kit(m,role):
    if role==0:
        # Raised keel and cupped side wings form a single riveted chest cuirass.
        rows=[]
        for z,w,front,edge in [(1.075,.150,-.172,-.10),(1.10,.177,-.183,-.082),(1.18,.191,-.205,-.069),(1.31,.200,-.205,-.068),(1.39,.195,-.174,-.072),(1.434,.143,-.141,-.081)]:
            row=[]
            for i in range(25):
                t=(i-12)/12; u=abs(t)
                row.append(m.vertex((w*t,front+(edge-front)*u**1.7+.007*u,z-.01*u*u),"Chest"))
            rows.append(row)
        for lo,hi in zip(rows,rows[1:]):
            for j in range(24): m.face((lo[j],lo[j+1],hi[j+1],hi[j]),"BlueSteel")
        for edge in [0,-1]:
            pts=[m.v[row[edge]] for row in rows]; m.tube(pts,[.008]*len(pts),"Steel","Chest",10)
        for row in [rows[0],rows[-1]]:
            pts=[m.v[v] for v in row];m.tube(pts,[.006]*len(pts),"Brass","Chest",10)
        m.tube([m.v[row[12]] for row in rows],[.005]*len(rows),"Steel","Chest",10)
        for s in [-1,1]:
            for j in [2,4]: rivet(m,m.v[rows[j][3 if s<0 else 21]],.008,"Chest")
            # Cheek plates swing from side rivets; central face opening remains legible.
            m.panel([(s*.107,-.058,1.709),(s*.118,-.002,1.704),(s*.107,-.025,1.596),(s*.065,-.084,1.562)],"BlueSteel","Head")
            m.tube([(s*.108,-.061,1.70),(s*.100,-.063,1.633),(s*.065,-.087,1.563)],[.006,.006,.005],"Steel","Head",10)
            rivet(m,(s*.104,-.064,1.675),.008,"Head")
        m.tube([(0,.222,1.034),(0,.225,1.08),(0,.225,1.407),(0,.225,1.458),(0,.225,1.48)],[.049,.087,.087,.071,.035],"FieldPaint","Chest",40)
        for z in [1.13,1.355]: m.tube([(0,.225,z),(0,.225,z+.029)],[.091,.091],"Leather","Chest",32)
        m.box((.01,.22,1.51),(.087,.053,.044),"Brass","Chest",.45)
        m.tube([(.035,.22,1.51),(.14,.185,1.50),(.165,.048,1.47),(.157,-.088,1.427),(.115,-.15,1.354)],[.010,.015,.015,.015,.009],"Rubber","Chest",16)
        dial(m,(.103,-.167,1.333),(.18,-1,.18),.044,"Chest")
    if role==1:
        # Offset prism case, two stacked objective lenses, leather mounting band.
        m.box((-.086,-.089,1.689),(.076,.072,.157),"Brass","Head",.45)
        m.box((-.09,-.103,1.767),(.087,.084,.079),"BlueSteel","Head",.45)
        m.box((-.088,-.058,1.729),(.092,.024,.124),"Leather","Head",.3)
        for p,r in [((-.089,-.151,1.776),.038),((-.086,-.127,1.671),.031)]:
            dial(m,p,(0,-1,.04),r,"Head",True)
        m.tube([(-.113,-.054,1.674),(-.126,.003,1.676),(-.12,.075,1.687),(-.063,.114,1.699)], [.011]*4,"Leather","Head",12)
        # Knurled range selector on the temple next to the moving prism.
        center=Vector((-.114,-.021,1.706)); axis=Vector((-.93,-.28,.10)).normalized()
        m.tube([center,center+axis*.026],[.038,.038],"Brass","Head",48)
        dial(m,center+axis*.029,axis,.029,"Head")
        for i in range(18):
            a=i*2*pi/18; c=center+axis*.015+Vector((0,cos(a),sin(a)))*.038
            m.tube([c,c+axis*.012],[.0025,.0025],"Steel","Head",6)
    if role==2:
        # Goggles raised onto a soft field cap and high cloth scarf. The jacket leaves the chest soft.
        for s in [-1,1]:
            dial(m,(s*.046,-.115,1.716),(s*.17,-1,.15),.033,"Head",True)
        m.tube([(-.098,.012,1.718),(-.083,-.077,1.719),(0,-.113,1.715),(.084,-.079,1.719),(.099,.012,1.718)],[.012]*5,"Leather","Head",12)
    if role in (0,2):
        # Telescoping shin linkages are purely visual articulated exoskeleton pieces.
        for side,s in [("L",-1),("R",1)]:
            leg=side+"Shin"; thigh=side+"Thigh"; x=s*(.196 if role==0 else .188)
            m.tube([(x,.018,.85),(x,-.016,.61),(x,-.029,.545)],[.023 if role==0 else .012]*3,"BlueSteel",thigh,18)
            m.tube([(x,-.027,.55),(x,.024,.21),(x,.017,.148)],[.026 if role==0 else .009]*3,"Steel",leg,18)
            p=Vector((x,-.025,.536)); ax=Vector((s,0,0))
            m.tube([p-ax*.018,p+ax*.022],[.052 if role==0 else .034]*2,"BlueSteel",leg,28)
            m.tube([p+ax*.021,p+ax*.032],[.021,.019],"Brass",leg,20)
            for height,y in [(.70,.008),(.34,.005),(.20,.025)]:
                name=thigh if height>.55 else leg
                m.tube([(s*.118,y,height),(s*.118,y,height+.021)],[.087 if height>.55 else .070]*2,"Leather",name,24)
                m.box((x,y,height+.013),(.035,.055,.036),"Brass",name,.6)
            if role==2:
                spring(m,(x,.014,.397),(x,.022,.235),.014,.0038,8,leg)
                m.tube([(x,.052,.48),(x,.073,.36),(x,.055,.20)],[.008,.008,.008],"BlueSteel",leg,14)
            else:
                m.tube([(x+.013*s,.032,.425),(x+.013*s,.039,.244)],[.025,.025],"Brass",leg,22)
                m.tube([(x+.013*s,.038,.242),(x+.013*s,.031,.166)],[.015,.015],"Steel",leg,20)

def rig_model(role):
    actor=body(role); spec=rig_spec()
    data=bpy.data.armatures.new("Field_anatomy")
    arm=bpy.data.objects.new("FieldSkeleton",data); bpy.context.collection.objects.link(arm)
    bpy.context.view_layer.objects.active=arm; arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for name,(head,tail,parent) in spec.items():
        eb=data.edit_bones.new(name); eb.head=head; eb.tail=tail
        if parent: eb.parent=data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    mod=actor.modifiers.new("Tailored body skin","ARMATURE"); mod.object=arm; actor.parent=arm
    arm.animation_data_create()
    for name,duration in [("Idle",48),("Walk",25),("Run",19),("Aim",18),("Fire",9),("Reload",40),("Hit",12),("Fall",26),("Mask",22)]:
        action=bpy.data.actions.new(name); arm.animation_data.action=action
        frames=list(range(1,duration+1,1 if name in ("Walk","Run","Fall") else 2))
        if frames[-1]!=duration: frames.append(duration)
        for frame in frames:
            t=(frame-1)/(duration-1); cycle=2*pi*t
            for bone in arm.pose.bones:
                bone.rotation_mode="XYZ"; bone.rotation_euler=(0,0,0); bone.location=(0,0,0)
            def rot(bone,axis,degrees): arm.pose.bones[bone].rotation_euler[axis]=radians(degrees)
            wave=sin(cycle)
            if name=="Idle":
                rot("Chest",0,.85*wave); rot("Head",2,1.8*sin(cycle)); rot("RArm",0,.65*wave)
            elif name in ("Walk","Run"):
                field_gait.stride(arm,t,name=="Run",role)
            elif name=="Aim":
                e=min(1,t*3); rot("Chest",0,-5*e); rot("RArm",0,-12*e); rot("LArm",0,-10*e)
            elif name=="Fire":
                e=exp(-t*7)*sin(t*pi*2.2); rot("Chest",0,6*e); rot("RArm",0,-10*e); rot("Head",0,3.6*e)
            elif name=="Reload":
                e=sin(pi*t); rot("Chest",2,7*e); rot("Head",0,12*e); rot("RArm",2,19*e)
                rot("LArm",0,13*e); rot("LForearm",0,29*sin(pi*t)**2); rot("LHand",1,-18*e)
            elif name=="Hit":
                e=sin(t*pi)*exp(-2*t); rot("Chest",2,17*e); rot("Head",0,-14*e)
            elif name=="Mask":
                e=sin(t*pi); rot("LArm",0,-37*e); rot("LForearm",0,42*e); rot("Head",0,7*e)
            else:
                e=t*t*(3-2*t); angle=-88*e
                rot("Pelvis",0,angle); rot("Chest",0,-3*e); rot("Head",0,5*e); rot("Head",2,15*e)
                rot("RThigh",0,-6*e); rot("LThigh",0,-3*e); rot("RShin",0,9*e); rot("LShin",0,4*e)
                rot("RArm",1,14*e); rot("LArm",1,-9*e)
                arm.pose.bones["Pelvis"].location.y=.87*cos(radians(angle))+.22*sin(radians(-angle))-.87
            for bone in arm.pose.bones:
                bone.keyframe_insert(data_path="rotation_euler",frame=frame,group=bone.name)
                if bone.name=="Pelvis": bone.keyframe_insert(data_path="location",frame=frame,group=bone.name)
        if name=="Fall":
            # Match the visible uniform to the floor through the entire transition, including boots and pack.
            for frame in frames:
                bpy.context.scene.frame_set(frame)
                skin=actor.evaluated_get(bpy.context.evaluated_depsgraph_get()).data
                gap=min(v.co.z for v in skin.vertices)
                hips=arm.pose.bones["Pelvis"]
                hips.location.y+=(.013-gap)/.99986
                hips.keyframe_insert(data_path="location",frame=frame,group=hips.name)
        arm.animation_data.action=None
        track=arm.animation_data.nla_tracks.new();track.name=name
        strip=track.strips.new(name,1,action); strip.name=name; track.mute=True
    for bone in arm.pose.bones:
        bone.rotation_euler=(0,0,0);bone.location=(0,0,0)
    bpy.context.scene.frame_set(1)
    return arm,actor

def marker(name,pos):
    ob=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(ob);ob.location=pos;ob.empty_display_size=.015
    return ob

def rifle():
    import carbine_workshop
    return carbine_workshop.build(marker)

def hands():
    result=[]
    for side,s in [("L",-1),("R",1)]:
        m=Mesh()
        wrist=Vector((.006,.012,-.015)) if s==1 else Vector((-.013,-.355,.03))
        start=Vector((s*.14,.43,-.20)) if s==1 else Vector((-.21,.24,-.218))
        elbow=lerp(start,wrist,.44)+Vector((s*.025,0,-.012))
        pts=[lerp(start,elbow,t) for t in [0,.25,.52,.76,1]]+[lerp(elbow,wrist,t) for t in [.23,.50,.74,.90,.98]]
        radii=[(.095,.089),(.095,.082),(.088,.079),(.080,.073),(.075,.071),(.073,.065),(.069,.059),(.058,.052),(.052,.044),(.046,.040)]
        m.tube(pts,radii,"Wool",None,34,.065)
        m.tube([lerp(elbow,wrist,t) for t in [.72,.75,.87,.91]],[.054,.058,.054,.050],"Canvas",None,28)
        glove(m,wrist,s,None,True)
        for j in range(9):
            angle=j*pi/8
            a=lerp(elbow,wrist,.79)
            x=a.x+.054*cos(angle); z=a.z+.048*sin(angle)
            m.tube([(x,a.y-.009,z),(x,a.y-.005,z+.0005)],[.0016,.0012],"Canvas",None,6)
        ob=m.finish("FieldGlove"+side)
        result.append(ob)
        for label, pos in [("Cuff",start),("Grip",wrist+Vector((0,-.049,-.004)))]:
            marker=bpy.data.objects.new("Field"+label+side,None)
            bpy.context.collection.objects.link(marker)
            marker.parent=ob
            marker.location=pos
            marker.empty_display_size=.012
            result.append(marker)
    return result

def save_export(name,objs,animate=False):
    bpy.ops.object.select_all(action="DESELECT")
    for ob in objs: ob.select_set(True)
    bpy.context.view_layer.objects.active=objs[0]
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(SRC/(name+".blend")),compress=True)
    # Export each active NLA strip as a separate named take.
    if animate:
        for tr in objs[0].animation_data.nla_tracks: tr.mute=False
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+".fbx")),use_selection=True,axis_forward="-Z",axis_up="Y",
        apply_unit_scale=True,apply_scale_options="FBX_SCALE_UNITS",add_leaf_bones=False, bake_anim=animate,
        bake_anim_use_all_actions=False,bake_anim_use_nla_strips=True,bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=.2, path_mode="STRIP",use_tspace=False)

def render_portrait(role):
    scene=bpy.context.scene; scene.render.engine="CYCLES";scene.cycles.samples=10
    scene.cycles.use_denoising=True; scene.render.resolution_x=320;scene.render.resolution_y=384
    scene.render.resolution_percentage=100; scene.world=bpy.data.worlds.new("Field studio")
    scene.world.color=(.16,.16,.16)
    def aim(ob,target): ob.rotation_euler=(Vector(target)-ob.location).to_track_quat("-Z","Y").to_euler()
    def lamp(name,pos,power,color,size):
        d=bpy.data.lights.new(name,"AREA");d.energy=power;d.color=color;d.shape="DISK";d.size=size
        o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=pos;aim(o,(0,0,1.45))
    lamp("Large daylight",(-1.8,-2.5,3.45),245,(1,.89,.76),2.1)
    lamp("Courtyard bounce",(1.5,-1.8,2.2),105,(.68,.80,1),1.4)
    lamp("Warm rim",(.3,1.2,2.6),120,(1,.76,.46),1.0)
    camera=bpy.data.cameras.new("Lens");ob=bpy.data.objects.new("Portrait camera",camera);scene.collection.objects.link(ob)
    ob.location=(.25,-2.7,1.67);aim(ob,(0,-.04,1.526));camera.type="ORTHO";camera.ortho_scale=.78;scene.camera=ob
    scene.render.film_transparent=True
    scene.render.filepath=str(OUT/(ROLES[role]+"Portrait.png"));bpy.ops.render.render(write_still=True)

if __name__=="__main__":
    portraits="portraits" in sys.argv
    if "hands-only" in sys.argv:
        clear(); save_export("FieldHands",hands())
    elif portraits:
        for i,n in enumerate(ROLES):
            bpy.ops.wm.open_mainfile(filepath=str(SRC/(n+".blend")));render_portrait(i)
    else:
        if "props" not in sys.argv:
            for role,name in enumerate(ROLES):
                if "exposed-faces" in sys.argv and role not in (0,1):continue
                clear(); arm,model=rig_model(role); save_export(name,[arm,model]+separate_helmet(model),True)
        if "actors-only" not in sys.argv:
            clear(); save_export("ServiceCarbine",rifle())
            import carbine_workshop
            carbine_workshop.export()
            clear(); save_export("FieldHands",hands())

