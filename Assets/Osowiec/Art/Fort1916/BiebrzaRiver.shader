Shader "Steel and Ashes/Biebrza River"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+30" }
        Pass
        {
            Tags {"LightMode"="UniversalForward"}
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float _OsowiecLandscapeLow;
            float _OsowiecWaterSkyTurn;
            TEXTURE2D(_OsowiecWaterSky); SAMPLER(sampler_OsowiecWaterSky);
            struct Vertex { float4 position:POSITION; half4 data:COLOR; };
            struct Pixel { float4 clip:SV_POSITION; float3 world:TEXCOORD0; half3 water:TEXCOORD1; };
            Pixel Vert(Vertex v)
            {
                Pixel o;
                o.world=TransformObjectToWorld(v.position.xyz);
                o.clip=TransformWorldToHClip(o.world);
                o.water=half3(v.data.rg,ComputeFogFactor(o.clip.z));
                return o;
            }
            float corner(float2 cell)
            {
                float3 h=frac(cell.xyx*float3(.1031,.1030,.0973));
                h+=dot(h,h.yzx+33.33);
                return frac((h.x+h.y)*h.z);
            }

            // Smooth height and its two slopes, sampled in continuous world coordinates.
            float3 waterNoise(float2 p)
            {
                float2 cell=floor(p), f=frac(p);
                float2 blend=f*f*(3.-2.*f), gradient=6.*f*(1.-f);
                float a=corner(cell), b=corner(cell+float2(1,0));
                float c=corner(cell+float2(0,1)), d=corner(cell+1.);
                float saddle=a-b-c+d;
                return float3(a+(b-a)*blend.x+(c-a)*blend.y+saddle*blend.x*blend.y,
                              gradient*float2(b-a+saddle*blend.y,c-a+saddle*blend.x));
            }
            // Each drop lies far enough inside its cell that its ring dies
            // before the boundary. Pixel footprint filters rings out at map zoom.
            float2 rainSlope(float2 p, float seconds)
            {
                float2 id=floor(p);
                float3 hash=frac(float3(id.xyx)*float3(.1031,.1030,.0973));
                hash+=dot(hash,hash.yxz+33.33);
                float3 seed=frac((hash.xxy+hash.yzz)*hash.zyx);
                float2 offset=frac(p)-lerp(.35,.65,seed.xy);
                float radius=length(offset);
                float age=frac(seconds*(.61+.47*seed.z)+seed.x);
                float front=(radius-age*.30)*65.;
                float envelope=exp2(-.18*front*front)*smoothstep(.025,.11,age)*(1.-smoothstep(.65,.96,age));
                float footprint=max(fwidth(p.x),fwidth(p.y));
                float filtered=1.-smoothstep(.06,.19,footprint);
                return offset*(rcp(max(.008,radius))*sin(front)*envelope*filtered);
            }
            half4 Frag(Pixel i):SV_Target
            {
                float2 p=i.world.xz;
                float current=i.water.y;
                float t=_Time.y*(.025+.095*current);
                float2 flow=p-float2(.74,-.43)*t;
                float3 pools=waterNoise(p*.089+float2(13.8,24.1));
                float2 reach=float2(dot(flow,float2(.43,.14)),dot(flow,float2(-.29,.89)));
                float3 broad=waterNoise(reach+pools.x*.71);
                float2 slope=broad.y*float2(.028,.009)+broad.z*float2(-.020,.057);
                float footprint=max(length(ddx(p)),length(ddy(p)));
                float closeDetail=1.-smoothstep(.045,.30,footprint);
                if(_OsowiecLandscapeLow<.5 && closeDetail>.001)
                {
                    float2 r=flow+float2(-.37,.28)*t;
                    float2 fineUV=float2(dot(r,float2(1.9,-1.2)),dot(r,float2(2.3,3.7)));
                    float3 fine=waterNoise(fineUV+float2(broad.x,pools.x)*1.17);
                    slope+=closeDetail*(fine.y*float2(.014,-.009)+fine.z*float2(.016,.026));
                    slope+=.023*closeDetail*rainSlope(p*2.4,_Time.y);
                }
                half3 n=normalize(half3(slope.x,1.,slope.y));
                half3 view=GetWorldSpaceNormalizeViewDir(i.world);
                half3 ray=reflect(-view,n);
                half3 sky=lerp(half3(.083,.101,.104),half3(.234,.263,.258),
                                saturate(ray.y*.87+.16+(pools.x-.5)*.47));
                if(_OsowiecLandscapeLow<.5)
                {
                    float2 skyUV=float2(frac(.5-atan2(ray.z,ray.x)*.15915494-_OsowiecWaterSkyTurn),
                                        .5+asin(clamp(ray.y,-1.,1.))*.31830989);
                    half3 clouds=SAMPLE_TEXTURE2D_LOD(_OsowiecWaterSky,sampler_OsowiecWaterSky,skyUV,2.5).rgb;
                    half luminance=dot(clouds,half3(.24,.68,.08));
                    sky+=half3(.23,.24,.21)*(luminance/(luminance+.53h));
                }
                half depth=smoothstep(.02,.90,i.water.x+.13*(pools.x-.5));
                half3 silt=lerp(half3(.084,.069,.046),half3(.160,.138,.091),pools.x);
                half3 bed=lerp(silt,half3(.029,.050,.052),depth);
                Light light=GetMainLight(TransformWorldToShadowCoord(i.world));
                half fres=.19h+.61h*pow(1-saturate(dot(n,view)),2.6h);
                half3 reflected=lerp(bed,sky,fres*lerp(.68h,1.h,depth));
                half sheen=pow(saturate(dot(ray,light.direction)),34)*.038h;
                half3 col=reflected*(.80+.20*light.shadowAttenuation)+light.color*sheen*light.shadowAttenuation;
                return half4(MixFog(col,i.water.z),1);
            }
            ENDHLSL
        }
    }
}

