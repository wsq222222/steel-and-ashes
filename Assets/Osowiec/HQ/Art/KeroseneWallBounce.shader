Shader "Steel and Ashes/Static kerosene bounce"
{
    Properties
    {
        _LampWorld("Stationary lamp position", Vector) = (1.95,2.01,3.36,0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+15" "RenderType" = "Opaque" }
        Pass
        {
            Name "Indirect light on the wall"
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Blend DstColor One
            ColorMask RGB
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _LampWorld;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
            };
            Varyings Vert(float3 positionOS : POSITION, float3 normalOS : NORMAL)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(normalOS);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float3 toLamp = _LampWorld.xyz - i.positionWS;
                float d2 = dot(toLamp, toLamp);
                clip(1.44 - d2);
                half incidence = saturate(dot(normalize(i.normalWS), toLamp * rsqrt(max(d2, .001))));
                half falloff = saturate(1.0 - d2 / 1.44);
                half bounce = incidence * falloff * falloff / (1.0 + 3.0*d2);
                return half4(half3(.92,.43,.14) * bounce, 1);
            }
            ENDHLSL
        }
    }
}
