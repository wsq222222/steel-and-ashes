"""Visible carpentry and the cut peat lip along the three western approaches."""
import math
from random import Random
from mathutils import Vector

UP = Vector((0, 0, 1))


def build(groups, pole, path, ground, path_distance, leaf_uv):
    rng = Random(280261)
    wood_uv = {}

    def plank(name, centre, along, across, length, width, thickness, chipped):
        # End checks and a relieved arris catch light even at a grazing angle.
        normal = along.cross(across).normalized()
        w = width / 2
        e = min(.014, width * .095, thickness * .22)
        t = thickness / 2
        section = [(-w+e,-t), (w-e,-t), (w,-t+e), (w,t-e), (w-e,t),
                   (.28*w,t+.001), (.19*w,t+.002), (.06*w,t+.001), (-w+e,t), (-w,t-e), (-w,-t+e)]
        end_offsets = []
        for end in range(2):
            shear = rng.uniform(-.014,.014)
            row = [shear*y/w+rng.uniform(-.006,.006) for y,z in section]
            if chipped and end == chipped-1:
                shear = rng.uniform(.032,.094)
                for i in (3,4): row[i] -= shear
                row[6] -= rng.uniform(.066,.205)
            end_offsets.append(row)
        cup = rng.uniform(-.009,.015)
        bend = rng.uniform(-.012,.012)
        twist = rng.uniform(-.015,.015)
        u = rng.choice((.117,.177,.227,.327,.439,.488,.567,.685,.798,.936))
        v = rng.random()
        grain = rng.uniform(.37,.50)
        vertices=[]; coords=[]; faces=[]
        for j,f in enumerate((-.5,-.26,.06,.33,.5)):
            for i,(y,z) in enumerate(section):
                x = f*length
                if j in (0,4): x += (-1 if j==0 else 1)*end_offsets[j==4][i]
                warp = 1 - (2*f)**2
                vertices.append(centre+along*x+across*(y+bend*warp)+normal*(z+cup*warp+twist*2*f*y/w))
                coords.append((u+.038*y/w,v+x*grain))
            if j:
                for i in range(len(section)):
                    a=(j-1)*len(section)+i
                    b=(j-1)*len(section)+(i+1)%len(section)
                    faces.append((a,b,b+len(section),a+len(section)))
        n=len(section)
        faces += [tuple(reversed(range(n))),tuple(range(n*4,n*5))]
        groups[name].add(vertices,faces,"Timber")
        wood_uv.setdefault(name,[]).extend(coords)

    def original_box_points(data):
        for face in data.faces:
            base = face[-1]
            if len(face)==4 and face==(base+3,base+2,base+1,base):
                yield [Vector(v) for v in data.verts[base:base+8]]

    def blade(batch, base, towards, side, length, bend, width, slope):
        points=[]; faces=[]; coords=[]
        for k,t in enumerate((0,.20,.44,.68,.86,1)):
            # The heel first grows upwards, then follows its own weight over the lip.
            mid = base+towards*(length*(.18*t+.82*t*t))+UP*(length*(slope*t-bend*t*t))
            w = width*(.035+math.sin(math.pi*(.11+.89*t))**.72)
            points.extend((mid-side*w, mid+towards*w*.55, mid+side*w))
            coords.extend(((0,t),(.5,t),(1,t)))
            if k:
                i=3*k
                faces.extend(((i-3,i-2,i+1,i),(i-2,i-1,i+2,i+1)))
        groups[batch].add(points,faces,"Sedge")
        leaf_uv.setdefault(batch,[]).extend(coords)

    def cut_bank(idx, centre, forward, out, half_width):
        batch="West_bank_turf_"+str(idx)
        count=max(8,int(half_width*9))
        verts=[]; faces=[]; materials=[]; roots=[]
        for j in range(count+1):
            s=j/count; along=(2*s-1)*half_width
            profile=math.sin(math.pi*s)**.65
            pos=centre+forward*along
            wav=.13*math.sin(5*s+idx)+.084*math.cos(14*s+idx)
            r=3.20+wav
            back=pos+out*(r+.56)
            heel=pos+out*(r+.15)
            nose=pos+out*(r-.04-profile*(.24+rng.uniform(0,.18)))
            bottom=pos+out*(r-.22-profile*.24)
            z=ground(heel.x,heel.y)
            # Ends and the back of the sod dovetail back into the surveyed terrain.
            tip_height=ground(nose.x,nose.y)*(1-profile)+z*profile+.045*profile-.035
            points=[Vector((back.x,back.y,ground(back.x,back.y)-.07)),
                    Vector((heel.x,heel.y,z+.04*profile-.026)),
                    Vector((nose.x,nose.y,tip_height)),
                    Vector((nose.x,nose.y,tip_height-profile*rng.uniform(.13,.26)-.05)),
                    Vector((bottom.x,bottom.y,ground(bottom.x,bottom.y)-.08))]
            verts.extend(points)
            roots.append(points[2])
            if j:
                for k,mat in enumerate(("Sod","Sod","Earth","Earth")):
                    a=j*5+k
                    face=(a-5,a-4,a+1,a)
                    if forward.cross(out).z<0: face=face[::-1]
                    faces.append(face); materials.append(mat)
        groups[batch].add(verts,faces,materials)
        leaves="Folded_sedge_West_"+str(idx)
        for j,root in enumerate(roots[1:-1]):
            if j%3==1:
                start=root-UP*.08
                elbow=start-out*rng.uniform(.06,.16)+forward*rng.uniform(-.08,.08)-UP*rng.uniform(.13,.24)
                tip=elbow+forward*rng.uniform(-.14,.14)-UP*rng.uniform(.13,.34)
                pole(batch,"Bark",start,elbow,.028,.016,7)
                pole(batch,"Bark",elbow,tip,.016,.003,6)
                whisker=elbow-forward*.13-UP*.15
                pole(batch,"Bark",elbow,whisker,.009,.002,5)
            # Pressed thatch lies diagonally along the ledge. Its shorter, interleaved tips
            # make a continuous sod mat between the large hanging tufts.
            if j%4:
                for k in range(6):
                    direction=(forward*rng.uniform(-.9,.62)-out*rng.uniform(.22,.73)).normalized()
                    blade(leaves,root-UP*.01+out*rng.uniform(.0,.12),direction,UP.cross(direction),
                          rng.uniform(.11,.31),rng.uniform(.36,.82),rng.uniform(.010,.020),rng.uniform(.52,.96))
            if j%5 in (1,2):continue
            foot=root+out*.028-UP*.02
            for k in range(rng.randint(7,15)):
                a=rng.uniform(-1.48,1.48)
                d=-out*math.cos(a)+forward*math.sin(a)
                folded=(k%6==0)
                blade(leaves,foot+forward*rng.uniform(-.06,.06),d,UP.cross(d),
                      rng.uniform(.27,.66),rng.uniform(.45,.86) if folded else rng.uniform(.8,1.53),
                      rng.uniform(.009,.020),rng.uniform(1.10,1.48) if folded else rng.uniform(.45,1.02))
            if j%6==0:
                peak=foot+forward*rng.uniform(-.12,.13)-out*.16+UP*rng.uniform(.45,.75)
                pole(batch,"Seed",foot,peak,.009,.0035,6)
                for k in range(5):
                    at=peak-UP*(k*.029)
                    to=at+forward*(.036*(-1)**k)-out*.028-UP*.01
                    pole(batch,"Seed",at,to,.003,.002,5)
                    pole(batch,"Seed",to,to-UP*rng.uniform(.038,.07),.009,.002,6)

    for idx,(a,b) in enumerate(zip(path[:3],path[1:4])):
        a=Vector((*a,0)); b=Vector((*b,0))
        forward=(b-a).normalized(); left=UP.cross(forward)
        src=groups["Walkway_"+str(idx)]
        deck="Weathered_Walkway_"+str(idx)
        for i,points in enumerate(original_box_points(src)):
            centre=sum(points,Vector())/8
            if i<2:
                plank(deck,centre,forward,left,(points[1]-points[0]).length,.12,.045,0)
                continue
            along=points[3]-points[0]; along.z=0; length=along.length; along.normalize()
            # Wide squared sleepers alternate with narrow repairs and boards spliced at one bearer.
            width=(points[1]-points[0]).length * rng.choice((.86,.95,1.,1.04))
            across=UP.cross(along)
            centre+=along*rng.uniform(-.058,.058)+UP*rng.uniform(-.008,.008)
            thickness=rng.uniform(.051,.102)
            if (i+idx*4)%13 in (3,4):
                frac=rng.uniform(.28,.71)
                for lo,hi in ((-.5,frac-.5),(frac-.5,.5)):
                    mid=centre+along*((lo+hi)*length*.5)+UP*rng.uniform(-.006,.007)
                    plank(deck,mid,along,across,(hi-lo)*length-.022,width,thickness,rng.randint(1,2))
            else:
                plank(deck,centre,along,across,length,width,thickness,rng.choice((0,0,1,2)))
        src=groups["Trench_revetment_"+str(idx)]
        wall="Weathered_Trench_revetment_"+str(idx)
        for i,points in enumerate(original_box_points(src)):
            centre=sum(points,Vector())/8
            along=points[1]-points[0]; along.z=0; length=along.length; along.normalize()
            width=(points[4]-points[0]).length
            plank(wall,centre,along,UP,length,width*rng.uniform(.90,1.08),rng.uniform(.11,.15),rng.choice((0,0,1,2)))
        bark_faces=[face for face,paint in zip(src.faces,src.paint) if paint=="Bark"]
        if bark_faces:
            ids=sorted({v for f in bark_faces for v in f}); remap={v:i for i,v in enumerate(ids)}
            groups["West_bank_reused_posts"].add([src.verts[v] for v in ids],[tuple(remap[i] for i in f) for f in bark_faces],"Bark")
        # Tussocks gather on windward lips. Leave gaps at trench junctions and the ward entrance.
        patches=([(-1,2.8,1.55),(1,4.8,1.27),(-1,7.0,1.55),(1,8.5,1.1),(-1,11.7,1.42)],
                 [(1,3.30,1.39),(-1,3.35,1.52)],
                 [(-1,2.7,1.3),(1,4.1,1.68),(-1,6.35,.95)])[idx]
        for j,(sign,at,half) in enumerate(patches):
            p=a+forward*at
            crest=p+left*sign*3.35
            if path_distance(crest.x,crest.y,path)<2.8:continue
            cut_bank(idx*10+j,p,forward,left*sign,half)
    return wood_uv
