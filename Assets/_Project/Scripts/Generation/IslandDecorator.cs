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

        public static void Decorate(GameObject islandGO, IslandData data, IslandWorld world)
        {
            var terrain = islandGO.GetComponent<Terrain>();
            if (terrain != null) PaintTerrain(terrain.terrainData, data);

            ViewBlockers.Clear();

            AddWater(islandGO.transform, data);
            AddInlandWater(islandGO.transform, world);
            var plan = ScatterPlanner.Plan(data);
            if (terrain != null) AddGrass(terrain, data, world, plan);
            AddScatter(islandGO.transform, data, world, plan);
            if (Application.isPlaying)
            {
                var wind = islandGO.GetComponent<IslandWind>();
                if (wind == null) wind = islandGO.AddComponent<IslandWind>();
                wind.SetDirection((data.Seed & 0x7fffffff) % 360); // each island has its own wind
            }
            if (Application.isPlaying) ApplyAtmosphere(); // don't rewrite the open scene's lighting from the editor tool
            AddBoughtWind(islandGO.transform);
        }

        // Bought foliage shaders bend with Unity's wind zones; the pack's own wind prefab drives them.
        private static void AddBoughtWind(Transform island)
        {
            var art = BoughtArt.Current;
            if (art == null || art.windZone == null || !Application.isPlaying) return;
            if (Object.FindAnyObjectByType<WindZone>() != null) return;
            Object.Instantiate(art.windZone, island, false).name = "Wind";
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
            // Bought ground textures where this machine has them, plain noisy colour otherwise.
            var art = BoughtArt.Current;
            for (int i = 0; i < biomeCount; i++)
            {
                var bought = biomes[i] != null ? BoughtArt.BiomeFor(biomes[i].name) : null;
                layers[i] = bought != null && bought.ground != null ? bought.ground
                          : MakeLayer(biomes[i] != null ? biomes[i].groundColor : Color.gray, 1000 + i);
            }
            layers[cliffL] = art != null && art.cliff != null ? art.cliff : MakeLayer(CliffColor, 999);
            layers[dirtL]  = art != null && art.dirt  != null ? art.dirt  : MakeLayer(DirtColor, 998);
            layers[mossL]  = art != null && art.moss  != null ? art.moss  : MakeLayer(MossColor, 997);
            td.terrainLayers = layers;
            float offA = (data.Seed & 0xFFF) * 0.37f, offB = ((data.Seed >> 12) & 0xFFF) * 0.41f;

            // About 2 m a texel on the default island, so cliff faces follow the fine terrain's real slopes.
            int aRes = Mathf.Clamp(Mathf.ClosestPowerOfTwo(td.heightmapResolution - 1) / 2, 16, 1024);
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

                    float slope = Mathf.Tan(td.GetSteepness((ax + 0.5f) / aRes, (az + 0.5f) / aRes) * Mathf.Deg2Rad);
                    float cliff = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.1f, slope));
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

        // ── Grass ──────────────────────────────────────────────────────────────

        // Short grass drawn by the terrain itself (thousands of blades within a few dozen metres, cheap), one layer per
        // biome so each gets its own tint and cover. Thick on open meadows, thin in the shade of trees and in patches,
        // none on cliffs, sand, ash or under water. Blades are fixed crossed cards that bend in the wind, never
        // camera-facing ones that turn as you walk round them.
        private const int   GrassPerCell     = 4;    // most blades in one detail cell
        private const float GrassDistance    = 70f;  // metres; the haze hides where it stops
        private const float GrassNoSlope     = 38f;  // degrees: none steeper than this
        private const float GrassFullSlope   = 26f;

        private static void AddGrass(Terrain terrain, IslandData data, IslandWorld island, List<ScatterInstance> plan)
        {
            var s = data.Settings;
            var biomes = s.biomes;
            if (biomes == null || biomes.Count == 0) return;
            var td = terrain.terrainData;
            int n = biomes.Count;

            var tex = GrassTexture();
            var dry = new Color(0.62f, 0.55f, 0.32f);
            var protos = new DetailPrototype[n];
            for (int i = 0; i < n; i++)
            {
                var tint = biomes[i] != null ? biomes[i].grassColor : Color.gray;
                protos[i] = new DetailPrototype
                {
                    prototypeTexture = tex,
                    usePrototypeMesh = false,
                    renderMode   = DetailRenderMode.Grass,
                    minWidth     = 0.5f, maxWidth  = 1.0f,
                    minHeight    = 0.3f, maxHeight = 0.7f,
                    noiseSpread  = 0.3f,
                    healthyColor = tint,
                    dryColor     = Color.Lerp(tint, dry, 0.55f),
                };
            }
            td.detailPrototypes = protos;

            int res = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.RoundToInt(s.worldSize)), 128, 1024); // about 1 m a cell
            td.SetDetailResolution(res, 32);
#if UNITY_2022_2_OR_NEWER
            td.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
#endif
            float world = s.worldSize, cell = world / res;
            Vector3 origin = terrain.transform.position;

            // Under the trees' crowns: how many trees stand in each ~8 m square.
            const int shadeRes = 128;
            var shade = new float[shadeRes, shadeRes];
            foreach (var inst in plan)
            {
                if (inst.Kind != ScatterKind.Tree) continue;
                int sx = Mathf.Clamp((int)(inst.WorldPos.x / world * shadeRes), 0, shadeRes - 1);
                int sz = Mathf.Clamp((int)(inst.WorldPos.z / world * shadeRes), 0, shadeRes - 1);
                shade[sz, sx] += 1f;
            }

            // No grass under water, nor on the strip of shore just above it.
            var wet = new bool[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    var p = origin + new Vector3((x + 0.5f) * cell, 0f, (z + 0.5f) * cell);
                    wet[z, x] = island.WaterSurfaceAt(p) > island.GroundAt(p) - 0.15f;
                }

            float sea = s.seaLevel * s.maxHeight + 0.4f;
            var rng = new System.Random(data.Seed ^ 0x6A55);
            float ox = (float)rng.NextDouble() * 1000f, oz = (float)rng.NextDouble() * 1000f;
            var layers = new int[n][,];
            for (int i = 0; i < n; i++) layers[i] = new int[res, res];

            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    double roll = rng.NextDouble(); // drawn every cell so the pattern doesn't shift when one changes
                    if (wet[z, x]) continue;
                    float nx = (x + 0.5f) / res, nz = (z + 0.5f) / res;
                    float wx = nx * world, wz = nz * world;
                    int b = ScatterPlanner.SampleBiome(data, wx, wz);
                    if (b < 0 || b >= n || biomes[b] == null || biomes[b].grassCover <= 0f) continue;
                    if (td.GetInterpolatedHeight(nx, nz) < sea) continue;

                    float steep = td.GetSteepness(nx, nz);
                    if (steep >= GrassNoSlope) continue;
                    float slopeFade = 1f - Mathf.InverseLerp(GrassFullSlope, GrassNoSlope, steep);

                    int sx = Mathf.Min(shadeRes - 1, (int)(nx * shadeRes)), sz = Mathf.Min(shadeRes - 1, (int)(nz * shadeRes));
                    float trees = shade[sz, sx];
                    float shadeFade = 1f / (1f + 0.45f * trees);

                    // Patches: meadows with bare runs between them, not an even lawn.
                    float patch = Mathf.SmoothStep(0.2f, 1f, Mathf.InverseLerp(0.3f, 0.68f,
                                      Mathf.PerlinNoise(wx * 0.045f + ox, wz * 0.045f + oz)));

                    float density = biomes[b].grassCover * slopeFade * shadeFade * patch * GrassPerCell;
                    int count = (int)density + (roll < density - (int)density ? 1 : 0);
                    if (count > 0) layers[b][z, x] = count;
                }
            }
            for (int i = 0; i < n; i++) td.SetDetailLayer(0, 0, i, layers[i]);

            terrain.detailObjectDistance = GrassDistance;
            terrain.detailObjectDensity  = 1f;
            td.wavingGrassAmount   = 0.35f;
            td.wavingGrassSpeed    = 0.45f;
            td.wavingGrassStrength = 0.45f;
            td.wavingGrassTint     = new Color(0.85f, 0.9f, 0.75f);
        }

        // Grey blades on a clear background, tinted per biome by the detail colours. Drawn here, so no asset needed.
        private static Texture2D _grassTexture;

        private static Texture2D GrassTexture()
        {
            if (_grassTexture != null) return _grassTexture;
            const int w = 128, h = 128;
            var px  = new Color32[w * h];
            var rng = new System.Random(4242);
            for (int blade = 0; blade < 46; blade++)
            {
                float x0     = 4f + (float)rng.NextDouble() * (w - 8);
                float height = (0.4f + 0.6f * (float)rng.NextDouble()) * (h - 2);
                float lean   = ((float)rng.NextDouble() - 0.5f) * 0.4f * w;
                float width  = 2.2f + (float)rng.NextDouble() * 2.6f;
                float tone   = 0.8f + (float)rng.NextDouble() * 0.35f;
                for (int y = 0; y < (int)height; y++)
                {
                    float t     = y / height;
                    float cx    = x0 + lean * t * t;
                    float halfW = width * (1f - t) * 0.5f + 0.3f;
                    float v     = Mathf.Clamp01((0.45f + 0.55f * t) * tone); // darker at the root
                    byte  g     = (byte)(v * 255f);
                    for (int x = Mathf.Max(0, (int)(cx - halfW - 1)); x <= Mathf.Min(w - 1, (int)(cx + halfW + 1)); x++)
                        if (Mathf.Abs(x + 0.5f - cx) <= halfW) px[y * w + x] = new Color32(g, g, g, 255);
                }
            }
            _grassTexture = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                name = "Grass", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            _grassTexture.SetPixels32(px);
            _grassTexture.Apply(true);
            return _grassTexture;
        }

        // ── Water ──────────────────────────────────────────────────────────────

        private static void AddWater(Transform parent, IslandData data)
        {
            var s = data.Settings;
            var water = Placeholder.Primitive(PrimitiveType.Plane); // 10 x 10 units
            water.name = "Sea";
            Object.DestroyImmediate(water.GetComponent<Collider>()); // immediate: the NavMesh bakes this frame
            water.transform.SetParent(parent, false);
            water.transform.localPosition = new Vector3(s.worldSize * 0.5f, s.seaLevel * s.maxHeight, s.worldSize * 0.5f);
            // 20 km across: wide enough that, even from a peak or the title flight, the sea runs out to the horizon
            // and its edges are lost in the haze instead of showing as a square.
            water.transform.localScale    = new Vector3(2000f, 1f, 2000f);

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
            _water.SetFloat("_Cull", 0f); // both faces, so the surface still shows from just under it
            return _water;
        }

        // Lakes and streams, drawn straight from the island's water field: one surface over the wet vertices, reaching
        // one vertex past them, where the bank stands above it, so every edge tucks under the ground. The field
        // keeps surfaces gentle, so there are no falls, curtains or floating sheets. In chunks, so the camera only
        // draws what it can see.
        private const int WaterChunk = 128; // vertices along a chunk's side

        private static void AddInlandWater(Transform parent, IslandWorld world)
        {
            var root = new GameObject("InlandWater").transform;
            root.SetParent(parent, false);
            var art = BoughtArt.Current;
            // The calm swamp water: the bought river material's foam read as white sheets.
            Material mat = art != null ? (art.lakeWater != null ? art.lakeWater : art.riverWater) : null;

            int n = world.Size;
            var drawn = new float[n * n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                    drawn[z * n + x] = WaterField.DrawnSurface(world, x, z);

            var verts = new List<Vector3>();
            var uvs   = new List<Vector2>();
            var tris  = new List<int>();
            var index = new Dictionary<int, int>();
            for (int cz = 0; cz < n - 1; cz += WaterChunk)
                for (int cx = 0; cx < n - 1; cx += WaterChunk)
                {
                    verts.Clear(); uvs.Clear(); tris.Clear(); index.Clear();
                    int ex = Mathf.Min(cx + WaterChunk, n - 1), ez = Mathf.Min(cz + WaterChunk, n - 1);
                    for (int z = cz; z < ez; z++)
                        for (int x = cx; x < ex; x++)
                        {
                            int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
                            if (float.IsNaN(drawn[a]) || float.IsNaN(drawn[b]) || float.IsNaN(drawn[c]) || float.IsNaN(drawn[d])) continue;
                            if (!world.WetVertex(x, z) && !world.WetVertex(x + 1, z)
                                && !world.WetVertex(x, z + 1) && !world.WetVertex(x + 1, z + 1)) continue;
                            int ia = Vertex(a), ib = Vertex(b), ic = Vertex(c), id = Vertex(d);
                            tris.Add(ia); tris.Add(ic); tris.Add(id);
                            tris.Add(ia); tris.Add(id); tris.Add(ib);
                        }
                    if (tris.Count == 0) continue;
                    var mesh = new Mesh { name = "Water", indexFormat = IndexFormat.UInt32 };
                    mesh.SetVertices(verts);
                    mesh.SetUVs(0, uvs);
                    mesh.SetColors(White(verts.Count));
                    mesh.SetTriangles(tris, 0);
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                    AddMesh(root, "Water", mesh, mat);
                }

            int Vertex(int i)
            {
                if (index.TryGetValue(i, out int v)) return v;
                int x = i % n, z = i / n;
                var p = new Vector3(x * world.Cell, drawn[i], z * world.Cell);
                v = verts.Count;
                verts.Add(p);
                uvs.Add(new Vector2(p.x, p.z) * 0.05f);
                index[i] = v;
                return v;
            }
        }

        // The forest pack's water where this machine has it, a plain see-through surface otherwise.
        private static void AddMesh(Transform root, string name, Mesh mesh, Material bought)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = bought != null ? bought : WaterMaterial();
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static Color[] White(int n)
        {
            var c = new Color[n];
            for (int i = 0; i < n; i++) c[i] = Color.white;
            return c;
        }


        // ── Trees and rocks ────────────────────────────────────────────────────

        private static void AddScatter(Transform parent, IslandData data, IslandWorld world, List<ScatterInstance> plan)
        {
            var root = new GameObject("Scatter").transform;
            root.SetParent(parent, false);

            var biomes  = data.Settings.biomes;
            var trunk   = NewLit(TrunkColor, 0.05f);
            var deadTrk = NewLit(DeadTrunkColor, 0.05f);
            var foliage = new Dictionary<int, Material>();
            var rocks   = new Dictionary<int, Material>();
            float GroundAt(Vector3 local) => world.GroundAt(parent.TransformPoint(local)); // the island sits unrotated at its origin

            foreach (var inst in plan)
            {
                var b = biomes[inst.BiomeIndex];
                Vector3 pos = inst.WorldPos;
                pos.y = GroundAt(pos);
                // Nothing grows in the water or right at its edge (leaves stuck up through the surface). Rocks may sit
                // in the shallows, but not out of sight in a lake.
                var at = parent.TransformPoint(pos);
                float depth = world.WaterSurfaceAt(at) - world.GroundAt(at);
                if (inst.Kind != ScatterKind.Rock ? depth > -0.3f : depth > 0.4f) continue;

                // Bought models first, then the biome's free imported models, then primitives.
                var bought = BoughtArt.BiomeFor(b.name);
                var boughtModels = bought == null ? null
                                 : inst.Kind == ScatterKind.Tree ? bought.trees
                                 : inst.Kind == ScatterKind.Rock ? bought.rocks
                                 : bought.plants;
                bool useBought = BoughtArt.Has(boughtModels);
                var models = useBought ? boughtModels
                           : inst.Kind == ScatterKind.Tree ? b.treeModels
                           : inst.Kind == ScatterKind.Rock ? b.rockModels
                           : b.plantModels;
                var prefab = Pick(models, inst.Variant);
                if (prefab != null)
                {
                    float treeHeight = useBought && BoughtArt.Current != null ? BoughtArt.Current.treeHeight : TreeHeight;
                    // Bought foliage sways in its own shader with the pack's wind zone; turning the whole model on top of
                    // that tipped bushes so their bases lifted off the ground.
                    bool shaderWind = useBought && BoughtArt.Current != null && BoughtArt.Current.windZone != null;
                    PlaceModel(prefab, root, pos, inst, treeHeight, GroundAt, shaderWind);
                    continue;
                }

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
            // Skip empty slots (a bought pack removed after setup) without changing which model a seed gets.
            for (int i = 0; i < models.Length; i++)
            {
                var m = models[(variant + i) % models.Length];
                if (m != null) return m;
            }
            return null;
        }

        // Sizes match the primitives they replace: trees ~7 m, rocks ~1.5 m across, plants ~1.2 m, at scale 1.
        private const float TreeHeight = 7f, RockWidth = 1.5f, PlantHeight = 1.2f;
        // Rocks lower than this above the ground get no collider. The NavMesh climbs anything 0.75 m high, and on a
        // slope it climbed onto taller boxes from the uphill side, so paths ran straight over stones and stumps that
        // the survivor (who steps 0.3 m) walked into and stuck on. A stump's box also takes in its spreading roots,
        // so it stands taller than it looks: up to hip height, walk over it.
        private const float WalkOverHeight = 1.0f;
        // Low, spreading plants (banana leaves, big ferns) scaled up to plant height become leaves metres wide
        // that fill the screen when you crawl past. No plant gets wider than this at scale 1.
        private const float PlantMaxWidth = 1.8f;

        private static void PlaceModel(GameObject prefab, Transform root, Vector3 pos, ScatterInstance inst, float treeHeight,
                                       System.Func<Vector3, float> groundAt, bool shaderWind = false)
        {
            var b = ModelFit.PrefabBounds(prefab);
            float s = inst.Kind == ScatterKind.Rock
                ? RockWidth * inst.Scale / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z))
                : (inst.Kind == ScatterKind.Tree ? treeHeight : PlantHeight) * inst.Scale / Mathf.Max(0.01f, b.size.y);
            if (inst.Kind == ScatterKind.Plant)
                s = Mathf.Min(s, PlantMaxWidth * inst.Scale / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z)));
            // Bought trees come at their real size. Stretched up to full tree height, a sapling's thin twigs became
            // long bare poles across the view with a few leaves hanging off their ends, so never enlarge one much.
            if (shaderWind && inst.Kind == ScatterKind.Tree) s = Mathf.Min(s, 1.25f * inst.Scale);

            var go = Object.Instantiate(prefab, root, false);
            go.name = prefab.name;
            // Only the colliders added below count. Bought models bring their own: a banyan's was a 4.5 m wide,
            // 15 m tall capsule round a much thinner trunk, and plants meant to be walk-through came with capsules,
            // so the survivor stopped against nothing in open forest. Immediate: the NavMesh bakes this frame.
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            // A plant or tree made only of cards spins as you walk round it: leave it out.
            {
                var group = go.GetComponentInChildren<LODGroup>();
                var first = group != null && group.lodCount > 0 ? group.GetLODs()[0].renderers : go.GetComponentsInChildren<Renderer>();
                if (AllStandIns(first)) { Object.DestroyImmediate(go); return; }
            }
            go.transform.localRotation = Quaternion.Euler(0f, inst.Yaw, 0f);
            go.transform.localScale    = Vector3.one * s;
            // Sink rocks a little so they sit in the ground rather than on it.
            float sink = inst.Kind == ScatterKind.Rock ? b.size.y * s * 0.2f : 0.05f;
            // On a slope the ground under the downhill edge is lower than at the centre, so a plant or a tree's
            // roots float there. Sink to the lowest ground across the base instead.
            if (inst.Kind != ScatterKind.Rock)
            {
                float reach = inst.Kind == ScatterKind.Tree ? Mathf.Min(b.size.x, b.size.z) * s * 0.2f
                                                            : Mathf.Max(b.size.x, b.size.z) * s * 0.35f;
                sink += BaseDrop(pos, reach, groundAt) * (inst.Kind == ScatterKind.Tree ? 1f : 0.6f); // leaves bend, a trunk can't
                if (inst.Kind == ScatterKind.Tree) sink += 0.15f;
            }
            // Bought trees and plants are modelled with their pivot at ground level and roots or stems running a
            // little below it, so they sit right on slopes. Lifting their lowest point to the ground left roots
            // and ferns hanging in the air: trust such a pivot. Rocks, and models whose pivot is well inside them,
            // still rest on their lowest point.
            bool pivotAtGround = inst.Kind != ScatterKind.Rock && b.min.y < 0f && -b.min.y < b.size.y * 0.25f;
            float lift = pivotAtGround ? 0f : -b.min.y * s;
            go.transform.localPosition = pos + Vector3.up * (lift - sink);

            // Collision (and NavMesh carving) for trunks and rocks only; plants are walk-through.
            if (inst.Kind == ScatterKind.Tree)
            {
                if (Application.isPlaying)
                {
                    // Sway the model, not the root: the trunk collider has to stay put or physics re-inserts it
                    // every frame.
                    if (shaderWind) ViewBlockers.RegisterTree(go.transform.position, null, 0f);
                    else ViewBlockers.RegisterTree(go.transform.position, SwayPivot(go.transform), 0.55f);
                }
                var col = go.AddComponent<CapsuleCollider>();
                col.radius = 0.35f / s * inst.Scale;
                col.height = b.size.y * 0.5f;
                col.center = new Vector3(0f, b.min.y + col.height * 0.5f, 0f);
            }
            else if (inst.Kind == ScatterKind.Rock && b.size.y * s * 0.8f >= WalkOverHeight)
            {
                var col = go.AddComponent<BoxCollider>();
                col.center = b.center;
                col.size   = b.size;
            }
            else if (inst.Kind != ScatterKind.Rock)
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
                    // Shader-wind foliage moves in its own shader and is left unturned: tilting a whole bought bush
                    // (with gusts, or pushed aside) lifted its far leaves off the ground, where they hung in the air.
                    ViewBlockers.Register(wb.center, radius, renderers, shaderWind ? null : go.transform, 1f, cover);
                }
            }

            // Stop drawing small things once they're a few pixels tall; the haze hides the pop.
            float cullBelow = inst.Kind == ScatterKind.Plant ? 0.012f : inst.Kind == ScatterKind.Rock ? 0.006f : 0.003f;
            var own = go.GetComponentInChildren<LODGroup>();
            if (own != null)
            {
                // Bought models bring their own detail levels; keep them and only make the last one drop out
                // at the same distance as everything else.
                var levels = own.GetLODs();
                // Far levels made of flat cards turn to face the camera: from a distance a forest's leaves spun in
                // circles as you moved, and plants read as leaves and caps hanging in the air. Trees and plants keep
                // their last real mesh instead, and fade out in the haze.
                // A far level with any cards in it goes (a trunk with card leaves still spins its leaves), but a
                // renderer the nearer levels share stays on.
                int keep = levels.Length;
                while (keep > 1 && AnyStandIn(levels[keep - 1].renderers)) keep--;
                if (keep < levels.Length)
                {
                    var kept = new HashSet<Renderer>();
                    for (int i = 0; i < keep; i++)
                        foreach (var r in levels[i].renderers) if (r != null) kept.Add(r);
                    for (int i = keep; i < levels.Length; i++)
                        foreach (var r in levels[i].renderers) if (r != null && !kept.Contains(r)) r.enabled = false;
                }
                bool dropped = keep < levels.Length;
                if (dropped) System.Array.Resize(ref levels, keep);
                if (levels.Length > 0 && (dropped || levels[levels.Length - 1].screenRelativeTransitionHeight < cullBelow))
                {
                    levels[levels.Length - 1].screenRelativeTransitionHeight = cullBelow;
                    for (int i = levels.Length - 2; i >= 0; i--)
                        levels[i].screenRelativeTransitionHeight = Mathf.Max(levels[i].screenRelativeTransitionHeight,
                                                                             levels[i + 1].screenRelativeTransitionHeight + 0.001f);
                    // Some bought trees ship levels above 1 (2.1, 1.5, ...: their near levels never show). SetLODs
                    // clamps those to 1, finds two equal levels and refuses the lot, a thousand errors per island.
                    levels[0].screenRelativeTransitionHeight = Mathf.Min(levels[0].screenRelativeTransitionHeight, 1f);
                    for (int i = 1; i < levels.Length; i++)
                        levels[i].screenRelativeTransitionHeight = Mathf.Min(levels[i].screenRelativeTransitionHeight,
                                                                             levels[i - 1].screenRelativeTransitionHeight - 0.001f);
                    own.SetLODs(levels);
                }
                own.RecalculateBounds();
                return;
            }
            var lod = go.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(cullBelow, go.GetComponentsInChildren<Renderer>()) });
            lod.RecalculateBounds();
        }

        // A camera-facing card (billboard, cross or impostor shader) rather than a model.
        private static bool AllStandIns(Renderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0) return false;
            foreach (var r in renderers)
                if (r != null && !IsStandIn(r)) return false;
            return true;
        }

        private static bool AnyStandIn(Renderer[] renderers)
        {
            if (renderers == null) return false;
            foreach (var r in renderers)
                if (r != null && IsStandIn(r)) return true;
            return false;
        }

        private static bool IsStandIn(Renderer r)
        {
            if (r is BillboardRenderer) return true;
            // A few crossed quads, whatever its shader is called: the bought plants' far levels are cards drawn with
            // the same leaf shader as the near ones, so the name test missed them and they spun round as you moved.
            if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null
                && IndexCount(mf.sharedMesh) <= MaxCardIndices) return true;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || m.shader == null) continue;
                string n = m.shader.name;
                if (n.IndexOf("Cross", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Billboard", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Impostor", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private const int MaxCardIndices = 60; // 20 triangles: up to five crossed quads, both sides

        private static long IndexCount(Mesh mesh)
        {
            long n = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) n += mesh.GetIndexCount(i);
            return n;
        }

        // How far the lowest ground within `reach` of `pos` lies below it (0 on flat ground), capped so a tree on a
        // cliff edge doesn't vanish into the hill.
        private static float BaseDrop(Vector3 pos, float reach, System.Func<Vector3, float> groundAt)
        {
            if (groundAt == null || reach < 0.05f) return 0f;
            float low = pos.y;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                low = Mathf.Min(low, groundAt(pos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * reach));
            }
            return Mathf.Min(pos.y - low, 1.5f);
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
            var go = Placeholder.Primitive(type);
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
            var mat = new Material(Placeholder.LitBase) { enableInstancing = true };
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
#if UNITY_6000_5_OR_NEWER
                foreach (var l in Object.FindObjectsByType<Light>())
#else
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
#endif
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
