Shader "Steel and Ashes/Tactical valley haze"
{
    Properties { _CloudMap ("Broken overcast", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-40" "RenderType"="Transparent" }
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_CloudMap); SAMPLER(sampler_CloudMap);
            float _OsowiecLandscapeLow;
            struct Input { float4 positionOS:POSITION; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 ground:TEXCOORD0; };
            Varyings Vert(Input input)
            {
                Varyings o;
                float3 world=TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(world);
                o.ground=world.xz;
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float2 drift=_Time.y*float2(.16,-.07);
                float2 uv=(i.ground+drift)*float2(.0108,.0162);
                half cloud=SAMPLE_TEXTURE2D(_CloudMap,sampler_CloudMap,uv).r;
                [branch] if (_OsowiecLandscapeLow < .5)
                {
                    float2 crosswind=float2(uv.y*.79+uv.x*.61,uv.y*.61-uv.x*.79);
                    cloud=cloud*.73h+.27h*SAMPLE_TEXTURE2D(_CloudMap,sampler_CloudMap,crosswind*2.18+float2(.23,.43)).r;
                }
                half edge=1.h-smoothstep(.68h,.99h,max(abs(i.ground.x)/140.h,abs(i.ground.y)/96.h));
                half opacity=smoothstep(.61h,.94h,cloud)*edge*lerp(.168h,.106h,_OsowiecLandscapeLow);
                return half4(.50h,.54h,.56h,opacity);
            }
            ENDHLSL
        }
    }
}
