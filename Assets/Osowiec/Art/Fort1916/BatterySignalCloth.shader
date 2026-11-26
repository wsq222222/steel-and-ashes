Shader "Steel and Ashes/Battery signal cloth"
{
    Properties
    {
        _BaseMap ("Canvas", 2D) = "white" {}
        _BaseColor ("Canvas tint", Color) = (1,1,1,1)
        [Normal] _BumpMap ("Weave", 2D) = "bump" {}
        _BumpScale ("Weave strength", Float) = 1
        _MetallicGlossMap ("Surface", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0
        _Smoothness ("Smoothness", Range(0,1)) = .4
        _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull [_Cull]
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
        #include "FloodplainWeather.hlsl"
        static const float3 yardHoist=float3(33.04,3.38,1.85);

        // All five signal flags share one imported mesh. Only the cloth beside the yard cabinet moves.
        void MoveCloth(inout float3 positionOS, inout float3 normalOS)
        {
            float3 p=TransformObjectToWorld(positionOS);
            float3 panel=p-yardHoist;
            if(panel.x<-.015 || panel.x>1.01 || abs(panel.z)>.18 || panel.y<-.50 || panel.y>.09) return;
            float u=saturate(panel.x/.944);
            float2 gust=FloodplainGust(yardHoist.xz,_Time.y);
            float phase=6.8*u-2.6*_Time.y+1.4*panel.y+.65*gust.x;
            float fine=12.7*u-4.7*_Time.y+2.7*panel.y;
            float strength=.073+.045*gust.x;
            float fold=strength*sin(phase)+.013*sin(fine)+.082*gust.y;
            float weight=u*u;
            float slopeX=(2.*u*fold+weight*(strength*6.8*cos(phase)+.013*12.7*cos(fine)))/.944;
            float slopeY=weight*(strength*1.4*cos(phase)+.013*2.7*cos(fine));
            p.z+=weight*fold;
            p.y-=weight*(.035-.014*gust.x);
            float3 n=TransformObjectToWorldNormal(normalOS);
            n.xy-=n.z*float2(slopeX,slopeY);
            positionOS=TransformWorldToObject(p);
            normalOS=TransformWorldToObjectNormal(normalize(n));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Forward
            #pragma fragment LitPassFragment
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            // Rough dyed canvas is diffuse even where the wet folds face the sky.
            #define _ENVIRONMENTREFLECTIONS_OFF 1
            #define _SPECULARHIGHLIGHTS_OFF 1
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            Varyings Forward(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                MoveCloth(input.positionOS.xyz,input.normalOS);
                return LitPassVertex(input);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Shadow
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            Varyings Shadow(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                MoveCloth(input.positionOS.xyz,input.normalOS);
                return ShadowPassVertex(input);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Depth
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            Varyings Depth(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 unusedNormal=float3(0,0,1);
                MoveCloth(input.position.xyz,unusedNormal);
                return DepthOnlyVertex(input);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormal
            #pragma fragment DepthNormalsFragment
            #pragma shader_feature_local _NORMALMAP
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"
            Varyings DepthNormal(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                MoveCloth(input.positionOS.xyz,input.normal);
                return DepthNormalsVertex(input);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/Meta"
    }
}
