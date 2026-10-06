#!/usr/bin/env bash
# Level-design check: for every "*Stairs" mesh in the running map, report where its top step leads
# (what you land on just past the top, and whether a wall blocks the way). Play mode, map loaded.
cd "$(dirname "$0")/.."
Tools/u.sh eval '
var sb = new System.Text.StringBuilder(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + "\n");
foreach (var mf in UnityEngine.Object.FindObjectsByType<UnityEngine.MeshCollider>(UnityEngine.FindObjectsSortMode.None))
{
    if (!mf.name.Contains("Stairs") || mf.sharedMesh == null) continue;
    var vs = mf.sharedMesh.vertices;
    float maxY = float.MinValue, minY = float.MaxValue;
    for (int i = 0; i < vs.Length; i++) { var w = mf.transform.TransformPoint(vs[i]); maxY = UnityEngine.Mathf.Max(maxY, w.y); minY = UnityEngine.Mathf.Min(minY, w.y); }
    UnityEngine.Vector3 top = UnityEngine.Vector3.zero, bot = UnityEngine.Vector3.zero; int nt = 0, nb = 0;
    for (int i = 0; i < vs.Length; i++) { var w = mf.transform.TransformPoint(vs[i]); if (w.y > maxY - 0.05f) { top += w; nt++; } if (w.y < minY + 0.3f) { bot += w; nb++; } }
    top /= nt; bot /= nb;
    var dir = top - bot; dir.y = 0; dir.Normalize();
    string land = "nothing";
    if (UnityEngine.Physics.Raycast(top + dir * 0.7f + UnityEngine.Vector3.up * 1.5f, UnityEngine.Vector3.down, out var h, 30f)) land = h.collider.name + " dy " + (h.point.y - maxY).ToString("F2");
    string ahead = UnityEngine.Physics.Raycast(top + UnityEngine.Vector3.up, dir, out var h2, 1.2f) ? "WALL " + h2.collider.name + " @" + h2.distance.ToString("F1") : "open";
    sb.Append(mf.name + " top " + top.ToString("F1") + " rise " + (maxY - minY).ToString("F1") + " -> " + land + " | " + ahead + "\n");
}
return sb.ToString();' | tail -40
