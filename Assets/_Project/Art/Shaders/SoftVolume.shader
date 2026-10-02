// Light and air with no surface: sun shafts through the canopy (added light) and drifting ground mist (blended).
// Soft where it meets the ground or a trunk (needs the camera depth texture), fades out right at the lens, and on
// crossed cards it fades the ones seen edge-on, so a shaft never shows as a flat plank. Fog applies.
Shader "ProjectFossil/SoftVolume"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _SoftRange ("Soft edge at geometry (m)", Float) = 1.5
        _NearFade ("Fade out this close to the camera (m)", Float) = 3
        _EdgeFade ("Fade cards seen edge-on", Range(0, 1)) = 1
        _Additive ("Added light (1) or blended mist (0)", Range(0, 1)) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off

        Pass
        {
            Name "SoftVolume"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                float  _SoftRange, _NearFade, _EdgeFade, _Additive;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 viewWS     : TEXCOORD2;
                float4 screenPos  : TEXCOORD3;
                float  fogCoord   : TEXCOORD4;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.color      = input.color * _BaseColor;
                o.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                o.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                o.viewWS     = GetCameraPositionWS() - positionWS;
                o.screenPos  = ComputeScreenPos(o.positionCS);
                o.fogCoord   = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * input.color;

                float  dist   = length(input.viewWS);
                float3 toEye  = input.viewWS / max(dist, 1e-4);
                float  nLen   = length(input.normalWS); // particles may come without normals
                float  facing = nLen > 1e-4 ? abs(dot(input.normalWS / nLen, toEye)) : 1.0;
                float  fade   = lerp(1.0, saturate(facing * 1.6), _EdgeFade);
                fade *= saturate((dist - _NearFade * 0.5) / max(_NearFade * 0.5, 1e-3));

                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float  scene    = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                fade *= saturate((scene - input.screenPos.w) / max(_SoftRange, 1e-3));

                c.a *= fade;
                // Added light is weighed by its alpha and fades to nothing in the haze; mist blends into the fog.
                half3 added   = MixFogColor(c.rgb * c.a, half3(0, 0, 0), input.fogCoord);
                half3 blended = MixFog(c.rgb, input.fogCoord);
                return half4(lerp(blended, added, _Additive), c.a);
            }
            ENDHLSL
        }
    }
}
