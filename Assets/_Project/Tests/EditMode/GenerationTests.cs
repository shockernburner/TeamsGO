using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ProjectFossil.Core;
using ProjectFossil.Generation;

namespace ProjectFossil.Tests.EditMode
{
    public class GenerationTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static IslandSettings MakeSettings(int res = 65)
        {
            var s = ScriptableObject.CreateInstance<IslandSettings>();
            s.resolution          = res;
            s.worldSize           = 500f;
            s.maxHeight           = 100f;
            s.noiseScale          = 0.003f;
            s.islandRadiusFraction= 0.42f;
            s.heightRemap         = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            s.extractionZoneCount = 2;
            s.lootCacheCount      = 4;
            s.ruinsCount          = 2;
            s.spawnZoneCount      = 3;
            s.minPOISpacing       = 50f;
            s.biomes              = new List<BiomeDefinition> { MakeBiome("Everywhere", 0f, 1f, 0f, 1f) };
            return s;
        }

        private static BiomeDefinition MakeBiome(string name, float minH, float maxH, float minM, float maxM)
        {
            var b = ScriptableObject.CreateInstance<BiomeDefinition>();
            b.biomeName   = name;
            b.minHeight   = minH; b.maxHeight   = maxH;
            b.minMoisture = minM; b.maxMoisture = maxM;
            return b;
        }

        // ── RNG ───────────────────────────────────────────────────────────────

        [Test]
        public void RNGService_SameSeed_ProducesSameSequence()
        {
            var a = new RNGService(42);
            var b = new RNGService(42);
            for (int i = 0; i < 20; i++)
                Assert.AreEqual(a.Next(1000), b.Next(1000), $"Mismatch at iteration {i}");
        }

        [Test]
        public void RNGService_DifferentSeeds_ProduceDifferentSequences()
        {
            var a = new RNGService(1);
            var b = new RNGService(2);
            bool anyDiff = false;
            for (int i = 0; i < 20 && !anyDiff; i++)
                anyDiff = a.Next(10000) != b.Next(10000);
            Assert.IsTrue(anyDiff);
        }

        // ── Determinism ───────────────────────────────────────────────────────

        [Test]
        public void IslandGenerator_SameSeed_ProducesSameHeightmap()
        {
            var s  = MakeSettings();
            var a  = new IslandGenerator(12345, s).Generate();
            var b  = new IslandGenerator(12345, s).Generate();

            for (int y = 0; y < 10; y++)
                for (int x = 0; x < 10; x++)
                    Assert.AreEqual(a.Heightmap[y, x], b.Heightmap[y, x],
                        $"Heightmap differs at [{y},{x}]");
        }

        [Test]
        public void IslandGenerator_DifferentSeeds_ProduceDifferentHeightmaps()
        {
            var s  = MakeSettings();
            var a  = new IslandGenerator(1, s).Generate();
            var b  = new IslandGenerator(2, s).Generate();

            bool anyDiff = false;
            for (int y = 0; y < a.Resolution && !anyDiff; y++)
                for (int x = 0; x < a.Resolution && !anyDiff; x++)
                    anyDiff = a.Heightmap[y, x] != b.Heightmap[y, x];
            Assert.IsTrue(anyDiff, "Different seeds produced identical heightmaps");
        }

        // ── Heightmap sanity ──────────────────────────────────────────────────

        [Test]
        public void IslandGenerator_HeightmapValuesInRange()
        {
            var data = new IslandGenerator(999, MakeSettings()).Generate();
            for (int y = 0; y < data.Resolution; y++)
                for (int x = 0; x < data.Resolution; x++)
                {
                    float h = data.Heightmap[y, x];
                    Assert.IsTrue(h >= 0f && h <= 1f,
                        $"Heightmap out of range [{y},{x}] = {h}");
                }
        }

        [Test]
        public void IslandGenerator_HasLandAndWater()
        {
            var data  = new IslandGenerator(42, MakeSettings()).Generate();
            int land  = 0, water = 0;
            for (int y = 0; y < data.Resolution; y++)
                for (int x = 0; x < data.Resolution; x++)
                    if (data.LandMask[y, x]) land++; else water++;

            Assert.Greater(land,  0, "Island has no land");
            Assert.Greater(water, 0, "Island has no water/coast");
        }

        [Test]
        public void IslandGenerator_CornerCellsAreWater()
        {
            var data = new IslandGenerator(7, MakeSettings()).Generate();
            int last  = data.Resolution - 1;
            Assert.IsFalse(data.LandMask[0,    0],    "Top-left corner should be water");
            Assert.IsFalse(data.LandMask[0,    last], "Top-right corner should be water");
            Assert.IsFalse(data.LandMask[last, 0],    "Bottom-left corner should be water");
            Assert.IsFalse(data.LandMask[last, last], "Bottom-right corner should be water");
        }

        // ── POI placement ─────────────────────────────────────────────────────

        [Test]
        public void IslandGenerator_POIsOnLand()
        {
            var data = new IslandGenerator(42, MakeSettings()).Generate();
            foreach (var poi in data.PointsOfInterest)
            {
                int gx = poi.GridPos.x, gy = poi.GridPos.y;
                Assert.IsTrue(data.LandMask[gy, gx],
                    $"POI {poi.Type} at {poi.GridPos} is in water");
            }
        }

        [Test]
        public void IslandGenerator_HasAtLeastOneExtractionZone()
        {
            var data = new IslandGenerator(42, MakeSettings()).Generate();
            int count = 0;
            foreach (var p in data.PointsOfInterest)
                if (p.Type == POIType.ExtractionZone) count++;
            Assert.Greater(count, 0, "No extraction zone placed");
        }

        // ── Validation across many seeds ──────────────────────────────────────

        [Test]
        public void IslandGenerator_ValidationPassesForMostSeeds()
        {
            var s   = MakeSettings();
            int fail = 0;

            for (int seed = 0; seed < 30; seed++)
            {
                var result = IslandValidator.Validate(new IslandGenerator(seed, s).Generate());
                if (!result.IsValid)
                {
                    Debug.LogWarning($"[Test] Seed {seed} failed: {string.Join(", ", result.Errors)}");
                    fail++;
                }
            }

            // Allow up to 2 failures in 30 seeds (occasional degenerate noise combinations)
            Assert.LessOrEqual(fail, 2, $"{fail}/30 seeds failed validation");
        }

        [Test]
        public void Scatter_AcrossSeeds_StaysOnLandAndClearOfPOIs()
        {
            var s = MakeSettings();
            s.biomes[0].treesPerHectare = 30f;
            s.biomes[0].rocksPerHectare = 8f;
            float clear2 = s.scatterClearance * s.scatterClearance;

            for (int seed = 0; seed < 20; seed++)
            {
                var data = new IslandGenerator(seed, s).Generate();
                var plan = ScatterPlanner.Plan(data);
                Assert.LessOrEqual(plan.Count, s.maxScatterInstances);

                foreach (var inst in plan)
                {
                    Assert.Greater(ScatterPlanner.SampleHeight(data, inst.WorldPos.x, inst.WorldPos.z), s.seaLevel,
                                   $"Seed {seed}: {inst.Kind} below sea level");
                    foreach (var poi in data.PointsOfInterest)
                    {
                        float dx = poi.WorldPos.x - inst.WorldPos.x, dz = poi.WorldPos.z - inst.WorldPos.z;
                        Assert.GreaterOrEqual(dx * dx + dz * dz, clear2, $"Seed {seed}: {inst.Kind} blocks a {poi.Type}");
                    }
                }
            }
        }

        // ── Rivers, lakes, relief ─────────────────────────────────────────────

        [Test]
        public void Rivers_FlowDownhill_AndAreNotLand()
        {
            var s = MakeSettings(257); // the game's resolution: rivers are a few cells wide
            s.worldSize = 1000f;
            int rivers = 0;
            for (int seed = 0; seed < 6; seed++)
            {
                var data = new IslandGenerator(seed, s).Generate();
                foreach (var river in data.Rivers)
                {
                    rivers++;
                    for (int i = 1; i < river.Points.Count; i++)
                        Assert.LessOrEqual(river.Points[i].y, river.Points[i - 1].y + 1e-3f, $"Seed {seed}: river runs uphill at {i}");
                    Assert.AreEqual(river.Points.Count, river.HalfWidths.Count);

                    // The drawn line is smoothed, so a few points may sit just beside the carved channel.
                    int wet = 0;
                    foreach (var p in river.Points)
                    {
                        int gx = Mathf.RoundToInt(p.x / s.worldSize * (data.Resolution - 1));
                        int gz = Mathf.RoundToInt(p.z / s.worldSize * (data.Resolution - 1));
                        if (!data.LandMask[gz, gx]) wet++;
                    }
                    Assert.Greater(wet, river.Points.Count * 3 / 4, $"Seed {seed}: river mostly on dry land");
                }
            }
            Assert.Greater(rivers, 0, "No rivers in 6 islands");
        }

        [Test]
        public void PlayerDropZone_IsOnDryLand()
        {
            var s = MakeSettings(129);
            for (int seed = 0; seed < 15; seed++)
            {
                var data = new IslandGenerator(seed, s).Generate();
                if (data.SpawnZones.Count == 0) continue;
                var c = data.SpawnZones[0].Center;
                Assert.IsTrue(data.LandMask[c.y, c.x], $"Seed {seed}: drop zone in water");
            }
        }

        [Test]
        public void Relief_HasHillsAndFlats()
        {
            var s = MakeSettings(129);
            s.worldSize = 1000f;
            s.maxHeight = 150f;
            s.noiseScale = 0.0045f;
            var data = new IslandGenerator(7, s).Generate();
            int land = 0, hilly = 0, flat = 0;
            float step = s.worldSize / (data.Resolution - 1);
            for (int z = 1; z < data.Resolution - 1; z++)
                for (int x = 1; x < data.Resolution - 1; x++)
                {
                    if (!data.LandMask[z, x]) continue;
                    land++;
                    float slope = ScatterPlanner.SampleSlope(data, x * step, z * step);
                    if (slope > 0.3f) hilly++;
                    if (slope < 0.2f) flat++;
                }
            Assert.Greater(hilly, land / 10, "Island is too flat");
            Assert.Greater(flat, land / 10, "Island has no open ground");
        }
    }
}
