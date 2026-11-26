"""Survey-coordinate rivers, cut banks and remnant channel of the Biebrza floodplain."""
from collections import defaultdict
from functools import lru_cache
from math import exp, floor, hypot, sin

WATER_LEVEL = -.39
# Positions and bankfull half-widths, in metres. The game map compresses the valley.
MAIN = [(-282,-92,14),(-206,-80,11),(-169,-41,12),(-114,-49,11),
        (-91,-29,10),(-84,-1,8.4),(-65,8,7.7),(-48,3.6,6.9),
        (-37,3.3,5.9),(-31.9,10.4,5.6),(-37,19.5,6.3),(-47,30.6,7.1),
        (-43,43.4,7.7),(-22,53.3,8.7),(10,52,9.4),(39,57,11.1),
        (66,84,11.7),(99,95,10.3),(142,86,12.2),(177,69,12.4),(215,74,13),(282,111,14)]
OXBOW = [(-8,-31,.3),(-13,-29.8,1.6),(-21,-26.4,2.5),(-30,-25.3,3.0),
         (-39,-27.6,3.3),(-45,-33.6,3.1),(-42,-39,3.0),(-34,-40.4,2.6),
         (-24,-38.1,1.8),(-20.2,-34.9,.36)]
BIN = 12
bins = defaultdict(list)


def smooth(a,b,x):
    t=max(0., min(1., (x-a)/(b-a)))
    return t*t*(3-2*t)


def curve(knots, lake):
    samples=[]
    for j,(p,q) in enumerate(zip(knots,knots[1:])):
        a=knots[max(j-1,0)]
        b=knots[min(j+2,len(knots)-1)]
        count=max(4,round(hypot(q[0]-p[0],q[1]-p[1])/2.3))
        for k in range(count):
            t=k/count
            samples.append(tuple(.5*(2*p[i]+(-a[i]+q[i])*t+
                 (2*a[i]-5*p[i]+4*q[i]-b[i])*t*t+
                 (-a[i]+3*p[i]-3*q[i]+b[i])*t*t*t) for i in range(3)))
    samples.append(knots[-1])
    for (x,y,r),(x1,y1,s) in zip(samples,samples[1:]):
        dx,dy=x1-x,y1-y
        reach=max(r,s)+10
        segment=(x,y,r,dx,dy,s-r,1/(dx*dx+dy*dy),lake)
        for ix in range(floor((min(x,x1)-reach)/BIN),floor((max(x,x1)+reach)/BIN)+1):
            for iy in range(floor((min(y,y1)-reach)/BIN),floor((max(y,y1)+reach)/BIN)+1):
                bins[ix,iy].append(segment)


curve(MAIN,False)
curve(OXBOW,True)


@lru_cache(maxsize=280000)
def channel(x,y):
    candidates=bins.get((floor(x/BIN),floor(y/BIN)),())
    best=1e5
    result=(30.,0.,False)
    for sx,sy,r,dx,dy,dr,inverse,lake in candidates:
        px,py=x-sx,y-sy
        t=max(0.,min(1.,(px*dx+py*dy)*inverse))
        radius=r+t*dr
        distance=hypot(px-t*dx,py-t*dy)-radius
        if distance<best:
            best=distance
            result=(distance,radius,lake)
    return result


def far_ground(x,y):
    hummocks=.20*sin(x*.16+y*.11)+.17*sin(y*.27-.17*x)+.09*sin(.52*y-.33*x)
    ridge=3.8*exp(-((x+107)/61)**2-((y-80)/27)**2)
    ridge+=5.4*exp(-((x-77)/85)**2-((y+88)/25)**2)
    return .82+hummocks+.42*sin(.041*x+.023*y)+ridge


def grade(x,y,h,protected):
    # A broken lip above the northern turn lets the water surface be seen from eye height.
    opening=exp(-((x+34.3)/5.3)**4-((y+3.7)/4.6)**4)
    h=h*(1-opening)+(.46+.12*sin(x*1.05-y*.5))*opening
    disturbance=protected*(.72*exp(-((x+10)/7.3)**2-((y+2.7)/3.1)**2)
                 +.97*exp(-((x-4)/6.6)**2-((y-4)/2.8)**2)
                 +.74*exp(-((x+28)/5.5)**2-((y-12)/4.8)**2)
                 -.50*exp(-((x-3)/11)**2-((y+6)/1.9)**2))
    h+=disturbance
    d,r,lake=channel(x,y)
    if d>=8.: return h
    # Eroded peat has a steep outer face alternating with long shallow mud shelves.
    side=.5+.5*sin(x*.10-y*.069+.68*sin(.13*y))
    shelf=1.9+3.8*side
    setback=d+.33*sin(.64*x+.31*y)+.17*sin(.85*y-.36*x)
    depth=.54 if lake else 1.48
    submerged=WATER_LEVEL-depth*smooth(0.,max(1.15,r*.78),-setback)
    top=.58+.47*side+.18*sin(x*.41-y*.25)
    bank=WATER_LEVEL+top*smooth(0,shelf,setback)
    toe=max(0.,1.-abs(setback-1.45)/2.3)
    bank+=toe*(.11*sin(x*1.9-y*.8)+.07*sin(x*.84+y*2.7))
    shaped=submerged if setback<0 else bank
    w=smooth(shelf+.35,8.,setback)
    return h*w+shaped*(1-w)


def outer_axes(half):
    positive=[half+n for n in [1,2,3,4,5,6,7,8,10,12,14,16,18,20,22,24,27,30,33,36,40,44,48]]
    positive+=list(range(half+52,265,5))
    positive.append(267)
    return [[-v for v in reversed(positive)]+[-half],
            [i*.5 for i in range(-2*half,2*half+1)], [half]+positive]


def fill_water(points):
    """Clip precisely against the same triangles used by the ground collider."""
    result=[]
    for a,b in zip(points[-1:]+points[:-1],points):
        if (a[2]<WATER_LEVEL)!=(b[2]<WATER_LEVEL):
            t=(WATER_LEVEL-a[2])/(b[2]-a[2])
            result.append((a[0]+t*(b[0]-a[0]),a[1]+t*(b[1]-a[1]),0.))
        if b[2]<WATER_LEVEL:
            result.append((b[0],b[1],min(1.,(WATER_LEVEL-b[2])/.85)))
    return result


def water_triangle(groups, depths, points):
    if min(p[2] for p in points)>=WATER_LEVEL: return
    x=sum(v[0] for v in points)/3
    y=sum(v[1] for v in points)/3
    distance,_,lake=channel(x,y)
    if distance>2.8: return
    polygon=fill_water(points)
    if len(polygon)<3: return
    name=("Stillwater_oxbow_" if lake else "River_Biebrza_")+f"{floor(x/35)}_{floor(y/35)}"
    groups[name].add([(x,y,WATER_LEVEL+.008) for x,y,d in polygon],
                     [tuple(range(len(polygon)))],"River")
    depths[name].extend([d for _,_,d in polygon])

