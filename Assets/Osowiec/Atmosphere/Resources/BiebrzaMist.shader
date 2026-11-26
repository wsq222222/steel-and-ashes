Shader "Steel and Ashes/Biebrza suspended mist"
{
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-30" "RenderType"="Transparent" }
  Pass
  {
   Cull Front ZWrite Off ZTest Always Blend One OneMinusSrcAlpha
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
   TEXTURE3D(_Billows); SAMPLER(sampler_Billows);
   float4 _MistCenter,_MistSize;
   float _MistAmount,_Motion,_Glass;
   struct Input { float3 positionOS : POSITION; };
   struct Varyings { float4 pos : SV_POSITION; float3 world : TEXCOORD0; };
   Varyings vert(Input a)
   {
    Varyings o; o.world=TransformObjectToWorld(a.positionOS); o.pos=TransformWorldToHClip(o.world); return o;
   }
   float billows(float3 w)
   {
    float3 q=w*float3(.013,.026,.019)+float3(-.0019,-.0008,.00055)*_Time.y*_Motion;
    return SAMPLE_TEXTURE3D_LOD(_Billows,sampler_Billows,q,0).r*.64
         + SAMPLE_TEXTURE3D_LOD(_Billows,sampler_Billows,q*2.07+.39,0).r*.26
         + SAMPLE_TEXTURE3D_LOD(_Billows,sampler_Billows,q*4.1+.21,0).r*.1;
   }
   half4 frag(Varyings i) : SV_Target
   {
    float2 uv=GetNormalizedScreenSpaceUV(i.pos);
    float depth=SampleSceneDepth(uv);
    #if UNITY_REVERSED_Z
     float nearDepth=1;
    #else
     depth=lerp(UNITY_NEAR_CLIP_VALUE,1,depth);
     float nearDepth=UNITY_NEAR_CLIP_VALUE;
    #endif
    float3 origin=ComputeWorldSpacePosition(uv,nearDepth,UNITY_MATRIX_I_VP);
    float3 ray=normalize(i.world-origin);
    float3 invdir=rcp(float3(ray.x<0?-1:1,ray.y<0?-1:1,ray.z<0?-1:1)*max(abs(ray),1e-5));
    float3 a=(_MistCenter.xyz-_MistSize.xyz*.5-origin)*invdir;
    float3 b=(_MistCenter.xyz+_MistSize.xyz*.5-origin)*invdir;
    float3 lo=min(a,b),hi=max(a,b);
    float3 hit=ComputeWorldSpacePosition(uv,depth,UNITY_MATRIX_I_VP);
    float start=max(0,max(lo.x,max(lo.y,lo.z)));
    float end=min(dot(hit-origin,ray),min(hi.x,min(hi.y,hi.z)));
    clip(end-start-.001);
    // Short paths near the feathered perimeter need fewer volume samples.
    int steps=clamp((int)ceil((end-start)*2.4),6,16);
    float stride=(end-start)/steps;
    float3 invExtent=2.0/_MistSize.xyz;
    float jitter=frac(52.9829189*frac(dot(floor(i.pos.xy),float2(.06711056,.00583715))));
    float3 samplePos=origin+ray*(start+stride*(.25+jitter*.5));
    float3 rgb=0; float trans=1;
    [loop] for(int n=0;n<steps;n++)
    {
     float3 local=(samplePos-_MistCenter.xyz)*invExtent;
     float v=billows(samplePos);
     float2 rim=abs(local.xz);
     float radius=pow(dot(rim*rim,rim),1.0/3.0);
     float edge=1-smoothstep(.47+v*.14,.98,radius+(v-.5)*.13);
     float h=saturate(local.y*.5+.5);
     float roof=smoothstep(0,.24, .35+v*.67-h);
     float density=smoothstep(.27,.69,v)*edge*roof*smoothstep(0,.12,h)*_MistAmount;
     float extinction=1-exp(-density*stride*.80);
     float light=saturate(.17+h*.77+(v-.45)*.47);
     rgb+=trans*extinction*lerp(float3(.098,.108,.062),float3(.61,.64,.33),light);
     trans*=1-extinction;
     if(trans<.012) break;
     samplePos+=ray*stride;
    }
    float inside=saturate(1-max(abs((origin.x-_MistCenter.x)/(_MistSize.x*.5)),abs((origin.z-_MistCenter.z)/(_MistSize.z*.5)))*1.06);
    inside*=saturate(1-abs((origin.y-_MistCenter.y)/(_MistSize.y*.5)));
    float glass=inside*_Glass*min(1,1-trans);
    if(glass>.005)
    {
     float2 wave=float2(sin(uv.y*33+_Time.y*1.5+sin(uv.x*10)),cos(uv.x*28+_Time.y*.83));
     rgb+=SampleSceneColor(saturate(uv+wave*.0012*glass))*trans*glass;
     trans*=1-glass;
    }
    return half4(rgb,1-trans);
   }
   ENDHLSL
  }
 }
}