"""Build the Osowiec medical stretcher cart, original editable geometry.
Blender front is -Y, ground origin halfway between the wheels.
"""
from pathlib import Path
import math, sys
import bpy
from mathutils import Vector
from math import pi, sin, cos, exp

src=Path(__file__).resolve().parent
sys.path.insert(0,str(src))
from make_field_cast import Mesh, marker, material, clear, save_export, TEX, dial

def tint(name,rgb):
    # Build a separate editable livery swatch without touching the cast maps.
    img=bpy.data.images.new(name,width=96,height=96,alpha=False)
    data=[]
    for y in range(96):
        for x in range(96):
            weave=(.6*cos(2*pi*x/4)+.4*cos(2*pi*(x+y)/8))*.055
            n=sin(x*46.4+y*17.0)*.026
            data.extend([min(1,max(0,v*(1+weave+n))) for v in rgb]+[1])
    img.pixels[:]=data; img.filepath_raw=str(TEX/(name+".png"));img.file_format="PNG";img.save()
    bpy.data.images.remove(img)

def beam(m,a,b,width,height,mat):
    a,b=Vector(a),Vector(b)
    v=(b-a).normalized()
    guide=Vector((1,0,0)) if abs(v.x)<.85 else Vector((0,1,0))
    u=(guide-v*guide.dot(v)).normalized()*width*.5
    w=v.cross(u).normalized()*height*.5
    ring=[]
    for p in [a,b]:
        ring.append([m.vertex(p+u*s+w*t,None) for s,t in [(-1,-1),(1,-1),(1,1),(-1,1)]])
    m.grid(ring,mat)

def wheel(name,x):
    m=Mesh()
    bands=[(.445,.036,"Walnut"),(.473,.049,"Walnut"),(.488,.045,"Steel")]
    def ring(inner,outer,half,mat):
        rows=[]
        for xx,rr in [(-half,inner),(-half,outer),(half,outer),(half,inner)]:
            rows.append([m.vertex((xx,rr*cos(a*2*pi/64),rr*sin(a*2*pi/64)),None) for a in range(64)])
        for j in range(4):
            rs=rows[j];ns=rows[(j+1)%4]
            for k in range(64): m.face((rs[k],rs[(k+1)%64],ns[(k+1)%64],ns[k]),mat)
    last=.403
    for rad,half,mat in bands:
        ring(last,rad,half,mat);last=rad
    m.tube([(-.08,0,0),(-.053,0,0),(0,0,0),(.052,0,0),(.073,0,0)],[.049,.071,.078,.068,.044],"Walnut",None,32)
    m.tube([(-.084,0,0),(-.063,0,0)],[.041,.047],"Steel",None,24)
    m.tube([(.061,0,0),(.084,0,0)],[.051,.038],"Brass",None,24)
    for k in range(14):
        a=2*pi*k/14
        c=Vector((0,cos(a),sin(a)))
        skew=.013 if k%2==0 else -.013
        m.tube([c*.060+Vector((skew,0,0)),c*.159,c*.333,c*.422],[(.020,.024),(.019,.023),(.013,.017),(.014,.016)],"Walnut",None,8)
    o=m.finish(name);o.location=(x,.08,.488)
    return o

def canvas(m):
    # A single irregular blanket over a raised stretcher load; draped sides hang inside the wooden frame.
    nx,ny=33,50
    surface=[]
    for j in range(ny):
        t=j/(ny-1)
        y=-.75+t*1.46
        row=[]
        for i in range(nx):
            q=i/(nx-1)*2-1; x=q*.34
            rounding=1-abs(q)**2.1
            shoulder=.16*exp(-((y-.28)/.30)**2)
            knee=.122*exp(-((y+.36)/.21)**2)
            broad=.09+.06*sin(pi*t)+shoulder+knee
            edge=.003*sin(2*pi*3*t)
            crease=.010*cos((q*3.1+t*1.4)*pi)+.014*cos(t*9.5*pi+3*q)*(abs(q)**.8)
            z=.698+broad*rounding +.030*cos(t*4*pi)*rounding+crease-.135*abs(q)**7
            row.append(m.vertex((x+edge*abs(q),y,z),None))
        surface.append(row)
    for j in range(ny-1):
        for i in range(nx-1):
            mat="Wool"
            if j in [5,6] or j in [43,44]: mat="Piping"
            m.face((surface[j][i],surface[j][i+1],surface[j+1][i+1],surface[j+1][i]),mat)
    for side in [0,-1]:
        m.tube([m.v[row[side]] for row in surface],[.008]*len(surface),"Canvas",None,10)
    # Rolled reserve canvas strapped at the foot end, with visible wound ends and webbing.
    y=-.80;z=.804
    m.tube([(-.282,y,z),(-.269,y,z),(0,y,z),(.269,y,z),(.282,y,z)],[.061,.073,.076,.073,.06],"Canvas",None,32,.048)
    for x in [-.16,.16]:
        m.tube([(x-.013,y,z),(x+.013,y,z)],[.078,.078],"Leather",None,28)
    for side in [-1,1]:
        points=[]
        for k in range(46):
            a=k*2*pi/19;r=.009+.045*k/45
            points.append((side*.283,y+cos(a)*r,z+sin(a)*r))
        m.tube(points,[.0035]*len(points),"Piping",None,7)

def lamp(m):
    x=.394;y=.776;z=.618
    m.tube([(x,y,z-.108),(x,y,z-.094),(x,y,z-.072)],[.040,.048,.043],"Steel",None,16)
    m.tube([(x,y,z-.073),(x,y,z-.038),(x,y,z+.038),(x,y,z+.071)],[.035,.047,.047,.031],"AmberPane",None,12)
    m.tube([(x,y,z+.061),(x,y,z+.083),(x,y,z+.099)],[.046,.042,.022],"Brass",None,16)
    for k in range(4):
        a=k*pi/2+pi/4
        m.tube([(x+.044*cos(a),y+.044*sin(a),z-.065),(x+.049*cos(a),y+.049*sin(a),z+.014),(x+.041*cos(a),y+.041*sin(a),z+.070)],[.0035]*3,"Steel",None,8)
    pts=[]
    for k in range(21):
        a=pi*k/20
        pts.append((x+.043*cos(a),y,z+.084+.075*sin(a)))
    m.tube(pts,[.004]*len(pts),"Steel",None,8)
    beam(m,(.33,.77,.794),(.424,.77,.794),.014,.018,"BlueSteel")
    m.tube([(.399,.77,.793),(.399,.77,.770),(.397,.778,.760)],[.005]*3,"Brass",None)

def pneumatic_drive(frame):
    # The exchange bottle sits between the front shafts. Its visible crank is on the right.
    x,y,z=.254,-1.105,.537
    frame.box((0,-1.08,.412),(.53,.48,.071),"BlueSteel",None,.62)
    for side in [-1,1]:
        beam(frame,(side*.221,-1.29,.416),(side*.315,-.78,.55),.037,.032,"Steel")
    frame.tube([(-.215,-1.23,.626),(-.20,-1.23,.626),(-.158,-1.23,.626),
                (.143,-1.23,.626),(.19,-1.23,.626),(.207,-1.23,.626)],
               [.035,.096,.12,.12,.095,.045],"FieldPaint",None,48)
    for sx in [-.138,.127]:
        frame.tube([(sx-.011,-1.23,.626),(sx+.011,-1.23,.626)],[.122,.122],"Leather",None,40)
        frame.box((sx,-1.23,.747),(.028,.038,.013),"Brass",None,.45)
    frame.tube([(-.208,-1.23,.626),(-.245,-1.23,.626),(-.251,-1.23,.678),
                (-.251,-1.16,.707),(-.19,-1.09,.739),(-.13,-1.09,.739)],
               [.021,.018,.016,.014,.014,.012],"Brass",None,12)
    dial(frame,(-.10,-1.1,.75),(0,-.65,.76),.062,None)
    frame.tube([(-.184,-1.015,.519),(-.165,-1.015,.519),(.165,-1.015,.519),
                (.191,-1.015,.519)],[.071,.085,.085,.071],"BlueSteel",None,32)
    for sx in [-.131,-.099,-.067,-.035,-.003,.029,.061,.093,.125]:
        frame.tube([(sx-.003,-1.015,.519),(sx+.003,-1.015,.519)],[.091,.091],"Steel",None,28)
    # Perforated fender, riveted corner plates and a piping stripe stay fixed.
    for k in range(23):
        a0=pi*(-.05+.99*k/23);a1=pi*(-.05+.99*(k+1)/23)
        p0=(x,y+.177*cos(a0),z+.177*sin(a0))
        p1=(x,y+.177*cos(a1),z+.177*sin(a1))
        beam(frame,p0,p1,.067,.012,"FieldPaint")
        if k%3==0:
            frame.tube([(x+.037,p0[1],p0[2]),(x+.037,y+.112*cos(a0),z+.112*sin(a0))],
                       [.003,.003],"Steel",None,8)
    frame.box((-.043,-1.305,.674),(.097,.012,.045),"BlueSteel",None,.45)
    for sx in [-.078,-.01]:
        frame.oval((sx,-1.314,.676),(.003,.002,.003),"Brass",None,8,5)
    # Timing wheel and crank have independent origins so the exported axes stay meaningful.
    wheel=Mesh()
    loop=[(0,.145*cos(k*2*pi/56),.145*sin(k*2*pi/56)) for k in range(57)]
    wheel.tube(loop,[.018]*len(loop),"Steel",None,12)
    for k in range(6):
        a=k*pi/3
        beam(wheel,(0,.032*cos(a),.032*sin(a)),(0,.145*cos(a+.13),.145*sin(a+.13)),.013,.013,"BlueSteel")
    wheel.tube([(-.038,0,0),(.027,0,0)],[.034,.031],"Brass",None,30)
    wheel.oval((.023,.072,0),(.018,.021,.021),"Brass",None,16,8)
    flywheel=wheel.finish("DriveFlywheelGeometry");flywheel.location=(x,y,z)
    rod=Mesh()
    beam(rod,(0,0,0),(0,.365,0),.016,.024,"Steel")
    for axis in [0,.365]:
        rod.tube([(-.011,axis,0),(.014,axis,0)],[.020,.019],"Brass",None,16)
    conrod=rod.finish("DriveRodGeometry");conrod.location=(x+.031,y+.072,z)
    for yy in [-1.01,-.93,-.86]:
        frame.box((x+.031,yy,.538),(.05,.018,.094),"BlueSteel",None,.45)
    # The drive shaft runs beneath the litter to the live axle. A viewing slot
    # between the rails shows the coupling, bearing collars and the final housing.
    frame.tube([(.189,-1.04,.44),(.189,-.77,.396),(.189,-.17,.396),(.189,.073,.46)],
               [.023,.019,.019,.024],"Steel",None,18)
    for yy in [-.76,-.58,-.19]:
        frame.tube([(.189,yy-.013,.396),(.189,yy+.013,.396)],[.028,.028],"Brass",None,16)
    frame.oval((.189,.076,.469),(.067,.087,.051),"BlueSteel",None,24,12)
    # All readings are visible about the steering position, beside the sanitary load.
    return [flywheel,conrod]

def cart():
    m=Mesh()
    # Mortised side rails, planked floor and rigid iron axle.
    for s in [-1,1]:
        m.box((s*.34,.026,.565),(.060,1.83,.10),"Walnut",None,.32)
        m.box((s*.30,.025,.485),(.049,1.32,.076),"Walnut",None,.32)
    for j in range(11):
        y=-.803+j*.165
        m.box((0,y,.611),(.687,.154,.043),"Walnut",None,.28)
        for s in [-1,1]:m.oval((s*.295,y,.636),(.008,.008,.003),"Steel",None,10,6)
    m.tube([(-.57,.08,.488),(.57,.08,.488)],[.028,.028],"Steel",None,16)
    for s in [-1,1]:
        x=s*.345
        m.box((x,.079,.469),(.093,.19,.062),"BlueSteel",None,.34)
        for j in range(3):
            p=[(x,-.42+j*.045,.482+j*.011),(x,-.27,.508+j*.012),(x,.06,.523+j*.010),(x,.32,.502+j*.011),(x,.51-j*.045,.479+j*.012)]
            for a,b in zip(p,p[1:]): beam(m,a,b,.054,.012,"Steel")
        # Shafts rise naturally to gripping height, with grip wraps and bolted pivots.
        pts=[(s*.324,-.40,.53),(s*.33,-.78,.56),(s*.30,-1.12,.63),(s*.272,-1.53,.768),(s*.264,-1.68,.81)]
        m.tube(pts,[(.037,.039),(.034,.036),(.026,.033),(.024,.025),(.026,.022)],"Walnut",None,12)
        m.tube([pts[-2],pts[-1]],[.027,.027],"Leather",None,16)
        for yy in [-.67,.70]:
            m.box((x,yy,.70),(.031,.046,.266),"Walnut",None,.2)
            m.oval((s*.368,yy,.604),(.006,.013,.013),"Steel",None,12,8)
        m.box((x,.014,.757),(.028,1.54,.073),"Canvas",None,.15)
        m.box((s*.363,.014,.761),(.007,1.51,.024),"SignalRed",None,.2)
        for yy in [-.101,.10]:
            m.box((s*.368,yy,.758),(.012,.011,.102),"Leather",None,.26)
        # Neutral white diamond-shaped medical stencil set within an oxide-red panel.
        m.box((s*.367,.399,.761),(.012,.245,.119),"SignalRed",None,.20)
        outer=s*.374
        m.panel([(outer,.399,.807),(outer,.464,.761),(outer,.399,.714),(outer,.334,.761)],"IvoryStencil",None)
    m.box((0,.878,.68),(.687,.032,.142),"Walnut",None,.2)
    m.box((0,.898,.68),(.64,.012,.036),"SignalRed",None,.2)
    for y in [-.883,.844]:
        beam(m,(-.38,y,.66),(.38,y,.66),.035,.034,"Walnut")
    for x in [-.278,.278]:
        m.tube([(x,-.93,.684),(x,.96,.684)],[.019,.019],"Canvas",None,14)
    for side in [-1,1]:
        m.tube([(side*.32,.69,.565),(side*.319,.872,.623),(side*.266,1.06,.915),
                (side*.30,1.15,.921)],[.023,.022,.018,.018],"BlueSteel",None,14)
    m.tube([(-.348,1.15,.922),(.348,1.15,.922)],[.017,.017],"Walnut",None,18)
    for sx in [-.288,.288]:
        m.tube([(sx-.051,1.15,.922),(sx+.051,1.15,.922)],[.021,.021],"Leather",None,16)
    canvas(m);lamp(m)
    drive=pneumatic_drive(m)
    return drive+[m.finish("MercyCarriage"),wheel("WheelLeftGeometry",-.491),wheel("WheelRightGeometry",.491),
            marker("CartFront",(0,-1.68,.8)),marker("CartBack",(0,.9,.68))]

clear()
tint("SignalRed",(.33,.045,.026));tint("IvoryStencil",(.72,.66,.52));tint("AmberPane",(.94,.48,.13))
save_export("MercyCart",cart())
