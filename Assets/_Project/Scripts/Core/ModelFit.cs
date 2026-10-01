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
            if (def.trimMeshes != null && def.trimMeshes.Length > 0 && def.keepBones != null)
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                    if (System.Array.IndexOf(def.trimMeshes, smr.name) >= 0) TrimToBones(smr, def.keepBones);
            if (def.attachments != null)
                foreach (var a in def.attachments) Attach(go.transform, a);

            float yaw = def.yawOffset;
            if (def.faceHeadForward) yaw += HeadYaw(go.transform);
            else yaw += HumanoidYaw(go); // a humanoid rig knows its own left and right: face its chest forward
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            var b = MeasureLocal(go, parent);
            float s = b.size.y > 1e-4f ? def.height / b.size.y : 1f;
            go.transform.localScale = Vector3.one * s;
            go.transform.localPosition = new Vector3(-b.center.x * s, -b.min.y * s, -b.center.z * s);
            return go;
        }

        // For a humanoid rig: the turn that makes its chest face +Z, found from its shoulders (forward = right x up).
        // 0 for anything else. Character packs differ in which way their prefabs face, so this beats a fixed offset.
        private static float HumanoidYaw(GameObject go)
        {
            var anim = go.GetComponentInChildren<Animator>();
            if (anim == null || !anim.isHuman) return 0f;
            var l = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var r = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (l == null || r == null) return 0f;
            Vector3 right = go.transform.InverseTransformPoint(r.position) - go.transform.InverseTransformPoint(l.position);
            right.y = 0f;
            if (right.sqrMagnitude < 1e-6f) return 0f;
            Vector3 fwd = Vector3.Cross(right, Vector3.up);
            return -Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        }

        // Puts every skinned mesh of `attachment` onto `model`'s skeleton, matching bones by name.
        // Works for pieces exported on the same rig (hair, beards, outfit parts).
        public static void Attach(Transform model, GameObject attachment)
        {
            if (attachment == null) return;
            var bones = new Dictionary<string, Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>())
                if (!bones.ContainsKey(t.name)) bones[t.name] = t;

            var inst = Object.Instantiate(attachment);
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var src = smr.bones;
                var mapped = new Transform[src.Length];
                for (int i = 0; i < src.Length; i++)
                    mapped[i] = src[i] != null && bones.TryGetValue(src[i].name, out var b) ? b : null;
                smr.bones = mapped;
                if (smr.rootBone != null && bones.TryGetValue(smr.rootBone.name, out var root)) smr.rootBone = root;
                smr.transform.SetParent(model, false); // bones drive the vertices; this only keeps it in the hierarchy
            }
            if (Application.isPlaying) Object.Destroy(inst); else Object.DestroyImmediate(inst);
        }

        private static readonly Dictionary<Mesh, Mesh> TrimCache = new Dictionary<Mesh, Mesh>();

        // Keeps only the triangles mostly skinned to the given bones (list every bone you want). Used to show just the head of a full-body character under an outfit, which
        // otherwise clips through the clothes. Needs the mesh imported as readable.
        public static void TrimToBones(SkinnedMeshRenderer smr, string[] keepBones)
        {
            var src = smr.sharedMesh;
            if (src == null) return;
            if (TrimCache.TryGetValue(src, out var cached)) { smr.sharedMesh = cached; return; }
            if (!src.isReadable) { Debug.LogWarning($"[ModelFit] {src.name} is not readable; can't trim it."); return; }

            var keep = new bool[smr.bones.Length];
            for (int i = 0; i < keep.Length; i++)
                keep[i] = smr.bones[i] != null &&
                          System.Array.Exists(keepBones, n => string.Equals(n, smr.bones[i].name, System.StringComparison.OrdinalIgnoreCase));

            var weights = src.boneWeights;
            var keepVertex = new bool[weights.Length];
            for (int v = 0; v < weights.Length; v++)
            {
                var w = weights[v];
                float kept = (keep[w.boneIndex0] ? w.weight0 : 0f) + (keep[w.boneIndex1] ? w.weight1 : 0f) +
                             (keep[w.boneIndex2] ? w.weight2 : 0f) + (keep[w.boneIndex3] ? w.weight3 : 0f);
                keepVertex[v] = kept >= 0.5f;
            }

            var mesh = Object.Instantiate(src);
            mesh.name = src.name + "_Trimmed";
            var tris = new List<int>();
            for (int sub = 0; sub < src.subMeshCount; sub++)
            {
                tris.Clear();
                var t = src.GetTriangles(sub);
                for (int i = 0; i + 2 < t.Length; i += 3)
                    if (keepVertex[t[i]] && keepVertex[t[i + 1]] && keepVertex[t[i + 2]])
                    { tris.Add(t[i]); tris.Add(t[i + 1]); tris.Add(t[i + 2]); }
                mesh.SetTriangles(tris, sub);
            }
            TrimCache[src] = mesh;
            smr.sharedMesh = mesh;
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
