Shader "Hidden/PSXPost"
{
    Properties
    {
        _PaletteSteps ("Palette Steps (RGB)", Vector) = (5,5,5,0)
        _TargetScale ("Resolution Scale (0.1-1)", Float) = 0.5
        _DitherStrength ("Dither Strength", Range(0,1)) = 0.4
        _JitterStrength ("Screen Jitter", Range(0,2)) = 0.25
        _ZJitter ("Depth Jitter", Range(0,2)) = 0.6

        _FogColor ("Fog Color", Color) = (0.1,0.1,0.15,1)
        _FogStart ("Fog Start (m)", Float) = 12
        _FogEnd   ("Fog End (m)", Float) = 40
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "PSXPost"
            // This pass works with the URP Fullscreen Pass feature.

            HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            // IMPORTANT: Blit.hlsl already declares:
            // TEXTURE2D_X(_BlitTexture)
            // SAMPLER(sampler_PointClamp), SAMPLER(sampler_LinearClamp)

            // Depth texture (we still declare this one)
            TEXTURE2D_X_FLOAT(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            float4 _PaletteSteps;
            float  _TargetScale;
            float  _DitherStrength;
            float  _JitterStrength;
            float  _ZJitter;

            float4 _FogColor;
            float  _FogStart;
            float  _FogEnd;

            static const float bayer4x4[16] = {
                0,  8,  2, 10,
               12,  4, 14,  6,
                3, 11,  1,  9,
               15,  7, 13,  5
            };

            float OrderedDither4x4(float2 uv)
            {
                float2 pix = uv * _ScreenParams.xy;
                int2 ip = int2((int)pix.x & 3, (int)pix.y & 3);
                int idx = ip.y * 4 + ip.x;
                return (bayer4x4[idx] / 16.0) - 0.5;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }

            float3 Quantize(float3 c, float3 steps, float2 uvForDither, float ditherAmt)
            {
                float d = OrderedDither4x4(uvForDither) * ditherAmt;
                float3 s = max(1.0, steps);
                return saturate(floor(s * (c + d)) / s);
            }

            float RemapFog(float depth01, float startM, float endM)
            {
                float start01 = saturate(startM / max(endM, 0.0001));
                return saturate((depth01 - start01) / max(1.0 - start01, 0.0001));
            }
            ENDHLSL

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                float2 targetRes = max(_ScreenParams.xy * saturate(_TargetScale), 1.0);
                float2 uvLow = floor(uv * targetRes) / targetRes;

                float raw = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, uvLow).r;
                float depth01 = Linear01Depth(raw, _ZBufferParams);

                float zJ = lerp(_ZJitter, 0.0, depth01);
                float j = (hash21(uvLow * _ScreenParams.xy) - 0.5)
                          * _JitterStrength * zJ / max(targetRes.x, 1.0);
                float2 uvJit = uvLow + j;

                // _BlitTexture + sampler_LinearClamp come from Blit.hlsl
                float4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uvJit);

                float3 q = Quantize(col.rgb, _PaletteSteps.xyz, uvJit, _DitherStrength);

                float fogT = RemapFog(depth01, _FogStart, _FogEnd);
                float3 finalRgb = lerp(q, _FogColor.rgb, fogT);

                return float4(finalRgb, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
