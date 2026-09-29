using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ProjectFossil.Core;

namespace ProjectFossil.Generation
{
    // Presentation for a generated island: biome-coloured ground, sea, trees, rocks and haze.
    // Uses the biome's imported models where it has them, and primitives with flat colours otherwise.
    public static class IslandDecorator
    {
        private static readonly Color TrunkColor     = new Color(0.33f, 0.24f, 0.16f);
        private static readonly Color DeadTrunkColor = new Color(0.28f, 0.25f, 0.22f);
        private static readonly Color CliffColor     = new Color(0.42f, 0.4f, 0.37f);
        private static readonly Color DirtColor      = new Color(0.36f, 0.29f, 0.19f);
        private static readonly Color MossColor      = new Color(0.15f, 0.25f, 0.09f);

        public static void Decorate(GameObject islandGO, IslandData data)
        {
            var terrain = islandGO.GetComponent<Terrain>();
            if (terrain != null) PaintTerrain(terrain.terrainData, data);

            var s = data.Settings;
            ViewBlockers.Clear();
            ViewBlockers.SetWaterHeight(islandGO.transform.TransformPoint(new Vector3(0f, s.seaLevel * s.maxHeight, 0f)).y);

            AddWater(islandGO.transform, data);
            AddInlandWater(islandGO.transform, data);
            if (terrain != null) AddScatter(islandGO.transform, terrain, data, ScatterPlanner.Plan(data));
            if (Application.isPlaying)
            {
                var wind = islandGO.GetComponent<IslandWind>();
                if (wind == null) wind = islandGO.AddComponent<IslandWind>();
                wind.SetDirection((data.Seed & 0x7fffffff) % 360); // each island has its own wind
            }
            if (Application.isPlaying) ApplyAtmosphere(); // don't rewrite the open scene's lighting from the editor tool
        }

        // ── Ground ─────────────────────────────────────────────────────────────

        // One layer per biome plus a cliff layer on steep ground. Neighbouring biomes blend over a few metres.
        public static void PaintTerrain(TerrainData td, IslandData data)
        {
            var biomes = data.Settings.biomes;
            int biomeCount = biomes != null ? biomes.Count : 0;
            if (biomeCount == 0) return;

            // Biome layers, then cliff, bare dirt and dark moss. Dirt and moss come in patches so the ground
            // doesn't read as one mown lawn.
            int cliffL = biomeCount, dirtL = biomeCount + 1, mossL = biomeCount + 2;
            var layers = new TerrainLayer[biomeCount + 3];
            for (int i = 0; i < biomeCount; i++)
                layers[i] = MakeLayer(biomes[i] != null ? biomes[i].groundColor : Color.gray, 1000 + i);
            layers[cliffL] = MakeLayer(CliffColor, 999);
            layers[dirtL]  = MakeLayer(DirtColor, 998);
            layers[mossL]  = MakeLayer(MossColor, 997);
            td.terrainLayers = layers;
            float offA = (data.Seed & 0xFFF) * 0.37f, offB = ((data.Seed >> 12) & 0xFFF) * 0.41f;

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
                    float dirt  = 0.7f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 0.75f, Mathf.PerlinNoise(x * 0.018f + offA, z * 0.018f + offB)));
                    float moss  = 0.6f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.72f, Mathf.PerlinNoise(x * 0.03f + offB, z * 0.03f + offA)));
                    float rest  = (1f - cliff) * (1f - dirt) * (1f - moss);
                    for (int l = 0; l < biomeCount; l++) alpha[az, ax, l] = weights[l] * rest;
                    alpha[az, ax, cliffL] = cliff;
                    alpha[az, ax, dirtL]  = (1f - cliff) * dirt;
                    alpha[az, ax, mossL]  = (1f - cliff) * (1f - dirt) * moss;
                }
            }
            td.SetAlphamaps(0, 0, alpha);
        }

        // Small noisy texture so flat colour doesn't look like plastic. Alpha is smoothness in URP terrain,
        // so it stays low: a full alpha made the old ground mirror the sky and look like snow.
        private static TerrainLayer MakeLayer(Color baseColor, int seed)
        {
            const int size = 64;
            var rng = new System.Random(seed);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
            var px  = new Color[size * size];
            for (int i = 0; i < px.Length; i++)
            {
                float v = 0.8f + (float)rng.NextDouble() * 0.4f;
                px[i] = new Color(baseColor.r * v, baseColor.g * v, baseColor.b * v, 0.08f);
            }
            tex.SetPixels(px);
            tex.Apply(true);

            return new TerrainLayer
            {
                diffuseTexture = tex,
                tileSize       = new Vector2(5f, 5f),
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

            var r = water.GetComponent<Renderer>();
            var mat = WaterMaterial();
            r.sharedMaterial   = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static Material _water;

        private static Material WaterMaterial()
        {
            if (_water != null) return _water;
            _water = NewLit(new Color(0.1f, 0.3f, 0.36f, 0.82f), 0.85f);
            MakeTransparent(_water);
            return _water;
        }

        // Rivers as ribbons following their centre line at the water surface; lakes as flat discs.
        // The banks were carved by the generator, so the edges tuck under the ground.
        private static void AddInlandWater(Transform parent, IslandData data)
        {
            if (data.Rivers.Count == 0 && data.Lakes.Count == 0) return;
            var root = new GameObject("InlandWater").transform;
            root.SetParent(parent, false);

            foreach (var river in data.Rivers)
                if (river.Points.Count >= 2) AddMesh(root, "River", RiverMesh(river));
            foreach (var lake in data.Lakes)
                AddMesh(root, "Lake", DiscMesh(lake.Center, lake.Radius + 4f, 32));

            // Wading through inland water breaks a scent trail too.
            foreach (var river in data.Rivers)
                for (int i = 0; i < river.Points.Count; i += 2)
                    ViewBlockers.RegisterWater(parent.TransformPoint(river.Points[i]), river.HalfWidths[i] + 0.5f);
            foreach (var lake in data.Lakes)
                ViewBlockers.RegisterWater(parent.TransformPoint(lake.Center), lake.Radius + 2f);
        }

        private static void AddMesh(Transform root, string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = WaterMaterial();
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static Mesh RiverMesh(RiverPath river)
        {
            int n = river.Points.Count;
            var verts = new Vector3[n * 2];
            var tris  = new int[(n - 1) * 6];
            for (int i = 0; i < n; i++)
            {
                Vector3 prev = river.Points[Mathf.Max(0, i - 1)], next = river.Points[Mathf.Min(n - 1, i + 1)];
                Vector3 dir = next - prev; dir.y = 0f;
                Vector3 side = dir.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, dir.normalized) : Vector3.right;
                float w = river.HalfWidths[i] + 3f; // run a little under the banks so no gap shows
                verts[i * 2]     = river.Points[i] - side * w;
                verts[i * 2 + 1] = river.Points[i] + side * w;
            }
            for (int i = 0; i < n - 1; i++)
            {
                int v = i * 2, t = i * 6;
                tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
                tris[t + 3] = v + 1; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            var mesh = new Mesh { name = "River", vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh DiscMesh(Vector3 centre, float radius, int segments)
        {
            var verts = new Vector3[segments + 1];
            var tris  = new int[segments * 3];
            verts[0] = centre;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                verts[i + 1] = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                tris[i * 3] = 0; tris[i * 3 + 1] = 1 + (i + 1) % segments; tris[i * 3 + 2] = 1 + i;
            }
            var mesh = new Mesh { name = "Lake", vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
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

                // Imported models when the biome has them; primitives otherwise.
                var models = inst.Kind == ScatterKind.Tree ? b.treeModels
                           : inst.Kind == ScatterKind.Rock ? b.rockModels
                           : b.plantModels;
                var prefab = Pick(models, inst.Variant);
                if (prefab != null) { PlaceModel(prefab, root, pos, inst); continue; }

                if (inst.Kind == ScatterKind.Tree)
                {
                    if (!foliage.TryGetValue(inst.BiomeIndex, out var leaf))
                        foliage[inst.BiomeIndex] = leaf = NewLit(b.foliageColor, 0.1f);
                    var go = b.treeShape == TreeShape.Dead ? MakeDeadTree(deadTrk)
                           : b.treeShape == TreeShape.Tall ? MakeTallTree(trunk, leaf)
                           : MakeRoundTree(trunk, leaf);
                    Place(go, root, pos, inst);
                    if (Application.isPlaying) ViewBlockers.RegisterTree(go.transform.position, null, 0f);
                }
                else if (inst.Kind == ScatterKind.Rock)
                {
                    if (!rocks.TryGetValue(inst.BiomeIndex, out var stone))
                        rocks[inst.BiomeIndex] = stone = NewLit(b.rockColor, 0.15f);
                    var go = MakeRock(stone, inst);
                    Place(go, root, pos + Vector3.down * 0.25f * inst.Scale, inst);
                }
                // Plants have no primitive stand-in: bare ground reads better than blobs.
            }
        }

        private static GameObject Pick(GameObject[] models, int variant)
        {
            if (models == null || models.Length == 0) return null;
            return models[variant % models.Length];
        }

        // Sizes match the primitives they replace: trees ~7 m, rocks ~1.5 m across, plants ~1.2 m, at scale 1.
        private const float TreeHeight = 7f, RockWidth = 1.5f, PlantHeight = 1.2f;

        private static void PlaceModel(GameObject prefab, Transform root, Vector3 pos, ScatterInstance inst)
        {
            var b = ModelFit.PrefabBounds(prefab);
            float s = inst.Kind == ScatterKind.Rock
                ? RockWidth * inst.Scale / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z))
                : (inst.Kind == ScatterKind.Tree ? TreeHeight : PlantHeight) * inst.Scale / Mathf.Max(0.01f, b.size.y);

            var go = Object.Instantiate(prefab, root, false);
            go.name = prefab.name;
            go.transform.localRotation = Quaternion.Euler(0f, inst.Yaw, 0f);
            go.transform.localScale    = Vector3.one * s;
            // Sink rocks a little so they sit in the ground rather than on it.
            float sink = inst.Kind == ScatterKind.Rock ? b.size.y * s * 0.2f : 0.05f;
            go.transform.localPosition = pos + Vector3.up * (-b.min.y * s - sink);

            // Collision (and NavMesh carving) for trunks and rocks only; plants are walk-through.
            if (inst.Kind == ScatterKind.Tree)
            {
                if (Application.isPlaying)
                {
                    // Sway the model, not the root: the trunk collider has to stay put or physics re-inserts it
                    // every frame.
                    var pivot = SwayPivot(go.transform);
                    ViewBlockers.RegisterTree(go.transform.position, pivot, 0.55f);
                }
                var col = go.AddComponent<CapsuleCollider>();
                col.radius = 0.35f / s * inst.Scale;
                col.height = b.size.y * 0.5f;
                col.center = new Vector3(0f, b.min.y + col.height * 0.5f, 0f);
            }
            else if (inst.Kind == ScatterKind.Rock)
            {
                var col = go.AddComponent<BoxCollider>();
                col.center = b.center;
                col.size   = b.size;
            }
            else
            {
                var renderers = go.GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                    r.shadowCastingMode = ShadowCastingMode.Off; // thousands of small shadows cost more than they add

                // Walk-through plants hide when they get between the camera and the player.
                if (Application.isPlaying && renderers.Length > 0)
                {
                    var wb = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) wb.Encapsulate(renderers[i].bounds);
                    float radius = Mathf.Max(wb.extents.x, wb.extents.z);
                    // Bushes and tall ferns you can crouch in count as cover; ankle-high grass doesn't.
                    bool cover = radius >= 0.55f && wb.size.y >= 0.7f;
                    ViewBlockers.Register(wb.center, radius, renderers, go.transform, 1f, cover);
                }
            }

            // Stop drawing small things once they're a few pixels tall; the haze hides the pop.
            float cullBelow = inst.Kind == ScatterKind.Plant ? 0.012f : inst.Kind == ScatterKind.Rock ? 0.006f : 0.003f;
            var lod = go.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(cullBelow, go.GetComponentsInChildren<Renderer>()) });
            lod.RecalculateBounds();
        }

        // Moves a model's drawing under a new child at its base and returns that, so the whole model can lean in the
        // wind while the root (and its collider) stays still. Many imported trees are one mesh on the root itself:
        // that mesh moves to the child too, and the root keeps only the collider.
        private static Transform SwayPivot(Transform model)
        {
            var pivot = new GameObject("Sway").transform;
            pivot.SetParent(model, false);
            for (int i = model.childCount - 1; i >= 0; i--)
            {
                var child = model.GetChild(i);
                if (child != pivot) child.SetParent(pivot, true);
            }

            var filter   = model.GetComponent<MeshFilter>();
            var renderer = model.GetComponent<MeshRenderer>();
            if (filter != null && renderer != null)
            {
                pivot.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var copy = pivot.gameObject.AddComponent<MeshRenderer>();
                copy.sharedMaterials    = renderer.sharedMaterials;
                copy.shadowCastingMode  = renderer.shadowCastingMode;
                copy.receiveShadows     = renderer.receiveShadows;
                Object.DestroyImmediate(renderer); // immediate, so the LODGroup below only finds the moving copy
                Object.DestroyImmediate(filter);
            }
            return pivot;
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
            sun.color = new Color(1f, 0.93f, 0.82f); // warm tropical light
            if (sun.shadows == LightShadows.None) sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
        }

        // Light haze gives depth and hides the far edge of the sea. Scene-level, set at runtime only.
        public static void ApplyAtmosphere()
        {
            EnsureSun();

            // Soft three-colour ambient so shaded sides aren't black (the scene has no baked lighting).
            RenderSettings.ambientMode         = AmbientMode.Trilight;
            // Bright enough that the floor of a dense forest, lit by ambient alone, still reads.
            RenderSettings.ambientSkyColor     = new Color(0.64f, 0.72f, 0.74f);
            RenderSettings.ambientEquatorColor = new Color(0.54f, 0.58f, 0.48f);
            RenderSettings.ambientGroundColor  = new Color(0.32f, 0.3f, 0.23f);

            // Humid, green-grey haze: far hills fade out and the jungle feels deep.
            RenderSettings.fog        = true;
            RenderSettings.fogMode    = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0052f;
            RenderSettings.fogColor   = new Color(0.58f, 0.66f, 0.62f);
        }
    }
}
