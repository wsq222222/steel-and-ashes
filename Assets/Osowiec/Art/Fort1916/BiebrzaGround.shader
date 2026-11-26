Shader "Steel and Ashes/Biebrza Ground"
{
    Properties {
        _Earth ("Peat albedo", 2D) = "white" {}
        [Normal] _EarthNormal ("Peat normal", 2D) = "bump" {}
        _EarthMask ("Peat smoothness", 2D) = "white" {}
        _Till ("Exposed gravel and clay", 2D) = "white" {}
        [Normal] _TillNormal ("Wet gravel normal", 2D) = "bump" {}
        _TillMask ("Wet gravel height and roughness", 2D) = "white" {}
        _Sod ("Bank albedo", 2D) = "white" {}
        [Normal] _SodNormal ("Bank normal", 2D) = "bump" {}
        _SodMask ("Bank smoothness", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "Forward"
            Tags {"LightMode"="UniversalForward"}
            HLSLPROGRAM
            #pragma target 4.6
            #pragma vertex SurveyVertex
            #pragma hull SoilHull
            #pragma domain SoilDomain
            #define SOIL_TESSELLATION 1
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "BiebrzaSoilGeometry.hlsl"
            #include "BiebrzaSoilLighting.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags {"LightMode"="ShadowCaster"}
            ColorMask 0
            HLSLPROGRAM
            #pragma target 4.6
            #pragma vertex SurveyVertex
            #pragma hull SoilHull
            #pragma domain SoilDomain
            #define SOIL_TESSELLATION 1
            #pragma fragment SoilDepth
            #pragma multi_compile _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define SOIL_SHADOW 1
            #include "BiebrzaSoilGeometry.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags {"LightMode"="DepthOnly"}
            ColorMask R
            HLSLPROGRAM
            #pragma target 4.6
            #pragma vertex SurveyVertex
            #pragma hull SoilHull
            #pragma domain SoilDomain
            #define SOIL_TESSELLATION 1
            #pragma fragment SoilDepth
            #include "BiebrzaSoilGeometry.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags {"LightMode"="DepthNormals"}
            HLSLPROGRAM
            #pragma target 4.6
            #pragma vertex SurveyVertex
            #pragma hull SoilHull
            #pragma domain SoilDomain
            #define SOIL_TESSELLATION 1
            #pragma fragment SoilNormals
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "BiebrzaSoilGeometry.hlsl"
            ENDHLSL
        }
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "Forward"
            Tags {"LightMode"="UniversalForward"}
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex GroundFallback
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "BiebrzaSoilGeometry.hlsl"
            #include "BiebrzaSoilLighting.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags {"LightMode"="ShadowCaster"}
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex GroundFallback
            #pragma fragment SoilDepth
            #pragma multi_compile _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define SOIL_SHADOW 1
            #include "BiebrzaSoilGeometry.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags {"LightMode"="DepthOnly"}
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex GroundFallback
            #pragma fragment SoilDepth
            #include "BiebrzaSoilGeometry.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags {"LightMode"="DepthNormals"}
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex GroundFallback
            #pragma fragment SoilNormals
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "BiebrzaSoilGeometry.hlsl"
            ENDHLSL
        }
    }
}
