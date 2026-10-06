using UnityEngine;
using UnityEngine.UIElements;

namespace Salvo
{
    /// <summary>Central content registry, loaded once from Resources/SalvoConfig.</summary>
    [CreateAssetMenu(menuName = "SALVO/Game Config", fileName = "SalvoConfig")]
    public class GameConfig : ScriptableObject
    {
        static GameConfig instance;
        public static GameConfig I
        {
            get
            {
                if (instance == null) instance = Resources.Load<GameConfig>("SalvoConfig");
                return instance;
            }
        }

        [Header("Content")]
        public WeaponDef[] weapons;
        public SkinDef[] skins;
        public MapDef[] maps;

        [Header("Prefabs & Models")]
        public GameObject combatantPrefab;
        public GameObject trooperModel;
        public GameObject fpArmsModel;

        [Header("Materials")]
        public Material worldMaterial;
        public Material characterMaterial;
        public Material weaponMaterial;
        public Material skinTemplate;
        public Material glassMaterial;
        public Material teamRed;
        public Material teamBlue;
        public Material fxAlpha;
        public Material fxDecal;
        public Material fxTracer;
        public Material fxFlash;
        public Texture2D puffTexture;
        public Texture2D sparkTexture;
        public Color[] playerColors;

        [Header("Rendering")]
        public UnityEngine.Rendering.Universal.UniversalRendererData rendererData;

        [Header("UI")]
        public PanelSettings panelSettings;
        public VisualTreeAsset uiLayout;
        public StyleSheet uiStyle;
        public Texture2D scopeOverlay;
        public Texture2D logo;

        [Header("Audio")]
        public SoundBank sounds;

        [Header("Bots")]
        public string[] botNames;

        public WeaponDef Weapon(int id)
        {
            if (weapons == null) return null;
            for (int i = 0; i < weapons.Length; i++) if (weapons[i] != null && weapons[i].id == id) return weapons[i];
            return weapons.Length > 0 ? weapons[0] : null;
        }

        public SkinDef Skin(int id)
        {
            if (skins == null) return null;
            for (int i = 0; i < skins.Length; i++) if (skins[i] != null && skins[i].id == id) return skins[i];
            return skins.Length > 0 ? skins[0] : null;
        }

        public Color PlayerColor(int index)
        {
            if (playerColors == null || playerColors.Length == 0) return Color.white;
            return playerColors[Mathf.Abs(index) % playerColors.Length];
        }

        Material[] ffaMats;
        public Material TeamMaterial(Team team, int colorIndex)
        {
            if (team == Team.Red) return teamRed;
            if (team == Team.Blue) return teamBlue;
            if (ffaMats == null || ffaMats.Length != playerColors.Length)
            {
                ffaMats = new Material[playerColors.Length];
                for (int i = 0; i < ffaMats.Length; i++)
                {
                    ffaMats[i] = new Material(teamRed) { name = "FFA_" + i };
                    ffaMats[i].SetColor("_BaseColor", playerColors[i]);
                }
            }
            return ffaMats[Mathf.Abs(colorIndex) % ffaMats.Length];
        }

        public static Color TeamColor(Team team, int colorIndex)
        {
            switch (team)
            {
                case Team.Red: return SalvoMath.Hex("E0614F");
                case Team.Blue: return SalvoMath.Hex("4A8FD6");
                default: return I.PlayerColor(colorIndex);
            }
        }
    }
}
