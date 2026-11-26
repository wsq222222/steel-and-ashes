"""Blender authoring source for the canvas dispatch case seen on the casemate door."""
from math import cos, pi, sin
from pathlib import Path

import bpy
from mathutils import Vector


source = Path(__file__).resolve().parent
target = source.parent / "FieldCase.fbx"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
palette = {
    "Canvas": (.19, .17, .085), "Leather": (.12, .061, .025),
    "Brass": (.45, .30, .11), "Piping": (.37, .30, .17), "Oxide": (.067, .056, .04),
}
materials = {}
for slot, rgb in palette.items():
    mat = bpy.data.materials.new(slot)
    mat.diffuse_color = (*rgb, 1)
    materials[slot] = mat


def surface(name, verts, faces, material, smooth=True):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    mesh.materials.append(materials[material])
    for poly in mesh.polygons:
        poly.use_smooth = smooth
    # Tiled real-size UVs, projected on the dominant physical surface.
    layer = mesh.uv_layers.new(name="Cloth")
    for poly in mesh.polygons:
        main = max(range(3), key=lambda i: abs(poly.normal[i]))
        flat = [i for i in range(3) if i != main]
        for li in poly.loop_indices:
            point = mesh.vertices[mesh.loops[li].vertex_index].co
            layer.data[li].uv = (point[flat[0]]*4.3+.32, point[flat[1]]*4.3+.58)
    return obj


def cord(name, coords, radius, material, closed=False):
    data = bpy.data.curves.new(name, 'CURVE')
    data.dimensions = '3D'
    data.resolution_u = 1
    data.bevel_depth = radius
    data.bevel_resolution = 2
    data.use_fill_caps = True
    spline = data.splines.new('POLY')
    spline.points.add(len(coords)-1)
    for node, point in zip(spline.points, coords):
        node.co = (*point, 1)
    spline.use_cyclic_u = closed
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    data.materials.append(materials[material])
    return obj


def ribbon(name, sections, slot, thickness):
    """Closed material ribbon; each section is its left and right edge."""
    n = len(sections)
    outside = [p for pair in sections for p in pair]
    inside = [(x, y-thickness, z) for x,y,z in outside]
    faces = []
    for i in range(0,2*n-2,2):
        faces += [(i,i+2,i+3,i+1),(2*n+i+1,2*n+i+3,2*n+i+2,2*n+i),
                  (i,2*n+i,2*n+i+2,i+2),(i+1,i+3,2*n+i+3,2*n+i+1)]
    faces += [(0,1,2*n+1,2*n),(2*n-2,4*n-2,4*n-1,2*n-1)]
    return surface(name,outside+inside,faces,slot)


# Sculpted canvas cross sections. X is width, +Y comes away from the wall, Z is up.
count = 56
profiles = [
    (-.159,.102,.030,.047),(-.155,.128,.046,.052),(-.141,.140,.053,.056),
    (-.118,.145,.055,.058),(-.082,.145,.059,.060),(-.035,.142,.060,.061),
    (.021,.142,.059,.061),(.059,.140,.057,.059),(.095,.135,.051,.058),
    (.112,.129,.046,.056),(.120,.114,.039,.055),(.122,.088,.025,.056),
]
rings = []
for row,(z,width,depth,mid) in enumerate(profiles):
    ring=[]
    for j in range(count):
        a = j*2*pi/count
        cx,cy=cos(a),sin(a)
        x=width*(1 if cx>=0 else -1)*abs(cx)**.43
        y=mid+depth*(1 if cy>=0 else -1)*abs(cy)**.48
        if cy>0:
            envelope = min(1,(z+.16)/.045,(.123-z)/.027)
            y += envelope*(.0028*sin(18*x+5*z)+.0019*cos(73*x+29*z)+.0012*sin(110*x-20*z))
            # Two creases run diagonally away from the buckles at the bottom.
            for side in (-1,1):
                offset = (x-side*(.090+.24*(z+.12)))
                y -= envelope*.0046*max(0,1-abs(offset)/.011)*max(0,1-abs(z+.09)/.09)
        ring.append((x,max(.003,y),z+.0016*sin(a*3+row*.16)))
    rings.append(ring)
vertices=[v for ring in rings for v in ring]
faces=[tuple(range(count-1,-1,-1))]
for r in range(len(rings)-1):
    for j in range(count):
        k=(j+1)%count
        faces.append((r*count+j,r*count+k,(r+1)*count+k,(r+1)*count+j))
faces.append(tuple(range((len(rings)-1)*count,len(rings)*count)))
surface("Packed canvas",vertices,faces,"Canvas")
cord("Bound bottom seam",rings[2],.0021,"Piping",True)

# Overlapping flap with an arched lip, fabric thickness and hand stitching.
flap=[]
width_steps, length_steps = 25, 10
for v in range(length_steps):
    t=v/(length_steps-1)
    edge_width=.133-(.006*t*t)
    for u in range(width_steps):
        q=u/(width_steps-1)*2-1
        x=q*edge_width
        y=.061+.068*sin(min(1,t*2.3)*pi/2)-.019*abs(q)**4+.0018*cos(x*57+4*t)
        z=.123-.131*t**1.55+.020*(q*q)*t
        flap.append((x,y,z))
flap_faces=[]
for row in range(length_steps-1):
    for col in range(width_steps-1):
        a=row*width_steps+col
        flap_faces.append((a,a+1,a+1+width_steps,a+width_steps))
cap=surface("Rolled canvas flap",flap,flap_faces,"Canvas")
thick=cap.modifiers.new("Flap hem thickness","SOLIDIFY")
thick.thickness=.0035
thick.offset=-1
front=flap[-width_steps:]
cord("Folded hem",front,.0027,"Canvas")
for i in range(0,width_steps-1,2):
    a,b=front[i],front[i+1]
    cord("Hem stitch",[(a[0],a[1]+.0015,a[2]+.004),(b[0],b[1]+.0015,b[2]+.004)],.00085,"Piping")

# Worn external carrying straps and their hardware are visible at typical FPS distance.
for side in [-1,1]:
    x=side*.088
    rows=[(-.122,.121,.008),(-.113,.126,.012),(-.067,.132,.013),(-.030,.131,.013),
          (.010,.134,.014),(.057,.133,.014),(.093,.133,.014),(.110,.128,.014),
          (.118,.116,.014),(.124,.100,.014),(.127,.080,.014),(.127,.059,.014)]
    strip=[((x-w,y,z),(x+w,y,z)) for z,y,w in rows]
    ribbon("Overflap strap",strip,"Leather",.003)
    for off in (-.010,.010):
        cord("Edge of strap",[(x+off,y+.0008,z) for z,y,w in rows[1:]],.0007,"Piping")
    # An open rectangular buckle silhouette, with rounded corners and a central tongue.
    y,z=.137,-.042
    points=[(-.018,-.011),(-.018,.011),(-.014,.016),(.014,.016),(.018,.011),(.018,-.011),(.014,-.016),(-.014,-.016)]
    cord("Buckle frame",[(x+a,y,z+b) for a,b in points],.0024,"Brass",True)
    cord("Buckle pin",[(x,y+.001,z-.014),(x,y+.004,z+.011)],.0017,"Brass")
    # The two riveted wall tabs are visibly anchored to the back of the pouch.
    ribbon("Mounting ear",[((x-.017,.006,.083),(x+.017,.006,.083)),
                           ((x-.016,.006,.141),(x+.016,.006,.141)),
                           ((x-.011,.006,.151),(x+.011,.006,.151))],"Leather",.0055)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=6,location=(x,.0075,.137))
    rivet=bpy.context.object;rivet.name="Mounting rivet";rivet.scale=(.006,.0025,.006)
    rivet.data.materials.append(materials["Brass"])

# Flattened leather grip bends around the top; opening clearly silhouettes in oblique views.
grip=[]
for i in range(29):
    t=i/28
    x=-.084+.168*t
    z=.120+.046*max(0,sin(pi*t))**.67
    y=.057+.005*sin(t*pi)
    grip.append(((x,y,z-.007),(x,y,z+.007)))
ribbon("Raised handle",grip,"Leather",.010)
cord("Stitched grip",[(a[0],a[1]+.0005,a[2]+.002) for a,b in grip],.0009,"Piping")

# Maker's brass plaque and stamped abstract tally (visible identifying mark).
plaque=[((-.029,.133,.031),(.030,.133,.031)),((-.035,.134,.036),(.035,.134,.036)),
        ((-.035,.134,.056),(.035,.134,.056)),((-.030,.133,.061),(.029,.133,.061))]
ribbon("Brass tally plate",plaque,"Brass",.002)
for x in [-.026,.026]:
    cord("Plaque rivet",[(x-.001,.136,.046),(x+.001,.136,.046)],.0015,"Oxide")
for x in [-.011,-.005]:
    cord("Stamped tally",[(x,.136,.039),(x,.136,.052)],.0011,"Oxide")
cord("Stamped tally",[(.001,.136,.052),(.006,.136,.039),(.012,.136,.052)],.0011,"Oxide")

# Freeze the authored surfaces into five draw batches.
for obj in list(bpy.context.scene.objects):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True);bpy.context.view_layer.objects.active=obj
    if obj.type=='CURVE' or obj.modifiers:
        bpy.ops.object.convert(target='MESH')
for slot,mat in materials.items():
    objs=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.data.materials[0]==mat]
    bpy.ops.object.select_all(action='DESELECT')
    for ob in objs: ob.select_set(True)
    bpy.context.view_layer.objects.active=objs[0]
    bpy.ops.object.join()
    obj=bpy.context.object
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    obj.name="Case_"+slot
    triangulate=obj.modifiers.new("Game triangles","TRIANGULATE")
    bpy.ops.object.modifier_apply(modifier=triangulate.name)
    # Uniform face orientation is required for the opaque, single-sided URP cloth.
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')

for name,position in [("CaseOrigin",(0,0,0)),("CaseOutward",(0,.1,0)),("CaseUp",(0,0,.1))]:
    obj=bpy.data.objects.new(name,None);obj.empty_display_size=.012
    bpy.context.collection.objects.link(obj);obj.location=position
bpy.ops.wm.save_as_mainfile(filepath=str(source/"FieldCase.blend"),compress=True)
bpy.ops.export_scene.fbx(filepath=str(target),axis_forward='-Z',axis_up='Y',apply_unit_scale=True,
                         apply_scale_options='FBX_SCALE_UNITS',add_leaf_bones=False,
                         bake_anim=False,path_mode='STRIP',use_tspace=True)
print("Authored field case:",[(o.name,len(o.data.polygons)) for o in bpy.context.scene.objects if o.type=="MESH"])
