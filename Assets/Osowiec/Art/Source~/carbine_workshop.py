"""Editable exterior meshes for the Osowiec theatrical prop and its game presentation."""
from pathlib import Path
from math import sin, cos, pi, copysign, exp
import bpy
from mathutils import Vector

SRC = Path(__file__).resolve().parent
CAST = SRC.parent / 'Cast'
ROWS = {'walnut': (0, 620), 'oiled': (620, 895), 'steel': (895, 1235),
        'pewter': (1235, 1390), 'leather': (1390, 1650), 'web': (1650, 1830),
        'brass': (1830, 1930), 'dark': (1930, 1990), 'thread': (1990, 2048)}


def finish_material():
    mat = bpy.data.materials.new('WorkshopFinish')
    mat.use_nodes = True
    tree = mat.node_tree
    bsdf = tree.nodes.get('Principled BSDF')
    def texture(suffix, raw=False):
        node = tree.nodes.new('ShaderNodeTexImage')
        node.image = bpy.data.images.load(str(CAST / 'Textures' / ('WorkshopFinish'+suffix+'.png')), check_existing=True)
        if raw: node.image.colorspace_settings.name = 'Non-Color'
        return node
    albedo = texture(''); albedo.label = 'Varnish, grain, patina and handling marks'
    tree.links.new(albedo.outputs['Color'], bsdf.inputs['Base Color'])
    normal = tree.nodes.new('ShaderNodeNormalMap'); normal.inputs['Strength'].default_value = .70
    tree.links.new(texture('_N', True).outputs['Color'], normal.inputs['Color'])
    tree.links.new(normal.outputs['Normal'], bsdf.inputs['Normal'])
    mask = texture('_MS', True); mask.image.alpha_mode = 'CHANNEL_PACKED'
    rgb = tree.nodes.new('ShaderNodeSeparateColor'); tree.links.new(mask.outputs['Color'], rgb.inputs[0]); tree.links.new(rgb.outputs[0], bsdf.inputs['Metallic'])
    invert = tree.nodes.new('ShaderNodeMath'); invert.operation = 'SUBTRACT'; invert.inputs[0].default_value=1
    tree.links.new(mask.outputs['Alpha'], invert.inputs[1]); tree.links.new(invert.outputs[0], bsdf.inputs['Roughness'])
    return mat


def signed_power(v, degree):
    return copysign(abs(v)**degree, v)


class Parts:
    def __init__(self, material):
        self.material = material
        self.objects = []

    def rings(self, name, rows, finish, u0=.006, u1=.994):
        """Unroll around the section, with the pigment grain following the long axis."""
        n = len(rows[0]); count = len(rows)
        centers = [sum((Vector(p) for p in row), Vector())/n for row in rows]
        lengths = [0.]
        for a,b in zip(centers, centers[1:]): lengths.append(lengths[-1]+(b-a).length)
        along = [(u0 + (u1-u0)*l/lengths[-1]) for l in lengths]
        low, high = ROWS[finish]; v0, v1 = (low+5)/2048, (high-5)/2048
        around = [v0+(v1-v0)*j/n for j in range(n+1)]
        coords = [p for row in rows for p in row]; faces=[]; uv=[]
        for i in range(count-1):
            for j in range(n):
                k=(j+1)%n
                faces.append((i*n+j, i*n+k, (i+1)*n+k, (i+1)*n+j))
                uv.extend(((along[i],around[j]),(along[i],around[j+1]),(along[i+1],around[j+1]),(along[i+1],around[j])))
        for i in (0,count-1):
            ids=list(reversed(range(n))) if i==0 else list(range(n))
            faces.append(tuple(i*n+j for j in ids))
            for j in ids:
                a=2*pi*j/n
                uv.append((.5+.17*cos(a), .5*(v0+v1)+.45*(v1-v0)*sin(a)))
        data = bpy.data.meshes.new(name); data.from_pydata(coords,[],faces); data.update()
        obj=bpy.data.objects.new(name,data); bpy.context.collection.objects.link(obj); data.materials.append(self.material)
        layer=data.uv_layers.new(name='WorkshopUV'); layer.uv.foreach_set('vector',[scalar for pair in uv for scalar in pair])
        for poly in data.polygons: poly.use_smooth=len(poly.vertices)<=4
        self.objects.append(obj)
        return obj

    def barrel_form(self,name,sections,finish,n=36,degree=.85,offset=0):
        rows=[]
        for y,x,z,w,h in sections:
            rows.append([(x+w*signed_power(cos(2*pi*j/n+offset),degree),y,z+h*signed_power(sin(2*pi*j/n+offset),degree)) for j in range(n)])
        return self.rings(name,rows,finish)

    def cord(self,name,points,rad,finish,n=12,degree=1.,u0=.01,u1=.80):
        points=[Vector(p) for p in points]; rows=[]; prev=None
        for i,(p,(rx,ry)) in enumerate(zip(points,rad)):
            t=(points[min(i+1,len(points)-1)]-points[max(i-1,0)]).normalized()
            guide=Vector((1,0,0)) if abs(t.x)<.87 else Vector((0,1,0))
            axis=prev if prev is not None else guide
            side=(axis-t*axis.dot(t)).normalized(); normal=t.cross(side); prev=side
            rows.append([p+rx*signed_power(cos(2*pi*j/n),degree)*side+ry*signed_power(sin(2*pi*j/n),degree)*normal for j in range(n)])
        return self.rings(name,rows,finish,u0,u1)

    def bar(self,name,a,b,r,finish,n=14):
        a,b=Vector(a),Vector(b); pts=[a,a.lerp(b,.075),a.lerp(b,.925),b]
        return self.cord(name,pts,[(r*.80,r*.80),(r,r),(r,r),(r*.80,r*.80)],finish,n)

    def boss(self,name,c,r,finish):
        x,y,z=c; w,l,h=r; sections=[]
        for i in range(13):
            a=pi*(.002+.996*i/12)
            sections.append((y+l*cos(a),x,z,w*sin(a),h*sin(a)))
        return self.barrel_form(name,sections,finish,28,1)

    def block(self,name,c,size,finish,bevel=.16):
        x,y,z=c; w,l,h=[dim*.5 for dim in size]; inset=min(w,l,h)*bevel
        sections=[(y+l,x,z,w-inset,h-inset),(y+l-inset,x,z,w,h),
                  (y-l+inset,x,z,w,h),(y-l,x,z,w-inset,h-inset)]
        return self.barrel_form(name,sections,finish,20,.27)


def walnut_point(section, angle, lift=0):
    y,w,bottom,top=section; cx=cos(angle); sz=sin(angle)
    mid=(bottom+top)*.5; h=(top-bottom)*.5
    cheek=exp(-((y-.243)/.068)**2)*exp(-((sz-.23)/.69)**2)
    channel=exp(-((y+.395)/.16)**4)*exp(-((sz-.31)/.23)**2)
    girth=w*(1-.13*max(0,-sz)+.20*cheek-.21*channel)
    return ((girth+lift)*signed_power(cx,.72),y,mid+(h+lift)*signed_power(sz,.79))


def stock_shape(p):
    # Width, belly, comb, and asymmetric palm swell are independent art-directable curves.
    profile=[(.340,.022,-.089,.060),(.336,.029,-.096,.065),(.317,.034,-.095,.069),
             (.273,.038,-.081,.076),(.224,.039,-.059,.082),(.177,.035,-.039,.080),
             (.146,.031,-.023,.073),(.116,.027,-.014,.058),(.089,.025,-.010,.053),
             (.064,.028,-.005,.055),(.041,.030,.011,.067),(.008,.033,.027,.081),
             (-.044,.035,.030,.088),(-.112,.039,.024,.092),(-.163,.038,.030,.095),
             (-.222,.035,.039,.097),(-.301,.032,.047,.096),(-.392,.029,.054,.094),
             (-.482,.026,.060,.094),(-.554,.024,.064,.092),(-.598,.023,.068,.091),(-.601,.021,.071,.089)]
    rows=[[walnut_point(section,2*pi*j/52) for j in range(52)] for section in profile]
    p.rings('01 • Hand-carved walnut, cheek swell and finger grooves',rows,'walnut')
    p.barrel_form('02 • Rolled heel cap',[(.346,0,-.014,.020,.070),(.344,0,-.014,.028,.079),(.341,0,-.015,.031,.081),(.337,0,-.015,.030,.080)],'steel',48,.75)
    upper=[(-.201,0,.097,.029,.018),(-.211,0,.098,.031,.020),(-.246,0,.099,.031,.020),
           (-.278,0,.098,.029,.019),(-.303,0,.097,.030,.021),(-.362,0,.098,.029,.020),
           (-.417,0,.100,.027,.019),(-.486,0,.101,.025,.017),(-.550,0,.101,.024,.017),
           (-.577,0,.102,.022,.015),(-.585,0,.102,.019,.012)]
    p.barrel_form('03 • Raised separate varnished handguard',upper,'oiled',40,.91)
    # A narrow seam keeps the upper and lower timber legible under a raking sky.
    for side in (-1,1):
        pts=[(side*w*.98,y,z-.003) for y,x,z,w,h in upper[1:-1]]
        p.cord('Handguard undercut',pts,[(.0014,.0012)]*len(pts),'dark',8)
    return profile


def fittings(p):
    # Hero silhouettes seen at eye level: domed cap, layered shroud, scalloped collar and face plate.
    p.barrel_form('04 • Crown and long oxidised jacket',[(.048,0,.102,.019,.019),(.042,0,.102,.024,.023),
       (.028,0,.102,.026,.024),(-.015,0,.102,.026,.024),(-.033,0,.102,.025,.023),
       (-.049,0,.102,.025,.022),(-.084,0,.103,.023,.022),(-.139,0,.103,.025,.022),
       (-.161,0,.104,.029,.026),(-.179,0,.104,.028,.026),(-.191,0,.105,.025,.023),
       (-.218,0,.105,.022,.020),(-.230,0,.106,.020,.018)],'steel',48)
    # Small rounded concentric steps keep the butt-facing surface from appearing as a dead black cut.
    p.barrel_form('05 • Domed milled rear cap',[(.082,0,.100,.010,.009),(.081,0,.100,.014,.013),
        (.077,0,.100,.017,.015),(.067,0,.101,.019,.017),(.060,0,.101,.019,.018),
        (.055,0,.101,.017,.016),(.047,0,.102,.016,.016),(.043,0,.102,.021,.020)],'pewter',40)
    p.barrel_form('06 • Dark cap inset',[(.084,.001,.102,.006,.005),(.083,.001,.102,.011,.009),(.081,.001,.102,.012,.010)],'steel',32)
    for y,z,rx,rz in [(.028,.102,.026,.024),(-.157,.104,.028,.025),(-.183,.104,.027,.025),(-.218,.105,.022,.020)]:
        p.barrel_form('Jacket edge and reflected rim',[(y+.002,0,z,rx,rz),(y+.001,0,z,rx+.0011,rz+.0011),(y-.001,0,z,rx+.0011,rz+.0011),(y-.002,0,z,rx,rz)],'pewter',40)
    p.barrel_form('07 • Forward external tube', [(-.207,0,.108,.018,.017),(-.228,0,.108,.017,.016),
         (-.410,0,.109,.015,.014),(-.59,0,.109,.015,.014),(-.69,0,.109,.013,.013),(-.723,0,.109,.013,.013)],'steel',36)
    # Closed theatrical face has an inset dark finish and a visible rolled rim.
    p.barrel_form('08 • Muzzle face bevel', [(-.717,0,.109,.013,.013),(-.722,0,.109,.014,.014),(-.725,0,.109,.014,.014),(-.727,0,.109,.012,.012)],'pewter',36)
    p.barrel_form('Inset face', [(-.7267,0,.109,.009,.009),(-.7273,0,.109,.009,.009)],'dark',32)
    for y,w,bot,top in [(-.286,.032,.046,.123),(-.549,.026,.059,.120),(-.593,.024,.066,.120)]:
        z=(top+bot)/2; h=(top-bot)/2; half=.009 if y>-.59 else .006
        p.barrel_form('09 • Curved wrap band',[(y+half,0,z,w,h),(y+half-.002,0,z,w+.002,h+.002),
            (y-half+.002,0,z,w+.002,h+.002),(y-half,0,z,w,h)],'steel',40,.68)
        p.barrel_form('Band burnished leading edge',[(y-half+.003,0,z,w+.0019,h+.0019),(y-half+.0015,0,z,w+.0022,h+.0022),(y-half,0,z,w+.0004,h+.0004)],'pewter',40,.68)
    p.block('10 • Graduated raised spine',(0,-.199,.128),(.037,.077,.013),'steel')
    p.block('Spine crown',(0,-.204,.136),(.026,.058,.007),'pewter')
    for i in range(7):
        p.block('Spine incised marking',(-.007,-.18-i*.007,.140),(.009 if i%2==0 else .005,.0016,.0013),'dark')
    for side in (-1,1):
        p.block('Rear silhouette ear',(side*.014,-.173,.141),(.009,.015,.013),'steel',.35)
    p.barrel_form('Front foot', [(-.654,0,.120,.012,.008),(-.658,0,.121,.013,.009),(-.683,0,.121,.012,.008)],'steel',28,.48)
    p.barrel_form('Front silhouette fin', [(-.660,0,.127,.005,.004),(-.663,0,.132,.005,.010),(-.671,0,.137,.004,.012),(-.677,0,.132,.004,.007)],'steel',16,.5)
    for side in (-1,1):
        p.barrel_form('Side inset',[(.024,side*.030,.070,.0019,.008),(.012,side*.032,.071,.0025,.012),
             (-.068,side*.035,.076,.0026,.013),(-.083,side*.035,.074,.0016,.009)],'steel',24,.60)
        for y,x,z in [(.006,.034,.072),(-.064,.037,.076),(-.284,.034,.082),(-.549,.029,.085)]:
            p.boss('Recessed fastener', (side*x,y,z),(.0028,.0048,.0048),'pewter')
            p.bar('Fastener shadow',(side*(x+.0026),y-.0026,z-.0015),(side*(x+.0026),y+.0026,z+.0015),.00065,'dark',6)
    p.cord('11 • Curved exterior knob stem',[(.018,-.018,.111),(.027,-.018,.113),(.037,-.015,.113),(.048,-.009,.107),(.057,-.006,.096),(.060,-.006,.087)],
            [(.009,.009),(.010,.010),(.008,.008),(.0072,.0072),(.0072,.0072),(.008,.008)],'pewter',24)
    p.boss('Pear-shaped blued knob',(.060,-.005,.082),(.015,.016,.018),'steel')
    pts=[(0,.034,.028),(0,.034,.008),(0,.028,-.006),(0,.010,-.013),(0,-.025,-.012),(0,-.043,-.005),(0,-.051,.014),(0,-.053,.032)]
    p.cord('Curving lower frame',pts,[(.007,.0036)]*len(pts),'steel',18,.64)
    p.cord('Inner decorative crescent',[(0,-.015,.027),(0,-.020,.014),(0,-.018,.006),(0,-.012,.003)],[(.0038,.004),(.0033,.0038),(.0028,.003),(.0015,.002)],'pewter',12)


def strap(p,profile):
    # Wide ribbon rolls from edge-on to broadside as it droops. Its back and both edges are closed geometry.
    pts=[(.014,.280,-.039),(.027,.254,-.064),(.043,.214,-.095),(.058,.150,-.127),
         (.064,.065,-.146),(.065,-.030,-.150),(.060,-.130,-.142),(.052,-.224,-.119),
         (.040,-.312,-.084),(.032,-.386,-.043),(.028,-.455,.003),(.023,-.517,.054)]
    pts=[Vector(p) for p in pts]; rows=[]; sides=[]
    for i,c in enumerate(pts):
        t=(pts[min(i+1,len(pts)-1)]-pts[max(0,i-1)]).normalized()
        outward=Vector((1,0,0)); outward=(outward-t*t.dot(outward)).normalized()
        theta=.28+.32*sin(i*.45); width=t.cross(outward)*cos(theta)+outward*sin(theta)
        norm=t.cross(width).normalized()
        row=[c+width*(.0135*signed_power(cos(2*pi*j/16),.44))+norm*(.0027*signed_power(sin(2*pi*j/16),.62)) for j in range(16)]
        rows.append(row); sides.append((width,norm))
    p.rings('12 • Sagging woven sling and closed selvage', rows, 'web')
    for sign in (-1,1):
        seam=[center+width*(sign*.0125) for center,(width,normal) in zip(pts,sides)]
        p.cord('Waxed sling selvedge',seam,[(.0016,.0015)]*len(seam),'leather',8)
    for i in (0,1,9,10):
        a,b=pts[i],pts[i+1]; side,normal=sides[i]
        pad=[]
        for u in (.06,.10,.90,.94):
            c=a.lerp(b,u)+normal*.003
            width=.011 if u in (.06,.94) else .013
            pad.append([c+side*(width*signed_power(cos(2*pi*j/16),.46))+normal*(.0025*signed_power(sin(2*pi*j/16),.57)) for j in range(16)])
        p.rings('Weathered leather return',pad,'leather')
    for i in (1,9):
        c=pts[i].lerp(pts[i+1],.52); side,normal=sides[i]; t=(pts[i+1]-pts[i]).normalized()
        corners=[c+normal*.005+side*(a*.016)+t*(b*.009) for a,b in [(-1,-1),(-1,1),(1,1),(1,-1),(-1,-1)]]
        p.cord('Rounded keeper with open centre',corners,[(.0027,.0024)]*5,'brass',12)
    p.cord('Front suspension eye lug',[(.012,-.527,.080),(.018,-.526,.070),(.021,-.525,.064),(.022,-.521,.058)],
           [(.006,.005),(.006,.005),(.0045,.0045),(.004,.004)],'steel',16)
    for c in ((.012,.280,-.039),(.017,-.520,.053)):
        x,y,z=c; ring=[]
        for j in range(25):
            a=2*pi*j/24; ring.append((x+.014*cos(a),y+.003*sin(a),z+.009*sin(a)))
        p.cord('Swivel loop',ring,[(.0026,.0026)]*len(ring),'pewter',10)
    # Restraint behind the wrist leaves the front fore-end entirely timber.
    def cheek_section(y):
        a,b=next((a,b) for a,b in zip(profile,profile[1:]) if a[0]>=y>=b[0]); t=(y-a[0])/(b[0]-a[0])
        return [v+(w-v)*t for v,w in zip(a,b)]
    for y,half in ((.296,.018),(.226,.006)):
        ys=[y+half,y+half-.002,y+half-.004,y,y-half+.004,y-half+.002,y-half]
        rows=[[walnut_point(cheek_section(yy),2*pi*j/52,offset) for j in range(52)]
              for yy,offset in zip(ys,[.001,.0028,.0035,.0038,.0035,.0028,.001])]
        p.rings('Rolled saddle leather keeper',rows,'leather')
        for yy in (y+half-.004,y-half+.004):
            section=cheek_section(yy)
            for k in range(0,32,2):
                a,b=[walnut_point(section,2*pi*(k+i)/32,.00395) for i in (.08,1.00)]
                p.bar('Waxed thread on binding',a,b,.00070,'thread',6)
    p.boss('Stamped brass unit medallion',(-.045,.250,.029),(.0025,.014,.0095),'brass')
    for offset in (-.005, 0, .005):
        p.bar('Unit tally',(-.0473,.249+offset,.025),(-.0473,.251+offset,.033),.0008,'dark',6)


def merge(items,name):
    bpy.ops.object.select_all(action='DESELECT')
    copies=[]
    for source in items:
        obj=source.copy(); obj.data=source.data.copy(); bpy.context.collection.objects.link(obj)
        obj.select_set(True); copies.append(obj)
    bpy.context.view_layer.objects.active=copies[0]; bpy.ops.object.join()
    result=copies[0]; result.name=name; result.data.name=name
    return result


def build(marker):
    p=Parts(finish_material()); profile=stock_shape(p); fittings(p); strap(p,profile)
    body=merge(p.objects,'Field_carbine')
    feed=Parts(p.material)
    feed.barrel_form('Stamped lower housing',[(.039,0,.000,.015,.019),(.035,0,-.001,.019,.028),
        (.023,0,-.002,.020,.030),(-.025,0,-.003,.019,.029),(-.036,0,-.001,.017,.025)],'steel',24,.48)
    feed.block('Lower lip',(0,0,-.031),(.041,.075,.007),'pewter',.46)
    for side in (-1,1):
        for y in (-.020,.000,.020):
            feed.cord('Pressed scallop',[(side*.0175,y+.001,-.020),(side*.020,y-.001,-.014),(side*.020,y+.002,.008),(side*.0185,y+.004,.017)],
                [(.001,.0012),(.0018,.0015),(.0018,.0015),(.001,.001)],'pewter',12)
    cassette=merge(feed.objects,'FieldFeed'); cassette.location=(0,-.084,.023)
    markers=[marker('FieldRifleGrip',(0,0,0)),marker('FieldRifleMuzzle',(0,-.728,.109))]
    # Keep each moulding editable in the sculpt document; runtime contains exactly two visible objects.
    collection=bpy.data.collections.new('Workshop • separately editable exterior parts'); bpy.context.scene.collection.children.link(collection)
    for obj in p.objects+feed.objects:
        for old in list(obj.users_collection): old.objects.unlink(obj)
        collection.objects.link(obj)
        if obj in feed.objects: obj.location=cassette.location
    collection.hide_render=True; collection.hide_viewport=True
    return [body,cassette]+markers


def export():
    bpy.context.preferences.filepaths.save_version=0
    scene=bpy.context.scene
    near=bpy.data.objects['Field_carbine']; feed=bpy.data.objects['FieldFeed']
    distant=near.copy(); distant.data=near.data.copy(); distant.name='Distant_carbine'; distant.data.name='Distant_carbine'
    scene.collection.objects.link(distant)
    reducer=distant.modifiers.new('Tactical distance silhouette','DECIMATE'); reducer.ratio=.20
    items=[near,feed,distant,bpy.data.objects['FieldRifleGrip'],bpy.data.objects['FieldRifleMuzzle']]
    for obj in [near,feed,distant]: obj.modifiers.new('Game triangle normals','TRIANGULATE')
    bpy.ops.object.select_all(action='DESELECT')
    for obj in items: obj.select_set(True)
    bpy.context.view_layer.objects.active=near
    bpy.ops.export_scene.fbx(filepath=str(CAST/'ServiceCarbine.fbx'),use_selection=True,axis_forward='-Z',axis_up='Y',
            apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',add_leaf_bones=False,bake_anim=False,path_mode='STRIP',use_tspace=True)
    distant.hide_set(True); distant.hide_render=True
    bpy.ops.file.pack_all(); bpy.ops.wm.save_as_mainfile(filepath=str(SRC/'ServiceCarbine-sculpt.blend'),compress=True)

