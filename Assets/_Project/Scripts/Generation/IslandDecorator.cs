using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectFossil.Generation
{
    // Presentation for a generated island: biome-coloured ground, sea, trees, rocks and haze.
    // Placeholder art made from primitives and flat colours; swap the Make* methods for real prefabs later.
    public static class IslandDecorator
    {
        private static readonly Color TrunkColor     = new Color(0.33f, 0.24f, 0.16f);
        private static readonly Color DeadTrunkColor = new Color(0.28f, 0.25f, 0.22f);
        private static readonly Color CliffColor     = new Color(0.42f, 0.4f, 0.37f);

        public static void Decorate(GameObject islandGO, IslandData data)
        {
            var terrain = islandGO.GetComponent<Terrain>();
            if (terrain != null) PaintTerrain(terrain.terrainData, data);

            AddWater(islandGO.transform, data);
            if (terrain != null) AddScatter(islandGO.transform, terrain, data, ScatterPlanner.Plan(data));
            if (Application.isPlaying) ApplyAtmosphere(); // don't rewrite the open scene's lighting from the editor tool
        }

        // ── Ground ─────────────────────────────────────────────────────────────

        // One layer per biome plus a cliff layer on steep ground. Neighbouring biomes blend over a few metres.
        public static void PaintTerrain(TerrainData td, IslandData data)
        {
            var biomes = data.Settings.biomes;
            int biomeCount = biomes != null ? biomes.Count : 0;
            if (biomeCount == 0) return;

            var layers = new TerrainLayer[biomeCount + 1];
            for (int i = 0; i < biomeCount; i++)
                layers[i] = MakeLayer(biomes[i] != null ? biomes[i].groundColor : Color.gray, 1000 + i);
            layers[biomeCount] = MakeLayer(CliffColor, 999);
            td.terrainLayers = layers;

            int aRes = Mathf.ClosestPowerOfTwo(Mathf.Clamp(data.Resolution - 1, 16, 1024));
            td.alphamapResolution = aRes;

            float world = data.Settings.worldSize;
            float step  = world / (data.Resolution - 1);
            var alpha   = new float[aRes, aRes, layers.Length];
            var weights = new float[layers.Length];

            for (int az = 0; az < aRes; az++)
            {
                for (int ax = 0; ax < aRes; ax++)
                {
                    float x = (ax + 0.5f) / aRes * world;
                    float z = (az + 0.5f) / aRes * world;

                    System.Array.Clear(weights, 0, weights.Length);
                    for (int oz = -1; oz <= 1; oz++)
                        for (int ox = -1; ox <= 1; ox++)
                            weights[ScatterPlanner.SampleBiome(data, x + ox * step * 1.5f, z + oz * step * 1.5f)] += 1f / 9f;

                    float cliff = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.1f, ScatterPlanner.SampleSlope(data, x, z)));
                    for (int l = 0; l < biomeCount; l++) alpha[az, ax, l] = weights[l] * (1f - cliff);
                    alpha[az, ax, biomeCount] = cliff;
                }
            }
            td.SetAlphamaps(0, 0, alpha);
        }

        // Small noisy texture so flat colour doesn't look like plastic. Alpha is smoothness in URP terrain,
        // so it stays low: a full alpha made the old ground mirror the sky and look like snow.
        private static TerrainLayer MakeLayer(Color baseColor, int seed)
        {
            const int size = 32;
            var rng = new System.Random(seed);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
            var px  = new Color[size * size];
            for (int i = 0; i < px.Length; i++)
            {
                float v = 0.88f + (float)rng.NextDouble() * 0.24f;
                px[i] = new Color(baseColor.r * v, baseColor.g * v, baseColor.b * v, 0.08f);
            }
            tex.SetPixels(px);
            tex.Apply(true);

            return new TerrainLayer
            {
                diffuseTexture = tex,
                tileSize       = new Vector2(6f, 6f),
                smoothness     = 0f,
                metallic       = 0f,
            };
        }

        // ── Water ──────────────────────────────────────────────────────────────

        private static void AddWater(Transform parent, IslandData data)
        {
            var s = data.Settings;
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane); // 10 x 10 units
            water.name = "Sea";
            Object.DestroyImmediate(water.GetComponent<Collider>()); // immediate: the NavMesh bakes this frame
            water.transform.SetParent(parent, false);
            water.transform.localPosition = new Vector3(s.worldSize * 0.5f, s.seaLevel * s.maxHeight, s.worldSize * 0.5f);
            water.transform.localScale    = new Vector3(s.worldSize * 0.4f, 1f, s.worldSize * 0.4f); // 4x the island

            var mat = NewLit(new Color(0.1f, 0.33f, 0.42f, 0.8f), 0.85f);
            MakeTransparent(mat);
            var r = water.GetComponent<Renderer>();
            r.sharedMaterial   = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ── Trees and rocks ────────────────────────────────────────────────────

        private static void AddScatter(Transform parent, Terrain terrain, IslandData data, List<ScatterInstance> plan)
        {
            var root = new GameObject("Scatter").transform;
            root.SetParent(parent, false);

            var biomes  = data.Settings.biomes;
            var trunk   = NewLit(TrunkColor, 0.05f);
            var deadTrk = NewLit(DeadTrunkColor, 0.05f);
            var foliage = new Dictionary<int, Material>();
            var rocks   = new Dictionary<int, Material>();

            foreach (var inst in plan)
            {
                var b = biomes[inst.BiomeIndex];
                Vector3 pos = inst.WorldPos;
                pos.y = terrain.SampleHeight(parent.TransformPoint(pos)) + terrain.transform.position.y;

                if (inst.Kind == ScatterKind.Tree)
                {
                    if (!foliage.TryGetValue(inst.BiomeIndex, out var leaf))
                        foliage[inst.BiomeIndex] = leaf = NewLit(b.foliageColor, 0.1f);
                    var go = b.treeShape == TreeShape.Dead ? MakeDeadTree(deadTrk)
                           : b.treeShape == TreeShape.Tall ? MakeTallTree(trunk, leaf)
                           : MakeRoundTree(trunk, leaf);
                    Place(go, root, pos, inst);
                }
                else
                {
                    if (!rocks.TryGetValue(inst.BiomeIndex, out var stone))
                        rocks[inst.BiomeIndex] = stone = NewLit(b.rockColor, 0.15f);
                    var go = MakeRock(stone, inst);
                    Place(go, root, pos + Vector3.down * 0.25f * inst.Scale, inst);
                }
            }
        }

        private static void Place(GameObject go, Transform root, Vector3 pos, ScatterInstance inst)
        {
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, inst.Yaw, 0f);
            go.transform.localScale    = Vector3.one * inst.Scale;
        }

        // Broadleaf: short trunk, two overlapping round crowns. About 7 m tall at scale 1.
        private static GameObject MakeRoundTree(Material trunk, Material leaf)
        {
            var tree = new GameObject("Tree");
            Part(tree, PrimitiveType.Cylinder, trunk, new Vector3(0f, 1.6f, 0f), new Vector3(0.45f, 1.6f, 0.45f), keepCollider: true);
            Part(tree, PrimitiveType.Sphere,   leaf,  new Vector3(0f, 4.6f, 0f), new Vector3(4.2f, 3.4f, 4.2f));
            Part(tree, PrimitiveType.Sphere,   leaf,  new Vector3(0.9f, 5.8f, -0.5f), new Vector3(2.6f, 2.2f, 2.6f));
            return tree;
        }

        // Tall and narrow, like a pine or palm silhouette.
        private static GameObject MakeTallTree(Material trunk, Material leaf)
        {
            var tree = new GameObject("Tree");
            Part(tree, PrimitiveType.Cylinder, trunk, new Vector3(0f, 2.6f, 0f), new Vector3(0.35f, 2.6f, 0.35f), keepCollider: true);
            Part(tree, PrimitiveType.Capsule,  leaf,  new Vector3(0f, 6.2f, 0f), new Vector3(2.2f, 2.4f, 2.2f));
            return tree;
        }

        // Bare trunk with one broken branch, for swamp and volcanic ground.
        private static GameObject MakeDeadTree(Material trunk)
        {
            var tree = new GameObject("DeadTree");
            Part(tree, PrimitiveType.Cylinder, trunk, new Vector3(0f, 2.4f, 0f), new Vector3(0.4f, 2.4f, 0.4f), keepCollider: true);
            var branch = Part(tree, PrimitiveType.Cylinder, trunk, new Vector3(0.7f, 3.6f, 0f), new Vector3(0.18f, 1f, 0.18f));
            branch.transform.localRotation = Quaternion.Euler(0f, 0f, -50f);
            return tree;
        }

        // Tilted, squashed cube: reads as a low-poly boulder.
        private static GameObject MakeRock(Material stone, ScatterInstance inst)
        {
            var rock = new GameObject("Rock");
            var body = Part(rock, PrimitiveType.Cube, stone, new Vector3(0f, 0.45f, 0f), new Vector3(1.5f, 0.9f, 1.2f), keepCollider: true);
            float tilt = (inst.Yaw * 7.3f) % 25f - 12f; // stable per-instance tilt from the planned yaw
            body.transform.localRotation = Quaternion.Euler(tilt, 45f, tilt * 0.6f);
            return rock;
        }

        private static GameObject Part(GameObject parent, PrimitiveType type, Material mat, Vector3 pos, Vector3 scale,
                                       bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale    = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        // ── Materials and atmosphere ───────────────────────────────────────────

        private static Material NewLit(Color color, float smoothness)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { enableInstancing = true };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            return mat;
        }

        private static void MakeTransparent(Material mat)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }

        // The match needs a high, angled sun. The first playtest scene had a point light where the sun should be,
        // which left the ground almost black once it stopped mirroring the sky.
        private static void EnsureSun()
        {
            var sun = RenderSettings.sun;
            if (sun == null || sun.type != LightType.Directional)
            {
                sun = null;
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sun = l; break; }
            }
            if (sun == null)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
            }

            // Keep a sun that's already high; fix one lying on the horizon.
            if (Vector3.Dot(sun.transform.forward, Vector3.down) < 0.4f)
                sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            if (sun.intensity < 0.8f) sun.intensity = 1.2f;
            if (sun.shadows == LightShadows.None) sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
        }

        // Light haze gives depth and hides the far edge of the sea. Scene-level, set at runtime only.
        public static void ApplyAtmosphere()
        {
            EnsureSun();

            // Soft three-colour ambient so shaded sides aren't black (the scene has no baked lighting).
            RenderSettings.ambientMode         = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor     = new Color(0.56f, 0.63f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.48f, 0.45f);
            RenderSettings.ambientGroundColor  = new Color(0.26f, 0.24f, 0.2f);

            RenderSettings.fog        = true;
            RenderSettings.fogMode    = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0032f;
            RenderSettings.fogColor   = new Color(0.66f, 0.74f, 0.78f);
        }
    }
}
