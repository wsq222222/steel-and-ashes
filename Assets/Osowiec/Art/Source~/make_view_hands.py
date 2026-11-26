"""Build fitted exterior gloves and worn greatcoat sleeves for the Osowiec viewmodel."""
from math import cos, pi, sin
from pathlib import Path
import sys

import bpy
from mathutils import Vector


ART = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name, tone, roughness):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*tone, 1)
    shader = mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*tone, 1)
    shader.inputs["Roughness"].default_value = roughness
    return mat


wool = material("Sleeve · Wet olive wool", (.172, .148, .094), .91)
pleats = material("Sleeve · Fold shadow", (.128, .109, .073), .90)
seam = material("Sleeve · Khaki seam", (.228, .190, .127), .93)
cuff = material("Sleeve · Turned cuff", (.106, .091, .062), .94)
hide = material("Gloves · Worn hide", (.135, .078, .043), .78)
hide_lit = material("Gloves · Knuckle wear", (.191, .115, .064), .86)
thread = material("Gloves · Dark stitching", (.084, .057, .032), .94)


def tube(name, rings, materials, angular_wear=0, sides=18):
    """Loft oriented elliptical cloth or leather profiles and give each quad a usable UV."""
    coordinates, faces = [], []
    axis = (Vector(rings[-1][0]) - Vector(rings[0][0])).normalized()
    across = axis.cross(Vector((0, 0, 1))).normalized()
    around = across.cross(axis).normalized()
    for row, (center, width, height) in enumerate(rings):
        for side in range(sides):
            angle = side * 2 * pi / sides
            bunch = 1 + angular_wear * sin(3 * angle + row * .94) + .024 * sin(7 * angle + row * .5)
            point = Vector(center) + bunch * (across * width * cos(angle) + around * height * sin(angle))
            coordinates.append(point)
    for row in range(len(rings) - 1):
        for side in range(sides):
            a = row * sides + side
            b = row * sides + (side + 1) % sides
            faces.append((a, a + sides, b + sides, b))
    faces.extend([tuple(range(sides)), tuple(reversed(range(len(coordinates) - sides, len(coordinates))))])
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(coordinates, [], faces)
    mesh.update()
    uv_map = mesh.uv_layers.new(name="Material UV")
    for face in mesh.polygons:
        if len(face.vertices) != 4:
            continue
        row, side = divmod(face.index, sides)
        slots = ((side / sides, row / len(rings)), (side / sides, (row + 1) / len(rings)),
                 ((side + 1) / sides, (row + 1) / len(rings)), ((side + 1) / sides, row / len(rings)))
        for loop, uv in zip(face.loop_indices, slots):
            uv_map.data[loop].uv = uv
        face.use_smooth = True
        if len(materials) > 1:
            face.material_index = 2 if side == 1 or side == sides - 2 else int((row + side * 3) % 11 == 0)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    for mat in materials:
        mesh.materials.append(mat)
    return obj


def oval(name, center, dimensions, mat, angle=0):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=10, location=center)
    obj = bpy.context.object
    obj.name = name
    obj.scale = dimensions
    obj.rotation_euler.x = angle
    obj.data.materials.append(mat)
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return obj


def hand(right):
    # Both arm assemblies share FieldRifle.blend's authoring coordinates (-X forward, +Z up).
    if right:
        pivot = Vector((-.075, .040, .331))
        wrist, elbow = Vector((.019, .075, .291)), Vector((.285, .169, .150))
        side = 1
    else:
        pivot = Vector((-.383, -.045, .403))
        wrist, elbow = Vector((-.279, -.103, .349)), Vector((-.095, -.290, .160))
        side = -1
    marker = bpy.data.objects.new("FP_RightHand" if right else "FP_LeftHand", None)
    bpy.context.collection.objects.link(marker)
    marker.location = pivot
    marker.empty_display_size = .05
    coats = []
    for t, w, h in ((0, .068, .062), (.14, .071, .059), (.30, .067, .056),
                    (.44, .060, .051), (.58, .064, .050), (.72, .057, .047),
                    (.86, .054, .048), (1, .045, .043)):
        center = elbow.lerp(wrist, t) + Vector((0, 0, .012 * sin(pi * t)))
        coats.append((center, w, h))
    parts = [tube("Bunched wool sleeve", coats, [wool, pleats, seam], .09)]
    parts.append(tube("Turnback cuff", [(elbow.lerp(wrist, .85), .058, .054),
                                         (elbow.lerp(wrist, .93), .055, .052),
                                         (wrist, .049, .048)], [cuff], .05))
    parts.append(oval("Fitted glove palm", pivot + Vector((.018, side * .006, -.004)), (.059, .036, .030),
                      hide, side * .3))
    parts.append(oval("Creased back of glove", pivot + Vector((.010, side * .019, .016)),
                      (.041, .022, .017), hide_lit, side * -.12))
    for index in range(4):
        x = -.039 + index * .021
        radius = .0108 - index * .0007
        points = [(pivot + Vector((x, side * .026, .011)), radius, radius),
                  (pivot + Vector((x - .008, side * .004, -.014)), radius, radius * .92),
                  (pivot + Vector((x - .006, side * -.018, -.005)), radius * .84, radius * .78),
                  (pivot + Vector((x - .004, side * -.025, .012)), radius * .69, radius * .60)]
        parts.append(tube("Bent glove finger " + str(index + 1), points, [hide], sides=10))
        parts.append(oval("Worn knuckle ridge", pivot + Vector((x, side * .027, .015)),
                          (.008, .009, .007), hide_lit))
    parts.append(tube("Thumb", [(pivot + Vector((.044, side * .028, -.009)), .015, .014),
                                 (pivot + Vector((.028, side * .013, .018)), .012, .012),
                                 (pivot + Vector((.002, side * -.015, .024)), .008, .009)], [hide_lit], sides=12))
    for index in range(5):
        parts.append(oval("Saddle stitch", pivot + Vector((-.007 + .010 * index, side * .035, .012)),
                          (.0048, .002, .002), thread))
    bpy.ops.object.select_all(action="DESELECT")
    for obj in parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    mesh = bpy.context.object
    mesh.name = marker.name + "_Mesh"
    world = mesh.matrix_world.copy()
    mesh.parent = marker
    mesh.matrix_world = world


hand(True)
hand(False)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.wm.save_as_mainfile(filepath=str(ART / "Source~" / "OsowiecViewHands.blend"), compress=True)
bpy.ops.export_scene.fbx(filepath=str(ART / "OsowiecViewHands.fbx"), use_selection=True, bake_anim=False,
                        axis_forward="-Z", axis_up="Y", apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS")
