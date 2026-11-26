"""Create original interwar-inspired uniform pieces for existing animated game rigs."""
from math import cos, sin, pi
from pathlib import Path
import sys

import bpy


ART = Path(sys.argv[sys.argv.index("--") + 1]).resolve()
SOURCE = ART / "Source~"


def surface(name, color, roughness):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    shader = mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color, 1)
    shader.inputs["Roughness"].default_value = roughness
    return mat


def style(item, name, material):
    item.name = name
    item.data.materials.append(material)
    return item


def box(name, place, size, material, slant=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=place)
    item = bpy.context.object
    item.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    item.rotation_euler.y = slant
    modifier = item.modifiers.new("Soft cloth edge", "BEVEL")
    modifier.width = min(.012, min(size) * .22)
    modifier.segments = 2
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    return style(item, name, material)


def profile(name, rings, material, segments=24, caps=True):
    points, faces = [], []
    for z, xradius, yradius, yshift in rings:
        for i in range(segments):
            theta = 2 * pi * i / segments
            points.append((xradius * cos(theta), yshift + yradius * sin(theta), z))
    for r in range(len(rings) - 1):
        for i in range(segments):
            a = r * segments + i
            b = r * segments + (i + 1) % segments
            faces.append((a, b, b + segments, a + segments))
    if caps:
        faces += [tuple(reversed(range(segments))), tuple(range(len(points) - segments, len(points)))]
    data = bpy.data.meshes.new(name)
    data.from_pydata(points, [], faces)
    data.update()
    item = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(item)
    item.data.materials.append(material)
    for poly in data.polygons:
        poly.use_smooth = len(poly.vertices) == 4
    return item


def button(name, position, metal, size=.009):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=size, location=position)
    item = bpy.context.object
    item.scale.y = .36
    style(item, name, metal)


def headgear(palette):
    cloth, stitching, leather, brass, glass = palette
    profile("Felt field helmet", [(.009,.145,.151,0),(.034,.151,.151,0),(.096,.136,.135,.012),
                                   (.146,.106,.110,.025),(.154,.061,.072,.028)], cloth)
    profile("Weathered brim", [(-.004,.160,.173,-.009),(.006,.161,.173,-.009),
                                (.013,.146,.156,0)], stitching)
    profile("Brow strap", [(.040,.152,.155,0),(.050,.152,.155,0)], leather, caps=False)
    bridge = box("Canvas goggles bridge", (0,-.144,.053), (.106,.025,.032), leather)
    bridge.rotation_euler.x = -.14
    for side in (-1, 1):
        bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=.041, depth=.019,
             location=(side*.047,-.150,.055), rotation=(pi/2,0,0))
        style(bpy.context.object, "Goggle rim", brass)
        bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=.032, depth=.011,
             location=(side*.047,-.163,.055), rotation=(pi/2,0,0))
        style(bpy.context.object, "Smoked goggle lens", glass)
    button("Field insignia", (0,-.121,.120), brass, .015)


def coat(palette):
    cloth, stitching, leather, brass, glass = palette
    profile("Heavy canvas greatcoat", [(-.09,.288,.176,.023),(-.03,.281,.173,.018),
                                       (.15,.245,.145,0),(.31,.254,.151,-.014),
                                       (.43,.296,.171,-.011),(.50,.268,.133,0),
                                       (.54,.137,.094,0)], cloth)
    profile("Turned collar", [(.502,.152,.098,0),(.557,.135,.104,0),(.583,.111,.090,0)], stitching)
    profile("Leather waist belt", [(.155,.255,.151,-.002),(.177,.254,.151,-.003),
                                    (.203,.249,.151,-.004)], leather, caps=False)
    box("Belt clasp", (.018,-.163,.178), (.083,.014,.055), brass)
    box("Double-breasted seam", (-.048,-.159,.312), (.028,.019,.236), stitching, .035)
    for level in range(4):
        for side in (-1, 1):
            button("Coat button", (side*.043,-.173,.233 + level*.061), brass)
    for side in (-1, 1):
        epaulet = box("Shoulder tab", (side*.208,-.021,.477), (.104,.181,.027), stitching, side*.34)
        epaulet.rotation_euler.z = side*.12
    box("Messenger strip", (.045,-.146,.340), (.043,.021,.376), leather, -.56)
    box("Field satchel", (-.222,.077,.104), (.191,.137,.235), leather, -.06)
    box("Satchel flap", (-.222,.144,.155), (.182,.022,.114), stitching, -.06)
    box("Satchel clasp", (-.218,.16,.111), (.041,.012,.05), brass)


def export(name, factory):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    factory((surface("Uniform_Cloth", (.29,.261,.176), .92),
             surface("Uniform_Seams", (.187,.163,.113), .88),
             surface("Uniform_Leather", (.116,.072,.041), .79),
             surface("Uniform_Brass", (.34,.258,.125), .63),
             surface("Uniform_Glass", (.12,.162,.143), .31)))
    bpy.ops.object.select_all(action="SELECT")
    bpy.context.view_layer.objects.active = bpy.context.selected_objects[0]
    bpy.ops.object.join()
    item = bpy.context.object
    item.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    front = bpy.data.objects.new("OsowiecFront", None)
    bpy.context.collection.objects.link(front)
    front.location = (0, -.22, 0)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / f"{name}.blend"), compress=True)
    bpy.ops.export_scene.fbx(filepath=str(ART / f"{name}.fbx"), use_selection=True, bake_anim=False,
                            axis_forward="-Z", axis_up="Y", apply_unit_scale=True,
                            apply_scale_options="FBX_SCALE_UNITS")


export("OsowiecHeadgear", headgear)
export("OsowiecGreatcoat", coat)
