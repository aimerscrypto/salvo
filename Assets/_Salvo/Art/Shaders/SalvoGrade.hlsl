#ifndef SALVO_GRADE_INCLUDED
#define SALVO_GRADE_INCLUDED

// The post-processing color grade, applied at the end of every Salvo shader instead of as a
// full-screen pass (which cost ~7 ms at 1080p on integrated GPUs). ShaderGrade.cs bakes URP's grade
// (exposure, white balance, contrast, lift/gamma/gain, saturation, neutral tonemap) into a 32^3 LUT;
// here it is one 3D texture fetch, plus the vignette and the HUD's low-health desaturation.

float  _SalvoGrade;          // 1 = grade here (post-processing off)
float4 _SalvoLutParams;      // x 1/range, y (size-1)/size, z 0.5/size
float4 _SalvoVignette;       // x intensity*3, y smoothness*5, z roundness
float3 _SalvoVignetteColor;
float  _SalvoSaturation;     // 1 = as graded, <1 drains color (low health)
TEXTURE3D(_SalvoLut); SAMPLER(sampler_SalvoLut);
float4 _SalvoScope;          // x 1 = sniper scope up, y radius (fraction of screen height), z edge AA width

// Sniper scope eyepiece: 1 inside the lens (with a darkened rim), 0 in the black surround. The
// shaders bail out early where it's 0, so looking through the scope costs less than not.
float SalvoScope(float4 positionCS)
{
    float vis = 1.0;
    UNITY_BRANCH
    if (_SalvoScope.x > 0.5)
    {
        float2 uv = GetNormalizedScreenSpaceUV(positionCS) - 0.5;
        uv.x *= _ScreenParams.x / _ScreenParams.y;
        float r = length(uv) / _SalvoScope.y;
        float lens = 1.0 - smoothstep(1.0 - _SalvoScope.z, 1.0, r);
        float rim = lerp(0.35, 1.0, smoothstep(1.0, 0.86, r));   // the lens edge falls off into shadow
        vis = lens * rim;
    }
    return vis;
}

float3 SalvoGrade(float3 c, float4 positionCS)
{
    float3 o = c;
    UNITY_BRANCH
    if (_SalvoGrade > 0.5)
    {
        float2 uv = GetNormalizedScreenSpaceUV(positionCS);
        float2 dist = abs(uv - 0.5) * _SalvoVignette.x;
        dist.x *= _SalvoVignette.z;
        float vfactor = pow(saturate(1.0 - dot(dist, dist)), _SalvoVignette.y);
        o *= lerp(_SalvoVignetteColor, 1.0, vfactor);

        float3 coord = sqrt(saturate(o * _SalvoLutParams.x)) * _SalvoLutParams.y + _SalvoLutParams.z;
        o = SAMPLE_TEXTURE3D_LOD(_SalvoLut, sampler_SalvoLut, coord, 0).rgb;
        float luma = dot(o, float3(0.2126729, 0.7151522, 0.0721750));
        o = lerp(luma.xxx, o, _SalvoSaturation);
    }
    return o;
}

#endif
