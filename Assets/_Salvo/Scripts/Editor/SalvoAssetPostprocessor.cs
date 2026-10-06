using UnityEditor;
using UnityEngine;

namespace Salvo.EditorTools
{
    /// <summary>
    /// Enforces SALVO's import conventions so generated assets are always game-ready:
    /// Blender FBX -> axis baked, no materials/tangents/animation; textures -> correct color space,
    /// wrap and size. Runs automatically on (re)import.
    /// </summary>
    public class SalvoAssetPostprocessor : AssetPostprocessor
    {
        const string ModelRoot = "Assets/_Salvo/Art/Models/";
        const string TextureRoot = "Assets/_Salvo/Art/Textures/";
        const string IconRoot = "Assets/_Salvo/Art/Icons/";
        const string AudioRoot = "Assets/_Salvo/Audio/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelRoot)) return;
            var mi = (ModelImporter)assetImporter;
            mi.bakeAxisConversion = true;
            mi.useFileScale = true;
            mi.globalScale = 1f;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importBlendShapes = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None;
            mi.weldVertices = true;
            mi.optimizeMeshPolygons = true;
            mi.optimizeMeshVertices = true;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.preserveHierarchy = true;
            mi.sortHierarchyByName = false;
            mi.generateSecondaryUV = false;
            mi.addCollider = false;
            // Map geometry is read by NavMesh baking and MeshColliders at edit time only; keep it non-readable for runtime memory.
            mi.isReadable = assetPath.Contains("/Map/");
        }

        void OnPreprocessTexture()
        {
            var ti = (TextureImporter)assetImporter;
            if (assetPath.StartsWith(TextureRoot))
            {
                string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                bool data = file == "T_Detail" || file == "T_CloudNoise" || file == "T_GroundGrain";
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = !data;
                ti.alphaSource = data ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = !data;
                ti.wrapMode = data ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.mipmapEnabled = true;
                ti.filterMode = FilterMode.Bilinear;
                ti.anisoLevel = data ? 4 : 1;
                ti.maxTextureSize = 512;
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.compressionQuality = 80;
            }
            else if (assetPath.StartsWith(IconRoot) || assetPath.Contains("/UI/"))
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.maxTextureSize = 1024;
                ti.textureCompression = TextureImporterCompression.Compressed;
            }
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(AudioRoot)) return;
            var ai = (AudioImporter)assetImporter;
            bool music = assetPath.Contains("/Music/");
            ai.forceToMono = !music;
            ai.loadInBackground = music;
            var s = ai.defaultSampleSettings;
            s.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = music ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
            s.quality = music ? 0.55f : 1f;
            s.preloadAudioData = !music;
            s.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;
            ai.defaultSampleSettings = s;
        }
    }
}
