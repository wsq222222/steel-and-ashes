Shader "Steel and Ashes/Osowiec Dawn"
{
    Properties
    {
        _Zenith("Cloud ceiling", Color) = (.25,.30,.34,1)
        _Horizon("Dawn haze", Color) = (.51,.54,.47,1)
        _Break("Distant light", Color) = (.76,.70,.56,1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith;
                half4 _Horizon;
                half4 _Break;
            CBUFFER_END

            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings Vert(float3 position : POSITION)
            {
                Varyings output;
                output.position = TransformObjectToHClip(position);
                output.direction = position;
                return output;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(41.7,289.5))) * 45758.5453);
            }

            float Noise(float2 p)
            {
                float2 c = floor(p);
                float2 a = frac(p);
                a = a * a * (3 - 2 * a);
                return lerp(lerp(Hash(c), Hash(c + float2(1, 0)), a.x),
                    lerp(Hash(c + float2(0, 1)), Hash(c + 1), a.x), a.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float height = saturate(direction.y);
                float2 cloudsUV = (direction.xz + direction.y * float2(-.43,.27)) / (height + .53) * 3.2;
                cloudsUV.x -= _Time.y * .008;
                float cloud = Noise(cloudsUV) * .62 + Noise(cloudsUV * 2.14) * .26
                    + Noise(cloudsUV * 4.32) * .12;
                float opening = pow(saturate(dot(direction, normalize(float3(.55,.18,.81)))), 8);
                half3 sky = lerp(_Horizon.rgb, _Zenith.rgb, smoothstep(0, .8, height));
                sky *= lerp(1, lerp(.67, 1.22, cloud), smoothstep(.02, .31, height));
                sky = lerp(sky, _Break.rgb * lerp(.74, 1, cloud), opening * .43);
                return half4(sky, 1);
            }
            ENDHLSL
        }
    }
}
