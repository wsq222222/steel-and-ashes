"""Author the unarmed ward cast, hand-held canvas litter and water pail. Blender Z up, front -Y."""
from pathlib import Path
import math
import sys
import bpy
from mathutils import Quaternion, Vector

source=Path(__file__).resolve().parent
sys.path.insert(0,str(source))
import make_field_cast as cast
from field_tailoring import coat, puttee, sleeve as coat_sleeve, folded_neckcloth

out=source.parent/"Ward"
out.mkdir(exist_ok=True)

def ward_rig():
    spec=cast.rig_spec()
    for side,s in (("L",-1),("R",1)):
        shoulder=(s*.204,.0,1.413); elbow=(s*.253,-.036,1.117); hand=(s*.282,-.162,.895)
        spec[side+"Arm"]=(shoulder,elbow,"Chest")
        spec[side+"Forearm"]=(elbow,hand,side+"Arm")
        spec[side+"Hand"]=(hand,(s*.282,-.219,.868),side+"Forearm")
    return spec

def guard_mesh():
    m=cast.Mesh(); spec=ward_rig()
    armholes=coat(m,1,"RifleWool")
    folded_neckcloth(m,"RifleWool")
    cast.head(m,1,uniform=True)
    cast.fit_head(m)
    # Pale bib apron, woven neck straps, patched hem and a pencil slip.
    rows=[]
    for j in range(29):
        t=j/28; z=.625+t*.77
        width=(.233-.067*max(0,(t-.45)/.55)-.082*max(0,(t-.68)/.32))
        row=[]
        for i in range(24):
            u=2*i/23-1
            depth=.149+.029*(1-t)+.012*math.cos(t*5+u*2)
            y=-depth+.087*abs(u)**2-.014*math.cos(11*u+.7*math.sin(5*t))*(1-.65*t)
            row.append(m.vertex((width*u,y,z+.007*(1-t)*math.sin(7*u)), "Chest" if z>1.04 else "Pelvis"))
        rows.append(row)
    for j in range(28):
        for i in range(23):
            m.face((rows[j][i],rows[j][i+1],rows[j+1][i+1],rows[j+1][i]),"IvoryStencil")
    for s in (-1,1):
        m.tube([(s*.08,-.135,1.39),(s*.092,-.073,1.447),(s*.086,-.036,1.463),(s*.075,.057,1.456)], [.007,.007,.008,.006],"Canvas","Chest",8)
    m.box((-.068,-.166,1.173),(.085,.012,.085),"Canvas","Chest",.30)
    m.tube([(-.066,-.177,1.19),(-.06,-.172,1.245)],[.004,.004],"Walnut","Chest",8)
    for side,s in (("L",-1),("R",1)):
        sh,el,_=spec[side+"Arm"]; _,wrist,_=spec[side+"Forearm"]
        def sleeve(a,b,rad,bone,mat):
            m.tube([cast.lerp(a,b,t) for t in (-.05,.08,.35,.76,1.03)],rad,mat,bone,24,.032)
        start=len(m.v)
        coat_sleeve(m,armholes[s],sh,el,side,-1,"RifleWool")
        # Continue from the actual final ring of the upper sleeve: even at full bedside
        # reach there is no separate cap or rigid tube to reveal inside the elbow.
        ring=list(m.faces.pop());m.ids.pop()
        shoulder,elbow,hand=map(Vector,(sh,el,wrist))
        upper=elbow-shoulder; lower=hand-elbow
        for i in range(start,len(m.v)):
            along=(Vector(m.v[i])-shoulder).dot(upper)/upper.length_squared
            blend=.5*max(0,min(1,(along-.78)/.265))
            if blend:m.skin[i]=((side+"Arm",1-blend),(side+"Forearm",blend))
        centre=sum((Vector(m.v[i]) for i in ring),Vector())/len(ring)
        turn=upper.normalized().rotation_difference(lower.normalized())
        section=[(turn@(Vector(m.v[i])-centre)).normalized() for i in ring]
        for t,radius,blend in ((.13,.067,.76),(.32,.068,1),(.55,.062,1),(.80,.050,1),(1.025,.047,1)):
            weights=((side+"Arm",1-blend),(side+"Forearm",blend)) if blend<1 else side+"Forearm"
            next_ring=[m.vertex(elbow+lower*t+r*radius,weights) for r in section]
            for i,a in enumerate(ring):
                j=(i+1)%len(ring);m.face((a,ring[j],next_ring[j],next_ring[i]),"RifleWool")
            ring=next_ring
        m.face(ring,"RifleWool")
        cast.glove(m,Vector(wrist),s,side+"Hand")
        hip,knee,_=spec[side+"Thigh"];_,ankle,_=spec[side+"Shin"]
        sleeve(hip,knee,[.085,.096,.099,.073,.064],side+"Thigh","RifleWool")
        sleeve(knee,ankle,[.058,.071,.069,.053,.047],side+"Shin","Canvas")
        puttee(m,knee,ankle,side)
        x=s*.118
        m.loft([(x,-.051,.015,.067,.152),(x,-.054,.042,.073,.15),(x,-.047,.083,.070,.139),(x,-.01,.124,.059,.093),(x,.017,.176,.049,.058)],"Boot",side+"Foot",28)
    return m.finish("Orderly_linen_apron")

def author_take(arm,name,frames):
    action=bpy.data.actions.new(name); arm.animation_data.action=action
    times=list(range(1,frames+1,1 if name in ("Walk","Carry","CarryBack") else 3))
    if times[-1]!=frames:times.append(frames)
    for f in times:
        t=(f-1)/(frames-1); p=t*math.tau
        for b in arm.pose.bones:
            b.rotation_mode="QUATERNION";b.rotation_quaternion=Quaternion();b.location=(0,0,0)
        def axis(key,xyz,degrees):
            b=arm.pose.bones[key]; local=b.bone.matrix_local.to_quaternion().inverted()@Vector(xyz)
            b.rotation_quaternion=Quaternion(local,math.radians(degrees))@b.rotation_quaternion
        stride=math.sin(p); walking=name in ("Walk","Carry","CarryBack")
        if walking:
            for side,s in (("L",-1),("R",1)):
                phase=p+(0 if s<0 else math.pi)
                if name=="CarryBack": phase=-phase
                contact=math.cos(phase)
                swing=max(0,-math.sin(phase))
                loading=max(0,math.sin(2*phase))*max(0,contact)
                hip=-(19 if name=="Walk" else 13)*contact-4*swing
                knee=5+12*loading+(41 if name=="Walk" else 34)*swing**1.4
                axis(side+"Thigh",(1,0,0),hip)
                axis(side+"Shin",(1,0,0),knee)
                axis(side+"Foot",(1,0,0),-hip-knee+11*swing-8*max(0,-contact))
                # At heel strike the opposite hand is forward. Let the elbow fold on the
                # forward pass, then open as the hand drifts back alongside the apron.
                shoulder=(17 if name=="Walk" else 2.2)*contact
                axis(side+"Arm",(1,0,0),shoulder+2)
                axis(side+"Forearm",(1,0,0),-13+10*contact if name=="Walk" else -8)
                axis(side+"Arm",(0,1,0),s*(3-1.8*contact))
            axis("Pelvis",(0,1,0),2.7*stride)
            axis("Pelvis",(0,0,1),4.0*math.cos(p))
            pelvis=arm.pose.bones["Pelvis"]
            pelvis.location=pelvis.bone.matrix_local.to_quaternion().inverted()@Vector((-.014*stride,0,-.015-.010*math.cos(2*p)))
            pelvis.keyframe_insert(data_path="location",frame=f,group=pelvis.name)
            axis("Chest",(0,0,1),-5*math.cos(p))
            axis("Chest",(1,0,0),3.2+1.3*math.cos(2*p))
            axis("Head",(0,0,1),1.8*math.cos(p))
        if name in ("Carry","CarryBack","Lift"):
            for side,s in (("L",-1),("R",1)):
                axis(side+"Arm",(0,1,0),-s*4)
                axis(side+"Arm",(1,0,0),-8)
                axis(side+"Forearm",(1,0,0),-7)
        if name in ("Tend","Lift","Write"):
            lean=(.5-.5*math.cos(p))
            axis("Chest",(1,0,0),12+lean*(24 if name=="Lift" else 13))
            axis("Head",(1,0,0),9+lean*8)
            for side,s in (("L",-1),("R",1)):
                axis(side+"Arm",(1,0,0),-(32+14*lean+4*s*math.sin(p)))
                axis(side+"Forearm",(1,0,0),-18+6*s*math.sin(2*p))
        if name=="Idle":
            axis("Chest",(1,0,0),1.5*math.sin(p))
            axis("Head",(0,0,1),4.5*math.sin(p))
        for b in arm.pose.bones:b.keyframe_insert(data_path="rotation_quaternion",frame=f,group=b.name)
    arm.animation_data.action=None
    track=arm.animation_data.nla_tracks.new();track.name=name
    tr=track.strips.new(name,1,action);tr.name=name;track.mute=True

def save_model(label,objects,animation=False):
    marks=[cast.marker(name,loc) for name,loc in [("WardOrigin",(0,0,0)),("WardFront",(0,-1,0)),("WardUp",(0,0,1))]]
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects+marks:obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.context.scene.render.fps=24;bpy.context.scene.frame_set(1)
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(source/(label+".blend")),compress=True)
    if animation:
        for track in objects[0].animation_data.nla_tracks:track.mute=False
    bpy.ops.export_scene.fbx(filepath=str(out/(label+".fbx")),use_selection=True,axis_forward="-Z",axis_up="Y",
        apply_scale_options="FBX_SCALE_UNITS",add_leaf_bones=False,bake_anim=animation,
        bake_anim_use_all_actions=False,bake_anim_use_nla_strips=True,bake_anim_simplify_factor=.2,path_mode="STRIP")

def staff():
    cast.clear()
    actor=guard_mesh(); spec=ward_rig()
    data=bpy.data.armatures.new("Orderly anatomy");arm=bpy.data.objects.new("WardRig",data)
    bpy.context.collection.objects.link(arm);bpy.context.view_layer.objects.active=arm;arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for name,(a,b,parent) in spec.items():
        bone=data.edit_bones.new(name);bone.head=a;bone.tail=b
        if parent:bone.parent=data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    mod=actor.modifiers.new("Cloth rig","ARMATURE");mod.object=arm;actor.parent=arm
    arm.animation_data_create()
    for name,duration in (("Idle",63),("Walk",21),("Carry",23),("CarryBack",23),("Tend",65),("Lift",37),("Write",51)):
        author_take(arm,name,duration)
    for b in arm.pose.bones:b.rotation_quaternion=Quaternion()
    save_model("WardOrderly",[arm,actor],True)

def casualty(m):
    # Wrapped shoulders, bent knees and the folded quilt edge break the straight cylindrical outline.
    rows=[]
    for j in range(36):
        t=j/35; row=[]
        for i in range(21):
            u=(i/20)*2-1
            legs=(.092*math.exp(-((u-.25)/.28)**2)+.070*math.exp(-((u+.28)/.31)**2))*math.exp(-((t-.24)/.19)**2)
            shoulders=.095*math.exp(-((t-.82)/.17)**2)
            h=.020+(.135+shoulders)*(1-u*u)+legs+.016*math.cos(31*t+4*u) - .041*abs(u)**6
            width=.218+.066*t+.038*math.exp(-((t-.79)/.17)**2)
            row.append(m.vertex((width*u,-.80+1.32*t,h),None))
        rows.append(row)
    for j in range(35):
        for i in range(20):m.face((rows[j][i],rows[j][i+1],rows[j+1][i+1],rows[j+1][i]),"HeavyWool")
    for side in (0,-1):
        edge=[m.v[r[side]] for r in rows]
        m.tube(edge,[.0045]*len(edge),"Canvas",None,8)
    # Use the project's sculpted/photo-textured face, lying face-up beneath a wound dressing.
    head=cast.Mesh();cast.head(head,0)
    indices={}
    for vertices,slot in zip(head.faces,head.ids):
        mat=head.mats[slot]
        if mat not in ("FaceAssault","Skin"):continue
        ids=[]
        for i in vertices:
            if i not in indices:
                x,y,z=head.v[i]
                vi=m.vertex((x, .623+(z-1.641), .098-y),None)
                if i in head.uv:m.uv[vi]=head.uv[i]
                indices[i]=vi
            ids.append(indices[i])
        m.face(ids,mat)
    # Three overlapping cloth turns on the brow, with a short folded loose end on the pillow.
    for j in range(3):
        y=.687+.022*j
        rr=[]
        for d in (-.017,.017):
            rr.append([m.vertex((.105*math.cos(k*math.tau/48),
                y+d+.012*math.cos(k*math.tau/48),.09+.103*math.sin(k*math.tau/48)),None) for k in range(48)])
        m.grid(rr,"IvoryStencil",False)
    m.oval((0,.738,.099),(.093,.065,.087),"IvoryStencil",None,30,14)
    m.tube([(-.09,.690,.107),(-.12,.678,.074),(-.135,.716,.039)],[.018,.017,.010],"IvoryStencil",None,10)
    m.oval((.014,.622,.046),(.162,.176,.052),"Canvas",None,28,16)

def litter():
    cast.clear(); m=cast.Mesh()
    for x in (-.302,.302):
        m.tube([(x,-1.14,0),(x,-1.08,0),(x,.99,0),(x,1.14,0)], [.025,.037,.036,.022],"Walnut",None,20)
        for y in (-.72,.72):m.box((x,y,-.082),(.065,.077,.147),"Steel",None,.35)
    for y in (-.75,.75):m.tube([(-.296,y,-.022),(.296,y,-.022)],[.028,.028],"Walnut",None,12)
    for k in range(18):
        y=-.81+k*.09;m.box((0,y,-.005),(.59,.089,.015),"Canvas",None,.23)
    casualty(m)
    save_model("WardLitter",[m.finish("Canvas_stretcher")])
    cast.clear(); m=cast.Mesh();casualty(m)
    save_model("WardPatient",[m.finish("Patient_under_a_wool_quilt")])

def pail():
    cast.clear();m=cast.Mesh();rr=[]
    for z,r in ((0,.103),(.017,.11),(.198,.145),(.216,.15),(.220,.147),(.21,.137),(.04,.105)):
        rr.append([m.vertex((r*math.cos(i*math.tau/32),r*math.sin(i*math.tau/32),z-.4),None) for i in range(32)])
    m.grid(rr,"Steel",False)
    arc=[(.146*math.cos(i*math.pi/24),0,-.196+.187*math.sin(i*math.pi/24)) for i in range(25)]
    m.tube(arc,[.007]*25,"Steel",None,8)
    save_model("WardPail",[m.finish("Enamel_pail")])

if "--staff-only" in sys.argv:staff()
elif "--casualty" in sys.argv:litter()
else:staff(); litter(); pail()
