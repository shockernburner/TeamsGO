using UnityEngine;

namespace ProjectFossil.Core
{
    public static class Placeholder
    {
        // Removes every renderer under `root` (the capsule a model replaces). Disabling isn't enough online:
        // the network layer switches all of an object's renderers back on when the host starts seeing it,
        // which put a grey shell around every dinosaur. Colliders stay; gameplay uses them.
        public static void RemoveRenderers(Transform root)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = false;
                var filter = r.GetComponent<MeshFilter>();
                Object.Destroy(r);
                if (filter != null) Object.Destroy(filter);
            }
        }
    }
}
