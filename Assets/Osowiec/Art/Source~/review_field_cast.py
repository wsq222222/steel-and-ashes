"""Blender validation views of the actual clean master characters with the matching issued carbine.
Run: blender --background --python review_field_cast.py
"""
from pathlib import Path
import bpy
import sys
from mathutils import Vector

src=Path(__file__).resolve().parent
output=src.parents[3]/"Logs"/"OsowiecReview"/"FieldCast"
if "--out" in sys.argv:output=Path(sys.argv[sys.argv.index("--out")+1]).resolve()
masters=Path(sys.argv[sys.argv.index("--masters")+1]).resolve() if "--masters" in sys.argv else src
output.mkdir(parents=True,exist_ok=True)

def aim(ob,target): ob.rotation_euler=(Vector(target)-ob.location).to_track_quat("-Z","Y").to_euler()
def light(scene,name,position,power,color,size):
    lamp=bpy.data.lights.new(name,"AREA");lamp.energy=power;lamp.color=color;lamp.shape="DISK";lamp.size=size
    obj=bpy.data.objects.new(name,lamp);scene.collection.objects.link(obj);obj.location=position;aim(obj,(0,0,1))
for role in (["MercyCart"] if "cart" in sys.argv else ["Assault","Scout","Support","Sentry"]):
    bpy.ops.wm.open_mainfile(filepath=str(masters/(role+".blend")))
    scene=bpy.context.scene
    scene.render.engine="CYCLES";scene.cycles.samples=12;scene.cycles.use_denoising=True
    scene.render.resolution_x=384;scene.render.resolution_y=600;scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new("Earth and cloud");scene.world.color=(.11,.12,.13)
    if role != "MercyCart":
        with bpy.data.libraries.load(str(src/"ServiceCarbine.blend"),link=False) as (loaded,selected):
            selected.objects=[name for name in loaded.objects if name in ("Field_carbine","FieldFeed")]
        for ob in selected.objects:
            scene.collection.objects.link(ob);ob.location+=Vector((.115,-.245,1.22))
    light(scene,"Grey daylight",(-1.95,-2.5,4.1),340,(.85,.89,1),2.8)
    light(scene,"Brick reflection",(1.2,-2.3,1.88),102,(1,.78,.62),1.6)
    light(scene,"Edge",(1.4,1.48,3.15),205,(1,.85,.68),1.45)
    lens=bpy.data.cameras.new("Cast review");eye=bpy.data.objects.new("Cast review camera",lens);scene.collection.objects.link(eye)
    scene.camera=eye;lens.type="ORTHO";lens.ortho_scale=2.05
    shots=[("front",(-1.15,-4.8,1.65)),("back",(2.15,4.4,1.6))]
    if "--detail" in sys.argv:shots=[("detail",(-2.35,-4.8,1.80))]
    if role=="MercyCart":
        shots=[("front",(3.4,-2.9,2.02)),("back",(3.4,2.9,1.66))]
        lens.ortho_scale=2.91
        scene.render.resolution_x=820;scene.render.resolution_y=510
    for shot,pos in shots:
        eye.location=pos;aim(eye,(0,-.23,.52) if role=="MercyCart" else (0,-.025,.93))
        if shot=="detail":
            aim(eye,(0,-.01,1.26));lens.ortho_scale=1.27
            scene.render.resolution_x=528;scene.render.resolution_y=660
        scene.render.film_transparent=True
        scene.render.filepath=str(output/(role+"-"+shot+".png"))
        bpy.ops.render.render(write_still=True)
