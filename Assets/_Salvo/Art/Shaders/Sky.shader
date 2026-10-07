// SALVO skybox: three-stop gradient, soft (non-blooming) sun disc, and a slow cloud band
// sampled from the shared detail texture. One texture fetch, no loops.
Shader "Salvo/Sky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.32,0.55,0.80,1)
        _HorizonColor ("Horizon", Color) = (0.80,0.88,0.92,1)
        _GroundColor ("Below Horizon", Color) = (0.78,0.72,0.62,1)
        _HorizonSharpness ("Horizon Sharpness", Range(0.5,8)) = 2.4
        _SunColor ("Sun", Color) = (1,0.96,0.86,1)
        _SunSize ("Sun Size", Range(0.995,0.99999)) = 0.9993
        _SunHalo ("Sun Halo", Range(0,1)) = 0.18
        [NoScaleOffset] _CloudMap ("Cloud Noise", 2D) = "gray" {}
        _CloudColor ("Cloud Color", Color) = (1,1,1,1)
        _CloudShade ("Cloud Shade", Color) = (0.78,0.82,0.9,1)
        _CloudCover ("Cloud Cover", Range(0,1)) = 0.45
        _CloudScale ("Cloud Scale", Float) = 0.6
        _CloudSpeed ("Cloud Speed", Float) = 0.004
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
            #include "SalvoGrade.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor, _HorizonColor, _GroundColor, _SunColor, _CloudColor, _CloudShade;
                half _HorizonSharpness, _SunSize, _SunHalo, _CloudCover;
                float _CloudScale, _CloudSpeed;
            CBUFFER_END
            TEXTURE2D(_CloudMap); SAMPLER(sampler_CloudMap);

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            V vert(A v)
            {
                V o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float scope = SalvoScope(i.positionCS);
                UNITY_BRANCH
                if (scope <= 0.0) return half4(0, 0, 0, 1);
                float3 d = normalize(i.dir);
                half up = d.y;
                half t = saturate(pow(saturate(up), 1.0 / _HorizonSharpness));
                half3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, t);
                sky = lerp(sky, _GroundColor.rgb, saturate(-up * 6.0));

                // clouds: project onto a dome, fade at the horizon and zenith
                float2 uv = d.xz / (d.y + 0.25) * _CloudScale + _Time.y * _CloudSpeed * float2(1, 0.35);
                half n = SAMPLE_TEXTURE2D(_CloudMap, sampler_CloudMap, uv).r;
                half cover = smoothstep(1.0 - _CloudCover, 1.0 - _CloudCover + 0.18, n);
                half band = smoothstep(0.02, 0.18, up) * (1.0 - smoothstep(0.55, 0.95, up));
                half3 cloud = lerp(_CloudShade.rgb, _CloudColor.rgb, smoothstep(0.4, 0.95, n));
                sky = lerp(sky, cloud, cover * band);

                Light sun = GetMainLight();
                half s = dot(d, sun.direction);
                half disc = smoothstep(_SunSize, _SunSize + 0.0003, s);
                half halo = pow(saturate(s), 64.0) * _SunHalo;
                sky += _SunColor.rgb * (disc * (1.0 - cover * band) * 0.6 + halo);
                return half4(SalvoGrade(sky, i.positionCS) * scope, 1);
            }
            ENDHLSL
        }
    }
}
