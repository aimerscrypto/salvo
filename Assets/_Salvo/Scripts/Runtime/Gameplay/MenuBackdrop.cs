using Unity.Netcode;
using UnityEngine;

namespace Salvo
{
    /// <summary>
    /// When a map is loaded outside a match it becomes the main-menu backdrop: a slow cinematic
    /// fly-through, plus a showcase trooper (with the selected weapon + skin) for loadout and shop.
    /// </summary>
    public class MenuBackdrop : MonoBehaviour
    {
        public static MenuBackdrop I { get; private set; }

        bool active;
        float pathT;
        bool showcase;
        Transform trooper, weaponHolder;
        GameObject weaponInst;
        int shownWeapon = -1, shownSkin = -1, shownColor = -1;
        Transform upR, upL, loR, loL, torso, head;
        TrooperHands hands;
        Quaternion upRBase, upLBase, loRBase, loLBase, torsoBase, headBase;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void Start()
        {
            var nm = NetworkManager.Singleton;
            active = nm == null || !nm.IsListening;
            if (!active) { enabled = false; return; }
            GameCamera.I?.EnterMenu();
            BuildTrooper();
            AudioManager.Music(AudioManager.Bank?.menuMusic, 0.5f);
        }

        public static void SetShowcase(bool on)
        {
            if (I != null) I.showcase = on;
        }

        void BuildTrooper()
        {
            var map = MapInfo.I;
            if (map == null || map.showcaseSpot == null) return;
            var cfg = GameConfig.I;
            var root = new GameObject("Showcase").transform;
            root.SetPositionAndRotation(map.showcaseSpot.position, map.showcaseSpot.rotation);
            var wrap = new GameObject("Wrap").transform;
            wrap.SetParent(root, false);
            trooper = Instantiate(cfg.trooperModel, wrap).transform;
            upR = trooper.FindDeep("UpperArm_R"); upL = trooper.FindDeep("UpperArm_L");
            loR = trooper.FindDeep("LowerArm_R"); loL = trooper.FindDeep("LowerArm_L");
            hands = new TrooperHands(trooper, loR, loL);
            torso = trooper.FindDeep("Torso"); head = trooper.FindDeep("Head");
            upRBase = upR.localRotation; upLBase = upL.localRotation; loRBase = loR.localRotation; loLBase = loL.localRotation;
            torsoBase = torso.localRotation; headBase = head.localRotation;
            weaponHolder = new GameObject("Weapon").transform;
            weaponHolder.SetParent(root, false);
            weaponHolder.localPosition = new Vector3(0.05f, 1.2f, 0.32f);
            weaponHolder.localRotation = Quaternion.Euler(8f, -25f, -6f);
            trooper = root;
        }

        void RefreshShowcase()
        {
            if (trooper == null) return;
            var cfg = GameConfig.I;
            int wid = MenuState.PreviewWeapon >= 0 ? MenuState.PreviewWeapon : Profile.Data.primary;
            int sid = MenuState.PreviewSkin >= 0 ? MenuState.PreviewSkin : Profile.EquippedSkin(wid);
            int col = Profile.Data.colorIndex;
            if (wid != shownWeapon || sid != shownSkin)
            {
                if (weaponInst != null) Destroy(weaponInst);
                var def = cfg.Weapon(wid);
                if (def != null && def.model != null)
                {
                    var wrap = new GameObject("Wrap").transform;
                    wrap.SetParent(weaponHolder, false);
                    if (def.fireMode == FireMode.Melee) wrap.localRotation = Quaternion.Euler(-55f, 0f, 0f);
                    var inst = Instantiate(def.model, wrap).transform;
                    CharacterVisual.ApplySkin(inst, def, sid);
                    var g = inst.FindDeep("GripR");
                    if (g != null) wrap.localPosition = -weaponHolder.InverseTransformVector(g.position - wrap.position);
                    weaponInst = wrap.gameObject;
                }
                shownWeapon = wid; shownSkin = sid;
            }
            if (col != shownColor)
            {
                var mat = cfg.TeamMaterial(Team.None, col);
                foreach (var r in trooper.GetComponentsInChildren<Renderer>(true)) // incl. hidden hand variants
                {
                    if (weaponInst != null && r.transform.IsChildOf(weaponInst.transform)) continue;
                    var mats = r.sharedMaterials;
                    bool ch = false;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] == cfg.teamRed || (mats[i] != null && mats[i].name.StartsWith("FFA_"))) { mats[i] = mat; ch = true; }
                    if (ch) r.sharedMaterials = mats;
                }
                shownColor = col;
            }
        }

        void LateUpdate()
        {
            if (!active) return;
            var cam = GameCamera.I;
            var map = MapInfo.I;
            if (cam == null || map == null) return;
            RefreshShowcase();
            AnimateTrooper();

            if (showcase && map.showcaseCamera != null)
            {
                cam.SetMenuPose(map.showcaseCamera.position, map.showcaseCamera.rotation, 34f);
                return;
            }
            var path = map.menuCameraPath;
            if (path == null || path.Length < 2) return;
            pathT += Time.deltaTime * 0.035f;
            float f = pathT % path.Length;
            int i = Mathf.FloorToInt(f);
            float t = Mathf.SmoothStep(0, 1, f - i);
            var a = path[i]; var b = path[(i + 1) % path.Length];
            Vector3 pos = Vector3.Lerp(a.position, b.position, t);
            Quaternion rot = Quaternion.Slerp(a.rotation, b.rotation, t);
            cam.SetMenuPose(pos, rot, 52f);
        }

        void AnimateTrooper()
        {
            if (upR == null || weaponInst == null) return;
            float breathe = Mathf.Sin(Time.time * 1.6f);
            torso.localRotation = torsoBase * Quaternion.Euler(breathe * 1.2f, 0, 0);
            head.localRotation = headBase * Quaternion.Euler(breathe * -0.8f, Mathf.Sin(Time.time * 0.4f) * 6f, 0);
            weaponHolder.localPosition = new Vector3(0.05f, 1.2f + breathe * 0.006f, 0.32f);
            // posed hands snap onto the showcase weapon, arms IK to their wrists
            var def = GameConfig.I.Weapon(shownWeapon);
            var g = weaponInst.transform.FindDeep("GripR");
            var gl = weaponInst.transform.FindDeep("GripL");
            upR.localRotation = upRBase; loR.localRotation = loRBase; upL.localRotation = upLBase; loL.localRotation = loLBase;
            hands.Use(def);
            if (hands.SnapRight(g, out var wR)) Aim(upR, loR, hands.WristR, wR, trooper.position + trooper.right * 0.6f + Vector3.down * 0.3f);
            else hands.RestRight();
            if (hands.SnapLeft(TrooperHands.SupportMarker(def, g, gl), out var wL)) Aim(upL, loL, hands.WristL, wL, trooper.position - trooper.right * 0.6f + Vector3.down * 0.3f);
            else hands.RestLeft();
        }

        static void Aim(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 pole)
        {
            float a = Vector3.Distance(upper.position, lower.position);
            float b = Vector3.Distance(lower.position, hand.position);
            Vector3 s = upper.position;
            Vector3 toT = target - s;
            float d = Mathf.Clamp(toT.magnitude, 0.05f, (a + b) * 0.999f);
            Vector3 dir = toT.normalized;
            float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            Vector3 axis = Vector3.Cross(dir, pole - s).normalized;
            Vector3 perp = Vector3.Cross(axis, dir).normalized;
            Vector3 elbow = s + dir * (a * cosA) + perp * (a * Mathf.Sqrt(1f - cosA * cosA));
            upper.rotation = Quaternion.FromToRotation(lower.position - s, elbow - s) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, target - lower.position) * lower.rotation;
        }
    }

    /// <summary>What the menu currently wants to preview on the showcase trooper.</summary>
    public static class MenuState
    {
        public static int PreviewWeapon = -1;
        public static int PreviewSkin = -1;
    }
}
