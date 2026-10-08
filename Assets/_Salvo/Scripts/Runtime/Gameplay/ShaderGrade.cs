using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Salvo
{
    /// <summary>
    /// Runs the scene's color grade inside the Salvo shaders instead of URP's post-processing pass,
    /// which cost ~7 ms per frame at 1080p on integrated GPUs (HDR target, LUT bake, full-screen uber
    /// pass). The grade (exposure, white balance, contrast, lift/gamma/gain, saturation, neutral
    /// tonemap - URP's own math) is baked on the CPU into a small 3D LUT that every shader reads with
    /// one texture fetch; evaluating it per pixel instead still cost ~4 ms. The vignette and the
    /// HUD's damage effects (Hurt / Desaturate) are cheap shader uniforms.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class ShaderGrade : MonoBehaviour
    {
        /// <summary>0..1 red damage vignette, set by the HUD.</summary>
        public static float Hurt;
        /// <summary>0..1 low-health desaturation, set by the HUD.</summary>
        public static float Desaturate;
        /// <summary>Sniper scope up (the HUD): the shaders draw the black eyepiece around the lens.</summary>
        public static bool Scope;
        public const float ScopeRadius = 0.475f;   // fraction of screen height; ScopeView draws the reticle to match
        /// <summary>Profiling: skip the grade entirely (dev -salvo-gfx grade=0).</summary>
        public static bool DevDisabled;

        const int LutSize = 32;
        const float LutRange = 2f;   // linear input covered by the LUT (indexed by sqrt(c / range))

        static readonly int Grade = Shader.PropertyToID("_SalvoGrade"), Lut = Shader.PropertyToID("_SalvoLut"),
            LutParams = Shader.PropertyToID("_SalvoLutParams"), Vig = Shader.PropertyToID("_SalvoVignette"),
            VigColor = Shader.PropertyToID("_SalvoVignetteColor"), Sat = Shader.PropertyToID("_SalvoSaturation"),
            ScopeId = Shader.PropertyToID("_SalvoScope");

        UniversalAdditionalCameraData camData;
        Volume volume;
        Texture3D lut;
        int lutHash;

        void Awake()
        {
            camData = GetComponent<UniversalAdditionalCameraData>();
            // the shaders always declare the LUT sampler: WebGL refuses to draw while a 3D slot holds
            // Unity's default 2D texture, so bind a tiny 3D one until the real LUT is baked
            if (placeholder == null)
                placeholder = new Texture3D(2, 2, 2, TextureFormat.RGBAHalf, false) { name = "SalvoLutPlaceholder" };
            Shader.SetGlobalTexture(Lut, placeholder);
        }

        static Texture3D placeholder;

        void OnDisable() { Shader.SetGlobalFloat(Grade, 0f); Shader.SetGlobalVector(ScopeId, Vector4.zero); }

        void OnDestroy() { if (lut != null) Destroy(lut); }

        void LateUpdate()
        {
            if (camData != null && camData.renderPostProcessing) camData.renderPostProcessing = false;
            Shader.SetGlobalVector(ScopeId, new Vector4(Scope ? 1f : 0f, ScopeRadius, 2.5f / Mathf.Max(1f, Screen.height * ScopeRadius), 0f));
            if (volume == null || !volume.isActiveAndEnabled) volume = FindVolume();
            var p = volume != null ? volume.sharedProfile : null;
            if (p == null || DevDisabled) { Shader.SetGlobalFloat(Grade, 0f); return; }

            p.TryGet(out ColorAdjustments ca);
            p.TryGet(out WhiteBalance wb);
            p.TryGet(out LiftGammaGain lgg);
            p.TryGet(out Vignette vg);
            var g = new Params
            {
                exposure = Mathf.Pow(2f, On(ca) ? ca.postExposure.value : 0f),
                contrast = 1f + (On(ca) ? ca.contrast.value : 0f) / 100f,
                saturation = 1f + (On(ca) ? ca.saturation.value : 0f) / 100f,
                balance = On(wb) ? ColorUtils.ColorBalanceToLMSCoeffs(wb.temperature.value, wb.tint.value) : Vector3.one,
            };
            (g.lift, g.gamma, g.gain) = On(lgg) ? ColorUtils.PrepareLiftGammaGain(lgg.lift.value, lgg.gamma.value, lgg.gain.value) : (Vector4.zero, Vector4.one, Vector4.one);
            int hash = g.GetHashCode();
            if (lut == null || hash != lutHash) { BakeLut(g); lutHash = hash; }

            Shader.SetGlobalFloat(Grade, 1f);
            Shader.SetGlobalTexture(Lut, lut);
            Shader.SetGlobalVector(LutParams, new Vector4(1f / LutRange, (LutSize - 1f) / LutSize, 0.5f / LutSize, 0f));
            // low health drains the color (the HUD used to animate the post saturation 8 -> -45)
            Shader.SetGlobalFloat(Sat, Mathf.Lerp(1f, 0.55f / 1.08f, Desaturate));
            float vInt = On(vg) ? vg.intensity.value : 0f, vSmooth = On(vg) ? vg.smoothness.value : 0.2f;
            Color vCol = On(vg) ? vg.color.value : Color.black;
            vInt = Mathf.Lerp(vInt, 0.48f, Hurt);
            vCol = Color.Lerp(vCol, new Color(0.55f, 0.08f, 0.05f), Hurt);
            float aspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 1.78f;
            Shader.SetGlobalVector(Vig, new Vector4(vInt * 3f, vSmooth * 5f, On(vg) && vg.rounded.value ? aspect : 1f, 0f));
            Shader.SetGlobalVector(VigColor, vCol);
        }

        struct Params
        {
            public float exposure, contrast, saturation;
            public Vector3 balance;
            public Vector4 lift, gamma, gain;
            public override int GetHashCode() => (exposure, contrast, saturation, balance, lift, gamma, gain).GetHashCode();
        }

        // ------------------------------------------------------------------ the grade, as URP does it
        // (LutBuilderHdr.shader with the neutral tonemapper; constants from core Color.hlsl)
        void BakeLut(Params g)
        {
            if (lut == null)
                lut = new Texture3D(LutSize, LutSize, LutSize, TextureFormat.RGBAHalf, false)
                { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "SalvoGradeLut" };
            var px = new Color[LutSize * LutSize * LutSize];
            for (int b = 0; b < LutSize; b++)
                for (int gg = 0; gg < LutSize; gg++)
                    for (int r = 0; r < LutSize; r++)
                    {
                        // texel -> linear input: the LUT is indexed by sqrt(c / range)
                        Vector3 c = new Vector3(Decode(r), Decode(gg), Decode(b));
                        c = GradeColor(c, g);
                        px[r + gg * LutSize + b * LutSize * LutSize] = new Color(c.x, c.y, c.z, 1f);
                    }
            lut.SetPixels(px);
            lut.Apply(false);
        }

        static float Decode(int i) { float u = i / (LutSize - 1f); return u * u * LutRange; }

        static Vector3 GradeColor(Vector3 c, Params g)
        {
            c *= g.exposure;
            c = LmsToLinear(Mul(LinearToLms(c), g.balance));
            c = new Vector3(Contrast(c.x, g.contrast), Contrast(c.y, g.contrast), Contrast(c.z, g.contrast));
            c = new Vector3(Mathf.Max(0f, c.x), Mathf.Max(0f, c.y), Mathf.Max(0f, c.z));
            c = new Vector3(c.x * g.gain.x + g.lift.x, c.y * g.gain.y + g.lift.y, c.z * g.gain.z + g.lift.z);
            c = new Vector3(SignPow(c.x, g.gamma.x), SignPow(c.y, g.gamma.y), SignPow(c.z, g.gamma.z));
            float luma = Vector3.Dot(c, new Vector3(0.2126729f, 0.7151522f, 0.0721750f));
            c = new Vector3(luma + g.saturation * (c.x - luma), luma + g.saturation * (c.y - luma), luma + g.saturation * (c.z - luma));
            return new Vector3(Neutral(c.x), Neutral(c.y), Neutral(c.z));
        }

        const float MidGray = 0.4135884f;
        const float LcA = 5.555556f, LcB = 0.047996f, LcC = 0.244161f, LcD = 0.386036f;
        static float Contrast(float x, float k)
        {
            float lg = LcC * Mathf.Log10(Mathf.Max(LcA * x + LcB, 0f)) + LcD;   // LinearToLogC
            lg = (lg - MidGray) * k + MidGray;
            return (Mathf.Pow(10f, (lg - LcD) / LcC) - LcB) / LcA;               // LogCToLinear
        }

        static float SignPow(float x, float p) => Mathf.Sign(x) * Mathf.Pow(Mathf.Abs(x), p);

        static float Neutral(float x)
        {
            x = Mathf.Max(0f, x);
            const float a = 0.2f, b = 0.29f, c = 0.24f, d = 0.272f, e = 0.02f, f = 0.3f, whiteLevel = 5.3f;
            float Curve(float v) => ((v * (a * v + c * b) + d * e) / (v * (a * v + b) + d * f)) - e / f;
            float whiteScale = 1f / Curve(whiteLevel);
            return Curve(x * whiteScale) * whiteScale;
        }

        static Vector3 Mul(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        static Vector3 LinearToLms(Vector3 x) => new Vector3(
            3.90405e-1f * x.x + 5.49941e-1f * x.y + 8.92632e-3f * x.z,
            7.08416e-2f * x.x + 9.63172e-1f * x.y + 1.35775e-3f * x.z,
            2.31082e-2f * x.x + 1.28021e-1f * x.y + 9.36245e-1f * x.z);
        static Vector3 LmsToLinear(Vector3 x) => new Vector3(
            2.85847e+0f * x.x - 1.62879e+0f * x.y - 2.48910e-2f * x.z,
            -2.10182e-1f * x.x + 1.15820e+0f * x.y + 3.24281e-4f * x.z,
            -4.18120e-2f * x.x - 1.18169e-1f * x.y + 1.06867e+0f * x.z);

        static bool On(VolumeComponent c) => c != null && c.active;

        static Volume FindVolume()
        {
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
                if (v.isGlobal && v.isActiveAndEnabled) return v;
            return null;
        }
    }
}
