"""Build the mission telephone and its drop pair from the editable Fort I and HQ assets."""
from pathlib import Path
from math import cos, sin, pi, radians
import bpy
import bmesh
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
ART = HERE.parent
HQ = ART.parent / "HQ" / "Art"
DESK = .9725
CENTRE = Vector((-14.98, -4.875, DESK))
PHONE_PARTS = {
    'Telephone foot', 'Field telephone chest', 'Tinned faceplate', 'Faceplate fastener',
    'Bell dome', 'Terminal screw', 'Handset fork', 'Bakelite handset bridge', 'Receiver cup',
    'Handset strain relief', 'Rear cord gland', 'Telephone lead', 'Ringing crank',
    'Crank arm', 'Crank handle'
}
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene


def import_parts(path, keep):
    with bpy.data.libraries.load(str(path)) as (source, target):
        target.objects = [name for name in source.objects if keep(name)]
    for obj in target.objects:
        scene.collection.objects.link(obj)
    bpy.context.view_layer.update()
    return target.objects


def geometry(name, verts, faces, material):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], faces)
    mesh.materials.append(bpy.data.materials[material])
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    return obj


def cylinder(name, start, end, radius, mat, sides=14, top_radius=None):
    a,b = Vector(start),Vector(end)
    axis = (b-a).normalized()
    u = axis.orthogonal().normalized()
    v = axis.cross(u)
    vertices = [p+r*(cos(i*2*pi/sides)*u+sin(i*2*pi/sides)*v)
                for p,r in ((a,radius),(b,top_radius if top_radius is not None else radius))
                for i in range(sides)]
    faces = [tuple(reversed(range(sides))),tuple(range(sides,2*sides))]
    faces += [(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)]
    obj = geometry(name, vertices, faces, mat)
    for poly in list(obj.data.polygons)[2:]:
        poly.use_smooth=True
    return obj


def cable(name, points, radius, material):
    curve=bpy.data.curves.new(name,'CURVE')
    curve.dimensions='3D'
    curve.resolution_u=6
    curve.bevel_depth=radius
    curve.bevel_resolution=2
    curve.resolution_u=8
    curve.use_fill_caps=True
    spline=curve.splines.new('BEZIER')
    spline.bezier_points.add(len(points)-1)
    for knot,point in zip(spline.bezier_points,points):
        knot.co=point
        knot.handle_left_type=knot.handle_right_type='AUTO'
    obj=bpy.data.objects.new(name,curve)
    curve.materials.append(bpy.data.materials[material])
    scene.collection.objects.link(obj)
    return obj


station,pole = sorted(import_parts(HERE/'Osowiec_FortI_1916.blend',
                                   lambda name: name in ('Field_station','Telegraph')),
                      key=lambda obj:obj.name)
# Keep every original shelter plank with its UVs, including the support shelf.
shell=bmesh.new()
shell.from_mesh(station.data)
bmesh.ops.delete(shell,geom=[f for f in shell.faces
                             if station.data.materials[f.material_index].name!='Timber'],context='FACES')
shell.to_mesh(station.data)
shell.free()
wood=bpy.data.materials['Timber']
station.data.materials.clear()
station.data.materials.append(wood)
for face in station.data.polygons:
    face.material_index=0

# Each leg ends at the actual vertex ring of an insulator on the existing overhead line.
insulators=[v.co.copy() for p in pole.data.polygons
            if pole.data.materials[p.material_index].name=='Concrete'
            for v in (pole.data.vertices[i] for i in p.vertices)
            if -17.3 < v.co.x < -16.9 and -6.6 < v.co.y < -5.5]
assert len(insulators)>=32
anchors=[]
for rear in (False,True):
    ring=[v for v in insulators if (v.y<-6.1)==rear]
    low,high=min(v.z for v in ring),max(v.z for v in ring)
    anchors.append(Vector(((max(v.x for v in ring)+min(v.x for v in ring))*.5,
                          (max(v.y for v in ring)+min(v.y for v in ring))*.5,high-.035)))
bpy.data.objects.remove(pole,do_unlink=True)

phone=import_parts(HQ/'Source~'/'CommandTable.blend',
                   lambda n:n.split('.')[0] in PHONE_PARTS)
assert {o.name.split('.')[0] for o in phone}==PHONE_PARTS
rotation=Matrix.Rotation(radians(108),4,'Z')
placement=Matrix.Translation(CENTRE) @ rotation @ Matrix.Scale(1.18,4)
move=placement @ Matrix.Translation(Vector((1.085,-.615,-1.118)))
for obj in phone:
    obj.matrix_world=move @ obj.matrix_world
# The desk model has a paired magneto bell, but the outdoor instrument also needs
# two binding posts and a continuous lead to the line outside the shelter.
for material,colour in [('HQ_Enamel',(.59,.61,.50,1)), ('HQ_Graphite',(.085,.077,.063,1))]:
    if bpy.data.materials.get(material) is None:
        bpy.data.materials.new(material).diffuse_color=colour
terminal_points=[]
for i,anchor in enumerate(anchors):
    side=-1 if i==0 else 1
    x=side*.075
    def handset_point(y,z):
        return placement @ Vector((x,y,z))
    cylinder('Porcelain terminal bushing',handset_point(.14,.139),handset_point(.162,.139),.017,'HQ_Enamel')
    cylinder('Knurled line post',handset_point(.163,.139),handset_point(.178,.139),.013,'HQ_Brass')
    jack=handset_point(.179,.139)
    terminal_points.append(jack)
    wall_x=-15.40+i*.088
    # Cable passes through the back of the shelter below the eave, then turns up outside the drip edge.
    inner=Vector((wall_x,-5.06,1.67+i*.065))
    outer=Vector((wall_x,-5.17,inner.z))
    cylinder('Ebonite through-wall sleeve',inner,outer,.017,'HQ_Bakelite')
    for q in (0,.013,.029):
        cylinder('Glazed retaining collar',inner+Vector((0,.006+q,0)),inner+Vector((0,.014+q,0)),.032-q*.22,'HQ_Enamel',20)
    for z in (1.21,1.46):
        cylinder('Cable staple', (wall_x,-5.074,z), (wall_x,-5.037,z),.015,'HQ_Brass')
    cable('Covered drop inside shelter',[jack,jack+Vector((-.055,.005,-.018)),
          (wall_x+.09,-4.985,1.005+i*.014),(wall_x,-5.035,1.10),
          (wall_x,-5.045,1.28),(wall_x,-5.045,1.49),inner],.007,'HQ_Graphite')
    neck=Vector((wall_x,-5.33,2.12+i*.06))
    for q in (0,.018,.036):
        cylinder('Outdoor drop insulator',neck+Vector((0,0,q)),neck+Vector((0,0,q+.011)),.032,'HQ_Enamel',20)
    cylinder('Eave bracket arm',(wall_x,-5.11,1.91),(wall_x,-5.33,1.91),.015,'HQ_Steel')
    cylinder('Eave bracket pin',(wall_x,-5.33,1.91),neck+Vector((0,0,.03)),.014,'HQ_Steel')
    seat=neck+Vector((0,0,.029))
    cable('Downlead clear of roof',[outer, (wall_x,-5.292,1.72+i*.055),
          (wall_x,-5.34,1.92),seat],.007,'HQ_Graphite')
    points=[seat.lerp(anchor,t/10)+Vector((0,0,-.18*sin(pi*t/10))) for t in range(11)]
    cable('Two-wire service drop',points,.009,'HQ_Graphite')

# Maintain recognisable, separable model parts in Blender. Unity receives a single
# mesh with shared material slots and the same survey-local axes as the Fort I mesh.
bpy.context.view_layer.update()
feet=[o for o in phone if o.name.startswith('Telephone foot')]
assert len(feet)==4
for foot in feet:
    bottom=min((foot.matrix_world@v.co).z for v in foot.data.vertices)
    assert abs(bottom-DESK)<.0005, (foot.name,bottom)
assert len(anchors)==2 and (anchors[0]-anchors[1]).length>.7
assert all(2 < (a-j).length < 5 for a,j in zip(anchors,terminal_points))
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'FieldTelephoneStation.blend'),compress=True)
bpy.ops.object.select_all(action='DESELECT')
for obj in list(scene.objects):
    if obj.type=='CURVE':
        bpy.context.view_layer.objects.active=obj
        obj.select_set(True)
        bpy.ops.object.convert(target='MESH')
        obj.select_set(False)
bpy.ops.object.select_all(action='SELECT')
bpy.context.view_layer.objects.active=station
bpy.ops.object.join()
station.name=station.data.name='FieldStationVisual'
print('FIELD PHONE:',len(station.data.vertices),'vertices',len(station.data.polygons),'polygons;',
      [m.name for m in station.data.materials], 'feet',len(feet),'anchors',[tuple(a) for a in anchors])
for axis,point in [('Survey_origin',(0,0,0)),('Survey_east',(1,0,0)),
                   ('Survey_north',(0,1,0)),('Survey_up',(0,0,1))]:
    mark=bpy.data.objects.new(axis,None)
    scene.collection.objects.link(mark)
    mark.location=point
bpy.ops.export_scene.fbx(filepath=str(ART/'Fort1916'/'FieldTelephoneStation.fbx'),
                         axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_UNITS',
                         bake_anim=False,object_types={'MESH','EMPTY'},mesh_smooth_type='FACE',
                         path_mode='STRIP',use_custom_props=False)

