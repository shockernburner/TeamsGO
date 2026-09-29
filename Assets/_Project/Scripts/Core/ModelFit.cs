using System.Collections.Generic;
using UnityEngine;

namespace ProjectFossil.Core
{
    // Places imported models at a known size, whatever units they were exported in: feet on the parent's
    // origin, centred, and scaled to a target height. Presentation only.
    public static class ModelFit
    {
        private static readonly Dictionary<GameObject, Bounds> BoundsCache = new Dictionary<GameObject, Bounds>();

        // Instantiates `def.model` under `parent`, fitted to def.height (local units of the parent).
        public static GameObject Spawn(ModelDefinition def, Transform parent)
        {
            if (def == null || def.model == null) return null;
            var go = Object.Instantiate(def.model, parent, false);
            go.name = def.model.name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale    = Vector3.one;

            float yaw = def.yawOffset;
            if (def.faceHeadForward) yaw += HeadYaw(go.transform);
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            var b = MeasureLocal(go, parent);
            float s = b.size.y > 1e-4f ? def.height / b.size.y : 1f;
            go.transform.localScale = Vector3.one * s;
            go.transform.localPosition = new Vector3(-b.center.x * s, -b.min.y * s, -b.center.z * s);
            return go;
        }

        // Bounds of a prefab at scale 1 with its own rotation, measured once per prefab and cached.
        public static Bounds PrefabBounds(GameObject prefab)
        {
            if (prefab == null) return new Bounds(Vector3.zero, Vector3.one);
            if (BoundsCache.TryGetValue(prefab, out var b)) return b;
            var probe = Object.Instantiate(prefab);
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            probe.transform.localScale = Vector3.one;
            b = MeasureLocal(probe, null);
            if (Application.isPlaying) Object.Destroy(probe); else Object.DestroyImmediate(probe);
            BoundsCache[prefab] = b;
            return b;
        }

        // Renderer bounds of `go` expressed in `space` (world space when null).
        public static Bounds MeasureLocal(GameObject go, Transform space)
        {
            bool any = false;
            var result = new Bounds();
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                Bounds wb = r is SkinnedMeshRenderer smr && smr.sharedMesh != null ? SkinnedWorldBounds(smr) : r.bounds;
                var c = wb.center; var e = wb.extents;
                for (int i = 0; i < 8; i++)
                {
                    var p = c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    if (space != null) p = space.InverseTransformPoint(p);
                    if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                    else result.Encapsulate(p);
                }
            }
            return any ? result : new Bounds(Vector3.zero, Vector3.one);
        }

        // A skinned renderer's own bounds can be stale before its first frame; the mesh's bind pose is reliable.
        private static Bounds SkinnedWorldBounds(SkinnedMeshRenderer smr)
        {
            var mb = smr.sharedMesh.bounds;
            var t  = smr.transform;
            var wb = new Bounds(t.TransformPoint(mb.center), Vector3.zero);
            var e  = mb.extents;
            for (int i = 0; i < 8; i++)
                wb.Encapsulate(t.TransformPoint(mb.center + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return wb;
        }

        // Degrees to turn the model so its head bone points along +Z. Zero when there is no head bone.
        private static float HeadYaw(Transform root)
        {
            Transform head = null;
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (t.name.ToLowerInvariant().Contains("head")) { head = t; break; }
            if (head == null) return 0f;

            var b = MeasureLocal(root.gameObject, root);
            Vector3 d = root.InverseTransformPoint(head.position) - b.center;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) return 0f;
            // Snap to the nearest quarter turn: exports are off by whole axes, never by odd angles.
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            return -Mathf.Round(yaw / 90f) * 90f;
        }
    }
}
