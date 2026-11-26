"""Generate modular Osowiec props from Blender in background mode."""
import math
from pathlib import Path
import random
import sys

import bpy


OUTPUT = Path(sys.argv[-1]).resolve()
OUTPUT.mkdir(parents=True, exist_ok=True)
SOURCE = OUTPUT / "Source~"
SOURCE.mkdir(exist_ok=True)


def material(name, rgb, roughness=.78, metallic=0):
    result = bpy.data.materials.new(name)
    result.diffuse_color = (*rgb, 1)
    result.use_nodes = True
    surface = result.node_tree.nodes.get("Principled BSDF")
    surface.inputs["Base Color"].default_value = (*rgb, 1)
    surface.inputs["Roughness"].default_value = roughness
    surface.inputs["Metallic"].default_value = metallic
    return result


def finish(obj, name, surface, bevel=0):
    obj.name = name
    obj.data.materials.append(surface)
    if bevel:
        modifier = obj.modifiers.new("Worn edge", "BEVEL")
        modifier.width = bevel
        modifier.segments = 2
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        normal = obj.modifiers.new("Weighted face normals", "WEIGHTED_NORMAL")
        bpy.ops.object.modifier_apply(modifier=normal.name)
    return obj


def box(name, position, dimensions, surface, bevel=.006):
    bpy.ops.mesh.primitive_cube_add(size=1, location=position)
    obj = bpy.context.object
    obj.dimensions = dimensions
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(obj, name, surface, bevel)


def round_part(name, position, radius1, radius2, height, surface, segments=16):
    bpy.ops.mesh.primitive_cone_add(vertices=segments, radius1=radius1,
                                   radius2=radius2, depth=height, location=position)
    return finish(bpy.context.object, name, surface, .003)


def build_lantern(palette):
    iron, _, _, glow = palette
    round_part("Base", (0, 0, .033), .14, .13, .06, iron)
    round_part("Reservoir", (0, 0, .083), .132, .101, .055, iron)
    round_part("Lens", (0, 0, .226), .084, .096, .24, glow)
    round_part("Crown", (0, 0, .362), .119, .081, .047, iron)
    round_part("Chimney", (0, 0, .405), .063, .062, .043, iron)
    for i in range(6):
        theta = i * math.tau / 6
        x, y = .103 * math.cos(theta), .103 * math.sin(theta)
        round_part("Cage", (x, y, .228), .009, .009, .28, iron, 8)
    bpy.ops.mesh.primitive_torus_add(location=(0, 0, .454),
                                    rotation=(math.pi / 2, 0, 0),
                                    major_radius=.039, minor_radius=.006,
                                    major_segments=18, minor_segments=6)
    finish(bpy.context.object, "Carry loop", iron)


def build_crate(palette):
    iron, wood, dark, _ = palette
    box("Board backing", (0, 0, .236), (.83, .56, .452), dark)
    for index in range(5):
        z = .052 + index * .09
        for sign in (-1, 1):
            box("Side slat", (0, sign * .292, z), (.85, .034, .084), wood[index % 4])
            box("End slat", (sign * .429, 0, z), (.039, .557, .084), wood[(index + 1) % 4])
    for index in range(5):
        y = (index - 2) * .12
        box("Lid slat", (0, y, .471), (.87, .115, .041), wood[(index + 2) % 4])
    for x in (-.291, .291):
        box("Lid batten", (x, 0, .509), (.076, .63, .039), dark)
        for sign in (-1, 1):
            box("Side batten", (x, sign * .320, .233), (.067, .032, .464), dark)
            for z in (.063, .405):
                pin = round_part("Rivet", (x, sign * .339, z), .012, .012, .009, iron, 8)
                pin.rotation_euler.x = math.pi / 2
    for sign in (-1, 1):
        box("Handle", (sign * .467, 0, .297), (.025, .175, .033), iron)
        for y in (-.072, .072):
            box("Handle foot", (sign * .456, y, .297), (.046, .029, .041), iron)


def build_duckboard(palette):
    iron, wood, dark, _ = palette
    rng = random.Random(12)
    for x in (-.29, .29):
        box("Runner", (x, 0, .032), (.095, 1.72, .065), dark)
    for index in range(11):
        y = (index - 5) * .156
        slat = box("Wet board", (rng.uniform(-.024, .024), y, .082),
                   (rng.uniform(.82, .89), .141, .04), wood[index % 4])
        slat.rotation_euler.z = rng.uniform(-.028, .028)
        for x in (-.29, .29):
            round_part("Nail", (x, y, .106), .009, .009, .006, iron, 8)


def build_timber(palette):
    iron, wood, dark, _ = palette
    rng = random.Random(31)
    for i in range(7):
        z = .11 + i * .229
        obj = box("Retaining timber", (0, 0, z), (1.75, .17, .217), wood[i % 4], .018)
        obj.rotation_euler.y = rng.uniform(-.009, .009)
    for x in (-.69, .69):
        box("Upright", (x, -.117, .829), (.157, .15, 1.658), dark, .018)
        for z in (.15, .81, 1.52):
            pin = round_part("Iron pin", (x, -.198, z), .017, .017, .013, iron, 8)
            pin.rotation_euler.x = math.pi / 2


def export(name, build):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    iron = material("Osowiec_OxidizedIron", (.083, .061, .046), .49, .52)
    dark = material("Osowiec_WetEndgrain", (.128, .073, .043), .66)
    wood = [material(f"Osowiec_Oak_{i}", (r, g, b), .58)
            for i, (r, g, b) in enumerate([
                (.224, .143, .088), (.182, .110, .069),
                (.249, .164, .104), (.165, .104, .076),
            ])]
    glow = material("Lantern_Glow", (.92, .49, .15), .45)
    surface = glow.node_tree.nodes.get("Principled BSDF")
    surface.inputs["Emission Color"].default_value = (1, .43, .088, 1)
    surface.inputs["Emission Strength"].default_value = 2.5
    build((iron, wood, dark, glow))
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    print(f"{name}: {len(obj.data.vertices)} vertices")
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / f"{name}.blend"), compress=True)
    bpy.ops.export_scene.fbx(filepath=str(OUTPUT / f"{name}.fbx"),
                            use_selection=True, bake_anim=False, apply_unit_scale=True,
                            axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_UNITS")


for prop_name, builder in (
    ("Lantern", build_lantern), ("SupplyCrate", build_crate),
    ("TrenchDuckboard", build_duckboard), ("TrenchTimber", build_timber),
):
    export(prop_name, builder)
