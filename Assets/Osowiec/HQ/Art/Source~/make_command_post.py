"""Original field-office modelling source. Blender 5+. Background build: blender -b -P this_file.
Objects are modelled in metres. +Y is towards the rear wall, Z is up.
"""
from pathlib import Path
import math, random
import sys
import bpy
from mathutils import Vector
from math import pi, sin, cos

ROOT=Path(__file__).resolve().parents[1]
SRC=Path(__file__).resolve().parent
rng=random.Random(416)
materials={}
palette={
    'QuarterSawn':(.31,.17,.070),'DarkWalnut':(.12,.07,.032),'Steel':(.095,.108,.101),
    'Oxide':(.091,.080,.058),'Brass':(.52,.33,.12),'Bakelite':(.064,.051,.040),
    'Paper':(.79,.685,.46),'Dispatch':(.78,.69,.50),'Profiles':(.78,.68,.49),
    'Map':(.84,.76,.61),'Canvas':(.17,.19,.103),'Leather':(.24,.115,.053),
    'Glass':(.57,.39,.19),'Flame':(1,.55,.13),'Graphite':(.13,.13,.12),'Wax':(.42,.105,.064),
    'Silt':(.092,.058,.037),'Water':(.19,.21,.18),'Linen':(.44,.38,.24),'Enamel':(.26,.31,.28),
    'LedgerCover':(.41,.37,.25)
}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version=0
    materials.clear()
    for name,color in palette.items():
        m=bpy.data.materials.new('HQ_'+name);m.diffuse_color=(*color,1)
        m.use_nodes=True
        bs=m.node_tree.nodes.get('Principled BSDF')
        bs.inputs['Base Color'].default_value=(*color,1)
        bs.inputs['Roughness'].default_value=.35 if name in ('Brass','Bakelite','Glass') else .7
        if name in ('Steel','Brass','Oxide'):bs.inputs['Metallic'].default_value=.7
        if name=='Water':
            bs.inputs['Roughness'].default_value=.09;bs.inputs['Alpha'].default_value=.55
        tex={'DarkWalnut':'QuarterSawn','QuarterSawn':'QuarterSawn','Dispatch':'Dispatch','Profiles':'Profiles','LedgerCover':'LedgerCover'}.get(name)
        if tex:
            img=bpy.data.images.load(str(ROOT/'Textures'/(tex+'.png')),check_existing=True)
            node=m.node_tree.nodes.new('ShaderNodeTexImage');node.image=img
            m.node_tree.links.new(node.outputs['Color'],bs.inputs['Base Color'])
        materials[name]=m


def assign(o,n,mat):
    o.name=n;o.data.materials.append(materials[mat])
    return o


def soft(o,amount):
    if not amount:return o
    m=o.modifiers.new('Worn edges','BEVEL');m.width=amount;m.segments=2
    m.affect='EDGES'
    bpy.context.view_layer.objects.active=o
    bpy.ops.object.modifier_apply(modifier=m.name)
    return o


def box(n,p,dim,mat,bev=.004,rz=0):
    bpy.ops.mesh.primitive_cube_add(size=1,location=p)
    o=bpy.context.object
    o.dimensions=dim
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    assign(o,n,mat)
    uv=o.data.uv_layers.active
    for f in o.data.polygons:
        for l in f.loop_indices:
            v=o.data.vertices[o.data.loops[l].vertex_index].co
            if abs(f.normal.z)>.7:
                u,w=v.x/max(dim[0],.005),v.y/max(dim[1],.005)
            elif abs(f.normal.y)>.7:
                u,w=v.x/max(dim[0],.005),v.z/max(dim[2],.005)
            else:u,w=v.y/max(dim[1],.005),v.z/max(dim[2],.005)
            uv.data[l].uv=(u+.5,w+.5)
    soft(o,bev);o.rotation_euler.z=rz
    return o


def cyl(n,p,r,h,mat,axis=(0,0,1),segments=24):
    bpy.ops.mesh.primitive_cylinder_add(vertices=segments,radius=r,depth=h,location=p)
    o=assign(bpy.context.object,n,mat)
    for poly in o.data.polygons:poly.use_smooth=len(poly.vertices)==4
    soft(o,min(.002,h*.17,r*.07))
    o.rotation_euler=Vector(axis).to_track_quat('Z','Y').to_euler()
    return o


def rod(n,a,b,r,mat,segments=12):
    d=Vector(b)-Vector(a)
    return cyl(n,(Vector(a)+Vector(b))*.5,r,d.length,mat,d,segments)


def tube(n,points,r,mat,smooth=True):
    c=bpy.data.curves.new(n,'CURVE');c.dimensions='3D';c.resolution_u=8
    s=c.splines.new('BEZIER' if smooth else 'POLY')
    if smooth:
        s.bezier_points.add(len(points)-1)
        for v,p in zip(s.bezier_points,points):
            v.co=p;v.handle_left_type='AUTO';v.handle_right_type='AUTO'
    else:
        s.points.add(len(points)-1)
        for v,p in zip(s.points,points):v.co=(*p,1)
    c.bevel_depth=r;c.bevel_resolution=2;c.resolution_u=7
    o=bpy.data.objects.new(n,c);bpy.context.collection.objects.link(o)
    c.materials.append(materials[mat])
    return o


def turned(n,cx,cy,z,profile,mat,seg=32):
    verts=[];faces=[]
    for dz,r in profile:
        for j in range(seg):
            a=j*2*pi/seg;verts.append((cx+r*cos(a),cy+r*sin(a),z+dz))
    for k in range(len(profile)-1):
        for j in range(seg):faces.append((k*seg+j,k*seg+(j+1)%seg,(k+1)*seg+(j+1)%seg,(k+1)*seg+j))
    faces.append(tuple(reversed(range(seg))))
    faces.append(tuple((len(profile)-1)*seg+j for j in range(seg)))
    m=bpy.data.meshes.new(n);m.from_pydata(verts,[],faces);m.update()
    o=bpy.data.objects.new(n,m);bpy.context.collection.objects.link(o);assign(o,n,mat)
    for poly in m.polygons:poly.use_smooth=len(poly.vertices)==4
    return o


def plane(n,coords,mat,uv=None):
    data=bpy.data.meshes.new(n);data.from_pydata(coords,[],[(0,1,2,3)]);data.update()
    o=bpy.data.objects.new(n,data);bpy.context.collection.objects.link(o);assign(o,n,mat)
    uvs=data.uv_layers.new(name='UV')
    u=uv or [(0,0),(1,0),(1,1),(0,1)]
    for i,coord in enumerate(u):uvs.data[i].uv=coord
    return o


def sheet(n,x,y,z,w,d,angle,mat):
    coords=[]
    for dx,dy,dz in [(-w/2,-d/2,.0007),(w/2,-d/2,.0004),(w/2,d/2,.0019),(-w/2,d/2,.0013)]:
        coords.append((x+dx*cos(angle)-dy*sin(angle),y+dx*sin(angle)+dy*cos(angle),z+dz))
    return plane(n,coords,mat)

def check_telephone_on_desk(desk_top):
    graph=bpy.context.evaluated_depsgraph_get()
    def vertices(name):
        obj=bpy.data.objects[name].evaluated_get(graph)
        mesh=obj.to_mesh()
        try:
            return [obj.matrix_world @ v.co for v in mesh.vertices]
        finally:
            obj.to_mesh_clear()
    paper_edge=max(v.y for v in vertices('Map support'))
    receiver_lead=vertices('Telephone lead')
    board_clearance=min(v.z for v in receiver_lead)-desk_top
    fits_behind_sheet=min(v.y for v in vertices('Field telephone chest'))-paper_edge
    cord_to_sheet=min(v.y for v in receiver_lead)-paper_edge
    board_end=max(v.y for v in vertices('Table leaf.008'))
    feet=[o.name for o in bpy.context.scene.objects if o.name.startswith('Telephone foot')]
    if len(feet)!=4 or any(abs(min(v.z for v in vertices(name))-desk_top)>.0002 for name in feet):
        raise ValueError("The four telephone feet must rest on the tabletop")
    if min(fits_behind_sheet,cord_to_sheet)<.02 or not -.0002<=board_clearance<=.002:
        raise ValueError(f"Desk clearance: case/paper {fits_behind_sheet}, cord/paper {cord_to_sheet}, cord/wood {board_clearance}")
    if max(v.y for v in receiver_lead)>board_end-.008:
        raise ValueError("Receiver lead extends beyond the rear board")
    print(f"Desk clearance (mm): case/paper {fits_behind_sheet*1000:.1f}, cord/paper {cord_to_sheet*1000:.1f}, cord/wood {board_clearance*1000:.1f}",flush=True)


def lozenge(n,x,y,z,rx,ry,mat,seed,height=.002):
    noise=random.Random(seed); points=[(x,y,z+height)]; faces=[]; count=32
    radii=[.88+noise.uniform(-.085,.07)+.08*sin(i*6*pi/count) for i in range(count)]
    for scale,h in ((.73,height*.96),(1,height*.14)):
        for i,r in enumerate(radii):
            a=2*pi*i/count;points.append((x+rx*r*scale*cos(a),y+ry*r*scale*sin(a),z+h))
    for i in range(count):
        j=(i+1)%count;faces.extend([(0,1+i,1+j),(1+i,1+count+i,1+count+j,1+j)])
    mesh=bpy.data.meshes.new(n);mesh.from_pydata(points,[],faces);mesh.update()
    ob=bpy.data.objects.new(n,mesh);bpy.context.collection.objects.link(ob);assign(ob,n,mat)
    for face in mesh.polygons:face.use_smooth=True
    return ob


def working_desk():
    # Spill stays on the bare boards and the lower right printed margin. Officers can still follow every route.
    rr=random.Random(917)
    for i in range(11):
        x=.471+i*.009+rr.uniform(-.004,.004);y=-.472+rr.uniform(-.006,.006);r=rr.uniform(.002,.006)
        lozenge('Silt from a wet dispatch',x,y,1.1376,r*2,r*.74,'Silt',i+11,r*.22)
    lozenge('Rain film over the map corner',.555,-.468,1.1384,.037,.017,'Water',317,.0009)
    for i in range(29):
        x=.495+rr.random()*.091; y=-.489+rr.random()*.045; r=rr.uniform(.0008,.0035)
        lozenge('Small fleck of damp soil',x,y,1.1383,r*1.5,r,'Silt' if i%4 else 'Water',i+88,r*.29)
    marker=bpy.data.objects.new('SurveyMapDrip',None);bpy.context.collection.objects.link(marker);marker.location=(.555,-.468,1.1399)
    # A well-used enamel cup and an ink bottle occupy the opposite end of the desk.
    x,y=1.216,-.304
    turned('Blue enamel mug',x,y,1.119,[(0,.049),(.003,.057),(.008,.056),(.112,.062),(.118,.065),(.121,.065),(.123,.062),(.119,.059),(.012,.051)],'Enamel',56)
    tube('Mug lip',[(x+.064*cos(i*pi/16),y+.064*sin(i*pi/16),1.240) for i in range(33)],.002,'Steel')
    tube('Enamel handle',[(x+.058,y,1.223),(x+.089,y,1.224),(x+.104,y,1.201),(x+.101,y,1.169),(x+.080,y,1.155),(x+.054,y,1.158)],.006,'Enamel')
    lozenge('Cold tea',x,y,1.190,.060,.060,'Water',87,.0009)
    x,y=.975,.30
    turned('Inkwell',x,y,1.119,[(0,.042),(.004,.048),(.037,.047),(.048,.035),(.050,.022),(.061,.021),(.066,.025)],'Bakelite',36)
    turned('Inkwell collar',x,y,1.181,[(0,.025),(.003,.026),(.006,.023)],'Brass')
    cyl('Inkwell mouth',(x,y,1.1876),.015,.002,'Graphite',segments=24)
    rod('Steel dip pen',(.698,.237,1.130),(.895,.277,1.131),.005,'DarkWalnut')
    rod('Dip pen nib',(.695,.236,1.130),(.665,.230,1.124),.0028,'Brass',8)
    for i in range(12):
        ang=i*2*pi/12
        a=Vector((-.96+.035*cos(ang),-.565+.035*sin(ang),1.151));b=a+Vector((.007*cos(ang),.007*sin(ang),0))
        rod('Compass dial graduation',a,b,.0012,'Graphite',6)
    for i in range(3):
        x=-.261+i*.369
        sheet('Leather-edge map reinforcement',x,-.502,1.138,.045,.013,.04*(-1)**i,'Canvas')
    for i in range(2):
        x,y=.98,-.43; z=1.145+i*.041; ang=-.09+.02*i
        def at(dx,dy,dz):return (x+dx*cos(ang)-dy*sin(ang),y+dx*sin(ang)+dy*cos(ang),z+dz)
        box('Exposed folio at fore edge',at(.173,0,-.0007),(.014,.207,.025),'Paper',.0017,ang)
        box('Exposed page ends',at(.006,-.109,-.0007),(.321,.006,.025),'Paper',.0009,ang)
        for k in range(4):
            rod('Folio deckle',at(-.146,-.1121,-.009+k*.005),at(.160,-.1121,-.009+k*.005),.00055,'Linen',6)
        box('Raised cloth spine',at(-.150,0,.018),(.048,.222,.008),'Leather',.003,ang)
        for dx in (-.027,.024):
            for dy in (-.087,.087):box('Raised spine band',at(-.150+dx,dy,.021),(.006,.028,.006),'Brass',.0016,ang)
        if i:
            sheet('Title block on signal ledger',x+.012,y,1.207,.182,.126,ang,'LedgerCover')
            for side in (-1,1):
                for key in (-1,1):
                    box('Folded brass book corner',at(.164*side,.098*key,.020),(.027,.020,.004),'Brass',.0018,ang)


def room_stores():
    reset()
    # Suspended signal cable, ceramic insulators and a hinged distribution box along the rear wall.
    for y,z in [(5.72,2.77),(5.66,2.83)]:
        tube('Braided overhead signal line',[(-4.18,y,z),(-3.38,y,z-.18),(-2.72,y,z-.038),(-1.66,y,z-.15),(-.65,y,z-.02),(.34,y,z-.17),(1.24,y,z-.035),(2.44,y,z-.20),(3.48,y,z-.02),(4.13,y,z-.12)],.015,'Bakelite')
    for x in [-4.12,-2.73,-.66,1.22,3.47]:
        box('Shaped line bracket',(x,5.74,2.80),(.10,.17,.065),'Steel',.009)
        for j in range(3):cyl('Glazed ceramic insulator',(x,5.63,2.746+j*.036),.042,.017,'Enamel',segments=28)
    for z in (.99,1.71):
        box('Radio shelf',(-3.08,5.42,z),(1.32,.42,.048),'QuarterSawn',.008)
        for x in (-3.60,-2.56):
            rod('Cast support',(x,5.70,z-.25),(x,5.23,z-.036),.017,'Steel',12)
    for i in range(3):
        x=-3.48+i*.36; y=5.40
        box('Signal battery cabinet',(x,y,1.187),(.309,.289,.335),'DarkWalnut',.015)
        box('Pressed instrument front',(x,y-.151,1.190),(.270,.012,.247),'Steel',.005)
        for dx in (-.070,.070):
            cyl('Instrument dial',(x+dx,y-.164,1.225),.038,.022,'Brass',(0,1,0),28)
            cyl('Porcelain dial face',(x+dx,y-.177,1.225),.030,.005,'Paper',(0,1,0),24)
            rod('Dial marker',(x+dx,y-.181,1.216),(x+dx+.015,y-.181,1.237),.002,'Graphite',8)
        cyl('Induction key',(x,y-.17,1.134),.027,.031,'Bakelite',(0,1,0),24)
    for i in range(4):
        box('Parcel wrapped in oilcloth',(-3.50+i*.245,5.43,1.817),(.217,.28,.156),'Linen' if i%2 else 'Canvas',.023,.033*(i-2))
        box('Binding around signal parcel',(-3.50+i*.245,5.43,1.819),(.029,.284,.157),'Leather',.008,.033*(i-2))
    # A coil of line and woven cable ends at the wall rather than hanging over the route.
    for i in range(6):
        pts=[]
        for j in range(33):
            a=2*pi*j/32; pts.append((2.00+.217*cos(a),5.67-i*.019+.012*cos(3*a),1.39+.279*sin(a)))
        tube('Coil of signal wire',pts,.014,'Graphite')
    tube('Slack cable tail',[(2.13,5.57,1.58),(2.33,5.64,1.37),(2.27,5.65,1.01),(2.35,5.65,.57)],.015,'Graphite')
    for x,y in [(-3.32,3.26),(-3.57,4.43)]:
        for z in (.66,1.61):
            box('Folded wool blanket',(x,y,z),(.74,.40,.11),'Linen' if y<4 else 'Canvas',.046,.038)
            for k in range(4):
                tube('Layers at the blanket fold',[(x-.34,y-.194,z-.037+k*.018),(x-.08,y-.207,z-.035+k*.018),(x+.34,y-.182,z-.038+k*.018)],.006,'Canvas')
    save('DugoutStores')


def table():
    reset()
    for y in [-.54,.54]:
        for x in [-1.17,1.17]:
            box('Tapered oak leg',(x,y,.505),(.123,.13,1.01),'DarkWalnut',.009)
            box('Shoe',(x,y,.048),(.132,.139,.096),'Steel',.007)
        box('End apron',(0,y,.947),(2.48,.062,.161),'DarkWalnut',.008)
    for x in [-1.18,1.18]:
        box('Rail',(x,0,.946),(.071,1.20,.173),'DarkWalnut',.006)
        box('Trestle beam',(x,0,.31),(.102,1.25,.096),'QuarterSawn',.008)
    box('Stretching rail',(0,0,.312),(2.47,.089,.094),'DarkWalnut',.007)
    for j in range(9):
        y=-.71+j*.177
        box('Table leaf',(0,y,1.061),(2.87+rng.uniform(-.014,.014),.176,.114),'QuarterSawn',.005)
    for x in [-1.365,1.365]:
        for y in [-.721,.721]:
            box('Corner strap',(x,y,1.118),(.133,.142,.008),'Oxide',.002)
            for dx,dy in [(.040,0),(-.038,.041),(-.038,-.043)]:cyl('Slotted screw',(x+dx,y+dy,1.125),.0075,.005,'Brass',segments=12)
    for x in [-.49,.49]:
        box('Map drawer',(x,-.635,.934),(.88,.07,.124),'QuarterSawn',.009)
        for dx in [-.068,.068]:cyl('Pull mount',(x+dx,-.677,.944),.017,.02,'Brass',(0,1,0))
        tube('Drawer pull',[(x-.065,-.679,.946),(x-.065,-.70,.919),(x,-.719,.912),(x+.065,-.70,.919),(x+.065,-.679,.946)],.007,'Brass')
    # The readable field map has its own abraded backing and an unfolded crease.
    box('Map support',(-.18,-.047,1.124),(1.60,.931,.010),'Paper',.003)
    sheet('Printed operation map',-.18,-.05,1.136,1.565,.903,0,'Map')
    for x,y in [(-.87,-.43),(.5,.32)]:cyl('Map weight',(x,y,1.15),.027,.018,'Oxide')
    # Magneto telephone case, cradle, bells, receiver, crank and insulated cord.
    x,y=-1.085,.615
    desk_top=1.118
    z=desk_top+.007
    for dx in (-.142,.142):
        for dy in (-.098,.098):
            cyl('Telephone foot',(x+dx,y+dy,desk_top+.007),.015,.014,'Bakelite',segments=14)
    box('Field telephone chest',(x,y,z+.102),(.39,.284,.195),'DarkWalnut',.014)
    box('Tinned faceplate',(x,y-.146,z+.101),(.348,.014,.147),'Steel',.004)
    for dx in (-.148,.148):
        for dz in (.046,.155):
            cyl('Faceplate fastener',(x+dx,y-.155,z+dz),.0048,.005,'Brass',(0,1,0),12)
    for dx in [-.071,.071]:
        turned('Bell dome',x+dx,y+.03,z+.199,[(0,.051),(.009,.059),(.026,.058),(.047,.043),(.061,.011)],'Brass')
        cyl('Terminal screw',(x+dx,y-.157,z+.112),.019,.014,'Brass',(0,1,0))
    for dx in [-.153,.153]:
        rod('Handset fork',(x+dx,y+.088,z+.203),(x+dx,y+.088,z+.284),.01,'Steel')
    tube('Bakelite handset bridge',[(x-.152,y+.088,z+.267),(x-.126,y+.088,z+.292),(x,y+.093,z+.32),(x+.124,y+.088,z+.292),(x+.151,y+.088,z+.268)],.0165,'Bakelite')
    for dx in [-.154,.154]:
        turned('Receiver cup',x+dx,y+.088,z+.236,[(0,.036),(.011,.041),(.023,.037),(.041,.021),(.047,.017)],'Bakelite')
    # A cloth-covered return lead travels behind the set to its rear socket.
    # The middle of the slack rests on the bare boards, clear of the map backing.
    cord_radius=.0052
    rest=desk_top+cord_radius+.0008
    rod('Handset strain relief',(x+.173,y+.091,z+.258),(x+.202,y+.103,z+.249),.009,'Bakelite')
    rod('Rear cord gland',(x+.090,y+.141,z+.089),(x+.090,y+.154,z+.089),.012,'Brass')
    cord=tube('Telephone lead',[
        (x+.199,y+.102,z+.250),(x+.233,y+.113,z+.213),
        (x+.260,y+.118,z+.104),(x+.285,y+.107,rest+.015),
        (x+.311,y+.100,rest),(x+.346,y+.110,rest),
        (x+.351,y+.140,rest),(x+.321,y+.155,rest),
        (x+.237,y+.158,rest),(x+.164,y+.156,rest+.016),
        (x+.111,y+.159,z+.063),(x+.090,y+.154,z+.089)
    ],cord_radius,'Bakelite')
    for point in cord.data.splines[0].bezier_points:
        if abs(point.co.z-rest)<.0001:
            point.handle_left_type='FREE';point.handle_right_type='FREE'
            point.handle_left.z=rest;point.handle_right.z=rest
    rod('Ringing crank',(x-.195,y-.006,z+.116),(x-.229,y-.006,z+.116),.012,'Brass')
    rod('Crank arm',(x-.229,y-.006,z+.116),(x-.229,y-.053,z+.073),.007,'Steel')
    rod('Crank handle',(x-.232,y-.052,z+.073),(x-.258,y-.052,z+.073),.009,'DarkWalnut')
    # Docket and stationery arranged beyond the central map.
    for i in range(9):
        sheet('Orders',.93,.01,1.127+i*.0016,.311,.39,-.15+i*.011,'Dispatch' if i in (6,8) else 'Paper')
    for i in range(2):
        box('Covered ledger',(.98,-.43,1.145+i*.041),(.359,.221,.038),'Canvas' if i else 'Leather',.006,-.09+.02*i)
    box('Pencil case',(.49,.61,1.15),(.33,.098,.05),'DarkWalnut',.005)
    for i in range(2):
        a=Vector((.65+i*.038,-.30,1.14));b=a+Vector((.034,.187,-.004))
        rod('Lacquer pencil',a,b,.0044,'Wax' if i==0 else 'Canvas',8)
        rod('Graphite point',b,b+(b-a).normalized()*.014,.0026,'Graphite',7)
    box('Survey rule',(-.08,.493,1.137),(.57,.036,.009),'QuarterSawn',.001,-.035)
    for i in range(29):
        x=-.35+i*.019
        box('Ruler marks',(x,.481-(x+.08)*.035,1.142),(.0012,.012 if i%5==0 else .006,.0012),'Bakelite',0)
    # A compass lid and two counters are visible from the approach.
    turned('Compass housing',-.96,-.565,1.122,[(0,.052),(.005,.057),(.018,.054),(.022,.050)],'Brass')
    cyl('Compass face',(-.96,-.565,1.147),.047,.004,'Paper')
    box('Needle',(-.96,-.565,1.152),(.068,.006,.0035),'Wax',0,.65)
    for i in range(3):turned('Field counter',-.61+.13*i,.411,1.128,[(0,.015),(.005,.018),(.011,.011),(.031,.009),(.035,.013)],'Steel')
    working_desk()
    check_telephone_on_desk(desk_top)
    save('CommandTable')


def lantern():
    reset()
    turned('Stamped oil reservoir',0,0,0,[(0,.104),(.008,.117),(.026,.121),(.062,.11),(.089,.079),(.107,.085),(.12,.062)],'Steel')
    turned('Burner band',0,0,0,[(.096,.079),(.102,.088),(.113,.089),(.121,.073)],'Brass')
    turned('Fluted amber globe',0,0,0,[(.122,.050),(.133,.071),(.166,.081),(.232,.075),(.292,.051),(.304,.053)],'Glass')
    turned('Wick glow',0,-.076,0,[(.168,.004),(.174,.007),(.182,.004),(.199,.001)],'Flame',16)
    turned('Chimney hood',0,0,0,[(.293,.055),(.302,.084),(.313,.085),(.319,.068),(.346,.049),(.355,.062),(.364,.065),(.376,.042)],'Oxide')
    for sx in [-1,1]:
        tube('Lift tube',[(sx*.09,0,.047),(sx*.115,0,.146),(sx*.108,0,.258),(sx*.072,0,.315)],.009,'Steel')
    for sy in [-1,1]:
        tube('Globe guard',[(-.093,0,.136),(-.050,sy*.069,.168),(.031,sy*.069,.226),(.094,0,.284)],.0032,'Brass')
        tube('Globe guard',[(.093,0,.136),(.049,sy*.069,.168),(-.031,sy*.069,.226),(-.094,0,.284)],.0032,'Brass')
    tube('Carry handle',[(-.10,0,.244),(-.144,.024,.356),(-.092,.021,.449),(0,.01,.473),(.087,.021,.452),(.141,.02,.36),(.10,0,.242)],.006,'Steel')
    rod('Thumbwheel axle',(.060,-.05,.107),(.087,-.104,.11),.005,'Brass')
    cyl('Thumbwheel',(.088,-.105,.11),.017,.007,'Brass',(0,1,0))
    save('StormLantern')


def board():
    reset()
    box('Wall mapboard',(0,0,0),(2.38,.069,1.35),'DarkWalnut',.014)
    for x in [-1.15,1.15]:box('Framing fillet',(x,-.053,0),(.047,.045,1.30),'QuarterSawn',.006)
    for z in [-.64,.64]:box('Framing fillet',(0,-.053,z),(2.29,.045,.046),'QuarterSawn',.006)
    plane('Profile plate',[(-1.01,-.045,-.51),(.58,-.045,-.51),(.58,-.045,.525),(-1.01,-.045,.525)],'Profiles')
    plane('Daily return',[(.69,-.049,-.19),(1.04,-.049,-.168),(1.021,-.049,.34),(.675,-.049,.324)],'Dispatch')
    for x,z in [(-.976,.49),(.55,.49),(-.977,-.477),(.55,-.477),(.717,.28),(.992,.29)]:
        cyl('Map tack',(x,-.052,z),.008,.013,'Brass',(0,1,0),16)
    save('SurveyBoard')


def gramophone():
    reset()
    for x in [-.3,.3]:
        for y in [-.21,.21]:
            box('Stand foot',(x,y,.414),(.06,.06,.828),'DarkWalnut',.004)
    box('Stand top',(0,0,.824),(.78,.54,.063),'QuarterSawn',.011)
    box('Stand shelf',(0,0,.308),(.66,.44,.031),'DarkWalnut',.006)
    for i in range(4):
        box('Record sleeve',(-.11,.018,.339+i*.016),(.268,.273,.012),'Paper' if i%2 else 'Leather',.003,i*.029-.036)
    box('Turntable case',(0,0,.946),(.59,.431,.183),'DarkWalnut',.013)
    box('Moulded base',(0,0,.865),(.628,.453,.037),'QuarterSawn',.009)
    box('Moulded top',(0,0,1.045),(.625,.444,.044),'QuarterSawn',.008)
    for x in [-.258,.258]:
        for y in [-.178,.178]:
            cyl('Corner screw',(x,y,1.069),.008,.004,'Brass',segments=10)
    x,y=-.046,-.033
    cyl('Platter', (x,y,1.078),.159,.015,'Canvas',segments=56)
    cyl('Shellac',(x,y,1.090),.158,.005,'Bakelite',segments=88)
    cyl('Printed paper label',(x,y,1.095),.048,.003,'Wax',segments=40)
    cyl('Spindle',(x,y,1.103),.004,.019,'Brass',segments=14)
    rod('Tone arm pivot',(.198,.091,1.075),(.198,.091,1.18),.019,'Steel')
    tube('Nickel tone arm',[(.198,.091,1.174),(.20,.022,1.185),(.136,-.063,1.169),(.094,-.071,1.142)],.009,'Brass')
    cyl('Sound box',(.091,-.073,1.132),.022,.012,'Brass',(1,0,0))
    rod('Needle',(.089,-.075,1.122),(.080,-.079,1.099),.002,'Steel')
    rod('Wind crank',(.296,-.03,.94),(.366,-.03,.94),.008,'Brass')
    rod('Wind crank elbow',(.366,-.03,.94),(.366,-.11,.957),.007,'Brass')
    cyl('Crank grip',(.394,-.109,.957),.012,.067,'Bakelite',(1,0,0))
    # Curved horn, double walled, with petal seams and a rolled rim.
    centers=[];rings=[];verts=[];faces=[];nb=32;segments=16
    for i in range(segments+1):
        t=i/segments
        c=Vector((.184-.22*t*t,.128-.27*t**2.3,1.128+.64*t-.20*t*t))
        tangent=Vector((-.44*t,-.621*t**1.3,.64-.40*t)).normalized()
        u=tangent.cross(Vector((1,0,0))).normalized()
        v=tangent.cross(u).normalized()
        radius=.016+.244*t**2.8
        ring=[]
        for j in range(nb):
            a=2*pi*j/nb
            petal=1+.048*cos(a*8)*t**2
            pos=c+petal*radius*(u*cos(a)+v*sin(a))
            ring.append(pos)
            verts.append(pos[:])
        centers.append(c);rings.append(ring)
    for i in range(segments):
        for j in range(nb):faces.append((i*nb+j,i*nb+(j+1)%nb,(i+1)*nb+(j+1)%nb,(i+1)*nb+j))
    mesh=bpy.data.meshes.new('Pressed brass bell');mesh.from_pydata(verts,[],faces);mesh.update()
    o=bpy.data.objects.new('Pressed brass bell',mesh);bpy.context.collection.objects.link(o);assign(o,'Petalled acoustic horn','Brass')
    for face in mesh.polygons:face.use_smooth=True
    solid=o.modifiers.new('Folded edge','SOLIDIFY');solid.thickness=.0025
    bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=solid.name)
    for j in range(0,nb,4):
        tube('Petal rolled seam',[rings[i][j] for i in range(3,segments+1)],.002,'Oxide')
    tube('Rolled lip',rings[-1]+[rings[-1][0]],.004,"Brass")
    save('FieldGramophone')


def save(name):
    bpy.ops.wm.save_as_mainfile(filepath=str(SRC/(name+'.blend')), compress=True)
    bpy.ops.object.select_all(action='DESELECT')
    objs=[]
    for o in list(bpy.context.scene.objects):
        if o.type=='CURVE':
            bpy.context.view_layer.objects.active=o;o.select_set(True)
            bpy.ops.object.convert(target='MESH');o.select_set(False)
        if o.type=='MESH':objs.append(o)
    # Join one mesh per material to preserve Unity authoring and reduce draw calls.
    for m in materials.values():
        parts=[o for o in objs if o and o.type=='MESH' and o.data.materials and o.data.materials[0]==m]
        if not parts:continue
        for o in parts:o.select_set(True)
        bpy.context.view_layer.objects.active=parts[0]
        bpy.ops.object.join()
        combined=bpy.context.object
        combined.name=m.name
        objs=[o for o in bpy.context.scene.objects if o.type=='MESH']
        bpy.ops.object.select_all(action='DESELECT')
    for name_,pos in [('SurveyOrigin',(0,0,0)),('SurveyNorth',(0,1,0)),('SurveyUp',(0,0,1))]:
        o=bpy.data.objects.new(name_,None);bpy.context.collection.objects.link(o);o.location=pos
    bpy.ops.export_scene.fbx(filepath=str(ROOT/(name+'.fbx')),axis_forward='-Z',axis_up='Y',
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',bake_anim=False,path_mode='STRIP')


table()
if '--table-only' not in sys.argv:
    room_stores()
if '--table-only' not in sys.argv and '--office' not in sys.argv:
    lantern()
    board()
    gramophone()
