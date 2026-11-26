Shader "Steel and Ashes/Osowiec Wet Surface"
{
    Properties
    {
        [MainColor] _BaseColor("Surface colour", Color) = (.34,.21,.15,1)
        _Smoothness("Dry smoothness", Range(0,1)) = .25
        _Wetness("Wetness", Range(0,1)) = .65
        _Brickwork("Brick and mortar", Range(0,1)) = 0
        _Puddle("Standing water", Range(0,1)) = 0
        _RippleStrength("Rain ripples", Range(0,1)) = .65
        [HideInInspector] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Smoothness;
                half _Wetness;
                half _Brickwork;
                half _Puddle;
                half _RippleStrength;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 fogAndVertexLight : TEXCOORD2;
                float4 screenPosition : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogAndVertexLight.x = ComputeFogFactor(output.positionCS.z);
                output.fogAndVertexLight.yzw = VertexLighting(output.positionWS, output.normalWS);
                output.screenPosition = ComputeScreenPos(output.positionCS);
                return output;
            }

            float Hash(float2 cell)
            {
                return frac(sin(dot(cell, float2(127.1,311.7))) * 43758.5453);
            }

            float Noise(float2 coordinate)
            {
                float2 cell = floor(coordinate);
                float2 f = frac(coordinate);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1, 0)), f.x),
                    lerp(Hash(cell + float2(0, 1)), Hash(cell + float2(1, 1)), f.x), f.y);
            }

            float Ripple(float2 uv)
            {
                float2 cell = floor(uv);
                float2 offset = float2(Hash(cell), Hash(cell + 7)) * .34 - .17;
                float distanceFromDrop = length(frac(uv) - .5 - offset);
                float age = frac(_Time.y * .85 + Hash(cell - 9));
                float ring = distanceFromDrop - age * .43;
                return sin(ring * 72) * (1 - smoothstep(0, .065, abs(ring)))
                    * (1 - age) * smoothstep(.00, .12, age);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 p = input.positionWS;
                half3 normal = normalize(input.normalWS);
                float2 surfaceUV = abs(normal.y) > .6 ? p.xz
                    : float2(abs(normal.x) > abs(normal.z) ? p.z : p.x, p.y);
                float relief = Noise(surfaceUV * 6.3);
                float grain = Noise(surfaceUV * 34);
                float runoff = Noise(surfaceUV * float2(8.4, .83));
                float broadStain = Noise(surfaceUV * 1.35);
                float stains = broadStain * .66 + runoff * .34;
                float brickRow = floor(p.y * 8);
                float2 wear = float2(Noise(surfaceUV * 15), Noise(surfaceUV.yx * 16 + 8)) - .5;
                float2 brickUV = float2(surfaceUV.x * 4.1 + frac(brickRow * .5), p.y * 8) + wear * .048;
                float2 cell = frac(brickUV);
                float2 seam = smoothstep(.038, .098, min(cell, 1 - cell));
                half brick = lerp(1, seam.x * seam.y, _Brickwork * (1 - abs(normal.y)));
                half colourVariation = lerp(.89, 1.09, relief) + (grain - .5) * .081;
                colourVariation *= lerp(1, lerp(.74, 1.19, Hash(floor(brickUV))), _Brickwork);
                half wet = saturate(_Wetness * lerp(.25, 1.04, stains));
                half overview = smoothstep(8, 22, _WorldSpaceCameraPos.y - p.y);
                float ripple = Ripple(p.xz * 2.1) + .5 * Ripple(p.xz * 3.5 + 1.2);

                float height = relief * .0041 + brick * _Brickwork * .0055
                    + ripple * _Puddle * _RippleStrength * .0008;
                float3 derivativeX = ddx(p);
                float3 derivativeY = ddy(p);
                float3 crossX = cross(derivativeY, normal);
                float3 crossY = cross(normal, derivativeX);
                float determinant = dot(derivativeX, crossX);
                float3 gradient = sign(determinant) * (ddx(height) * crossX + ddy(height) * crossY);
                normal = normalize(max(abs(determinant), 1e-7) * normal - gradient);

                SurfaceData surface = (SurfaceData)0;
                half3 mortar = lerp(_BaseColor.rgb * .62, half3(.102,.091,.071), .44);
                half3 surfaceColor = lerp(mortar, _BaseColor.rgb * colourVariation, brick)
                    * lerp(.98, .76, wet) * lerp(.81, 1.13, broadStain);
                surface.albedo = lerp(surfaceColor, surfaceColor * .55, _Puddle);
                half mineralSheen = lerp(min(_Smoothness, .36), .57, wet) + (relief - .5) * .075;
                half waterSheen = .75 + stains * .071 - saturate(abs(ripple)) * .045;
                surface.smoothness = saturate(lerp(lerp(mineralSheen, waterSheen, _Puddle), .39, overview));
                surface.metallic = _Puddle * .025;
                surface.occlusion = lerp(.77, 1, brick);
                surface.alpha = 1;
                surface.normalTS = half3(0, 0, 1);

                InputData lighting = (InputData)0;
                lighting.positionWS = p;
                lighting.positionCS = input.positionCS;
                lighting.normalWS = normal;
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(p);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    lighting.shadowCoord = input.screenPosition;
                #else
                    lighting.shadowCoord = TransformWorldToShadowCoord(p);
                #endif
                lighting.fogCoord = InitializeInputDataFog(float4(p, 1), input.fogAndVertexLight.x);
                lighting.vertexLighting = input.fogAndVertexLight.yzw;
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.bakedGI = max(SampleSH(normal), lerp(.14, .31, overview));
                lighting.shadowMask = half4(1, 1, 1, 1);
                half4 colour = UniversalFragmentPBR(lighting, surface);
                colour.rgb = MixFog(colour.rgb, lighting.fogCoord);
                return colour;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
