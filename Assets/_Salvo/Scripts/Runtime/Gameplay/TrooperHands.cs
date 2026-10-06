using UnityEngine;

namespace Salvo
{
    /// <summary>
    /// The trooper's posed hand variants (HandR_Grip / HandR_Fist, HandL_Guard / HandL_Vert / HandL_Pistol).
    /// Each variant's origin is its grip point and it is authored in the frame of the weapon marker it
    /// holds, so it snaps onto GripR / GripL with the marker's pose; the arm IK then only has to bring
    /// the forearm's wrist (Wrist_R / Wrist_L) to the hand's wrist point.
    /// </summary>
    public sealed class TrooperHands
    {
        sealed class Hand
        {
            public Transform t, wrist, lower;
            public Vector3 restPos;     // lower-arm space, used when the hand holds nothing
            public Quaternion restRot;
        }

        static readonly string[] NamesR = { "HandR_Grip", "HandR_Fist" };
        static readonly string[] NamesL = { "HandL_Guard", "HandL_Vert", "HandL_Pistol" };
        readonly Hand[] right = new Hand[NamesR.Length], left = new Hand[NamesL.Length];
        Hand curR, curL;

        public readonly Transform WristR, WristL;

        /// <summary>Detaches the hand variants from the forearms (re-parented to `model`) and hides them.</summary>
        public TrooperHands(Transform model, Transform lowerR, Transform lowerL)
        {
            WristR = model.FindDeep("Wrist_R");
            WristL = model.FindDeep("Wrist_L");
            for (int i = 0; i < NamesR.Length; i++) right[i] = Take(model, NamesR[i], lowerR);
            for (int i = 0; i < NamesL.Length; i++) left[i] = Take(model, NamesL[i], lowerL);
        }

        static Hand Take(Transform model, string name, Transform lower)
        {
            var t = model.FindDeep(name);
            if (t == null || lower == null) return null;
            var h = new Hand { t = t, wrist = t.FindDeep(name + "_W") ?? t, lower = lower };
            h.restPos = lower.InverseTransformPoint(t.position);
            h.restRot = Quaternion.Inverse(lower.rotation) * t.rotation;
            t.SetParent(model, true);
            t.gameObject.SetActive(false);
            return h;
        }

        /// <summary>Marker the support hand holds for this weapon (null = free hand).</summary>
        public static Transform SupportMarker(WeaponDef def, Transform gripR, Transform gripL)
        {
            var grip = def != null ? def.supportGrip : SupportGrip.Handguard;
            return grip == SupportGrip.Pistol ? gripR : grip == SupportGrip.None ? null : gripL;
        }

        /// <summary>Shows the hand variants that fit the weapon.</summary>
        public void Use(WeaponDef def)
        {
            bool melee = def != null && def.fireMode == FireMode.Melee;
            var grip = def != null ? def.supportGrip : SupportGrip.Handguard;
            curR = Show(right, right[melee ? 1 : 0] ?? right[0]);
            curL = Show(left, left[grip == SupportGrip.Pistol ? 2 : grip == SupportGrip.Handguard ? 0 : 1] ?? left[0]);
        }

        static Hand Show(Hand[] set, Hand want)
        {
            foreach (var h in set) if (h != null && h != want && h.t.gameObject.activeSelf) h.t.gameObject.SetActive(false);
            if (want != null && !want.t.gameObject.activeSelf) want.t.gameObject.SetActive(true);
            return want;
        }

        /// <summary>Puts the right hand on its marker; returns false (and leaves the hand alone) without one.</summary>
        public bool SnapRight(Transform marker, out Vector3 wrist) => Snap(curR, marker, out wrist);
        public bool SnapLeft(Transform marker, out Vector3 wrist) => Snap(curL, marker, out wrist);

        static bool Snap(Hand h, Transform marker, out Vector3 wrist)
        {
            wrist = default;
            if (h == null || marker == null) return false;
            h.t.SetPositionAndRotation(marker.position, marker.rotation);
            wrist = h.wrist.position;
            return true;
        }

        /// <summary>Hand that holds nothing: keep its rest pose relative to the (already solved) forearm.</summary>
        public void RestRight() => Rest(curR);
        public void RestLeft() => Rest(curL);

        static void Rest(Hand h)
        {
            if (h != null) h.t.SetPositionAndRotation(h.lower.TransformPoint(h.restPos), h.lower.rotation * h.restRot);
        }
    }
}
