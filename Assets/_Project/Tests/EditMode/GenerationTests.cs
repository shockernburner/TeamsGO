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
    }
}
