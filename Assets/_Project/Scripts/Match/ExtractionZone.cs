using UnityEngine;

namespace ProjectFossil.Match
{
    // Placeholder extraction beacon: a flat disc plus a tall pillar, red while closed, green when open.
    public class ExtractionZone : MonoBehaviour
    {
        public float radius = 8f;

        private static readonly Color ClosedColor = new Color(0.8f, 0.2f, 0.15f);
        private static readonly Color OpenColor   = new Color(0.2f, 0.9f, 0.3f);

        private Renderer[] _renderers;
        private bool? _open;

        public static ExtractionZone Create(Vector3 position, float radius, Transform parent)
        {
            var root = new GameObject("ExtractionZone");
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            Object.Destroy(disc.GetComponent<Collider>());
            disc.transform.SetParent(root.transform, false);
            disc.transform.localScale = new Vector3(radius * 2f, 0.05f, radius * 2f);

            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = "Beacon";
            Object.Destroy(pillar.GetComponent<Collider>());
            pillar.transform.SetParent(root.transform, false);
            pillar.transform.localPosition = Vector3.up * 20f;
            pillar.transform.localScale    = new Vector3(0.6f, 20f, 0.6f);

            var zone = root.AddComponent<ExtractionZone>();
            zone.radius = radius;
            zone.SetOpen(false);
            return zone;
        }

        public bool Contains(Vector3 position)
        {
            Vector3 d = position - transform.position;
            d.y = 0f;
            return d.sqrMagnitude <= radius * radius;
        }

        public void SetOpen(bool open)
        {
            if (_open == open) return;
            _open = open;
            if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>();
            foreach (var r in _renderers)
                r.material.color = open ? OpenColor : ClosedColor;
        }
    }
}
