using UnityEngine;

namespace Salvo
{
    [CreateAssetMenu(menuName = "SALVO/Skin", fileName = "Skin")]
    public class SkinDef : ScriptableObject
    {
        public byte id;
        public string displayName = "Skin";
        public Rarity rarity;
        public int price;
        public int unlockLevel = 1;
        public Color baseColor = Color.gray;
        public Color patternColor = Color.black;
        [Range(0, 1)] public float patternStrength;
        public float patternScale = 6f;
        [Range(0, 1)] public float grain = 0.15f;
        public float highlight = 0.1f;

        [System.NonSerialized] Material cached;

        /// <summary>Shared runtime material for this skin (one per skin keeps the SRP batcher happy).</summary>
        public Material GetMaterial(Material template)
        {
            if (cached != null) return cached;
            cached = new Material(template) { name = "Skin_" + displayName };
            cached.SetColor("_BaseColor", baseColor);
            cached.SetColor("_PatternColor", patternColor);
            cached.SetFloat("_PatternStrength", patternStrength);
            cached.SetFloat("_PatternScale", patternScale);
            cached.SetFloat("_DetailStrength", grain);
            cached.SetFloat("_SpecStrength", highlight);
            cached.SetFloat("_ObjectSpace", 1f);
            cached.SetFloat("_DetailScale", 4f);
            return cached;
        }
    }
}
