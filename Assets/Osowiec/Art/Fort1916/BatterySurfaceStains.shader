Shader "Steel and Ashes/Battery surface stains"
{
    Properties
    {
        _Kind("Floor, lime deposit, or water run",Float)=0
        _Opacity("Peak opacity",Range(0,1))=.3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Offset -1,-1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _Kind, _Opacity;
            CBUFFER_END
            struct Input {float4 p:POSITION; float3 n:NORMAL; float3 mark:TEXCOORD0;};
            struct Output
            {
                float4 p:SV_POSITION; float3 mark:TEXCOORD0; float3 world:TEXCOORD1;
                half3 normal:TEXCOORD2; half fog:TEXCOORD3;
            };
            Output Vert(Input v)
            {
                Output o;
                o.world=TransformObjectToWorld(v.p.xyz);o.p=TransformWorldToHClip(o.world);
                o.normal=TransformObjectToWorldNormal(v.n);o.mark=v.mark;
                o.fog=ComputeFogFactor(o.p.z);return o;
            }
            float Hash(float2 p)
            {
                float3 q=frac(float3(p.xyx)*float3(.1031,.1030,.0973));
                q+=dot(q,q.yzx+33.33);return frac((q.x+q.y)*q.z);
            }
            float Noise(float2 p)
            {
                float2 i=floor(p), f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),
                    lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            float LampSheen(float3 lamp,float3 p,float3 view,float3 n)
            {
                float3 to=lamp-p;
                float falloff=saturate(1-length(to)/5.3);
                float3 h=SafeNormalize(SafeNormalize(to)+view);
                return falloff*pow(saturate(dot(n,h)),15);
            }
            half4 Frag(Output i):SV_Target
            {
                float2 q=i.mark.xy*2-1, shift=float2(i.mark.z*.87, i.mark.z*.37);
                float coarse=Noise(q*2.1+shift);
                float middle=Noise(q*5.7+shift.yx);
                float fine=Noise(q*27.3+shift);
                fine=lerp(fine,.5,saturate(max(fwidth(q.x),fwidth(q.y))*23));
                float border=smoothstep(0,.20,1-max(abs(q.x),abs(q.y)));
                float warped=length(q)+(coarse-.50)*.38+(middle-.50)*.14;
                half coverage;
                half3 colour;
                if(_Kind<.5)
                {
                    coverage=(1-smoothstep(.42,.96,warped))*(.55+.27*middle+.18*fine);
                    float3 view=GetWorldSpaceNormalizeViewDir(i.world);
                    float3 n=normalize(i.normal);
                    float lamp=LampSheen(float3(47.20,-1.01,-3.5),i.world,view,n)
                             +LampSheen(float3(47.20,-1.01,4.5),i.world,view,n);
                    colour=float3(.014,.011,.008)*(.7+.60*fine)
                        +float3(.110,.068,.029)*lamp*(.38+.62*middle);
                }
                else if(_Kind<1.5)
                {
                    float islands=Noise(q*float2(6.9,3.8)+shift);
                    coverage=(1-smoothstep(.42,.91,warped))*(.14+.86*smoothstep(.22,.78,islands))*(.48+.52*fine);
                    colour=float3(.138,.108,.071)*(.75+.25*middle);
                }
                else
                {
                    float channel=q.x+.32*(Noise(float2(q.y*3.7,shift.x))-0.5);
                    float width=lerp(.15,.51,saturate(i.mark.y))*(.70+.30*middle);
                    coverage=(1-smoothstep(width*.12,width+.19,abs(channel)))
                        *smoothstep(0,.30,i.mark.y)*(1-smoothstep(.70,1,i.mark.y))*(.58+.42*fine);
                    colour=float3(.036,.028,.018);
                }
                #ifdef UNITY_COLORSPACE_GAMMA
                    colour=LinearToSRGB(colour);
                #endif
                return half4(MixFog(colour,i.fog),coverage*border*_Opacity);
            }
            ENDHLSL
        }
    }
}
