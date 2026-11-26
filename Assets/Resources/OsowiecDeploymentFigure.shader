Shader "Steel and Ashes/Deployment Figure"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS=TransformObjectToHClip(v.positionOS);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                // Screen-door coverage keeps overlapping garments from turning the preview solid.
                uint2 p=(uint2)i.positionCS.xy;
                if ((p.x+2u*p.y)%4u==0u) discard;
                half light=.5h+.5h*dot(normalize(i.normalWS),normalize(half3(-.58h,.81h,.25h)));
                return half4(lerp(half3(.14h,.17h,.14h),half3(.81h,.76h,.54h),light),1);
            }
            ENDHLSL
        }
    }
}
