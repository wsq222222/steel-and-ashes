Shader "Steel and Ashes/Floodplain grasses"
{
    Properties { _BladeAtlas ("Floodplain sedges", 2D) = "white" {}
    _BaseColor ("Leaf tint", Color) = (.37,.38,.24,1)
    _FoldedLeaf ("Folded close leaf", Float) = 0 }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest"}
        Pass
        {
            Tags {"LightMode"="UniversalForward"}
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FloodplainWeather.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BladeAtlas); SAMPLER(sampler_BladeAtlas);
            struct Input { float2 uv:TEXCOORD0; float4 pos:POSITION; float3 normal:NORMAL; half4 mask:COLOR; };
            struct Out { float2 uv:TEXCOORD4; float4 pos:SV_POSITION; float3 world:TEXCOORD0; float fog:TEXCOORD1; half3 normal:TEXCOORD2; half tip:TEXCOORD3; };
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _FoldedLeaf;
            CBUFFER_END
            Out vert(Input v)
            {
                Out o;
                o.world=TransformObjectToWorld(v.pos.xyz);
                o.tip=v.mask.r; o.uv=v.uv;
                o.world=BendFloodplainLeaf(o.world, o.tip);
                o.pos=TransformWorldToHClip(o.world);
                o.fog=ComputeFogFactor(o.pos.z);
                o.normal=TransformObjectToWorldNormal(v.normal);
                return o;
            }
            half4 frag(Out i):SV_Target
            {
                half4 blade=SAMPLE_TEXTURE2D(_BladeAtlas,sampler_BladeAtlas,i.uv);
                if (_FoldedLeaf>.5h)
                {
                    half rib=pow(saturate(1-abs(i.uv.x*2-1)),7);
                    half vein=.93h+.07h*cos(i.uv.x*75);
                    blade=half4(lerp(_BaseColor.rgb,half3(.46,.38,.16),i.tip*.48h)*vein+rib*.026h,1);
                }
                clip(blade.a-.32h);
                Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));
                half3 n=normalize(i.normal);
                half3 eye=GetWorldSpaceNormalizeViewDir(i.world);
                n=dot(n,eye)<0 ? -n : n;
                half transmitted=.47h+.42h*abs(dot(n,sun.direction));
                half patch=.50h+.30h*sin(i.world.x*.42h+.76h*sin(i.world.z*.51h))+.20h*sin(i.world.z*.31h);
                half scorched=smoothstep(.39h,.86h,patch)*smoothstep(.12h,.91h,i.tip);
                half3 pigment=blade.rgb*lerp(half3(.72,.80,.66),half3(1.10,.87,.59),scorched);
                half lightAtRoot=lerp(.69h,1.11h,i.tip);
                half3 light=SampleSH(half3(0,.9,.3))*.82h + sun.color*transmitted*sun.shadowAttenuation;
                half3 leaf=pigment*lightAtRoot*max(light,half3(.19,.21,.18));
                half3 h=SafeNormalize(eye+sun.direction);
                half glint=pow(saturate(dot(n,h)),37.0h)*sun.shadowAttenuation;
                half rim=pow(1.0h-saturate(dot(n,eye)),4.0h)*.017h;
                leaf+=(sun.color*glint*.097h+half3(.79,.86,.87)*rim)*(.42h+.58h*i.tip);
                return half4(MixFog(leaf,i.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FloodplainWeather.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            TEXTURE2D(_BladeAtlas); SAMPLER(sampler_BladeAtlas);
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _FoldedLeaf;
            CBUFFER_END
            float3 _LightDirection;
            float3 _LightPosition;
            struct Leaf { float4 pos:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; half4 mask:COLOR; };
            struct ShadowVertex { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            ShadowVertex ShadowVert(Leaf v)
            {
                ShadowVertex o;
                o.uv=v.uv;
                float3 pos=TransformObjectToWorld(v.pos.xyz);
                pos=BendFloodplainLeaf(pos, v.mask.r);
                float3 lightDir=_LightDirection;
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                lightDir=normalize(_LightPosition-pos);
                #endif
                o.pos=ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(pos,TransformObjectToWorldNormal(v.normal),lightDir)));
                return o;
            }
            half4 ShadowFrag(ShadowVertex i):SV_Target
            {
                if(_FoldedLeaf<.5h) clip(SAMPLE_TEXTURE2D(_BladeAtlas,sampler_BladeAtlas,i.uv).a-.32h);
                return 0;
            }
            ENDHLSL
        }
    }
}
