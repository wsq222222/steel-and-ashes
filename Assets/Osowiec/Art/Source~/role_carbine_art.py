"""Build the three fictional Osowiec role props from the shared exterior sculpt and PBR palette."""
from pathlib import Path
from math import sin, cos, pi
import sys
import bpy

SOURCE = Path(__file__).resolve().parent
sys.path.insert(0, str(SOURCE))
from carbine_workshop import Parts, merge

CAST = SOURCE.parent / 'Cast'


def collar(p, name, y, x, z, rx, rz, length, finish):
    edge = min(length * .18, .0025)
    rows = [(y + offset, x, z, rx * k, rz * k) for offset, k in
            [(length/2, .91), (length/2-edge, 1), (-length/2+edge, 1), (-length/2, .91)]]
    return p.barrel_form(name, rows, finish, 40, 1)


def cloth_turn(p, y, z, w, h, phase):
    pts = [(w*cos(i*pi/24+phase), y - i*.00018,
            z + h*sin(i*pi/24+phase)) for i in range(50)]
    p.cord('Linen binding', pts, [(.0034,.0048)]*len(pts), 'web', 8)


def observer(p):
    # The stacked shrouds and off-centre curved bracket have a distinctive profile from the player camera.
    x = -.012
    for y in (-.045, -.215):
        p.block('Saddle foot',(x,y,.135),(.054,.030,.034),'steel')
        p.block('Elevated sight bridge',(x,y,.160),(.032,.026,.047),'pewter')
    rows = [(y,x,.194,r,r) for y,r in
            [(.065,.026),(.061,.036),(.020,.036),(.005,.027),(-.025,.024),
             (-.160,.021),(-.231,.022),(-.269,.034),(-.317,.035),(-.325,.031)]]
    p.barrel_form('Ebonised observation tube', rows, 'dark', 48, 1)
    for y,r in [( .054,.038),(.020,.037),(-.045,.028),(-.214,.027),(-.279,.035),(-.315,.037)]:
        collar(p, 'Drawn brass ferrule', y,x,.194,r,r,.011,'brass')
    lens = bpy.data.materials.get('Lens') or bpy.data.materials.new('Lens')
    glass = Parts(lens)
    for y,r in [( .063,.030),(-.325,.026)]:
        glass.boss('Smoky optical glass',(x,y,.194),(r,.004,r),'steel')
    p.objects.extend(glass.objects)
    p.cord('Side scalloped rail',[(.034,.04,.096),(.038,.006,.132),(.032,-.025,.149),(.029,-.226,.149),(.035,-.249,.115)],
           [(.004,.004)]*5,'pewter')
    for i in range(3):
        cloth_turn(p, -.51-i*.010, .095, .023, .024, .3)


def assault(p):
    # Ribbed guard and exposed end rim, deliberately fictional and nonfunctional.
    rows=[(y,0,.101,w,h) for y,w,h in [(-.176,.026,.027),(-.197,.045,.044),(-.221,.051,.048),
            (-.338,.050,.050),(-.505,.041,.042),(-.602,.036,.039),(-.620,.034,.034)]]
    p.barrel_form('Hammer external armour cowl', rows,'steel',44,.84)
    for i in range(6):
        y=-.221-i*.070
        r=.052-i*.003
        collar(p, 'Raised armour rib', y,0,.101,r,r,.015, 'pewter' if i in (0,5) else 'steel')
    for s in (-1,1):
        p.cord('Recessed cheek strip',[(s*.053,-.222,.10),(s*.054,-.259,.10),(s*.043,-.557,.10),(s*.037,-.607,.10)],
               [(.005,.007)]*4,'brass',12)
        for i in range(6):
            p.boss('Peened armour stud',(s*(.054-i*.0025),-.238-i*.061,.11),(.004,.006,.006),'pewter')
    # A little raised, backward-facing enamel workshop badge is readable close to the player.
    bx,by,bz=.047,-.152,.128
    p.bar('Badge arm',(.019,-.156,.093),(bx,by,bz),.009,'steel')
    collar(p,'Enamel badge brass bezel',by,bx,bz,.023,.023,.012,'brass')
    en=Parts(bpy.data.materials.get('IvoryStencil') or bpy.data.materials.new('IvoryStencil'))
    en.boss('Ceramic face',(bx,by+.007,bz),(.019,.002,.019),'steel')
    p.objects+=en.objects
    p.bar('Etched diagonal',(bx-.009,by+.010,bz-.010),(bx+.009,by+.010,bz+.008),.0016,'dark',8)
    for j in range(4):
        a=j*pi/3.8
        p.boss('Badge enamel mark',(bx+.014*cos(a),by+.010,bz+.014*sin(a)),(.0016,.001,.0016),'dark')


def runner(p):
    # Compact scout outline, braided hand binding and a soft saddle cheek rest.
    for i in range(6):
        cloth_turn(p, -.247-i*.012, .077, .027, .041, .31)
    rows=[(y,0,.065,w,h) for y,w,h in [(.090,.020,.009),(.120,.028,.014),(.157,.035,.022),(.221,.039,.029),(.267,.033,.030),(.293,.024,.019)]]
    p.barrel_form('Stitched russet cheek rest',list(reversed(rows)),'leather',40,.56)
    for side in (-1,1):
        for i in range(11):
            y=.135+i*.011
            width=.027+.009*sin(pi*i/11)
            p.bar('Saddle stitch',(side*width,y,.072),(side*(width+.0007),y+.005,.074),.001,'thread',8)
    for i in range(3):
        collar(p,'Scout nose binding',-.410-i*.008,0,.106,.025,.023,.005,'brass')


def export(name, slender, front_scale, attachments):
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE/'ServiceCarbine-sculpt.blend'))
    bpy.context.preferences.filepaths.save_version=0
    old=bpy.data.objects['Field_carbine']
    old.name='Common exterior'
    old.data.name='Common exterior'
    prev=bpy.data.objects.get('Distant_carbine')
    if prev: bpy.data.objects.remove(prev,do_unlink=True)
    # Drop the source file's hidden workshop duplicates. Original editable common parts stay in the shared sculpt.
    for collection in list(bpy.data.collections):
        if collection.hide_render:
            for item in list(collection.objects): bpy.data.objects.remove(item,do_unlink=True)
            bpy.data.collections.remove(collection)
    def reshape(v):
        if v.y < -.238:
            v.y=-.238+(v.y+.238)*front_scale
        elif v.y > .062:
            v.y=.062+(v.y-.062)*slender
            v.x*=slender
    for vert in old.data.vertices: reshape(vert.co)
    muzzle=bpy.data.objects['FieldRifleMuzzle']
    reshape_loc=muzzle.location.copy()
    reshape(reshape_loc)
    muzzle.location=reshape_loc
    p=Parts(old.data.materials[0])
    attachments(p)
    near=merge([old]+p.objects,'Field_carbine')
    # Only the near presentation is joined; the animated feed remains an independent object.
    distant=near.copy(); distant.data=near.data.copy(); distant.name='Distant_carbine'
    bpy.context.scene.collection.objects.link(distant)
    dec=distant.modifiers.new('Distant silhouette','DECIMATE'); dec.ratio=.13
    feed=bpy.data.objects['FieldFeed']; grip=bpy.data.objects['FieldRifleGrip']
    bpy.ops.object.select_all(action='DESELECT')
    for obj in [near,feed,distant,muzzle,grip]: obj.select_set(True)
    bpy.context.view_layer.objects.active=near
    bpy.ops.export_scene.fbx(filepath=str(CAST/(name+'.fbx')),use_selection=True,axis_forward='-Z',axis_up='Y',
            apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',add_leaf_bones=False,bake_anim=False,path_mode='STRIP',use_tspace=True)
    print('EXPORTED', name,len(near.data.vertices),len(p.objects),'muzzle',tuple(round(v,4) for v in muzzle.location))


for params in [('RamCarbine',1.12,1.0,assault),('ObserverCarbine',1.,1.20,observer),('RunnerCarbine',.83,.69,runner)]:
    if "--" not in sys.argv or params[0] in sys.argv[sys.argv.index("--")+1:]:
        export(*params)
