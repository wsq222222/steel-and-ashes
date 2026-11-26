Shader "Steel and Ashes/Wet transfers on paving and timber"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-35" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float _RainClock;
            struct Input { float4 pos:POSITION; float2 uv:TEXCOORD0; float2 age:TEXCOORD1; float4 tint:COLOR; };
            struct Vary { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float age:TEXCOORD1; float4 tint:COLOR; float3 world:TEXCOORD2; float fog:TEXCOORD3; };
            Vary Vert(Input v)
            {
                Vary o; o.world=TransformObjectToWorld(v.pos.xyz); o.pos=TransformWorldToHClip(o.world);
                o.uv=v.uv; o.age=saturate((_RainClock-v.age.x)/max(v.age.y,.01));
                o.tint=v.tint; o.fog=ComputeFogFactor(o.pos.z); return o;
            }
            float soilNoise(float2 p)
            {
                float2 cell=floor(p), blend=frac(p);
                blend=blend*blend*(3-2*blend);
                float4 phases=dot(cell,float2(127.1,311.7))+float4(0,127.1,311.7,438.8);
                float4 n=frac(sin(phases)*43758.5453);
                return lerp(lerp(n.x,n.y,blend.x),lerp(n.z,n.w,blend.x),blend.y);
            }
            half4 Frag(Vary i):SV_Target
            {
                bool wheel=i.uv.x>1.5;
                float2 p=float2(wheel?i.uv.x-2:i.uv.x,i.uv.y);
                float grit=soilNoise(i.world.xz*58);
                float transfer=soilNoise(i.world.xz*11);
                float grain=grit*2-1;
                float field;
                float tread;
                if(wheel)
                {
                    field=(.77+.15*transfer-abs(p.x*2-1))+.13*grain;
                    tread=smoothstep(.65,.95,abs(p.x*2-1));
                }
                else
                {
                    float ball=1-length((p-float2(.49,.70))/float2(.443,.289));
                    float arch=1-length((p-float2(.39,.439))/float2(.258,.244));
                    float heel=1-length((p-float2(.489,.199))/float2(.349,.173));
                    field=max(ball,max(arch,heel))+.092*grain;
                    tread=smoothstep(.1,.7,cos((p.y+abs(p.x-.5)*.15)*57));
                }
                float coverage=smoothstep(-.07,.12,field)*(1-smoothstep(.28,1,i.age))*i.tint.a;
                coverage*=lerp(.18,.94,smoothstep(.18,.66,grit*.43+transfer*.57))*(wheel?1.:lerp(.52,1.,tread));
                clip(coverage-.012);
                float ridge=(1-smoothstep(.01,.19,field))*.72+tread*.30;
                Light sun=GetMainLight();
                float3 soil=lerp(float3(.010,.008,.006),float3(.16,.125,.078),saturate(ridge+.07*grain+.06*transfer));
                float3 illumination=SampleSH(float3(0,1,0))+.3*sun.color*saturate(sun.direction.y);
                return half4(MixFog(soil*(.48+illumination),i.fog),coverage);
            }
            ENDHLSL
        }
    }
}
