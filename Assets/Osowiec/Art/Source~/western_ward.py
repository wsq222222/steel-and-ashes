"""The west-trench casualty station. Coordinates match the surveyed fort, in metres."""
import math
from random import Random


def smooth(a, b, v):
    t = min(1., max(0., (v-a)/(b-a)))
    return t*t*(3.-2.*t)


def in_ward(x, y):
    return (-54 < x < -42.8 and -14.1 < y < -7.1 or
            -51.4 < x < -47.9 and -16.8 < y < -11.5)


def grade(x, y, natural):
    if x < -55.5 or x > -40.8 or y < -18.8 or y > -5:
        return natural
    room = smooth(0., 1.35, min(x+54.8, -41.5-x, y+14.65, -6.4-y))
    ramp = smooth(0., .85, min(x+51.45, -47.13-x, y+17.7, -10.4-y))
    descent = -.02-.60*smooth(-15.94, -12.03, y)
    peat=-.567+.009*math.sin(1.7*x+.41*y)+.007*math.sin(.54*x+2.9*y)
    return min(natural, natural+room*(peat-natural), natural+ramp*(descent-natural))


def build(box, pole, groups):
    rng = Random(280117)
    floor = -.54
    structure = "Solid_Ward_timber_revetment"
    furnishings = "Solid_Ward_cots_and_worktables"
    textile = "Ward_canvas_and_quilts"
    canopy = "Roof_Ward_torn_oilcloth"
    joinery = "Ward_joinery_and_supplies"

    def dome(g, m, x, y, z, rx, ry, rz):
        points, faces = [], []
        for j in range(11):
            phi = -math.pi/2 + math.pi*(.005+.99*j/10)
            for i in range(24):
                a = i*math.pi/12
                points.append((x+rx*math.cos(phi)*math.cos(a), y+ry*math.cos(phi)*math.sin(a), z+rz*math.sin(phi)))
        for j in range(10):
            for i in range(24):
                k = j*24+i; n = j*24+(i+1)%24
                faces.append((k,n,n+24,k+24))
        groups[g].add(points,faces,m)

    def cloth(g, m, x0, x1, y0, y1, shape, nx=30, ny=26):
        points=[]; faces=[]
        for j in range(ny+1):
            v=j/ny
            for i in range(nx+1):
                u=i/nx
                points.append((x0+u*(x1-x0),y0+v*(y1-y0),shape(u,v)))
                if i and j:
                    k=j*(nx+1)+i; faces.append((k-nx-2,k-nx-1,k,k-1))
        groups[g].add(points,faces,m)

    # The ramp and level deck meet exactly. The lip against the old trench remains open.
    for j in range(22):
        y=-16.45+j*.184
        z=-.015-.60*smooth(-15.94,-12.03,y)
        box("Walkway_Ward_loading_ramp","Timber",-49.40,y,z+.060,1.93,.178,.075,0,.003)
    bridge="Walkway_Ward_field_planks"
    # Short salvaged sleepers follow the aisle. The gaps expose the same peat that supports the cots.
    for j in range(51):
        if j in (8,9,27,39):continue
        x=-52.96+j*.182
        y=-11.61+.17*math.sin(j*.12+.8)+rng.uniform(-.039,.039)
        length=.87+.10*math.sin(j*.49)+rng.uniform(-.09,.17)
        if j%9==2:length*=.63
        box(bridge,"Timber",x,y,-.565+rng.uniform(-.006,.005),.151+rng.uniform(-.022,.016),length,.057,
            rng.uniform(-.052,.056),.006)
    # A pair of detachable planks serves each occupied bed.
    for x in (-52.86,-51.05):
        for side in (-1,1):
            box(bridge,"Timber",x+side*.114,-10.08,-.559,.20,1.74+side*.17,.043,side*.038,.005)
    for x in (-53.36,-43.00):
        for j in range(7):
            box(structure,"Timber",x,-10.37,floor+.13+j*.22,.15,6.16,.202,0,.014)
        for y in (-13.34,-10.5,-7.40):
            pole(structure,"Bark",(x,y,floor-.16),(x,y,1.57),.10,.078,n=11)
            if y != -13.34: pole(joinery,"Timber",(x,y-.46,.78),(x,y,1.54),.057,.049,n=8)
    for j in range(7):
        cut=-48.3+1.1*math.sin(j*2.41)
        for start,end in [(-53.43,cut),(cut+.020,-42.92)]:
            box(structure,"Timber",(start+end)/2,-7.36+rng.uniform(-.018,.018),floor+.13+j*.22,end-start,.15,.209,0,.020)
    for x in (-53.36,-49.85,-46.40,-43.00):
        pole(structure,"Timber",(x,-7.41,floor-.13),(x,-7.41,2.02),.1,.077,n=10)
    for y in (-12.68,-10.18,-7.40):
        pole(joinery,"Bark",(-53.5,y,1.49),(-42.89,y,1.80),.075,.061,n=11)
    for x in (-53.52,-46.26,-42.89):
        pole(joinery,"Bark",(x,-13.04,1.71),(x,-7.03,1.67),.060,.052,n=11)
    def roof(u,v):
        return 1.69+.26*u+.16*v-.33*math.sin(math.pi*u)**.65*abs(math.sin(v*math.pi*2.3))+.064*math.sin(33*u+5*v)+.033*math.sin(55*u-3*v)
    cloth(canopy,"Canvas",-53.66,-42.72,-12.80,-7.0,roof,63,33)
    # Dress the inward face. Its down-facing normals catch reflected lamplight inside the shelter.
    lining="Roof_Ward_canvas_lining"
    cloth(lining,"Canvas",-53.66,-42.72,-12.80,-7.0,lambda u,v:roof(u,v)-.025,63,33)
    groups[lining].faces[:]=[tuple(reversed(f)) for f in groups[lining].faces]
    # A burlap repair is sewn across the low western seam, visible from below.
    patch="Roof_Ward_burlap_patch"
    xmin,xmax,ymin,ymax=-51.80,-49.78,-11.15,-9.56
    def repair_z(x,y):
        return roof((x+53.66)/10.94,(y+12.80)/5.8)-.046-.035*math.sin((x-xmin)*1.5)*math.sin((y-ymin)*1.9)
    cloth(patch,"Canvas",xmin,xmax,ymin,ymax,lambda u,v:repair_z(xmin+(xmax-xmin)*u,ymin+(ymax-ymin)*v),20,16)
    groups[patch].faces[:]=[tuple(reversed(f)) for f in groups[patch].faces]
    for x in (xmin+.024,xmax-.024):
        for i in range(15):
            y=ymin+.04+i*.099
            pole(patch,"Timber",(x,y,repair_z(x,y)-.004),(x+.019,y+.058,repair_z(x+.019,y+.058)-.008),.006,.005,n=5)
    for u in (.01,.25,.50,.75,.99):
        prev=None
        for j in range(27):
            v=j/26; point=(-53.66+10.94*u,-12.80+5.8*v,roof(u,v)+.008)
            if prev: pole(canopy,"Tar",prev,point,.006,.006,n=5)
            prev=point
    # Shutter drawn across the rear side of the ward after the final delivery.
    for i in range(19):
        x=-47.80+i*.074
        box("Ward_final_curtain","Canvas",x,-7.77+.07*math.sin(i*1.05),.64,.082,.035,1.98-.034*math.sin(i*.8))
    # Narrow partition run, rolled back at the front to preserve a sight line.
    pole(joinery,"Timber",(-46.8,-12.7,-.37),(-46.8,-12.7,2.24),.08,.061,n=10)
    box(joinery,"Timber",-46.8,-12.7,1.98,1.18,.068,.39)
    for y in (-12.741,-12.659):
        box(joinery,"Flag",-46.80,y,1.98,.27,.012,.095)
        box(joinery,"Flag",-46.80,y,1.98,.095,.013,.27)
    # Headboards, bowed mattresses, webbing and three named casualty tags.
    for i,x in enumerate((-52.32,-50.50,-48.68,-46.86,-45.04)):
        y=-8.91
        for dx in (-.35,.35):
            pole(furnishings,"Timber",(x+dx,y-1.01,-.08),(x+dx,y+1.02,-.08),.037,n=9)
            for dy in (-.78,.78):
                pole(furnishings,"Timber",(x+dx,y+dy,floor),(x+dx,y+dy,-.05),.039,.032,n=9)
        cloth(textile,"Canvas",x-.34,x+.34,y-.85,y+.86,lambda u,v: -.052-.044*math.sin(u*math.pi),15,23)
        dome(textile,"Canvas",x,y+.55,.02,.24,.19,.074)
        g="Ward_admitted_"+str(i+1)
        def blanket(u,v):
            side=abs(2*u-1)
            return -.015+.235*(1-side**2)*(.65+.35*math.cos((v-.72)*math.pi)) + .019*math.sin(31*v+5*u)*(1-.53*side)-.098*side**4
        cloth(g,"Canvas",x-.355,x+.355,y-.84,y+.40,blanket,22,36)
        dome(g,"Canvas",x,y+.48,.135,.117,.137,.120)
        box(g,"Canvas",x+.31,y-.71,-.115,.094,.014,.115,-.14)
    # The wash station is against the east wall, clear of the five-cot intake aisle.
    rawbox,rawpole=box,pole
    def box(g,m,x,y,z,*args):rawbox(g,m,x+3.4,y,z,*args)
    def pole(g,m,a,b,*args,**opts):
        rawpole(g,m,(a[0]+3.4,a[1],a[2]),(b[0]+3.4,b[1],b[2]),*args,**opts)
    box(furnishings,"Timber",-47.02,-10.78,.27,.85,1.98,.10)
    for x in (-47.33,-46.72):
        for y in (-11.59,-9.97): box(furnishings,"Timber",x,y,-.16,.10,.10,.75)
    for j in range(4):
        box(joinery,"Canvas",-46.91,-10.12,.36+j*.046,.41,.42,.046,.035*j)
    def vessel(g,x,y,bottom,profile,mat):
        pts=[]; faces=[]
        for z,r in profile:
            for k in range(24):
                a=k*math.pi/12;pts.append((x+3.4+r*math.cos(a),y+r*math.sin(a),bottom+z))
        for j in range(len(profile)-1):
            for k in range(24):
                a=24*j+k;b=24*j+(k+1)%24
                faces.append((a,b,b+24,a+24))
        groups[g].add(pts,faces,mat)
    vessel(joinery,-47.09,-11.30,.321,[(0,.105),(.022,.12),(.11,.185),(.145,.205),(.157,.203),(.142,.187),(.035,.11)],"Concrete")
    for i in range(6):
        x=-46.94+(i%2)*.12; y=-10.50+(i//2)*.15
        vessel(joinery,x,y,.32,[(0,.04),(.11,.042),(.13,.026),(.17,.021),(.18,.025)],"Glass" if i%3==0 else "Brass")
        pole(joinery,"Timber",(x,y,.49),(x,y,.527),.019,n=9)
    # A rain butt near the foot of the ramp is the orderly's water source.
    vessel(joinery,-47.05,-12.22,floor,[(0,.29),(.10,.33),(.42,.366),(.69,.325),(.73,.31),(.72,.288)],"Timber")
    for h,r in ((.10,.336),(.55,.356)):
        vessel(joinery,-47.05,-12.22,floor+h,[(0,r),(.049,r)],"Oxide")
    # Hanging oil lamp with individual cage bars and a burner.
    box,pole=rawbox,rawpole
    for x,y in ((-49.06,-12.62),(-50.40,-8.01)):
        pole(joinery,"Oxide",(x,y,1.67),(x,y,1.30),.009,n=6)
        vessel(joinery,x-3.4,y,.93,[(0,.085),(.045,.10),(.065,.079),(.22,.069),(.25,.093),(.28,.034)],"Brass")
        dome(joinery,"Glass",x,y,1.065,.066,.066,.089)
        for k in range(5):
            a=k*math.pi*.4;dx=.084*math.cos(a);dy=.084*math.sin(a)
            pole(joinery,"Oxide",(x+dx,y+dy,.985),(x+dx*.86,y+dy*.86,1.178),.007,n=6)

    # Splashed peat gathers in the gaps at stretcher turns and under the cots.
    wear="Ward_mud_and_bedside_gear"
    for i in range(160):
        x=rng.uniform(-53.16,-43.20);y=rng.uniform(-13.14,-7.61)
        if rng.random()>.40 and y>-10.45:continue
        dx=rng.uniform(.04,.28);dy=dx*rng.uniform(.28,.9)
        base=grade(x,y,1.8)
        points=[(x,y,base+.014)]
        for j in range(12):
            a=j*math.tau/12;rough=rng.uniform(.72,1.1)
            xx=x+dx*rough*math.cos(a);yy=y+dy*rough*math.sin(a)
            points.append((xx,yy,grade(xx,yy,1.8)+rng.uniform(.001,.006)))
        groups[wear].add(points,[(0,j+1,(j+1)%12+1) for j in range(12)],"Earth")
        if i%3==0:
            pole(wear,"Seed",(x,y,base+.015),(x+dx*1.8,y+dy*.52,base+.009),.003,.0015,n=4)
    # Two dressing trays under the cots; bandages and turned-over enamelware.
    for idx,(x,y) in enumerate(((-52.07,-9.42),(-50.76,-8.95),(-44.04,-10.13))):
        box(wear,"Timber",x,y,-.484,.58,.42,.07,.08*idx)
        for off in (-.21,.21):box(wear,"Timber",x,y+off,-.413,.60,.029,.15,.01*idx)
        for off in (-.29,.29):box(wear,"Timber",x+off,y,-.413,.029,.42,.15)
        for k in range(4):
            sx=x-.17+.1*k;ey=y+.032*math.sin(k*2.2)
            pole(wear,"Canvas",(sx,ey-.105,-.405),(sx+.01,ey+.105,-.405),.046,.049,n=13)
        pole(wear,"Oxide",(x+.17,y-.12,-.42),(x+.17,y+.11,-.42),.015,.01,n=7)
    # A patched edge of oilcloth hangs unevenly, with the central doorway kept clear.
    for start,end in ((-53.56,-51.12),(-47.73,-43.00)):
        vv=[];ff=[]
        for j in range(39):
            x=start+(end-start)*j/38
            top=roof((x+53.66)/10.94,.0)
            drop=.14+.103*math.sin(j*.41)+.07*math.sin(j*.91)
            for k in range(3):
                vv.append((x,-12.80-.06*math.sin(j*.71+k*.4)-.046*k,top-k*drop*.70))
                if j and k: 
                    n=len(vv)-1;ff.append((n-4,n-1,n,n-3))
        groups[canopy].add(vv,ff,"Canvas")
    # Loose sacking, chests and rope against the back wall.
    for i,(x,y) in enumerate(((-52.97,-7.87),(-49.54,-7.68),(-43.40,-8.32))):
        box(wear,"Timber",x,y,-.384,.53,.51,.31,.09*(i-1),.022)
        for strap in (-.16,.16):box(wear,"Oxide",x+strap,y,-.222,.029,.51,.013,.025*i)
        dome(wear,"Canvas",x,y+.06,-.165,.214,.149,.086)
    # A collapsed canvas litter dries against two forked props behind the occupied beds.
    spare="Ward_spare_canvas_litter"
    span=.62;cx=-49.46
    for side in (-1,1):
        x=cx+side*span*.5
        pole(joinery,"Bark",(x,-7.87,-.55),(x+.12,-7.61,1.56),.036,.031,n=10)
        pole(joinery,"Bark",(x-.12,-7.68,.14),(x+.16,-7.55,1.44),.043,.025,n=9)
        pole(joinery,"Timber",(x+.10,-7.63,1.31),(x+.19,-7.64,1.52),.022,.013,n=7)
    vv=[];ff=[]
    for j in range(23):
        v=j/22
        for i in range(11):
            u=i/10
            vv.append((cx+(u-.5)*span+.11*v,-7.835+.19*v-.13*math.sin(math.pi*u)
                       +.024*math.sin(9*v+u*8),-.29+1.58*v-.061*math.sin(math.pi*u)))
            if i and j:
                n=j*11+i;ff.append((n-12,n-11,n,n-1))
    groups[spare].add(vv,ff,"Canvas")
