TEXTURE2D(_Till); SAMPLER(sampler_Till);
TEXTURE2D(_TillNormal); SAMPLER(sampler_TillNormal);
TEXTURE2D(_TillMask); SAMPLER(sampler_TillMask);
TEXTURE2D(_Earth); SAMPLER(sampler_Earth);
TEXTURE2D(_EarthNormal); SAMPLER(sampler_EarthNormal);
TEXTURE2D(_EarthMask); SAMPLER(sampler_EarthMask);
TEXTURE2D(_Sod); SAMPLER(sampler_Sod);
TEXTURE2D(_SodNormal); SAMPLER(sampler_SodNormal);
TEXTURE2D(_SodMask); SAMPLER(sampler_SodMask);
float groundNoise(float2 p)
{
    float2 tile=floor(p),f=frac(p);
    f=f*f*(3-2*f);
    float4 corners=frac(sin(dot(tile,float2(127.1,311.7))+float4(0,127.1,311.7,438.8))*43758.5453);
    return lerp(lerp(corners.x,corners.y,f.x),lerp(corners.z,corners.w,f.x),f.y);
}
// Project both scanned layers in metres. On a steep bank the side view carries
// the grain, while near-horizontal ground costs only one pair of tile samples.
struct GroundLayer { half3 colour; half2 slope; half gloss; half cavity; };
// Packed scan: height in G, ambient occlusion in B, smoothness in A.
// Walk only the major close-up projections; a distant tile samples each map once.
float reliefDepth(float2 uv,float2 dx,float2 dy,float2 ray,half sod)
{
    float prev=0, difference=1;
    [unroll] for(int k=1;k<=5;k++)
    {
        float d=k*.2;
        float2 sampleAt=uv-ray*d;
        half h=lerp(
            SAMPLE_TEXTURE2D_GRAD(_EarthMask,sampler_EarthMask,sampleAt/1.62,dx/1.62,dy/1.62).g,
            SAMPLE_TEXTURE2D_GRAD(_SodMask,sampler_SodMask,sampleAt/2.05+float2(.17,.41),dx/2.05,dy/2.05).g,sod);
        float next=(1-h)-d;
        if(next<=0) return lerp(prev,d,difference/max(.0001,difference-next));
        prev=d; difference=next;
    }
    return 1;
}
GroundLayer strata(float2 uv, float2 dx, float2 dy, half sod,
                   float2 projectedView, float depthScale, half axisWeight)
{
    GroundLayer o;
    [branch] if(depthScale>.001 && axisWeight>.16h)
    {
        float2 ray=projectedView*depthScale;
        uv-=ray*(reliefDepth(uv,dx,dy,ray,sod)-.45);
    }
    float2 earthUV=uv/1.62;
    float2 sodUV=uv/2.05+float2(.17,.41);
    half3 earth=SAMPLE_TEXTURE2D_GRAD(_Earth,sampler_Earth,earthUV,dx/1.62,dy/1.62).rgb;
    half3 turf=SAMPLE_TEXTURE2D_GRAD(_Sod,sampler_Sod,sodUV,dx/2.05,dy/2.05).rgb;
    half3 n0=UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_EarthNormal,sampler_EarthNormal,earthUV,dx/1.62,dy/1.62));
    half3 n1=UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_SodNormal,sampler_SodNormal,sodUV,dx/2.05,dy/2.05));
    half4 e=SAMPLE_TEXTURE2D_GRAD(_EarthMask,sampler_EarthMask,earthUV,dx/1.62,dy/1.62);
    half4 t=SAMPLE_TEXTURE2D_GRAD(_SodMask,sampler_SodMask,sodUV,dx/2.05,dy/2.05);
    sod=saturate(sod+(dot(turf,half3(.23,.56,.21))-.31h)*.18h);
    o.colour=lerp(earth*half3(1.25,1.22,1.18)+.012h,
                  turf*half3(.43,.46,.37),sod);
    o.slope=lerp(n0.xy/max(.45h,n0.z),n1.xy/max(.45h,n1.z),sod);
    half4 packed=lerp(e,t,sod);
    o.cavity=lerp(.32h,1.h,packed.b);
    o.gloss=packed.a + (1.h-packed.g)*.12h;
    // Dark organic soil remains in crevices; rain washes protruding grains clean.
    o.colour*=lerp(.90h,1.13h,packed.g);
    return o;
}
half4 Frag(Varyings i):SV_Target
{
    float3 world=i.positionWS;
    float3 dx=ddx(world), dy=ddy(world);
    float track=MarkHeight(world.xz);
    float landHeight=UntrackedHeight(world.xz,normalize(i.normal).y);
    half3 geom=normalize(i.normal);
    half3 reliefNormal=MudNormal(i,landHeight+track);
    half compression=saturate(-track*22.);
    #if !defined(SOIL_TESSELLATION)
        // Devices without hull/domain stages retain the ray-projected interior.
        float3 localView=GetWorldSpaceNormalizeViewDir(world);
        world.xz+=localView.xz*track/max(.35,localView.y);
    #endif
    float2 g=world.xz;
    float patch=groundNoise(g*.13)+.34*groundNoise(g*.58);
    float surveyEdge=abs(max(abs(g.x)-72,abs(g.y)-45));
    // The close survey and the outer marsh use different paint conventions.
    // One continuous vegetation band straddles their physical border.
    half cover=smoothstep(-.15,1.65,world.y+(patch-.67)*.8)*(.64+.24*patch);
    cover*=smoothstep(.35,.9,geom.y);
    half sod=saturate(lerp(cover,i.mixFog.x,smoothstep(0.,7.,surveyEdge)*.9));
    half3 weights=pow(abs(geom),6);
    weights/=max(.001h,weights.x+weights.y+weights.z);
    half3 col=0, bumps=0;
    float3 view=GetWorldSpaceNormalizeViewDir(world);
    float eyeDistance=distance(_WorldSpaceCameraPos,world);
    // The walkable bed already carries centimetre-scale geometry. Keep its scan
    // relief at grain scale, including when the camera looks along the surface.
    float scannedDepth=lerp(.048,.006,smoothstep(.55,.91,geom.y));
    float depthScale=(1.-saturate(_OsowiecFlatMaterials))*scannedDepth*(1.-smoothstep(2.,9.,eyeDistance))/max(.29,dot(view,geom));
    half polish=0, cavity=0;
    [branch] if(weights.y>.002h) {
        GroundLayer l=strata(world.xz,dx.xz,dy.xz,sod,view.xz,depthScale,weights.y);
        col+=l.colour*weights.y; bumps+=half3(l.slope.x,0,l.slope.y)*weights.y; polish+=l.gloss*weights.y; cavity+=l.cavity*weights.y;
    }
    [branch] if(weights.x>.002h) {
        GroundLayer l=strata(world.zy,dx.zy,dy.zy,sod,view.zy,depthScale,weights.x);
        col+=l.colour*weights.x; bumps+=half3(0,l.slope.y,l.slope.x)*weights.x; polish+=l.gloss*weights.x; cavity+=l.cavity*weights.x;
    }
    [branch] if(weights.z>.002h) {
        GroundLayer l=strata(world.xy,dx.xy,dy.xy,sod,view.xy,depthScale,weights.z);
        col+=l.colour*weights.z; bumps+=half3(l.slope.x,l.slope.y,0)*weights.z; polish+=l.gloss*weights.z; cavity+=l.cavity*weights.z;
    }
    // Rain strips back organic topsoil in patches. Exposed mineral beds
    // follow the bank elevation and split into rivulets below the crest.
    float warp=groundNoise(g*.83+float2(18.,7.));
    float vein=world.y*1.21+.35*g.x-.24*g.y+1.85*warp;
    float bedding=1.-smoothstep(.04,.29,abs(frac(vein*.76)-.50));
    float loss=smoothstep(.39,.69,groundNoise(g*.46+13.)+.13*warp);
    half incline=smoothstep(.07,.49,1.-geom.y);
    half channel=1.-smoothstep(.045,.18,abs(groundNoise(float2(g.x*.76+warp,g.y*1.17))-.49));
    half grit=saturate(incline*(.24+.51*bedding+.47*channel)*(.41+.65*loss)
                   +.18*channel*(1.-sod));
    [branch] if(grit>.035h)
    {
        // A continuous oblique projection follows both the face and the floor.
        float2 gravelUV=(g+float2(.63,-.41)*world.y)*.91+float2(.23,.61);
        float2 tx=(dx.xz+float2(.63,-.41)*dx.y)*.91;
        float2 ty=(dy.xz+float2(.63,-.41)*dy.y)*.91;
        half4 gravelMask=SAMPLE_TEXTURE2D_GRAD(_TillMask,sampler_TillMask,gravelUV,tx,ty);
        float2 shift=(view.xz+float2(.63,-.41)*view.y)*depthScale*(gravelMask.g-.5)*.91;
        gravelUV+=shift;
        half3 stone=SAMPLE_TEXTURE2D_GRAD(_Till,sampler_Till,gravelUV,tx,ty).rgb;
        half3 grain=UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_TillNormal,sampler_TillNormal,gravelUV,tx,ty));
        half blend=saturate((grit-.20h+(gravelMask.g-.44h)*.85h)*1.67h);
        col=lerp(col,stone*half3(.84,.77,.69),blend);
        half2 slope=grain.xy/max(.34h,grain.z);
        bumps=lerp(bumps,half3(slope.x,.63h*slope.x-.41h*slope.y,slope.y),blend);
        polish=lerp(polish,gravelMask.a*.83h+(1.-gravelMask.g)*.19h,blend);
        cavity=lerp(cavity,lerp(.38h,1.h,gravelMask.b),blend);
    }
    half drainage=saturate(.78h-world.y*.38h+.72h*(patch-.67h))*(1-sod)*smoothstep(.35,.91,geom.y);
    half mottling=.81h+.24h*patch;
    col*=lerp(half3(1,1,1),half3(.65,.71,.72),drainage)*mottling;
    // Broad patches cross mesh and paint boundaries in metre coordinates.
    // They are subtle at boot level and legible from the tactical camera.
    float2 sweep=(g+_Time.y*float2(.16,-.07))*float2(.042,.059);
    half cloud=groundNoise(float2(sweep.x+.34*sweep.y,sweep.y-.29*sweep.x)+23.6);
    half broadWeight=lerp(.28h,1.h,smoothstep(3.,18.,eyeDistance));
    col*=lerp(half3(1,1,1),lerp(half3(.78h,.82h,.87h),half3(1.16h,1.11h,1.04h),cloud),broadWeight);
    // With no height marching at this distance, retain the shallow folds
    // and furrows as a second, rotated scan several metres across.
    [branch] if (_OsowiecLandscapeLow < .5 && _OsowiecFlatMaterials < .5 && eyeDistance > 3.)
    {
        float2 ridgeAxis=float2(.83,.56);
        float2 crossAxis=float2(-.56,.83);
        float2 ridgeUV=float2(dot(g,ridgeAxis)*.12,dot(g,crossAxis)*.19)+float2(.28,.65);
        float2 ridgeDx=float2(dot(dx.xz,ridgeAxis)*.12,dot(dx.xz,crossAxis)*.19);
        float2 ridgeDy=float2(dot(dy.xz,ridgeAxis)*.12,dot(dy.xz,crossAxis)*.19);
        half3 ridge=UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_EarthNormal,sampler_EarthNormal,ridgeUV,ridgeDx,ridgeDy));
        half2 slope=(ridge.x*ridgeAxis+ridge.y*crossAxis)/max(.55h,ridge.z);
        half ridgeWeight=smoothstep(3.,16.,eyeDistance)*smoothstep(.17h,.80h,geom.y)*lerp(.69h,.34h,drainage);
        bumps+=half3(slope.x,0,slope.y)*ridgeWeight;
    }
    float farFade=1.-.48*smoothstep(15.,55.,eyeDistance);
    // Wet, pressed peat keeps the scanned grains but flattens them at the bottom.
    bumps*=lerp(1.,.42,compression);
    col*=lerp(half3(1,1,1),half3(.77,.80,.79),compression);
    polish=lerp(polish,.79h,compression*.65h);
    cavity*=1.-.18h*compression;
    half3 normal=normalize(reliefNormal+(bumps-reliefNormal*dot(bumps,reliefNormal))*lerp(1.32h,.73h,drainage)*farFade);
    InputData input=(InputData)0;
    input.positionWS=i.positionWS;input.positionCS=i.positionCS;input.normalWS=normal;
    input.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);
    input.shadowCoord=TransformWorldToShadowCoord(i.positionWS);
    input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
    input.bakedGI=SampleSH(normal);input.shadowMask=half4(1,1,1,1);
    SurfaceData surface=(SurfaceData)0;
    surface.albedo=col;surface.normalTS=half3(0,0,1);surface.occlusion=cavity;surface.alpha=1;
    surface.smoothness=saturate(polish*.79h+drainage*.32h);
    half4 result=UniversalFragmentPBR(input,surface);result.rgb=MixFog(result.rgb,i.mixFog.y);
    return result;
}
