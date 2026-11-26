Shader "Steel and Ashes/Weathered roof covering"
{
    Properties
    {
        _BaseMap("Surface scan", 2D)="white"{}
        [Normal] _BumpMap("Surface relief", 2D)="bump"{}
        _Mask("Surface height and roughness", 2D)="white"{}
        _Substrate("Exposed peat", 2D)="white"{}
        [Normal] _SubNormal("Peat relief", 2D)="bump"{}
        _SubMask("Peat height and roughness", 2D)="white"{}
        _Tint("Pigment", Color)=(1,1,1,1)
        _Earthwork("Earthwork", Float)=0
        [HideInInspector] _Cull("Cull", Float)=2
        _ClothRepeat("Dyed yarn repeats per normal atlas",Float)=1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "Forward"
            Cull [_Cull]
            Tags {"LightMode"="UniversalForward"}
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
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
            TEXTURE2D(_Mask); SAMPLER(sampler_Mask);
            TEXTURE2D(_Substrate); SAMPLER(sampler_Substrate);
            TEXTURE2D(_SubNormal); SAMPLER(sampler_SubNormal);
            TEXTURE2D(_SubMask); SAMPLER(sampler_SubMask);
            CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            half _Earthwork, _ClothRepeat;
            CBUFFER_END
            float _OsowiecFlatMaterials;
            struct Input { float4 p:POSITION; float3 n:NORMAL; float4 t:TANGENT; float2 uv:TEXCOORD0; half4 weather:COLOR; };
            struct Output { float4 p:SV_POSITION; float3 world:TEXCOORD0; float2 uv:TEXCOORD1;
                half3 n:TEXCOORD2; half4 t:TEXCOORD3; half4 weather:TEXCOORD4; half fog:TEXCOORD5; };
            Output Vert(Input v)
            {
                Output o; o.world=TransformObjectToWorld(v.p.xyz); o.p=TransformWorldToHClip(o.world);
                o.uv=v.uv; o.n=TransformObjectToWorldNormal(v.n);
                o.t=half4(TransformObjectToWorldDir(v.t.xyz),v.t.w*GetOddNegativeScale());
                o.weather=v.weather; o.fog=ComputeFogFactor(o.p.z); return o;
            }
            half4 Frag(Output i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC):SV_Target
            {
                half3 n=normalize(i.n)*IS_FRONT_VFACE(face,1,-1);
                half3 t=normalize(i.t.xyz-n*dot(i.t.xyz,n));
                half3 b=cross(n,t)*i.t.w, eye=GetWorldSpaceNormalizeViewDir(i.world);
                float2 uv=i.uv;
                half4 mask=SAMPLE_TEXTURE2D(_Mask,sampler_Mask,uv);
                float3 v=half3(dot(eye,t),dot(eye,b),max(.32h,dot(eye,n)));
                float reliefFade=(1-smoothstep(4.,13.,distance(_WorldSpaceCameraPos,i.world)))*(1-step(.5,_OsowiecFlatMaterials));
                uv+=(mask.g-.5)*v.xy/v.z*lerp(.0018,.014,_Earthwork)*reliefFade;
                float2 dyeUV=uv*lerp(_ClothRepeat,1.,_Earthwork);
                half3 colour=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,dyeUV).rgb*_Tint.rgb;
                half3 detail=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,uv),lerp(.68h,1.12h,_Earthwork));
                half wet=i.weather.g, variation=i.weather.b;
                half smoothness;
                if (_Earthwork>.5h)
                {
                    float2 puv=uv*1.58+float2(2.64,1.37);
                    half4 peat=SAMPLE_TEXTURE2D(_SubMask,sampler_SubMask,puv);
                    half bare=saturate(.11+.82*i.weather.r+.36*(peat.g-mask.g));
                    half3 soil=SAMPLE_TEXTURE2D(_Substrate,sampler_Substrate,puv).rgb;
                    half turfLuma=dot(colour,half3(.24,.65,.11));
                    half3 turf=lerp(turfLuma.xxx,colour,.57h)*lerp(half3(.70,.77,.68),half3(.92,.88,.76),variation);
                    half soilLuma=dot(soil,half3(.27,.61,.12));
                    half3 loam=lerp(soilLuma.xxx,soil,.51h)*half3(.98,.95,.89)+half3(.043,.038,.029);
                    half contact=saturate(1-i.weather.a);
                    loam=lerp(loam,soil*half3(.64,.57,.50),contact);
                    colour=lerp(turf,loam,bare);
                    detail=normalize(lerp(detail,UnpackNormal(SAMPLE_TEXTURE2D(_SubNormal,sampler_SubNormal,puv)),bare));
                    smoothness=lerp(.17+.20*mask.a,.20+.18*peat.a+.13*wet,bare);
                }
                else
                {
                    // The geometry supplies the depression and runoff mask. The fine
                    // dye map contains only yarn so stains cannot repeat across panels.
                    half stain=smoothstep(.13h,.8h,i.weather.r);
                    half yarn=smoothstep(.35h,.69h,mask.g);
                    half mottle=saturate(.40h+variation*.72h);
                    half3 silt=half3(.105h,.067h,.035h)*lerp(.69h,1.23h,yarn);
                    colour=lerp(colour*lerp(.83h,1.12h,mottle),silt,stain*(.52h+.26h*yarn));
                    half wear=smoothstep(.59h,.89h,variation)*(1-stain);
                    colour=lerp(colour,half3(.33h,.28h,.19h),wear*(.15h+.22h*yarn));
                    // Positive distance on both faces of the ridge carries water towards each eave.
                    float x=i.world.x, z=i.world.z;
                    float ridge=-10.17+.075*sin(x*1.16)+.045*cos(x*.64);
                    float slope=abs(z-ridge);
                    float lane=x*5.2+.31*sin(slope*2.3+x*.43)+.22*sin(x*3.64);
                    float stripe=1-smoothstep(.018,.115,abs(frac(lane)-.5));
                    float patch=smoothstep(.10,.95,slope)*saturate(wet+.28);
                    float wave=.5+.5*sin(slope*12.4-_Time.y*5.2+x*1.3);
                    half film=stripe*patch*(.39+.61*wave);
                    detail=normalize(detail+half3(.21*sin(lane*6.2832)*film,.19*wave*film,0));
                    colour*=lerp(1.03h,.70h,wet)*lerp(1.0h,.81h,film);
                    smoothness=.15h+.07h*mask.g+.26h*wet+.30h*film;
                }
                if (_OsowiecFlatMaterials<.5) n=normalize(n*detail.z+t*detail.x+b*detail.y);
                InputData d=(InputData)0; d.positionWS=i.world; d.positionCS=i.p;
                d.normalWS=n; d.viewDirectionWS=eye; d.shadowCoord=TransformWorldToShadowCoord(i.world);
                d.bakedGI=SampleSH(n); d.shadowMask=1; d.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.p);
                SurfaceData s=(SurfaceData)0; s.albedo=colour; s.normalTS=half3(0,0,1);
                s.occlusion=lerp(1.,lerp(.72,1.,mask.b),_Earthwork);
                s.smoothness=saturate(smoothness); s.alpha=1;
                half4 result=UniversalFragmentPBR(d,s); result.rgb=MixFog(result.rgb,i.fog); return result;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
