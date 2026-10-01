using UnityEngine;

namespace ProjectFossil.Core
{
    public static class Placeholder
    {
        private const string LitPath = "Shaders/PlainLit";
        private static Material _lit;

        // A plain URP Lit material that ships with the game. In a build, CreatePrimitive gives the built-in
        // default material, which URP can't draw (magenta), and Shader.Find only works for shaders something in
        // the build already uses. Everything made from code starts from this instead.
        public static Material LitBase
        {
            get
            {
                if (_lit != null) return _lit;
                _lit = Resources.Load<Material>(LitPath);
                if (_lit == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit");
                    _lit = new Material(shader != null ? shader : Shader.Find("Standard"));
                }
                return _lit;
            }
        }

        // A new lit material in this colour.
        public static Material Lit(Color color)
        {
            var m = new Material(LitBase) { color = color };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            return m;
        }

        // GameObject.CreatePrimitive with a material that draws in builds.
        public static GameObject Primitive(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = LitBase;
            return go;
        }

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
