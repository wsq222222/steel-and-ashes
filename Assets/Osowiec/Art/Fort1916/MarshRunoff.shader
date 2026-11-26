Shader "Steel and Ashes/Marsh Runoff"
{
    Properties { _Shallow ("Shallow mud", Color) = (.16,.143,.101,1) _Sky ("Reflected sky", Color) = (.43,.47,.44,1) }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-20"}
        Pass
        {
            Tags {"LightMode"="UniversalForward"}
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct Attributes { float4 positionOS: POSITION; half4 depth: COLOR; };
            struct Varyings { float4 positionCS: SV_POSITION; float3 world: TEXCOORD0; float fog: TEXCOORD1; half depth:TEXCOORD2; };
            CBUFFER_START(UnityPerMaterial)
            half4 _Shallow, _Sky;
            CBUFFER_END
            Varyings vert(Attributes a)
            {
                Varyings o; o.world=TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.world); o.fog=ComputeFogFactor(o.positionCS.z); o.depth=a.depth.r;return o;
            }
            float hash(float2 p) { return frac(sin(dot(p,float2(41.23,19.18)))*43758.5453); }
            half4 frag(Varyings i):SV_Target
            {
                float2 p=i.world.xz; float2 cell=floor(p*2.4);
                float2 center=.25+.5*float2(hash(cell),hash(cell+17));
                float2 uv=frac(p*2.4)-center; float r=length(uv);
                float t=frac(_Time.y*.83+hash(cell+5.7));
                float ripple=sin((r-t*.56)*86)*exp(-abs(r-t*.56)*17)*(1-t);
                float2 dir=normalize(uv+float2(.001,.0003));
                float3 norm=normalize(float3(.055*ripple*dir.x+.012*sin(p.x*7+_Time.y),1,.055*ripple*dir.y));
                float3 view=normalize(_WorldSpaceCameraPos-i.world); float ndv=saturate(dot(norm,view));
                float fres=.12+.79*pow(1-ndv,3.4);
                half3 reflectDir=reflect(-view,norm);
                half3 sky=GlossyEnvironmentReflection(reflectDir,i.world,.19h,1,GetNormalizedScreenSpaceUV(i.positionCS));
                half3 silt=lerp(_Shallow.rgb*.8h,half3(.038,.044,.031),i.depth);
                half3 reflected=lerp(_Sky.rgb*.40h,sky,.67h);
                half3 col=lerp(silt,reflected,fres)+.018*ripple;
                half shore=smoothstep(.006,.25,i.depth);
                return half4(MixFog(col,i.fog),shore*lerp(.43,.83,i.depth));
            }
            ENDHLSL
        }
    }
}
