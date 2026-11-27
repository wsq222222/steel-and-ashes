#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

TEXTURE2D(_OsowiecMudAtlas);
TEXTURE2D(_OsowiecMudPages);
float _OsowiecMudEnabled, _OsowiecMudClock, _OsowiecLandscapeLow, _OsowiecFlatMaterials;
float _OsowiecGallerySoil, _OsowiecBankCover;
float3 _LightDirection, _LightPosition;

struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; };
struct SoilVertex { float3 world:TEXCOORD0; float3 normal:TEXCOORD1; float cover:TEXCOORD2; };
struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normal:TEXCOORD1; half2 mixFog:TEXCOORD2; };

float2 SoilGradient(int2 cell)
{
    uint h=asuint(cell.x)*0x8da6b343u ^ asuint(cell.y)*0xd8163841u;
    h^=h>>16; h*=0x7feb352du; h^=h>>15;
    float2 g=float2(h&65535u,h>>16)*(2./65535.)-1.;
    return g*rsqrt(max(.001,dot(g,g)));
}

// Smooth gradient noise on a triangular lattice. Its broad, round depressions
// have no preferred furrow direction and no threshold that can form a steep lip.
float SoilNoise(float2 p)
{
    const float skew=.366025404, unskew=.211324865;
    int2 cell=(int2)floor(p+(p.x+p.y)*skew);
    float2 a=p-float2(cell)+(cell.x+cell.y)*unskew;
    int2 step=a.x>a.y ? int2(1,0) : int2(0,1);
    float2 b=a-float2(step)+unskew, c=a-1.+2.*unskew;
    float3 w=max(0.,.5-float3(dot(a,a),dot(b,b),dot(c,c)));
    w*=w;
    float3 d=float3(dot(a,SoilGradient(cell)),dot(b,SoilGradient(cell+step)),dot(c,SoilGradient(cell+1)));
    return 70.*dot(w*w,d);
}

// Pushed-up ridges, collapsed clods and shallow channels share a world-space
// height function across every survey tile, including the exchangeable crater shells.
float UntrackedHeight(float2 p, float up)
{
    float slope=smoothstep(.40,.88,up);
    float2 turned=float2(dot(p,float2(.8,.6)),dot(p,float2(-.6,.8)));
    float hollow=SoilNoise(p*.86+float2(17.5,7.1));
    float mound=SoilNoise(turned*1.94+float2(4.7,29.1));
    float clod=SoilNoise(p*4.51+float2(36.6,11.8));
    return slope*(.033*hollow+.013*mound+.004*clod-.005);
}

float MarkHeight(float2 p)
{
    [branch] if (_OsowiecMudEnabled<.5) return 0;
    float2 tile=floor(p*.5);
    [branch] if (any(abs(tile+.5)>=128)) return 0;
    float2 pages=SAMPLE_TEXTURE2D_LOD(_OsowiecMudPages,sampler_PointClamp,(tile+128.5)/256.,0).rg;
    float slot=round(lerp(pages.x,pages.y,_OsowiecGallerySoil)*255.)-1.;
    [branch] if (slot<0) return 0;
    // Layout matches OsowiecMudField: 96 interior texels and an apron on each side.
    float2 address=float2(fmod(slot,16.),floor(slot/16.));
    float2 uv=(address*98.+1.+frac(p*.5)*96.)/float2(1568.,784.);
    float2 rg=SAMPLE_TEXTURE2D_LOD(_OsowiecMudAtlas,sampler_LinearClamp,uv,0).rg;
    float2 stamp=SAMPLE_TEXTURE2D_LOD(_OsowiecMudAtlas,sampler_PointClamp,uv,0).ba;
    float born=dot(round(stamp*255.),float2(256,1));
    return (rg.y-rg.x)*(255./3600.)*(1.-smoothstep(38.,145.,_OsowiecMudClock-born));
}

SoilVertex SurveyVertex(Attributes a)
{
    SoilVertex o;
    o.world=TransformObjectToWorld(a.positionOS.xyz);
    o.normal=TransformObjectToWorldNormal(a.normalOS);
    o.cover=lerp(a.color.r,_OsowiecBankCover,_OsowiecGallerySoil);
    return o;
}

Varyings GroundVertex(SoilVertex s)
{
    Varyings o;
    o.normal=normalize(s.normal);
    float2 a=abs(s.world.xz-float2(-4,13)),b=abs(s.world.xz-float2(11,11));
    float rimDistance=min(abs(max(a.x,a.y)-2),abs(max(b.x,b.y)-2));
    s.world.y+=smoothstep(.012,.12,rimDistance)*(UntrackedHeight(s.world.xz,o.normal.y)+MarkHeight(s.world.xz));
    o.positionWS=s.world;
    #if defined(SOIL_SHADOW)
        #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
            float3 light=normalize(_LightPosition-s.world);
        #else
            float3 light=_LightDirection;
        #endif
        o.positionCS=ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(s.world,o.normal,light)));
    #else
        o.positionCS=TransformWorldToHClip(s.world);
    #endif
    o.mixFog=half2(s.cover,ComputeFogFactor(o.positionCS.z));
    return o;
}

#if defined(SOIL_TESSELLATION)
    // Neighbouring triangles calculate the same factor from the same world-space
    // edge endpoints. Refinement is limited to the visible detail scale near the eye.
    struct Factors {float edge[3]:SV_TessFactor; float inside:SV_InsideTessFactor;};
    float EdgeDetail(float3 a,float3 b)
    {
        float3 mid=.5*(a+b);
        float dist=distance(mid,_WorldSpaceCameraPos);
        float nearDetail=(1.-smoothstep(2.,8.,dist))*lerp(36.,23.,saturate(_OsowiecLandscapeLow));
        float midDetail=(1.-smoothstep(7.,24.,dist))*4.4;
        return clamp(length(a-b)*(nearDetail+midDetail),1.,30.);
    }
    Factors SoilFactors(InputPatch<SoilVertex,3> p)
    {
        Factors t;
        t.edge[0]=EdgeDetail(p[1].world,p[2].world);
        t.edge[1]=EdgeDetail(p[2].world,p[0].world);
        t.edge[2]=EdgeDetail(p[0].world,p[1].world);
        t.inside=(t.edge[0]+t.edge[1]+t.edge[2])/3.;
        return t;
    }
    [domain("tri")] [partitioning("fractional_odd")] [outputtopology("triangle_cw")]
    [outputcontrolpoints(3)] [patchconstantfunc("SoilFactors")]
    SoilVertex SoilHull(InputPatch<SoilVertex,3> p,uint id:SV_OutputControlPointID)
    { return p[id]; }
    [domain("tri")]
    Varyings SoilDomain(Factors t,OutputPatch<SoilVertex,3> p,float3 w:SV_DomainLocation)
    {
        SoilVertex s;
        s.world=w.x*p[0].world+w.y*p[1].world+w.z*p[2].world;
        s.normal=w.x*p[0].normal+w.y*p[1].normal+w.z*p[2].normal;
        s.cover=dot(w,float3(p[0].cover,p[1].cover,p[2].cover));
        return GroundVertex(s);
    }
#else
    Varyings GroundFallback(Attributes a){return GroundVertex(SurveyVertex(a));}
#endif

// Screen derivatives of the same metric height used by the geometry keep the lit
// wall and floor of dents smooth even where distant triangles are sparsely sampled.
half3 MudNormal(Varyings i,float height)
{
    float2 gx=ddx(i.positionWS.xz),gz=ddy(i.positionWS.xz);
    float det=gx.x*gz.y-gz.x*gx.y;
    float2 gradient=(ddx(height)*float2(gz.y,-gz.x)+ddy(height)*float2(-gx.y,gx.x))
                  * (abs(det)>1e-9 ? rcp(det) : 0.);
    half3 n=normalize(i.normal);
    return normalize(n-n.y*float3(clamp(gradient.x,-3.,3.),0,clamp(gradient.y,-3.,3.)));
}

float SoilDepth(Varyings i):SV_Target { return i.positionCS.z; }
half4 SoilNormals(Varyings i):SV_Target
{
    half3 n=MudNormal(i,UntrackedHeight(i.positionWS.xz,normalize(i.normal).y)+MarkHeight(i.positionWS.xz));
    #if defined(_GBUFFER_NORMALS_OCT)
        return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(n)*.5+.5)),0);
    #else
        return half4(n,0);
    #endif
}
