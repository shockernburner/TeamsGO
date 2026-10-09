using UnityEditor;
using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Editor
{
    // Looks for the loot caches, from Quaternius' Ultimate Modular Ruins Pack (CC0, see Docs/ASSET_LICENSES.md):
    // a closed chest for supply caches, and for ruin stashes the same chest in front of a broken, overgrown arch
    // with a column, a fallen wall and broken pots. The prefab and both model definitions are committed, so this
    // only needs running again after changing the layout.
    public static class CacheModelSetup
    {
        private const string Ruins     = "Assets/_Project/Art/ThirdParty/Quaternius/Ruins";
        private const string ModelData = "Assets/_Project/Data/Models";
        private const string StashPath = "Assets/_Project/Prefabs/Props/RuinStash.prefab";

        [MenuItem("Project Fossil/Art/Build Cache Models")]
        public static void Run()
        {
            FixColours();
            var chest = Piece("Ruins_Chest");
            if (chest == null) { Debug.LogWarning($"[CacheModelSetup] Ruin pieces missing under {Ruins}."); return; }

            Assign("Model_SupplyCache", chest, 0.9f);
            var stash = BuildStash();
            // The stash's crate is 1.6 units wide; this makes the arch about 3.5 m tall around a full-size chest.
            if (stash != null) Assign("Model_RuinStash", stash, 2.2f);
            AssetDatabase.SaveAssets();
            Debug.Log("[CacheModelSetup] Supply cache: closed chest. Ruin stash: chest before a broken arch.");
        }

        private static GameObject BuildStash()
        {
            var root = new GameObject("RuinStash");
            try
            {
                // Stone first, chest last, laid out round the chest so the stash centres on it.
                Place(root, "Ruins_WallArchOvergrownBroken", new Vector3(0f, 0f, 1.5f),    0f,  solid: true);
                Place(root, "Ruins_ColumnShort",             new Vector3(-2.3f, 0f, 0.4f), 0f,  solid: true);
                Place(root, "Ruins_WallBroken",              new Vector3(2.4f, 0f, 0.9f), -25f, solid: true);
                Place(root, "Ruins_Pot2Broken",              new Vector3(1.4f, 0f, -0.7f), 30f, solid: false);
                Place(root, "Ruins_Pot3Broken",              new Vector3(1.9f, 0f, -0.1f), 75f, solid: false);
                Place(root, "Ruins_Chest",                   Vector3.zero,                 0f,  solid: false);

                root.AddComponent<GroundedParts>(); // each piece settles on the slope where it lands
                EnsureFolder("Assets/_Project/Prefabs/Props");
                return PrefabUtility.SaveAsPrefabAsset(root, StashPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        // Stonework gets a box so nobody walks through a wall; the chest and pots stay inside the stash's own crate.
        private static void Place(GameObject root, string name, Vector3 pos, float yaw, bool solid)
        {
            var model = Piece(name);
            if (model == null) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            if (!solid) return;
            var bounds = new Bounds();
            bool any = false;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                if (!any) { bounds = b; any = true; } else bounds.Encapsulate(b);
            }
            if (!any) return;
            var box = go.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size   = bounds.size;
        }

        private static void Assign(string defName, GameObject model, float height)
        {
            var def = AssetDatabase.LoadAssetAtPath<ModelDefinition>($"{ModelData}/{defName}.asset");
            if (def == null) { Debug.LogWarning($"[CacheModelSetup] No {defName}."); return; }
            def.model = model;
            def.animator = null;
            def.height = height;
            def.faceHeadForward = false;
            EditorUtility.SetDirty(def);
        }

        // The OBJ importer read each .mtl colour (Kd) as linear and showed it brightened: the taupe stone came out
        // near white, the brown chest and terracotta pots pale grey, and with the default gloss the stash looked
        // unpainted. Each material is replaced by one of our own with the pack's colour as authored, matte.
        [MenuItem("Project Fossil/Art/Fix Ruin Colours")]
        public static void FixColours()
        {
            string folder = Ruins + "/Materials";
            EnsureFolder(folder);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var mtl in System.IO.Directory.GetFiles(Ruins, "*.mtl"))
            {
                string obj = System.IO.Path.ChangeExtension(mtl, ".obj").Replace('\\', '/');
                var importer = AssetImporter.GetAtPath(obj) as ModelImporter;
                if (importer == null) continue;
                string name = null; Color kd = Color.white; string tex = null;
                void Flush()
                {
                    if (name == null) return;
                    string path = $"{folder}/{name}.mat";
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null) { mat = new Material(lit); AssetDatabase.CreateAsset(mat, path); }
                    mat.SetColor("_BaseColor", kd);
                    mat.SetFloat("_Smoothness", 0.12f);
                    if (tex != null) mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Ruins}/{tex}"));
                    EditorUtility.SetDirty(mat);
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), mat);
                }
                foreach (var raw in System.IO.File.ReadAllLines(mtl))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("newmtl ")) { Flush(); name = line.Substring(7).Trim(); kd = Color.white; tex = null; }
                    else if (line.StartsWith("Kd "))
                    {
                        var v = line.Substring(3).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                        var ci = System.Globalization.CultureInfo.InvariantCulture;
                        kd = new Color(float.Parse(v[0], ci), float.Parse(v[1], ci), float.Parse(v[2], ci));
                    }
                    else if (line.StartsWith("map_Kd ")) tex = line.Substring(7).Trim();
                }
                Flush();
                importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[CacheModelSetup] Ruin pieces use their authored colours (matte).");
        }

        private static GameObject Piece(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{Ruins}/{name}.obj");

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
