"""Build an original solid exterior prop for the fictional Osowiec units."""
from math import cos, pi, sin
from pathlib import Path
import sys

import bpy
from mathutils import Vector


ART = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
SOURCE = ART / "Source~"
bpy.ops.wm.read_factory_settings(use_empty=True)


def surface(name, tint, roughness, metallic=0):
    result = bpy.data.materials.new(name)
    result.diffuse_color = (*tint, 1)
    shader = result.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*tint, 1)
    shader.inputs["Roughness"].default_value = roughness
    shader.inputs["Metallic"].default_value = metallic
    return result


wood = surface("Field Rifle · Weathered walnut", (.195, .093, .047), .64)
dark_wood = surface("Field Rifle · Dark end grain", (.107, .049, .027), .74)
steel = surface("Field Rifle · Blued metal", (.073, .086, .093), .48, .65)
iron = surface("Field Rifle · Worn edges", (.136, .145, .137), .62, .52)
cloth = surface("Field Rifle · Khaki wrap", (.29, .235, .147), .89)


def finish(obj, name, material, bevel=0):
    obj.name = name
    obj.data.materials.append(material)
    if bevel > 0:
        mod = obj.modifiers.new("Rounded silhouette", "BEVEL")
        mod.width = bevel
        mod.segments = 2
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return obj


def block(name, xyz, scale, material, bevel=.005):
    bpy.ops.mesh.primitive_cube_add(size=1, location=xyz)
    item = bpy.context.object
    item.dimensions = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(item, name, material, bevel)


def cylinder(name, start, end, start_radius, end_radius, material):
    a, b = Vector(start), Vector(end)
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=start_radius, radius2=end_radius,
                                   depth=(a-b).length, location=(a+b)*.5)
    item = bpy.context.object
    item.rotation_mode = "QUATERNION"
    item.rotation_quaternion = (b-a).to_track_quat("Z", "Y")
    return finish(item, name, material)


def stock():
    sides = 12
    profiles = [
        (.210, .354, .027, .066), (.195, .352, .034, .071),
        (.135, .355, .035, .064), (-.035, .393, .029, .043),
        (-.103, .379, .027, .052), (-.156, .392, .031, .040),
        (-.237, .413, .035, .035), (-.418, .433, .030, .025),
        (-.557, .437, .027, .017), (-.602, .441, .025, .015),
    ]
    points, faces = [], []
    for x, z, width, height in profiles:
        for i in range(sides):
            theta = 2 * pi * i / sides
            points.append((x, width * sin(theta), z + height * cos(theta)))
    faces.append(tuple(reversed(range(sides))))
    for row in range(len(profiles) - 1):
        for column in range(sides):
            a = row * sides + column
            b = row * sides + (column + 1) % sides
            faces.append((a, b, b + sides, a + sides))
    faces.append(tuple(range(len(points) - sides, len(points))))
    mesh = bpy.data.meshes.new("Hand-styled wooden silhouette")
    mesh.from_pydata(points, [], faces)
    mesh.update()
    item = bpy.data.objects.new("Carved exterior", mesh)
    bpy.context.collection.objects.link(item)
    item.data.materials.append(wood)
    return item


stock()
block("Dark butt trim", (.213, 0, .353), (.021, .062, .127), dark_wood)
block("Steel body silhouette", (-.245, 0, .457), (.30, .055, .048), steel)
cylinder("Solid forward silhouette", (-.319, 0, .473), (-.807, 0, .473), .020, .012, steel)
for x in (-.546, -.433):
    block("Dull exterior metal band", (x, 0, .443), (.021, .062, .054), iron)
for x in (-.21, -.25, -.289):
    block("Aged exterior plate", (x, .027, .455), (.011, .004, .008), iron, .001)
block("Protective brow", (-.719, 0, .494), (.038, .035, .015), steel, .004)
block("Rear cover", (-.32, 0, .497), (.055, .022, .021), iron, .004)
for i in range(3):
    block("Cloth wrap", (.149 + i * .012, 0, .356), (.01, .071, .127), cloth, .004)

bpy.ops.object.select_all(action="SELECT")
bpy.context.view_layer.objects.active = bpy.context.selected_objects[0]
bpy.ops.object.join()
body = bpy.context.object
body.name = "FieldRifle"
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.context.scene.cursor.location = (0, 0, 0)
bpy.ops.object.origin_set(type="ORIGIN_CURSOR")

for name, location in (
    ("FieldRifleMuzzle", (-.80713, -.008, .47313)),
    ("FieldRifleGrip", (-.1, 0, .35)),
):
    marker = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(marker)
    marker.empty_display_size = .025
    marker.location = location

bpy.ops.object.select_all(action="SELECT")
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / "FieldRifle.blend"), compress=True)
bpy.ops.export_scene.fbx(filepath=str(ART / "FieldRifle.fbx"), use_selection=True, bake_anim=False,
                        axis_forward="-Z", axis_up="Y", apply_unit_scale=True,
                        apply_scale_options="FBX_SCALE_UNITS")
