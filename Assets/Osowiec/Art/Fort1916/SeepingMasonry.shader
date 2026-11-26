Shader "Steel and Ashes/Seeping masonry"
{
    Properties
    {
        _BaseMap("Survey masonry",2D)="white"{}
        [Normal] _BumpMap("Pores and mortar",2D)="bump"{}
        _MetallicGlossMap("Roughness atlas",2D)="white"{}
        _BaseColor("Pigment",Color)=(1,1,1,1)
        _BumpScale("Relief",Range(0,2))=1
        _Smoothness("Smoothness",Range(0,1))=.92
        _ReliefUV("Measured height, UV displacement",Range(0,.025))=0
        _SoilColour("Marsh peat colour",2D)="white"{}
        [Normal] _SoilNormal("Marsh peat relief",2D)="bump"{}
        _SoilMask("Peat height, occlusion, smoothness",2D)="white"{}
        _Finish("Brick, cast concrete, hewn timber",Float)=0
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        Pass
        {
            Tags {"LightMode"="UniversalForward"}
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _ _EARTH_APRON _EARTH_ROOF
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
            TEXTURE2D(_SoilColour); SAMPLER(sampler_SoilColour);
            TEXTURE2D(_SoilNormal); SAMPLER(sampler_SoilNormal);
            TEXTURE2D(_SoilMask); SAMPLER(sampler_SoilMask);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST; half4 _BaseColor; half _BumpScale; half _Smoothness; half _ReliefUV; half _Finish;
            CBUFFER_END
            float _OsowiecFlatMaterials;
            struct Inp {float4 p:POSITION;float3 n:NORMAL;float4 t:TANGENT;float2 uv:TEXCOORD0;};
            struct Out {float4 p:SV_POSITION;float3 world:TEXCOORD0;float2 uv:TEXCOORD1;
                        half3 n:TEXCOORD2;half4 t:TEXCOORD3;half fog:TEXCOORD4;};
            Out Vert(Inp v)
            {
                Out o; o.world=TransformObjectToWorld(v.p.xyz);o.p=TransformWorldToHClip(o.world);
                o.n=TransformObjectToWorldNormal(v.n);o.t=half4(TransformObjectToWorldDir(v.t.xyz),v.t.w*GetOddNegativeScale());
                o.uv=TRANSFORM_TEX(v.uv,_BaseMap);o.fog=ComputeFogFactor(o.p.z);return o;
            }
            float hash21(float2 p) {return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float noise21(float2 p)
            {
                float2 id=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(hash21(id),hash21(id+float2(1,0)),f.x),
                            lerp(hash21(id+float2(0,1)),hash21(id+1),f.x),f.y);
            }
            // Isolated stepped shrinkage cracks with pockets of exposed lime mortar.
            // Adjacent bricks keep their sampled relief across each local scar.
            float2 jointDamage(float2 wall)
            {
                float2 cell=floor(wall/float2(3.2,2.6));
                float seed=hash21(cell+4.1);
                float2 local=wall-cell*float2(3.2,2.6);
                float y=local.y-(.35+seed*.86);
                float stepped=floor(y*5.)*.076;
                float lineX=.72+seed*1.36+stepped+.09*sin(y*6.+seed*9.)+.017*sin(y*28.);
                float taper=(1.-smoothstep(.39,.79,abs(y)))*smoothstep(.43,.58,seed);
                float dx=abs(local.x-lineX);
                float shoulder=.006+.017*noise21(wall*float2(19,38));
                float aa=max(.002,.5*fwidth(wall.x));
                float core=1.-smoothstep(0.,.008+aa,dx);
                float edge=1.-smoothstep(shoulder,shoulder+.012+aa,dx);
                float fade=1.-smoothstep(.007,.09,aa);
                return saturate(float2(core,edge-core))*taper*fade;
            }
            float2 heightUV(float2 uv,float2 dx,float2 dy,float3 viewTS,float distanceWS)
            {
                UNITY_BRANCH if (_OsowiecFlatMaterials > .5) return uv;
                float fade=(1.-smoothstep(3.,10.,distanceWS))*smoothstep(.08,.30,viewTS.z);
                if(_ReliefUV*fade<.00012) return uv;
                // Trace across layers of the measured displacement scan. Gradients are held
                // constant so the edge of a mortar joint cannot corrupt the chosen mip.
                float2 stride=(_ReliefUV*fade/5.)*viewTS.xy/max(.34,viewTS.z);
                float2 probe=uv+2.0*stride;
                float prev=1.-SAMPLE_TEXTURE2D_GRAD(_MetallicGlossMap,sampler_MetallicGlossMap,probe,dx,dy).g;
                [unroll] for(int layer=1;layer<=5;layer++){
                    float depth=layer*.20;
                    float2 next=probe-stride;
                    float delta=1.-SAMPLE_TEXTURE2D_GRAD(_MetallicGlossMap,sampler_MetallicGlossMap,next,dx,dy).g-depth;
                    if(delta<=0.)return lerp(probe,next,saturate(prev/max(.0001,prev-delta)));
                    prev=delta;probe=next;
                }
                return probe;
            }
            half4 Frag(Out o):SV_Target
            {
                float3 p=o.world;half3 geom=normalize(o.n);
                half3 tangent=normalize(o.t.xyz-geom*dot(o.t.xyz,geom));
                half3 bitangent=cross(geom,tangent)*o.t.w;
                half3 eye=GetWorldSpaceNormalizeViewDir(p);
                float2 dx=ddx(o.uv), dy=ddy(o.uv);
                float2 uv=heightUV(o.uv,dx,dy,float3(dot(eye,tangent),dot(eye,bitangent),dot(eye,geom)),distance(_WorldSpaceCameraPos,p));
                half3 base=SAMPLE_TEXTURE2D_GRAD(_BaseMap,sampler_BaseMap,uv,dx,dy).rgb*_BaseColor.rgb;
                half3 nTex=UnpackNormalScale(SAMPLE_TEXTURE2D_GRAD(_BumpMap,sampler_BumpMap,uv,dx,dy),_BumpScale);
                half3 normal=normalize(tangent*nTex.x+bitangent*nTex.y+geom*nTex.z);
                half4 scan=SAMPLE_TEXTURE2D_GRAD(_MetallicGlossMap,sampler_MetallicGlossMap,uv,dx,dy);
                half mask=scan.a;
                half relief=saturate(scan.g);
                half nearby=saturate(1.-distance(_WorldSpaceCameraPos,p)/15.);
                half pore=_ReliefUV>.0001h ? (1.-smoothstep(.08h,.51h,relief)) : 0;
                half timber=step(1.5h,_Finish);
                half vault=(1-smoothstep(12,15,abs(p.x)))*(1-smoothstep(3.9,6.2,abs(p.z+12)))*(1.-timber);
                float s=abs(geom.x)>.70?p.z:p.x;
                float2 wall=float2(s,p.y);
                half macro=noise21(wall*float2(.48,.79)+2.1);
                half grain=noise21(wall*float2(12.4,8.1));
                half vertical=saturate(1-abs(geom.y));
                half meander=noise21(wall*float2(1.22,.91));
                half streak=noise21(float2(s*5.5+meander*.87,p.y*.46));
                half hair=noise21(float2(s*28.0+meander*1.68,p.y*.84));
                half runnel=saturate((streak*.68+hair*.32-.44)*3.1)*(.38+.62*macro)*vertical;
                half tide=(1-smoothstep(.13,.82,p.y-.37*macro-.12*grain))*vertical;
                half limescale=smoothstep(.47,.63,macro)*smoothstep(.54,.81,grain)*(.25+.75*abs(geom.x+geom.z));
                // Water gathers along the uneven gutter and fills low mortar joints first.
                half floorFace=smoothstep(.72,.94,geom.y)*(1-smoothstep(.13,.42,p.y));
                half bank=noise21(p.xz*float2(.57,1.8));
                half seams=noise21(p.xz*float2(2.8,5.0));
                half waterline=abs(p.z+12) + .96*bank+.19*seams;
                half film=smoothstep(1.28,1.67,waterline)*floorFace;
                half puddle=smoothstep(1.59,1.75,waterline)*floorFace;
                half floorWet=saturate(film*.66+puddle*.34);
                normal=normalize(lerp(normal,geom,vault*puddle*.89));
                float2 damage=jointDamage(wall)*(vault*vertical);
                half soot=smoothstep(2.11,3.45,p.y)*(.38+.62*macro);
                base*=lerp(half3(1,1,1),half3(.64,.64,.53)+macro*.55,vault);
                base*=1-vault*(.48*runnel+.27*tide+.19*soot);
                base=lerp(base,base*half3(1.10,1.08,.91)+half3(.035,.031,.021),vault*limescale*.72);
                base=lerp(base,base*.56h+half3(.10,.082,.054),damage.y*.85);
                base*=1.-damage.x*.70h;
                base*=1-.46*vault*floorWet;
                half openRain=(1.-vault)*(1.-timber)*(.25h+.63h*macro)*(.27h+.58h*runnel+.48h*saturate(geom.y));
                base*=1.-.19h*openRain;
                half wet=saturate(tide*.78+runnel*.88+floorWet);
                InputData i=(InputData)0;i.positionWS=p;i.positionCS=o.p;i.normalWS=normal;
                i.shadowCoord=TransformWorldToShadowCoord(p);i.viewDirectionWS=GetWorldSpaceNormalizeViewDir(p);
                i.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(o.p);i.bakedGI=SampleSH(normal);i.shadowMask=1;
                SurfaceData surface=(SurfaceData)0;surface.albedo=base;surface.normalTS=half3(0,0,1);surface.alpha=1;
                surface.occlusion=saturate(1-.12*vault*(tide+soot)-.19h*pore*nearby);surface.smoothness=lerp(saturate(mask*_Smoothness+openRain*.29h),lerp(.25h,.84h,wet),vault*.88h);
                surface.smoothness=lerp(surface.smoothness,.94h,vault*puddle);
                surface.smoothness*=1.-damage.y*.38;
                #if defined(_EARTH_APRON) || defined(_EARTH_ROOF)
                float2 soilUV=p.xz/1.62;
                half4 soil=SAMPLE_TEXTURE2D(_SoilMask,sampler_SoilMask,soilUV);
                half3 peat=SAMPLE_TEXTURE2D(_SoilColour,sampler_SoilColour,soilUV).rgb;
                half3 grainN=UnpackNormal(SAMPLE_TEXTURE2D(_SoilNormal,sampler_SoilNormal,soilUV));
                float breakup=noise21(p.xz*.84)+.36*noise21(p.xz*3.1);
                #if defined(_EARTH_APRON)
                    // Broad tongues of tracked mud at each mouth run into the low mortar.
                    float fromMouth=9.98-abs(p.x-.25);
                    float tongues=1.65+1.21*sin(p.z*2.35+.38*sin(p.x))+.98*breakup;
                    half mud=1.-smoothstep(.12,1.63,fromMouth-tongues+1.65);
                    mud=max(mud,1.-smoothstep(.20,.70,fromMouth));
                    float gutter=smoothstep(.69,1.66,abs(p.z+12.)+.52*(breakup-.5));
                    float fines=(1.-smoothstep(.25,.58,relief))*saturate(.29+.74*gutter+.44*breakup);
                    mud=saturate(max(mud*.98,fines*.75+.23*gutter))*floorFace;
                    peat=(peat*half3(1.25,1.22,1.18)+.012)*half3(.65,.71,.72)*(.82+.24*noise21(p.xz*.13));
                    half mudGloss=saturate(soil.a*.79+.28);
                #else
                    // Thin turf gives way to mineral soil in discontinuous eroded patches.
                    half mud=smoothstep(.57,1.03,breakup+.36*(1-geom.y)+.19*(.5-soil.g));
                    peat=peat*half3(.93,.89,.73)+.009;
                    half mudGloss=soil.a*.71+.11;
                #endif
                half3 grainsWS=half3(grainN.x,0,grainN.y);
                half3 soilN=normalize(geom*max(.45,grainN.z)+(grainsWS-geom*dot(geom,grainsWS)));
                i.normalWS=normalize(lerp(normal,soilN,mud));
                i.bakedGI=SampleSH(i.normalWS);
                surface.albedo=lerp(surface.albedo,peat,mud);
                surface.occlusion=lerp(surface.occlusion,lerp(.32,1.,soil.b),mud);
                surface.smoothness=lerp(surface.smoothness,mudGloss,mud);
                #endif
                half4 c=UniversalFragmentPBR(i,surface);c.rgb=MixFog(c.rgb,o.fog);return c;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
