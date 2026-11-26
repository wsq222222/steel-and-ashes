"""Tailor the first-person headquarters uniform on the scout's locomotion rig."""
import sys
import bpy
from pathlib import Path
from mathutils import Vector

src = Path(__file__).resolve().parent
sys.path.insert(0, str(src))
import make_field_cast as cast
import field_tailoring

bpy.ops.wm.open_mainfile(filepath=str(src / "Scout.blend"))
bpy.context.preferences.filepaths.save_version = 0
arm = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
# The field coat includes equipment which runs over the shoulders and a cut neck
# opening. Sew the officer's coat separately and retain the shared cuff, glove,
# and boot geometry with the original bone names and animation clips.
for obj in list(bpy.context.scene.objects):
    if obj.type == "MESH" and obj.find_armature() == arm:
        data = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        if not data.users:
            bpy.data.meshes.remove(data)
coat = cast.Mesh()
wool = "RifleWool"
armholes = field_tailoring.coat(coat, 1, wool)
# A closed shoulder yoke terminates the neckline inside the turned wool stand.
# Every row of the collar is part of the same chest pose as the neckline.
coat.loft([(0,.012,1.451,.102,.086), (0,.012,1.472,.070,.069),
           (0,.012,1.483,.069,.068), (0,.012,1.488,.065,.065)],
          wool, "Chest", 48)
for side in (-1, 1):
    lapel = [(side*.055,-.059,1.462), (side*.121,-.090,1.405),
             (side*.102,-.125,1.352), (side*.021,-.137,1.295)]
    coat.panel(lapel, "Canvas", "Chest")
    coat.tube(lapel, [.002,.003,.0025,.001], "Piping", "Chest", 8)
for i in range(5):
    coat.oval((.016,-.132,1.30-i*.060),(.007,.006,.007),"Brass","Chest",12,7)
cast.uniform_limbs(coat, cast.rig_spec(), armholes, 1, wool)
skin = coat.finish("Uniform_and_equipment")
skin.parent = arm
skin.modifiers.new("Tailored body skin", "ARMATURE").object = arm
# The two tailored arms have matching segment lengths before the player poses them.
segments={}
for side,sign in (("L",-1),("R",1)):
    shoulder=arm.data.bones[side+"Arm"].head_local.copy()
    elbow=Vector((sign*.280,-.177,1.236))
    wrist=Vector((sign*.167,-.416,1.386))
    finger=Vector((sign*.146,-.472,1.418))
    for name,a,b in ((side+"Arm",shoulder,elbow),(side+"Forearm",elbow,wrist),(side+"Hand",wrist,finger)):
        bone=arm.data.bones[name]; original=bone.head_local.copy(); old_axis=bone.tail_local-original
        new_axis=b-a; twist=old_axis.normalized().rotation_difference(new_axis.normalized())
        segments[name]=(a,b,original,old_axis.normalized(),new_axis.length/old_axis.length,twist,twist@bone.z_axis)
for vertex in skin.data.vertices:
    original=vertex.co.copy(); delta=Vector()
    for influence in vertex.groups:
        segment=segments.get(skin.vertex_groups[influence.group].name)
        if not segment:continue
        start,_,old,axis,scale,turn,_=segment
        disp=original-old
        shaped=start+turn@(disp+axis*disp.dot(axis)*(scale-1))
        delta+=(shaped-original)*influence.weight
    vertex.co=original+delta
bpy.context.view_layer.objects.active=arm
arm.select_set(True); bpy.ops.object.mode_set(mode='EDIT')
for name,(a,b,old,axis,scale,turn,roll) in segments.items():
    bone=arm.data.edit_bones[name];bone.head=a;bone.tail=b;bone.align_roll(roll)
bpy.ops.object.mode_set(mode='OBJECT')
skin.data.update()
bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
skin.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.wm.save_as_mainfile(filepath=str(src / "StaffCoat.blend"), compress=True)
for track in arm.animation_data.nla_tracks:
    track.mute=False
bpy.ops.export_scene.fbx(filepath=str(src.parent / "Cast" / "StaffCoat.fbx"),
    use_selection=True,axis_forward="-Z",axis_up="Y",apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_UNITS",add_leaf_bones=False,bake_anim=True,
    bake_anim_use_all_actions=False,bake_anim_use_nla_strips=True,bake_anim_simplify_factor=.2,
    path_mode="STRIP")
print("HQ coat and anatomical arms:",len(skin.data.vertices),"vertices")
