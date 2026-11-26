"""Prepare the source .blend for art review in Blender. Optionally render game-height shots."""
from pathlib import Path
import sys
import bpy
from math import radians
from mathutils import Vector

root = Path(bpy.data.filepath).resolve().parent
surfaces = root.parent / "Fort1916" / "Surfaces"
if not surfaces.is_dir():
    raise RuntimeError("Open Osowiec_FortI_1916.blend before running this file.")
def im(path, colour=True):
    image = bpy.data.images.load(str(path), check_existing=True)
    image.colorspace_settings.name = "sRGB" if colour else "Non-Color"
    image.filepath = bpy.path.relpath(str(path))
    return image

for m in bpy.data.materials:
    m.use_nodes=True
    tree=m.node_tree
    nodes=tree.nodes;nodes.clear()
    out=nodes.new("ShaderNodeOutputMaterial");out.location=(580,0)
    shader=nodes.new("ShaderNodeBsdfPrincipled");shader.location=(270,0)
    tree.links.new(shader.outputs[0],out.inputs["Surface"])
    shader.inputs["Base Color"].default_value=m.diffuse_color
    path=surfaces/(m.name+"_colour.jpg")
    if path.exists():
        tex=nodes.new("ShaderNodeTexImage");tex.location=(-210,250);tex.image=im(path)
        multiply=nodes.new("ShaderNodeMixRGB");multiply.blend_type="MULTIPLY";multiply.inputs[0].default_value=1
        tint={"Earth":(1.12,1.18,1.24,1),"Timber":(.58,.49,.40,1),"Brick":(.85,.65,.55,1),
              "Sod":(.30,.38,.23,1),"Oxide":(.39,.42,.41,1)}
        multiply.inputs[2].default_value=tint.get(m.name,(.79,.77,.68,1))
        tree.links.new(tex.outputs["Color"],multiply.inputs[1]);tree.links.new(multiply.outputs[0],shader.inputs["Base Color"])
        tex=nodes.new("ShaderNodeTexImage");tex.location=(-390,5);tex.image=im(surfaces/(m.name+"_normal.jpg"),False)
        norm=nodes.new("ShaderNodeNormalMap");norm.location=(-100,5);norm.inputs["Strength"].default_value=.84
        tree.links.new(tex.outputs[0],norm.inputs["Color"]);tree.links.new(norm.outputs[0],shader.inputs["Normal"])
        tex=nodes.new("ShaderNodeTexImage");tex.location=(-450,-220);tex.image=im(surfaces/(m.name+"_mask.png"),False)
        tex.image.alpha_mode="CHANNEL_PACKED"
        inv=nodes.new("ShaderNodeMath");inv.operation="SUBTRACT";inv.inputs[0].default_value=1;inv.location=(-90,-180)
        tree.links.new(tex.outputs["Alpha"],inv.inputs[1]);tree.links.new(inv.outputs[0],shader.inputs["Roughness"])
    elif m.name in ("Grass","Reed"):
        tex=nodes.new("ShaderNodeTexImage");tex.image=im(surfaces/"Floodplain_sedges.png")
        tree.links.new(tex.outputs["Color"],shader.inputs["Base Color"])
        tree.links.new(tex.outputs["Alpha"],shader.inputs["Alpha"])
        shader.inputs["Roughness"].default_value=.82
    elif m.name in ("Water","River"):
        shader.inputs["Base Color"].default_value=(.055,.053,.043,1);shader.inputs["Roughness"].default_value=.12
        shader.inputs["Metallic"].default_value=.18
        rip=nodes.new("ShaderNodeTexWave");rip.wave_type="RINGS";rip.inputs["Scale"].default_value=15
        if m.name=="River":
            shader.inputs["Base Color"].default_value=(.065,.125,.135,1)
            shader.inputs["Metallic"].default_value=.36
            rip.wave_type="BANDS";rip.inputs["Scale"].default_value=1.5
            rip.inputs["Distortion"].default_value=3.3
            coords=nodes.new("ShaderNodeTexCoord");tree.links.new(coords.outputs["Object"],rip.inputs["Vector"])
        bump=nodes.new("ShaderNodeBump");bump.inputs["Strength"].default_value=.15;bump.inputs["Distance"].default_value=.024
        tree.links.new(rip.outputs["Fac"],bump.inputs["Height"]);tree.links.new(bump.outputs[0],shader.inputs["Normal"])
    elif m.name=="Glass":
        shader.inputs["Emission Color"].default_value=(1,.44,.12,1);shader.inputs["Emission Strength"].default_value=4
    else:
        shader.inputs["Roughness"].default_value=.77
        if m.name in ("Brass","Oxide"):shader.inputs["Metallic"].default_value=.72

ground=bpy.data.materials.get("Bank growth preview") or bpy.data.materials.new("Bank growth preview")
ground.use_nodes=True
nt=ground.node_tree;nt.nodes.clear()
out=nt.nodes.new("ShaderNodeOutputMaterial");pbr=nt.nodes.new("ShaderNodeBsdfPrincipled")
nt.links.new(pbr.outputs[0],out.inputs[0])
mix=nt.nodes.new("ShaderNodeMixRGB")
growth=nt.nodes.new("ShaderNodeVertexColor");growth.layer_name="BankGrowth"
nt.links.new(growth.outputs[0],mix.inputs[0])
coord=nt.nodes.new("ShaderNodeTexCoord")
nrmix=nt.nodes.new("ShaderNodeMixRGB");nt.links.new(growth.outputs[0],nrmix.inputs[0])
for i,(material,scale,tint) in enumerate([("Earth",2.25,(1.28,1.36,1.44,1)),("Sod",3.10,(.30,.38,.23,1))]):
    mapping=nt.nodes.new("ShaderNodeVectorMath");mapping.operation="SCALE";mapping.inputs[3].default_value=1/scale
    nt.links.new(coord.outputs["Object"],mapping.inputs[0])
    tex=nt.nodes.new("ShaderNodeTexImage");tex.image=im(surfaces/(material+"_colour.jpg"))
    nt.links.new(mapping.outputs[0],tex.inputs[0])
    tone=nt.nodes.new("ShaderNodeMixRGB");tone.blend_type="MULTIPLY";tone.inputs[0].default_value=1
    tone.inputs[2].default_value=tint;nt.links.new(tex.outputs[0],tone.inputs[1])
    nt.links.new(tone.outputs[0],mix.inputs[1+i])
    normal=nt.nodes.new("ShaderNodeTexImage");normal.image=im(surfaces/(material+"_normal.jpg"),False)
    nt.links.new(mapping.outputs[0],normal.inputs[0]);nt.links.new(normal.outputs[0],nrmix.inputs[1+i])
normal=nt.nodes.new("ShaderNodeNormalMap");normal.inputs["Strength"].default_value=.73
nt.links.new(nrmix.outputs[0],normal.inputs["Color"]);nt.links.new(normal.outputs[0],pbr.inputs["Normal"])
nt.links.new(mix.outputs[0],pbr.inputs["Base Color"]);pbr.inputs["Roughness"].default_value=.69
for item in bpy.context.scene.objects:
    if item.name.startswith(("Ground_","Flood_meadow_","Event_")):
        for slot in item.material_slots:slot.material=ground
    if item.name.startswith("Event_Hollow_"):
        item.hide_render=True
        item.hide_viewport=True
scene=bpy.context.scene
for obj in list(scene.objects):
    if obj.type in ("LIGHT","CAMERA"):bpy.data.objects.remove(obj,do_unlink=True)
world=scene.world or bpy.data.worlds.new("Wet dawn");scene.world=world;world.use_nodes=True
nt=world.node_tree;nt.nodes.clear()
out=nt.nodes.new("ShaderNodeOutputWorld");bg=nt.nodes.new("ShaderNodeBackground");bg.inputs[1].default_value=.68
env=nt.nodes.new("ShaderNodeTexEnvironment");env.image=bpy.data.images.load(str(surfaces/"OvercastSoil.hdr"),check_existing=True)
env.image.filepath=bpy.path.relpath(str(surfaces/"OvercastSoil.hdr"))
nt.links.new(env.outputs[0],bg.inputs[0]);nt.links.new(bg.outputs[0],out.inputs["Surface"])
sun=bpy.data.lights.new("Cold break in cloud","SUN");sun.energy=1.32;sun.angle=radians(13);sun.color=(.86,.87,.81)
obj=bpy.data.objects.new(sun.name,sun);scene.collection.objects.link(obj);obj.rotation_euler=(radians(58),radians(-24),radians(-34))
for i,(x,y) in enumerate([(-44,-18.8),(-34.7,-12.3),(-25.8,-6.2),(-15.5,-5.8),(-9.7,-13.6),(-3,-13.7),(3,-10.28),(9.1,-13.7),(21.4,-5.3),(31.7,1.2)]):
    indoor=-9<x<10 and abs(y+12)<2
    lamp=bpy.data.lights.new("Storm lantern "+str(i+1),"POINT");lamp.energy=175 if indoor else 78
    lamp.shadow_soft_size=.20;lamp.color=(1,.50,.22)
    if indoor:y+= .32 if y<-12 else -.32
    ob=bpy.data.objects.new(lamp.name,lamp);scene.collection.objects.link(ob);ob.location=(x,y,1.68)
cameras=[("01-trench",(-46.0,-15.65,1.67),(-32.6,-15.42,1.40),23),
         ("02-portal",(-12.45,-9.08,1.70),(-6.5,-12.64,1.54),22),
         ("03-casemate",(-8.85,-11.70,1.63),(4,-12.20,1.77),23),
         ("04-battery",(22.33,-3.16,1.69),(34.3,7.9,2.02),25),
         ("05-marsh",(-10.85,10.6,1.7),(10.2,14.7,1.39),26),
         ("06-Biebrza-turn",(-34,-8.95,1.81),(-36,5.1,-.19),22),
         ("07-Biebrza-overview",(-76,-66,75),(-21,8,.2),30),
         ("08-oxbow",(-28,-21,3.8),(-37,-32,-.3),24)]
for name,location,aim,focal in cameras:
    data=bpy.data.cameras.new(name);data.lens=focal;obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj)
    obj.location=location;obj.rotation_euler=(Vector(aim)-Vector(location)).to_track_quat("-Z","Y").to_euler()
scene.camera=bpy.data.objects[cameras[0][0]]
scene.render.engine="CYCLES";scene.cycles.samples=8;scene.cycles.use_denoising=True
scene.cycles.max_bounces=3
scene.render.resolution_x=1024;scene.render.resolution_y=576;scene.render.resolution_percentage=100
try:
    prefs=bpy.context.preferences.addons["cycles"].preferences
    prefs.compute_device_type="OPTIX";prefs.get_devices()
    gpu=[d for d in prefs.devices if d.type=="OPTIX"]
    if gpu:
        for d in prefs.devices:d.use=d.type=="OPTIX"
        scene.cycles.device="GPU"
except Exception as exc:print("Using available render device",exc)
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(root/"Osowiec_FortI_1916.blend"),compress=True)
if "--" in sys.argv:
    dest=Path(sys.argv[sys.argv.index("--")+1]).resolve();dest.mkdir(parents=True,exist_ok=True)
    for name,*_ in cameras:
        scene.camera=bpy.data.objects[name];scene.render.filepath=str(dest/(name+".png"))
        bpy.ops.render.render(write_still=True)
