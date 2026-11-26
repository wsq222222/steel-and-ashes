Shader "Steel and Ashes/Quartermaster stores"
{
    Properties
    {
        _BaseMap("Weathered surface",2D)="white"{}
        [Normal] _BumpMap("Fibre relief",2D)="bump"{}
        _RoughnessMap("Cavity and finish",2D)="white"{}
        _Relief("Relief strength",Range(0,3))=1
        _Cloth("Woven surface",Range(0,1))=0
        _ClothDye("Cloth dye",Color)=(1,1,1,1)
        _ClothDampness("Absorbed rain",Range(0,1))=0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
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
            TEXTURE2D(_RoughnessMap); SAMPLER(sampler_RoughnessMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half _Relief, _Cloth, _ClothDampness; half4 _ClothDye;
            CBUFFER_END
            float _OsowiecFlatMaterials;
            struct Attributes { float4 p:POSITION; half3 n:NORMAL; half4 t:TANGENT; float2 uv:TEXCOORD0; half4 weather:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float2 uv:TEXCOORD1;
                half3 normal:TEXCOORD2; half4 tangent:TEXCOORD3; half fog:TEXCOORD4; half damp:TEXCOORD5; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.world=TransformObjectToWorld(v.p.xyz);o.positionCS=TransformWorldToHClip(o.world);
                o.normal=TransformObjectToWorldNormal(v.n);
                o.tangent=half4(TransformObjectToWorldDir(v.t.xyz),v.t.w*GetOddNegativeScale());
                o.damp=v.weather.g; o.uv=TRANSFORM_TEX(v.uv,_BaseMap);o.fog=ComputeFogFactor(o.positionCS.z);return o;
            }
            float SoilOnCloth(float2 p)
            {
                float2 cell=floor(p), local=frac(p);
                local=local*local*(3.0-2.0*local);
                float4 sites=cell.x+float4(0,1,0,1)+(cell.y+float4(0,0,1,1))*57.0;
                float4 shade=frac(sin(sites*13.73)*4371.13);
                return lerp(lerp(shade.x,shade.y,local.x),lerp(shade.z,shade.w,local.x),local.y);
            }
            half4 Frag(Varyings i):SV_Target
            {
                // Horizontal planks use the long direction of the photographed grain.
                float2 uv=float2(i.uv.y,-i.uv.x);
                half3 colour=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv).rgb;
                half4 mask=SAMPLE_TEXTURE2D(_RoughnessMap,sampler_RoughnessMap,uv);
                half3 fibre=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,uv),_Relief);
                if(_Cloth>.5h)
                {
                    float2 yarn=uv*84.0;
                    float fade=1-smoothstep(.13,.49,max(fwidth(yarn.x),fwidth(yarn.y)));
                    fibre.xy+=fade*.22h*sin(yarn*6.28318)*cos(yarn.yx*3.14159);
                }
                half3 n=normalize(i.normal),t=normalize(i.tangent.xyz);
                if(_OsowiecFlatMaterials<.5)
                    n=normalize(n*fibre.z-t*fibre.y+cross(n,t)*i.tangent.w*fibre.x);
                float irregular=.065*sin(i.world.x*5.2+i.world.z*2.7)+.042*sin(i.world.z*7.4-i.world.x);
                half wet=(1-smoothstep(.07,.47,i.world.y+irregular))*(1-_Cloth*.62);
                wet=saturate(wet+_ClothDampness*i.damp);
                colour*=_ClothDye.rgb*lerp(half3(1,1,1),half3(.57,.56,.53),wet);
                if (_ClothDampness>.01h)
                {
                    // A sagged sack picks up silt on the underside. The texture varies
                    // across the whole pile and the light catches raised threads above it.
                    float2 p=i.world.xz+float2(.41,.79)*i.world.y;
                    float cloud=.60*SoilOnCloth(p*5.7)+.29*SoilOnCloth(p*19.1)+.11*SoilOnCloth(p*51.3);
                    half grit=smoothstep(.47,.74,cloud+i.damp*.26);
                    float2 yarn=uv*58.0;
                    float threadFade=1-smoothstep(.13,.36,max(fwidth(yarn.x),fwidth(yarn.y)));
                    float warp=cos((yarn.x+.14*sin(yarn.y*3.1416))*6.2832);
                    float weft=cos((yarn.y+.14*sin(yarn.x*3.1416))*6.2832);
                    half ridge=.5h+.5h*max(warp,weft);
                    colour*=lerp(.68h,1.48h,cloud)*lerp(1.0h,.63h+ridge*.58h,threadFade);
                    colour=lerp(colour,colour*half3(.38,.31,.24),grit*.69h);
                }
                half cavities=saturate(mask.g);
                half polish=lerp(.19+.33*mask.a+.095*cavities,.16+.20*mask.a,_Cloth);
                InputData data=(InputData)0;data.positionWS=i.world;data.positionCS=i.positionCS;
                data.normalWS=n;data.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.world);
                data.shadowCoord=TransformWorldToShadowCoord(i.world);data.bakedGI=SampleSH(n);data.shadowMask=1;
                data.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
                SurfaceData surface=(SurfaceData)0;surface.albedo=colour;surface.normalTS=half3(0,0,1);
                surface.smoothness=saturate(polish+wet*(.16+.13*cavities));
                surface.occlusion=lerp(.83,1,cavities);surface.alpha=1;
                half4 output=UniversalFragmentPBR(data,surface);
                output.rgb=MixFog(output.rgb,i.fog);return output;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
