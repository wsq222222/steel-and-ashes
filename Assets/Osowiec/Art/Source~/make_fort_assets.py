"""Generate stylised fortress modules and original Blender sources for Osowiec."""
from math import cos, sin, pi, radians
from pathlib import Path
import random
import sys

import bpy
from mathutils import Vector


output = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
output.mkdir(parents=True, exist_ok=True)
source = output / "Source~"
source.mkdir(exist_ok=True)


def material(name, rgb, roughness=.76, metallic=0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*rgb, 1)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    return mat


def style(obj, name, mat, bevel=0.0):
    obj.name = name
    obj.data.materials.append(mat)
    if bevel:
        modifier = obj.modifiers.new("Rounded edge", "BEVEL")
        modifier.width = bevel
        modifier.segments = 2
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    return obj


def box(name, center, size, mat, bevel=.015):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    obj = bpy.context.object
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return style(obj, name, mat, bevel)


def mesh(name, vertices, faces, mat):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    return obj


def wedge(name, inner, outer, start, end, z0, z1, mat, vertical=False):
    points = [
        (radius * cos(angle), radius * sin(angle), height)
        for height in (z0, z1)
        for radius, angle in ((inner, start), (outer, start), (outer, end), (inner, end))
    ]
    if vertical:
        points = [(x, -z, y + .93) for x, y, z in points]
    return mesh(name, points, [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4),
                               (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)], mat)


def cone(name, p0, p1, r0, r1, mat, count=12):
    a, b = Vector(p0), Vector(p1)
    bpy.ops.mesh.primitive_cone_add(vertices=count, radius1=r0, radius2=r1,
                                   depth=(b - a).length, location=(a + b) / 2)
    obj = bpy.context.object
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = (b - a).to_track_quat("Z", "Y")
    return style(obj, name, mat)


def arch(palette):
    stone, iron, timber, canvas, bricks = palette
    for side in (-1, 1):
        for index in range(5):
            box("Brick jamb", (side * 1.28, 0, (index + .5) * .184),
                (.27, .28, .178), bricks[(side + index) % len(bricks)], .008)
        box("Plinth", (side * 1.28, 0, .077), (.36, .37, .15), stone)
    for i in range(19):
        a, b = i * pi / 19 + .007, (i + 1) * pi / 19 - .007
        wedge("Radial brick", 1.145, 1.415 if i != 9 else 1.47, a, b, -.15, .15,
              bricks[(i * 3) % len(bricks)], True)


def platform(palette):
    stone, iron, timber, canvas, bricks = palette
    cone("Concrete platform", (0, 0, -.07), (0, 0, .06), 2.47, 2.44, stone, 40)
    for i in range(28):
        center = i * 2 * pi / 28
        if min(abs(center - pi), center, abs(center - 2 * pi)) < .45:
            continue
        angle = pi / 28 - .005
        wedge("Cut masonry parapet", 2.15, 2.48, center - angle, center + angle,
              .025, .55 if i % 6 != 0 else .43, bricks[i % len(bricks)])
        wedge("Coping stone", 2.11, 2.51, center - angle, center + angle,
              .55 if i % 6 != 0 else .43, .64 if i % 6 != 0 else .52, stone)
    # Solid exterior sculpture, so it reads as a battery from the map camera.
    cone("Display pedestal", (0, 0, .05), (0, 0, .33), .74, .56, iron, 20)
    box("Shield", (0, -.16, .73), (.92, .22, .82), iron, .045)
    box("Mount", (0, .23, .6), (.54, 1.12, .39), iron, .035)
    cone("Exterior barrel silhouette", (0, -.2, .84), (0, -2.37, 1.30), .137, .078, iron)
    cone("Decorative collar", (0, -.71, .95), (0, -1.03, 1.02), .167, .145, iron, 16)


def sandbags(palette):
    stone, iron, timber, canvas, bricks = palette
    rng = random.Random(82)
    for row in range(3):
        for column in range(4 - (row == 2)):
            x = (column - (3 - (row == 2)) / 2) * .384 + (row % 2) * .06
            bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8,
                                                location=(x, rng.uniform(-.04, .04), .105 + row * .15))
            obj = bpy.context.object
            obj.scale = (.25, .245, .116)
            obj.rotation_euler.z = rng.uniform(-.13, .13)
            style(obj, "Sewn canvas bag", canvas[column % len(canvas)])
            for polygon in obj.data.polygons:
                polygon.use_smooth = True


def tree(palette):
    stone, iron, timber, canvas, bricks = palette
    trunk = [(0, 0, -.04), (.035, .08, .53), (-.12, .09, 1.50),
             (-.12, .23, 2.27), (-.41, .35, 3.17)]
    radii = [.26, .20, .151, .10, .023]
    for i in range(4):
        cone("Shattered trunk", trunk[i], trunk[i + 1], radii[i], radii[i + 1], timber, 7)
    tips = [(.61, -.12, 2.17), (-.63, -.35, 2.65), (.28, .45, 2.99), (-.76, .18, 3.18)]
    for i, tip in enumerate(tips):
        root = Vector(trunk[2 + i // 2])
        middle = root.lerp(Vector(tip), .64)
        cone("Broken bough", root, middle, .083, .043, timber, 6)
        cone("Split edge", middle, tip, .043, .012, timber, 6)


def export(name, factory):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    stone = material("HewnConcrete", (.24, .235, .20))
    iron = material("DarkOxidizedIron", (.08, .091, .088), .55, .65)
    timber = material("StormTimber", (.112, .081, .056))
    canvas = [material(f"Canvas{i}", (.291 - i * .024, .233 - i * .022, .162 - i * .018))
              for i in range(4)]
    bricks = [material(f"Firebrick{i}", (.205 + i * .024, .102 + i * .011, .067 + i * .008))
              for i in range(5)]
    factory((stone, iron, timber, canvas, bricks))
    bpy.ops.object.select_all(action="SELECT")
    objects = bpy.context.selected_objects
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    print(f"{name}: {len(obj.data.vertices)} vertices, {len(obj.data.polygons)} faces")
    bpy.ops.wm.save_as_mainfile(filepath=str(source / f"{name}.blend"), compress=True)
    bpy.ops.export_scene.fbx(filepath=str(output / f"{name}.fbx"), use_selection=True,
                            bake_anim=False, axis_forward="-Z", axis_up="Y", apply_unit_scale=True,
                            apply_scale_options="FBX_SCALE_UNITS")


for name, factory in (("CasemateArch", arch), ("BatteryEmplacement", platform),
                      ("SandbagCover", sandbags), ("ShatteredTree", tree)):
    export(name, factory)
