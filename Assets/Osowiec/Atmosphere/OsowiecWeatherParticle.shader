Shader "Steel and Ashes/Osowiec Weather Particle"
{
    Properties
    {
        _Rain("Rain shape", Range(0,1)) = 0
        _Tint("Tint", Color) = (1,1,1,1)
        _FadeDistance("Depth fade distance", Float) = .8
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Rain;
                float _FadeDistance;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float depth : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.depth = -TransformWorldToView(output.positionWS).z;
                output.uv = input.uv;
                output.color = input.color * _Tint;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv * 2 - 1;
                float2 drift = float2(uv.x + cos(uv.y * 4 - _Time.y * .4) * .093, uv.y);
                float cloud = 1 - smoothstep(.0, .87, length(drift));
                cloud *= cloud;
                cloud *= .73 + sin(uv.x * 6 + uv.y * 2 + _Time.y * .3) * .13
                    + cos(uv.y * 8 - uv.x * 3 + _Time.y * .2) * .13;
                float streak = (1 - smoothstep(.05, .55, abs(uv.x)))
                    * smoothstep(-1, -.5, uv.y) * (1 - smoothstep(.04, 1, uv.y));
                float opacity = lerp(cloud, streak, _Rain);

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneDepth = IsPerspectiveProjection()
                    ? LinearEyeDepth(rawDepth, _ZBufferParams) : LinearDepthToEyeDepth(rawDepth);
                float softDepth = saturate((sceneDepth - input.depth) / max(.01, _FadeDistance));
                float nearFade = smoothstep(.28, lerp(2.6, 1.0, _Rain), input.depth);
                half4 color = input.color;
                color.a *= opacity * softDepth * nearFade;
                return color;
            }
            ENDHLSL
        }
    }
}
