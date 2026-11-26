"""Tailored patterns, field leatherwork and role equipment. Metres, Z up, -Y front."""
from math import atan2, cos, exp, pi, sin
from mathutils import Vector


def face_out(m, verts, material, normal):
    a,b,c=(Vector(m.v[i]) for i in verts[:3])
    m.face(verts if (b-a).cross(c-a).dot(Vector(normal))>=0 else verts[::-1], material)


def strap(m, points, widths, outward, material, bone, edge=None):
    """A real strap of flat stock, with a turned edge and a thickness on both sides."""
    points=list(map(Vector,points))
    normal=Vector(outward).normalized()
    rows=[]
    for i,(p,w) in enumerate(zip(points,widths)):
        tangent=points[min(i+1,len(points)-1)]-points[max(0,i-1)]
        side=normal.cross(tangent).normalized()
        if side.length<.1:side=Vector((1,0,0))
        rows.append([m.vertex(p+side*w*.50,bone),m.vertex(p-side*w*.50,bone),
                     m.vertex(p-side*w*.50-normal*.003,bone),m.vertex(p+side*w*.50-normal*.003,bone)])
    for lo,hi in zip(rows,rows[1:]):
        face_out(m,[lo[0],lo[1],hi[1],hi[0]],material,normal)
        m.face((lo[2],lo[3],hi[3],hi[2]),material)
        m.face((lo[0],hi[0],hi[3],lo[3]),material)
        m.face((lo[1],lo[2],hi[2],hi[1]),material)
    if edge:
        for j in (0,1):
            m.tube([m.v[row[j]] for row in rows],[.002]*len(rows),edge,bone,6)


def oval_loop(m,c,u,v,material,bone,width,height,wire=.003):
    c,u,v=Vector(c),Vector(u).normalized(),Vector(v).normalized()
    pts=[(-.36,-.5),(.36,-.5),(.5,-.33),(.5,.33),(.36,.5),(-.36,.5),(-.5,.33),(-.5,-.33),(-.36,-.5)]
    line=[c+u*(x*width)+v*(y*height) for x,y in pts]
    m.tube(line,[wire]*len(line),material,bone,8)


def folded_neckcloth(m, wool):
    # Bias-cut field wool under the blouse. The bottom rests on the shoulders and the top hugs the neck.
    # Large diagonal folds are geometry so the raking light and silhouette read on the actual infantry rig.
    sections, levels = 88, 22
    def point(a, v):
        front = (1 + cos(a)) * .5
        bottom = 1.442 - .048 * front - .037 * exp(-((a + .48) / .47) ** 2)
        top = 1.467 + .029 * (1 - front) + .005 * sin(2 * a)
        z = bottom + (top - bottom) * v
        width = .127 * (1 - v) + .063 * v
        depth = .134 * (1 - v) + .068 * v
        # Two overlapping turns of fabric. The slight phase shifts prevent parallel, machined rings.
        sweep = v + .17 * sin(a - .36) + .051 * sin(3*a + .4)
        roll = .009 * exp(-((sweep - .27) / .115) ** 2) + .006 * exp(-((sweep - .67) / .13) ** 2)
        weave = .0017 * sin(17*a + 4*v) * (1-v)
        return ((width + roll + weave) * sin(a), .004 - (depth + roll + weave) * cos(a), z)
    grid=[]
    for j in range(levels):
        grid.append([m.vertex(point(-pi + 2*pi*i/sections, j/(levels-1)), "Neck") for i in range(sections)])
    m.grid(grid, wool, False)
    rim=[point(-pi+2*pi*i/sections, 1) for i in range(sections+1)]
    m.tube(rim,[.0034]*len(rim),wool,"Neck",8)
    # The folded edge crosses the throat on a bias and disappears underneath the front overlap.
    hem=[point(-1.56 + i*.067, .46+.21*sin(-1.56 + i*.067)) for i in range(50)]
    m.tube(hem,[.0036]*len(hem),wool,"Neck",8)


def fitted_chinstrap(m, side):
    # Flat leather follows the jaw. Keep the lower edge below the chin as the head turns.
    s=side
    path=[(s*.101,-.016,1.709),(s*.099,-.023,1.655),(s*.092,-.028,1.614),
          (s*.079,-.039,1.573),(s*.061,-.052,1.541),(s*.036,-.054,1.520),(0,-.052,1.507)]
    outward=(s*.84,-.52,-.13)
    strap(m,path,[.016,.015,.015,.016,.016,.015,.015],outward,"Leather","Head")
    if s>0:
        center=Vector((.094,-.030,1.627))
        oval_loop(m,center,(-.28,-.93,.06),(-.16,.08,.98),"Brass","Head",.021,.028,.002)
        m.tube([(.093,-.028,1.627),(.095,-.036,1.633)],[.0017,.0013],"Brass","Head",6)


def weights(z,x,hem):
    skirt=max(0,min(1,(1.015-z)/max(.11,1.015-hem)))
    if skirt<=.001:return "Chest"
    leg=skirt*.31*min(1,abs(x)/.09)
    return tuple((b,q) for b,q in (("Chest",1-skirt*.9),("Pelvis",skirt*.9-leg),
        ("RThigh" if x>0 else "LThigh",leg)) if q>.00001)


def coat(m,role,wool):
    # Independently cut left/right fronts and a lapped back vent. The hem breaks
    # at the knees and its broad compression folds taper beneath the belt.
    hem=(.51,.63,.735,.63)[role]
    profile=[(hem,.25,.166,.022),(hem+.09,.238,.158,.022),(.91,.20,.144,.020),
             (1.025,.172,.125,.012),(1.09,.167,.122,.013),(1.22,.195,.140,.017),
             (1.27,.192,.138,.015),(1.35,.191,.140,.013),(1.393,.196,.125,.010),
             (1.415,.181,.109,.009),(1.435,.152,.097,.009),(1.452,.110,.084,.010),
             (1.473,.067,.065,.012)]
    armholes={}
    def shape(theta,z,side,layer=0):
        j=next((k for k in range(len(profile)-1) if z<=profile[k+1][0]),len(profile)-2)
        a,b=profile[j:j+2]; t=(z-a[0])/(b[0]-a[0])
        rx,ry,cy=[a[i]+(b[i]-a[i])*t for i in (1,2,3)]
        bell=max(0,min(1,(1.04-z)/(1.04-hem)))
        front_gap=.14*bell**1.6
        back_gap=(.05+.21*bell)*bell**2
        angle=front_gap+(pi-back_gap-front_gap)*theta/pi
        pleats=(.010*sin(angle*6+.61*side+.9*bell)+.006*sin(angle*11+.4-1.4*bell))
        # Side folds radiate from the hip pocket and rear kick-pleat.
        channel=exp(-((angle-1.72-.34*bell)/.13)**2)+.68*exp(-((angle-2.69+.2*bell)/.11)**2)
        dr=bell*(pleats-.011*channel)+.004*sin(2*angle+z*29)*exp(-((z-.98)/.13)**2)
        drift=.016*bell*sin(angle*3+side*.6)+.014*bell*cos(angle+side*.8)
        return Vector((side*(rx+dr+layer)*sin(angle),cy-(ry+dr+layer)*cos(angle),
                       z+drift+(.021 if side>0 else 0)*bell*exp(-(angle/.7)**2)))
    for side in (-1,1):
        heights=sorted(set([x[0] for x in profile]+[hem+.015,hem+.042, (hem+.91)/2,.958,1.14,1.307,1.372]))
        angles=[pi*j/38 for j in range(39)]
        grid=[]
        for z in heights:
            row=[]
            for angle in angles:
                if 1.27<z<1.435 and angles[10]<angle<angles[28]:
                    row.append(None)
                    continue
                p=shape(angle,z,side)
                vertex=m.vertex(p,weights(z,p.x,hem))
                # Continuous circumference and cloth grain. Each role owns its own
                # quadrant, with the damp tide line physically at the irregular hem.
                u=.5*angle/pi if side>0 else 1-.5*angle/pi
                v=max(0,min(1,(z-hem)/(1.474-hem)))
                m.uv[vertex]=((role%2+.012+.976*u)/2,(1-role//2+.012+.976*v)/2)
                row.append(vertex)
            grid.append(row)
        bottom,top=heights.index(1.27),heights.index(1.435)
        front,back=10,28
        # Leave a real armhole in each coat panel. The sleeve begins on these exact vertex indices.
        border=[(top,j) for j in range(19,back+1)]
        border += [(k,back) for k in range(top-1,bottom-1,-1)]
        border += [(bottom,j) for j in range(back-1,front-1,-1)]
        border += [(k,front) for k in range(bottom+1,top+1)]
        border += [(top,j) for j in range(front+1,19)]
        armholes[side]=[grid[k][j] for k,j in border]
        for k,(lo,hi) in enumerate(zip(grid,grid[1:])):
            for j in range(len(angles)-1):
                if bottom<=k<top and front<=j<back:continue
                angle=(angles[j]+angles[j+1])*.5
                face_out(m,[lo[j],lo[j+1],hi[j+1],hi[j]],"FieldGreatcoat",(side*sin(angle),-cos(angle),0))
        # The underside is separately coloured and closes the silhouette when walking.
        for rowj,edge in ((0,"Leather"),(len(angles)-1,wool)):
            border=[shape(angles[rowj],z,side,-.024) for z in heights]
            lining=[m.vertex(p,weights(z,p.x,hem)) for p,z in zip(border,heights)]
            for k in range(len(heights)-1):
                face_out(m,[grid[k][rowj],lining[k],lining[k+1],grid[k+1][rowj]],edge,(side,0,0))
        for rowidx,z2 in ((0,hem+.025),(1,hem+.041)):
            lining=[]
            for a in angles:
                p=shape(a,z2,side,-.008);lining.append(m.vertex(p,weights(z2,p.x,hem)))
            lo=grid[rowidx]
            for j in range(len(angles)-1):
                m.face((lo[j],lining[j],lining[j+1],lo[j+1]),"Boot" if rowidx==0 else wool)
        # Raised side seams, front facing and hem. Bind each seam ring to the same
        # distribution as its surrounding cloth so the gait does not tear it loose.
        for angle,top,rad,mat in ((.024,1.41,.0045,"Leather"),(1.62,1.397,.0038,"Canvas"),(2.83,1.39,.003,wool)):
            if angles[10]<angle<angles[28]:top=1.27
            hs=[z for z in heights if z<=top]
            pp=[shape(angle,z,side,.003) for z in hs]
            begin=len(m.skin);m.tube(pp,[rad]*len(pp),mat,"Chest",7)
            for j,(p,z) in enumerate(zip(pp,hs)):
                m.skin[begin+j*7:begin+(j+1)*7]=[weights(z,p.x,hem)]*7
        # Diagonal welt pocket and its rounded lip, attached along the actual garment surface.
        pp=[shape(a,z,side,.01) for a,z in ((.73,.946),(.87,.956),(1.11,.979),(1.39,1.005))]
        strap(m,pp,[.012,.026,.026,.015],(side*.85,-.47,.06),"Leather","Pelvis","Canvas")
    return armholes


def bag(m,center,normal,w,h,d,body,flap,bone,double=False):
    """A gusseted soft case: pinched mouth, rounded bottom and a curved saddle-stitched flap."""
    c=Vector(center); n=Vector(normal).normalized(); up=Vector((0,0,1))
    u=up.cross(n).normalized()
    shape=[(-.39,-.35),(-.49,-.20),(-.50,.21),(-.42,.42),(-.17,.50),
           (.17,.50),(.42,.42),(.50,.21),(.49,-.20),(.39,-.35)]
    rings=[]
    for z,s,bulge in ((-.5,.83,.78),(-.42,.98,.90),(-.22,1.04,1.10),(.30,1,.95),(.43,.94,.78),(.5,.86,.64)):
        ring=[]
        for x,y in shape:
            p=c+u*x*w*s+n*(y+.39)*d*bulge+up*(z*h-.007*abs(x))
            ring.append(m.vertex(p,bone))
        rings.append(ring)
    for lo,hi in zip(rings,rings[1:]):
        for j in range(len(shape)):
            j2=(j+1)%len(shape)
            face_out(m,[lo[j],lo[j2],hi[j2],hi[j]],body, u*(shape[j][0]+shape[j2][0])+n*(shape[j][1]+shape[j2][1]))
    face_out(m,rings[-1],body,up);face_out(m,rings[0],body,-up)
    top=[];bottom=[]
    for j in range(15):
        t=j/14*2-1
        top.append(c+u*t*w*.515+n*(.57-.23*t*t)*d+up*(h*.505))
        bottom.append(c+u*t*w*.472+n*(.985-.20*t*t)*d+up*(h*(.035+.114*t*t)))
    facegrid=[]
    for j in range(4):
        f=j/3;rr=[]
        for a,b in zip(top,bottom):
            p=a.lerp(b,f)+n*(sin(pi*f)*d*.14)
            rr.append(m.vertex(p,bone))
        facegrid.append(rr)
    for a,b in zip(facegrid,facegrid[1:]):
        for j in range(14):face_out(m,[a[j],a[j+1],b[j+1],b[j]],flap,n)
    m.tube(bottom,[.0028]*len(bottom),"Piping",bone,6)
    for pos in ((-.24,.24) if double else (0,)):
        at=c+u*w*pos+n*d*.97
        strap(m,[at+up*h*.32,at+up*h*.064,at-up*h*.23],[.020,.017,.015],n,"Leather",bone)
        oval_loop(m,at+n*.004-up*h*.045,u,up,"Brass",bone,.026,.024,.0034)
    return c+up*h*.26+n*d*.99,u,n


def mantle(m,role,wool):
    # Woven cape over the existing yoke. Interrupted at the front for the rifle
    # sling; its changing radius lies over the shoulder, armholes and pack straps.
    c="Chest"; spread=.286 if role==1 else .284; rr=[]
    for k,(rx,ry,z) in enumerate(((.069,.077,1.473),(.135,.107,1.450),(.212,.133,1.424),
                                  (spread*.93,.154,1.392),(spread,.166,1.341))):
        row=[]
        for j in range(53):
            t=j/52; a=.33+(2*pi-.66)*t
            if role==1:
                hem=-.122*(.5+.5*sin(a-.6))-.070*(.5-.5*cos(a))
            else:hem=-.082-.076*(.5-.5*cos(a))+.024*sin(3*a+.5)
            f=k/4
            w=.0065*f*f*cos(9*a+.4*f)
            p=(sin(a)*(rx+w+.006*sin(a)*f),.011-cos(a)*(ry+w),z+hem*f*f)
            share=.64*f*f*abs(sin(a))**3
            arm=("R" if sin(a)>0 else "L")+"Arm"
            row.append(m.vertex(p,((c,1-share),(arm,share)) if share>.001 else c))
        rr.append(row)
    for i,(a,b) in enumerate(zip(rr,rr[1:])):
        for j in range(len(a)-1):
            rad=.33+(2*pi-.66)*(j+.5)/52
            face_out(m,[a[j],a[j+1],b[j+1],b[j]],wool,(sin(rad),-cos(rad),.6))
    def braid(ids,radius,material):
        start=len(m.v)
        m.tube([m.v[i] for i in ids],[radius]*len(ids),material,c,7)
        for j,i in enumerate(ids):m.skin[start+j*7:start+(j+1)*7]=[m.skin[i]]*7
    braid(rr[-1],.003,"Leather" if role==1 else "Boot")
    for j in (0,52):braid([r[j] for r in rr],.0028,"Canvas")


def puttee(m,knee,foot,side):
    k,f=Vector(knee),Vector(foot);pts=[]; rows=[];bone=side+"Shin"
    # One overlapping continuous ribbon, disturbed in width over the ankle.
    for i in range(99):
        t=i/98; a=t*2*pi*6.35;z=.11+t*.75;mid=k.lerp(f,z)
        rad=.077-.027*t-.005*exp(-((t-.84)/.16)**2)
        p=mid+Vector((rad*cos(a),rad*sin(a),0))
        width=.029+.0025*sin(t*8)
        rows.append([m.vertex(p+Vector((0,0,width*.49)),bone),
                     m.vertex(p+Vector((.0018*cos(a),.0018*sin(a),-width*.49)),bone)])
        pts.append(p+Vector((.002*cos(a),.002*sin(a),-width*.46)))
    for j,(a,b) in enumerate(zip(rows,rows[1:])):
        angle=j/98*2*pi*6.35
        face_out(m,[a[0],a[1],b[1],b[0]],"Canvas",(cos(angle),sin(angle),0))
    m.tube(pts,[.0018]*len(pts),"Leather",bone,5)

def sleeve(m,armhole,sh,el,side,role,wool):
    # A set-in sleeve. The crown turns gradually from the vertical armhole plane
    # towards a section normal to the upper arm. Both sides share the seam vertices.
    shoulder,elbow=Vector(sh),Vector(el)
    axis=(elbow-shoulder).normalized()
    s=-1 if side=="L" else 1
    bone=side+"Arm"
    outer=Vector((s,0,0))
    outer=(outer-axis*outer.dot(axis)).normalized()
    rear=axis.cross(outer)*(-s)
    angles=[atan2((m.v[i][1]-.011)/.090,(m.v[i][2]-1.352)/.083) for i in armhole]
    rows=[armhole]
    for i in armhole:m.skin[i]=(("Chest",.90),(bone,.10))
    # Top and bottom of each oval are different landmarks. The high point drops
    # from the trapezius down over the acromion and onto the vertical deltoid.
    for tx,tz,bx,bz,depth,t,share in (
        (.188,1.430,.185,1.266,.080,.03,.27),
        (.211,1.424,.182,1.260,.084,.08,.47),
        (.236,1.410,.174,1.256,.085,.15,.67),
        (.253,1.391,.164,1.250,.082,.24,.82),
        (.267,1.363,.157,1.242,.077,.34,.94)):
        center=Vector((s*(tx+bx)*.5,shoulder.lerp(elbow,t).y,(tz+bz)*.5))
        high=Vector((s*(tx-bx)*.5,0,(tz-bz)*.5))
        rows.append([m.vertex(center+high*cos(a)+Vector((0,depth*sin(a),0)),(("Chest",1-share),(bone,share))) for a in angles])
    for t,r,d in ((.48,.074,.076),(.53,.075,.075),(.57,.075,.074),(.64,.073,.072),
                  (.68,.074,.070),(.75,.070,.067),(.86,.070,.064),(.97,.063,.061),(1.045,.060,.059)):
        center=shoulder.lerp(elbow,t)
        rows.append([m.vertex(center+(outer*r*cos(a)+rear*d*sin(a))*(1+.018*cos(5*a+t*5)),bone) for a in angles])
    for k,(lo,hi) in enumerate(zip(rows,rows[1:])):
        for j,a in enumerate(angles):
            n=(j+1)%len(angles)
            mid=atan2(sin(a)+sin(angles[n]),cos(a)+cos(angles[n]))
            cloth=wool
            if role<0 and 5<=k<=10:cloth="SignalRed" if k in (7,9) else "IvoryStencil"
            elif role>=0 and 7<=k<=12 and abs(mid)<(.48 if k in (7,12) else .71):
                cloth=("Leather","Canvas","HeavyWool","SlateWool")[role]
            quad=[lo[j],lo[n],hi[n],hi[j]]
            m.face(quad[::-1] if s>0 else quad,cloth)
    m.face(rows[-1] if s<0 else rows[-1][::-1],wool)
    if role==0:
        # A curved shell covers only the deltoid. Its inner edge sits below
        # the collarbone, and each row uses the same weights as the sleeve below it.
        columns=sorted((j for j,a in enumerate(angles) if abs(a)<1.34),key=lambda j:angles[j])
        plate=[]
        for k in range(1,7):
            row=[]
            for j in columns:
                a=angles[j]
                normal=(Vector((s*.55,sin(a),.78*cos(a))) if k<5 else outer*cos(a)+rear*sin(a)).normalized()
                i=rows[k][j]
                row.append(m.vertex(Vector(m.v[i])+normal*.005,m.skin[i]))
            plate.append(row)
        for lo,hi in zip(plate,plate[1:]):
            for j in range(len(columns)-1):
                face_out(m,[lo[j],lo[j+1],hi[j+1],hi[j]],"BlueSteel",(s,.0,.5))
        for edge in (plate[0],plate[-1]):
            for j in range(len(edge)-1):
                a,b=edge[j:j+2]
                m.tube([m.v[a],m.v[b]],[.002,.002],"Steel",m.skin[a],6)


def stamp(m,p,u,v,w,h,role,bone):
    p,u,v=Vector(p),Vector(u).normalized(),Vector(v).normalized()
    uv=((.015,.025),(.985,.025),(.985,.975),(.015,.975))
    coords=[(-1,-1),(1,-1),(1,1),(-1,1)]
    ids=[]
    for (x,y),(xx,yy) in zip(coords,uv):
        vertex=m.vertex(p+u*w*x*.5+v*h*y*.5,bone);ids.append(vertex)
        m.uv[vertex]=((xx+role%2)/2,(yy+1-role//2)/2)
    m.face(ids,"FieldTailorAtlas");m.face(list(reversed(ids)),"FieldTailorAtlas")


def kit(m,role,wool):
    c="Chest";up=Vector((0,0,1))
    # Belt gear is asymmetric and adapted to the job. A signalman carries
    # flat folding papers; an orderly carries a full bag with a divided mouth.
    if role==0:
        plate=[(-.148,-.126,.979),(-.151,-.142,.92),(-.152,-.151,.795),(-.12,-.159,.733)]
        for s in (-1,1):
            pp=[Vector((s*abs(x),y,z)) for x,y,z in plate]
            strap(m,pp,[.095,.119,.116,.084],(s*.40,-.91,.05),"Leather","Pelvis","Canvas")
            axis=Vector((s*.42,-.91,.03)).normalized();u=up.cross(axis)
            oval_loop(m,pp[1]+axis*.007,u,up,"Brass","Pelvis",.070,.038,.005)
        anchor,u,n=bag(m,(-.175,-.038,1.015),(-.89,-.48,0),.174,.185,.10,"Canvas","Boot",c,True)
        stamp(m,anchor,u,up,.078,.032,0,c)
        # An articulated left vambrace echoes the pressure bottle and cupped breastplate.
        p0=Vector((-.279,-.192,1.165)); p1=Vector((-.137,-.31,1.198));axis=(p1-p0).normalized()
        for f,rx in ((.16,.066),(.73,.057)):
            a=p0.lerp(p1,f)
            m.tube([a-axis*.011,a+axis*.011],[rx,rx],"Leather","LForearm",22)
        m.tube([p0+Vector((-.035,0,.036)),p0.lerp(p1,.43)+Vector((-.036,-.012,.059)),p1+Vector((-.016,0,.05))],
            [.021,.024,.016],"FieldPaint","LForearm",14)
        m.tube([(-.04,.293,1.385),(-.025,.323,1.40),(.026,.323,1.40),(.047,.292,1.385)],
            [.009]*4,"Brass",c,10)
        stamp(m,(0,.314,1.249),(-1,0,0),up,.081,.053,0,c)
    elif role==1:
        mantle(m,role,"Canvas")
        pp=[(-.16,.03,1.467),(-.135,-.125,1.396),(-.058,-.147,1.30),(.057,-.14,1.19),(.171,-.088,1.065)]
        strap(m,pp,[.033,.045,.042,.041,.033],(0,-1,.05),"Leather",c,"Canvas")
        anchor,u,n=bag(m,(.158,.005,.994),(.82,-.57,0),.208,.247,.113,"Leather","RifleWool",c,True)
        stamp(m,anchor+n*.003,u,up,.082,.035,1,c)
        # Folded survey tripod in its leather crown and toe boot, over the right shoulder.
        top=Vector((.181,.185,1.568));toe=Vector((.117,.224,.925));axis=(top-toe).normalized()
        for j in (-1,0,1):
            a=toe+Vector((j*.012,0,0));b=top+Vector((j*.021,0,0))
            m.tube([a,a.lerp(b,.66),b],[.008,.011,.009],"Walnut",c,12)
        for t in (.08,.45,.9):
            a=toe.lerp(top,t)
            m.tube([a-axis*.012,a+axis*.012],[(.030,.023)]*2,"Leather",c,16)
        m.tube([top-axis*.023,top,top+axis*.041],[(.029,.022),(.03,.023),(.015,.012)],"Brass",c,18)
    elif role==2:
        # A wind scarf folds over the left clavicle and hangs down the back.
        neck=(.002,-.082,1.463)
        strap(m,[(-.067,-.053,1.473),neck,(.063,-.065,1.464),(.087,-.041,1.464)],
              [.026,.030,.029,.025],(0,-1,.25),"HeavyWool","Neck")
        tail=[(.055,-.066,1.474),(.098,-.093,1.449),(.133,-.117,1.412),(.161,-.124,1.362),(.178,-.115,1.305)]
        strap(m,tail,[.048,.079,.078,.065,.042],(.44,-.9,.12),"HeavyWool",c,"Canvas")
        ptr=[tuple(Vector(p)+Vector((0,-.004,.001))) for p in tail]
        strap(m,ptr,[.008,.010,.012,.012,.008],(.44,-.9,.12),"SignalRed",c)
        strap(m,[(-.176,.032,1.456),(-.152,-.113,1.398),(-.063,-.155,1.30),(.060,-.151,1.181),(.168,-.084,1.05)],
             [.040,.055,.050,.048,.037],(0,-1,.08),"Canvas",c,"Leather")
        anchor,u,n=bag(m,(.189,-.018,.987),(.87,-.49,0),.239,.205,.126,"Canvas","Leather",c,False)
        stamp(m,anchor+n*.004,u,up,.10,.046,2,c)
        # Wrapped blanket sits diagonally against a slung pack and has rolled concentric ends.
        a=Vector((-.196,.177,1.045)); b=Vector((.125,.181,1.416)); axis=(b-a).normalized()
        positions=[a,a.lerp(b,.04),a.lerp(b,.23),a.lerp(b,.70),a.lerp(b,.96),b]
        radii=[.052,.063,.067,.069,.066,.050]
        m.tube(positions,radii,"HeavyWool",c,29,.085)
        for t in (.22,.75):
            p=a.lerp(b,t);m.tube([p-axis*.014,p+axis*.014],[.069,.068],"Leather",c,26)
        center=b+axis*.003; uu=axis.cross(Vector((0,1,0))).normalized();vv=axis.cross(uu)
        spiral=[]
        for j in range(46):
            t=j/45;spiral.append(center+(uu*cos(2*pi*t*2.25)+vv*sin(2*pi*t*2.25))*(.010+.041*t))
        m.tube(spiral,[.0032]*len(spiral),"Canvas",c,6)
        # Paired external leaf plates sit over the sprint springs, moving with each shin.
        for side,s in (("L",-1),("R",1)):
            leg=side+"Shin";n=Vector((s*.84,-.54,0));u=up.cross(n)
            center=Vector((s*.198,-.030,.411))
            strip=[center+up*.090,center+up*.057+n*.012,center-up*.026+n*.017,center-up*.112]
            strap(m,strip,[.025,.064,.052,.021],n,"FieldPaint",leg,"Steel")
    else:
        mantle(m,role,"Boot")
        # The protective bib and filter case interrupt the chest silhouette. Keep its
        # hose below the cheek so the mask is still readable in profile.
        cap=Vector((-.162,-.112,1.016));n=Vector((-.68,-.73,0));u=up.cross(n)
        anchor,_,_=bag(m,cap,n,.15,.205,.095,"SlateWool","Leather",c)
        stamp(m,anchor+n*.004,u,up,.072,.031,3,c)
        pts=[(.006,-.142,1.581),(.046,-.163,1.523),(.070,-.183,1.421),
             (.034,-.193,1.32),(-.062,-.180,1.21),(-.153,-.162,1.116)]
        m.tube(pts,[.019,.023,.024,.024,.021,.019],"Rubber",c,18)
        for i in (1,4):
            a,b=Vector(pts[i]),Vector(pts[i+1]);axis=(b-a).normalized()
            for off in (.15,.32,.49):
                p=a.lerp(b,off);m.tube([p-axis*.002,p+axis*.002],[.024,.024],"Boot",c,14)
        p=Vector((.176,-.079,1.268));n=Vector((.72,-.70,0));u=up.cross(n);stamp(m,p,u,up,.066,.039,3,c)
