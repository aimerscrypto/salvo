// SALVO stylized lit shader (URP).
// Albedo comes from sRGB vertex colors (palette baked in Blender), vertex alpha carries baked AO.
// Lighting: soft-ramped main light (not hard cel) + SH ambient (trilight gradient) + optional
// SSAO, a small stylized highlight, and a triplanar grain/camo detail map to break up flat areas.
// One main light, no additional-light loop: keeps variants and fragment cost tiny on iGPUs.
// SRP Batcher compatible: every material property lives in UnityPerMaterial.
Shader "Salvo/StylizedLit"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _BaseMap ("Albedo (optional)", 2D) = "white" {}
        [NoScaleOffset] _DetailMap ("Detail (R grain, G blobs)", 2D) = "gray" {}
        _DetailScale ("Detail Scale (1/m)", Float) = 0.35
        _DetailStrength ("Grain Strength", Range(0,1)) = 0.18
        _PatternColor ("Pattern Color", Color) = (0.2,0.2,0.2,1)
        _PatternStrength ("Pattern Strength", Range(0,1)) = 0
        _PatternScale ("Pattern Scale", Float) = 6
        _AOStrength ("Vertex AO Strength", Range(0,1)) = 1
        _AmbientTint ("Ambient Tint", Color) = (1,1,1,1)
        _RampSoftness ("Ramp Softness", Range(0.02,1)) = 0.45
        _RampOffset ("Ramp Offset", Range(-0.5,0.5)) = 0.0
        _SpecStrength ("Highlight Strength", Range(0,1)) = 0.08
        _SpecSize ("Highlight Tightness", Range(2,256)) = 40
        [Toggle] _ObjectSpace ("Object Space Detail (moving props)", Float) = 0
        _Emission ("Emission (signage only)", Color) = (0,0,0,0)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
    #include "SalvoGrade.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _BaseColor;
        half4 _PatternColor;
        half4 _AmbientTint;
        half4 _Emission;
        float _DetailScale;
        half _DetailStrength;
        half _PatternStrength;
        float _PatternScale;
        half _AOStrength;
        half _RampSoftness;
        half _RampOffset;
        half _SpecStrength;
        half _SpecSize;
        half _ObjectSpace;
        half _Cull;
    CBUFFER_END

    TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);
    TEXTURE2D(_DetailMap); SAMPLER(sampler_DetailMap);
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "IgnoreProjector"="True" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half4  color      : TEXCOORD2;   // linear albedo rgb + AO
                float2 uv         : TEXCOORD3;
                float4 detailPos  : TEXCOORD4;   // xyz = detail-space position, w = fog
                float3 detailNrm  : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(v.normalOS);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = nrm.normalWS;
                // palette is authored in sRGB; convert once per vertex
                o.color = half4(SRGBToLinear(v.color.rgb), lerp(1.0h, v.color.a, _AOStrength));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                float3 dpos = lerp(pos.positionWS, v.positionOS.xyz, _ObjectSpace);
                o.detailPos = float4(dpos * _DetailScale, ComputeFogFactor(pos.positionCS.z));
                o.detailNrm = normalize(lerp(nrm.normalWS, v.normalOS, _ObjectSpace));
                return o;
            }

            half2 Triplanar(float3 p, float3 n, float scale)
            {
                float3 w = pow(abs(n), 4.0);
                w /= (w.x + w.y + w.z + 1e-4);
                half2 x = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, p.zy * scale).rg;
                half2 y = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, p.xz * scale).rg;
                half2 z = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, p.xy * scale).rg;
                return x * w.x + y * w.y + z * w.z;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                half3 albedo = i.color.rgb * _BaseColor.rgb * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;

                // detail: grain (R) modulates value, blobs (G) drive the paint pattern on skins
                half grain = Triplanar(i.detailPos.xyz, i.detailNrm, 1.0).r;
                albedo *= lerp(1.0h, grain * 2.0h, _DetailStrength);
                UNITY_BRANCH
                if (_PatternStrength > 0.001h)
                {
                    half blobs = Triplanar(i.detailPos.xyz, i.detailNrm, _PatternScale).g;
                    half mask = smoothstep(0.52h, 0.56h, blobs);
                    albedo = lerp(albedo, _PatternColor.rgb, mask * _PatternStrength);
                }

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half ao = i.color.a;
                half directAO = 1.0h;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                    AmbientOcclusionFactor ssao = GetScreenSpaceAmbientOcclusion(suv);
                    ao *= ssao.indirectAmbientOcclusion;
                    directAO = ssao.directAmbientOcclusion;
                #endif

                half ndl = dot(n, mainLight.direction);
                half ramp = smoothstep(_RampOffset - _RampSoftness * 0.5h, _RampOffset + _RampSoftness * 0.5h, ndl);
                half atten = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half lit = ramp * atten * directAO;

                half3 ambient = SampleSH(n) * _AmbientTint.rgb * ao;
                half3 direct = mainLight.color * lit * lerp(0.75h, 1.0h, ao);

                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 h = normalize(mainLight.direction + v);
                half spec = smoothstep(0.55h, 0.65h, pow(saturate(dot(n, h)), _SpecSize)) * _SpecStrength * lit;

                half3 col = albedo * (ambient + direct) + mainLight.color * spec + _Emission.rgb;
                col = MixFog(col, i.detailPos.w);
                return half4(SalvoGrade(col, i.positionCS), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = positionCS;
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }
            half frag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
