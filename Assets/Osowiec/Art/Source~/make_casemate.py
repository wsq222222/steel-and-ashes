"""Build a modular brick entrance and bake its door animation to FBX."""
import sys
from pathlib import Path

import bpy
from mathutils import Vector

output = Path(sys.argv[-1]).resolve()
bpy.ops.wm.read_factory_settings(use_empty=True)

def material(name, color):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    return mat

brick = [material(f"Brick {i}", (0.30 + i * .025, .125 + i * .011, .078)) for i in range(4)]
stone = material("Wet mortar", (.17, .16, .14))
iron = material("Painted iron", (.065, .085, .077))
wood = material("Dark oak", (.16, .107, .062))

def block(name, location, size, mat, bevel=.014):
    bpy.ops.mesh.primitive_cube_add(size=1, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(mat)
    mod = obj.modifiers.new("Worn edges", "BEVEL")
    mod.width = bevel
    mod.segments = 2
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return obj

for side in (-1, 1):
    block("Pier core", (side * 1.56, 0, 1.35), (1.10, .60, 2.7), stone)
    for row in range(14):
        for col in range(4):
            x = side * (1.04 + col * .28 + .055 * (row % 2))
            block("Facing brick", (x, -.315, row * .186 + .10),
                  (.265, .10, .174), brick[(row + col * 3) % 4])
block("Stone lintel", (0, 0, 2.56), (2.15, .73, .32), stone)
block("Stone threshold", (0, 0, .055), (2.15, .84, .11), stone)

hinge = bpy.data.objects.new("Door hinge", None)
bpy.context.collection.objects.link(hinge)
hinge.location = (-.96, .05, .08)
for i in range(8):
    plank = block("Oak door plank", (-.83 + i * .235, .05, 1.23),
                  (.228, .12, 2.26), wood)
    plank.parent = hinge
    plank.location -= Vector(hinge.location)
for z in (.48, 1.94):
    strap = block("Iron strap", (0, -.025, z), (1.91, .045, .16), iron)
    strap.parent = hinge
    strap.location -= Vector(hinge.location)
for frame, angle in [(1, 0), (8, 0), (36, -1.57), (42, -1.53)]:
    hinge.rotation_euler.z = angle
    hinge.keyframe_insert("rotation_euler", frame=frame)
bpy.context.scene.frame_end = 42
bpy.context.scene.render.fps = 30
bpy.context.scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=str(output.with_suffix(".blend")))
bpy.ops.export_scene.fbx(filepath=str(output.with_suffix(".fbx")),
                         bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False)
