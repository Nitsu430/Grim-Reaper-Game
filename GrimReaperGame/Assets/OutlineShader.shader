Shader "URP/Unlit Outlined"
{
    Properties
    {
        _BaseMap      ("Base Map", 2D) = "white" {}
        _BaseColor    ("Base Color", Color) = (1,1,1,1)

        _OutlineColor     ("Outline Color", Color) = (0,0,0,1)
        _OutlineThickness ("Outline Thickness (World)", Range(0,0.05)) = 0.01

        // Keyword enum avoids parser issues with [Toggle(...)]
        [KeywordEnum(BasePlusOutline, OutlineOnly)] _OutlineMode ("Outline Mode", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        // -------- Base pass (always compiled; discarded when OutlineOnly) --------
        Pass
        {
            Name "FORWARD_UNLIT"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            // expose enum keywords
            #pragma shader_feature_local _OUTLINEMODE_BASEPLUSOUTLINE _OUTLINEMODE_OUTLINEONLY

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            float4 _BaseColor;

            struct Attributes {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posWS = TransformObjectToWorld(IN.positionOS);
                OUT.positionHCS = TransformWorldToHClip(posWS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                // If material is set to OutlineOnly, discard the whole base pass.
                #ifdef _OUTLINEMODE_OUTLINEONLY
                clip(-1); // kill fragment
                #endif

                float4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                return baseTex * _BaseColor;
            }
            ENDHLSL
        }

        // -------- Outline pass (always) --------
        Pass
        {
            Name "OUTLINE"
            Tags { "LightMode"="SRPDefaultUnlit" }

            Cull Front          // draw backfaces to create shell
            ZWrite On
            ZTest LEqual
            Offset 1,1          // reduce z-fighting

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _OUTLINEMODE_BASEPLUSOUTLINE _OUTLINEMODE_OUTLINEONLY

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _OutlineColor;
            float  _OutlineThickness;

            struct Attributes {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 posWS = TransformObjectToWorld(IN.positionOS);
                float3 nWS   = normalize(TransformObjectToWorldNormal(IN.normalOS));
                posWS += nWS * _OutlineThickness;  // world-space extrusion
                OUT.positionHCS = TransformWorldToHClip(posWS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
