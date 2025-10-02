Shader "UI/KawaseBlur_URP"
{
    Properties
    {
        [PerRendererData]_MainTex ("Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)

        _BlurRadius ("Blur Radius", Range(0, 8)) = 0
        _Iterations ("Iterations", Range(1, 6)) = 3
        _Downsample ("Downsample", Range(1, 4)) = 2

        // --- UI Stencil (needed for Mask/RectMask2D) ---
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"
               "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }

        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Stencil // lets UI Mask/RectMask2D work
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        ColorMask [_ColorMask]

        Pass
        {
            Name "UIKawase"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };
            struct v2f {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
                float2 uv    : TEXCOORD0;
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize; // x=1/w, y=1/h
            float4 _Tint;
            float  _BlurRadius;
            int    _Iterations;
            int    _Downsample;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.uv = v.uv;
                o.color = v.color * _Tint;
                return o;
            }

            float4 KawaseSample(float2 uv, float radius)
            {
                float2 texel = _MainTex_TexelSize.xy * radius;
                float4 c = 0;
                c += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( texel.x,  0));
                c += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-texel.x,  0));
                c += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( 0,  texel.y));
                c += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( 0, -texel.y));
                c += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( texel.x,  texel.y));
                c += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-texel.x,  texel.y));
                c += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2( texel.x, -texel.y));
                c += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-texel.x, -texel.y));
                return c * (1.0 / 8.0);
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                if (_Downsample > 1)
                    uv = floor(uv * _Downsample) / _Downsample + (0.5 / _Downsample);

                float4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                float radius = _BlurRadius;

                [loop]
                for (int k = 0; k < _Iterations; ++k)
                {
                    col = KawaseSample(uv, radius);
                    radius += 0.75;
                }

                return col * i.color;
            }
            ENDHLSL
        }
    }
}
