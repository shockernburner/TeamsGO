using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ProjectFossil.Generation;

namespace ProjectFossil.Tests.EditMode
{
    // Scatter planning on hand-built islands (no noise), so each rule can be checked in isolation.
    public class ScatterTests
    {
        private const int Res = 33;

        private static IslandSettings Settings(float trees, float rocks)
        {
            var b = ScriptableObject.CreateInstance<BiomeDefinition>();
            b.treesPerHectare = trees;
            b.rocksPerHectare = rocks;
            b.treeScale       = new Vector2(1f, 1f);

            var s = ScriptableObject.CreateInstance<IslandSettings>();
            s.resolution          = Res;
            s.worldSize           = 320f;
            s.maxHeight           = 100f;
            s.seaLevel            = 0.045f;
            s.scatterCellSize     = 8f;
            s.maxScatterInstances = 5000;
            s.scatterClearance    = 20f;
            s.maxTreeSlope        = 0.7f;
            s.biomes              = new List<BiomeDefinition> { b };
            return s;
        }

        // Flat plateau at 30% height with a sea border of `border` cells.
        private static IslandData Island(IslandSettings s, int seed, int border = 4, List<PointOfInterest> pois = null)
        {
            var h = new float[Res, Res];
            var biome = new int[Res, Res];
            var land = new bool[Res, Res];
            for (int z = 0; z < Res; z++)
                for (int x = 0; x < Res; x++)
                {
                    bool inside = x >= border && z >= border && x < Res - border && z < Res - border;
                    h[z, x] = inside ? 0.3f : 0f;
                    land[z, x] = inside;
                }
            return new IslandData(seed, s, h, biome, land, pois ?? new List<PointOfInterest>(), new List<SpawnZone>());
        }

        [Test]
        public void SameSeed_SamePlan()
        {
            var s = Settings(40f, 10f);
            var a = ScatterPlanner.Plan(Island(s, 1234));
            var b = ScatterPlanner.Plan(Island(s, 1234));
            Assert.That(a.Count, Is.GreaterThan(0));
            Assert.That(b.Count, Is.EqualTo(a.Count));
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(b[i].WorldPos.x, Is.EqualTo(a[i].WorldPos.x));
                Assert.That(b[i].WorldPos.z, Is.EqualTo(a[i].WorldPos.z));
                Assert.That(b[i].Kind, Is.EqualTo(a[i].Kind));
            }
        }

        [Test]
        public void DifferentSeed_DifferentPlan()
        {
            var s = Settings(40f, 10f);
            var a = ScatterPlanner.Plan(Island(s, 1));
            var b = ScatterPlanner.Plan(Island(s, 2));
            bool differs = a.Count != b.Count;
            for (int i = 0; !differs && i < a.Count; i++)
                differs = a[i].WorldPos.x != b[i].WorldPos.x;
            Assert.That(differs, Is.True);
        }

        [Test]
        public void NothingInTheSea()
        {
            var s = Settings(200f, 50f);
            var data = Island(s, 7, border: 8);
            foreach (var inst in ScatterPlanner.Plan(data))
                Assert.That(ScatterPlanner.SampleHeight(data, inst.WorldPos.x, inst.WorldPos.z),
                            Is.GreaterThan(s.seaLevel), $"{inst.Kind} at {inst.WorldPos.x},{inst.WorldPos.z}");
        }

        [Test]
        public void PointsOfInterest_StayClear()
        {
            var s = Settings(400f, 100f);
            var poi = new PointOfInterest { WorldPos = new Vector3(160f, 30f, 160f), Type = POIType.LootCache };
            var plan = ScatterPlanner.Plan(Island(s, 99, pois: new List<PointOfInterest> { poi }));
            Assert.That(plan.Count, Is.GreaterThan(50));
            foreach (var inst in plan)
            {
                float dx = inst.WorldPos.x - 160f, dz = inst.WorldPos.z - 160f;
                Assert.That(dx * dx + dz * dz, Is.GreaterThanOrEqualTo(s.scatterClearance * s.scatterClearance));
            }
        }

        [Test]
        public void ZeroDensity_PlacesNothing()
        {
            Assert.That(ScatterPlanner.Plan(Island(Settings(0f, 0f), 5)).Count, Is.EqualTo(0));
        }

        [Test]
        public void Density_RoughlyMatchesPerHectare()
        {
            // Plateau is (33 - 8 cells) * 10 m = 250 m square, about 6.25 ha. 20 trees/ha gives about 125.
            var s = Settings(20f, 0f);
            int trees = ScatterPlanner.Plan(Island(s, 42)).Count;
            Assert.That(trees, Is.InRange(70, 180));
        }

        [Test]
        public void Cap_IsRespected()
        {
            var s = Settings(400f, 0f);
            s.maxScatterInstances = 30;
            Assert.That(ScatterPlanner.Plan(Island(s, 3)).Count, Is.EqualTo(30));
        }

        [Test]
        public void Plants_OnlyWhenBiomeHasThem_AndDoNotChangeTreesOrRocks()
        {
            var s = Settings(20f, 5f);
            var before = ScatterPlanner.Plan(Island(s, 77));
            Assert.That(before.Exists(i => i.Kind == ScatterKind.Plant), Is.False);

            s.biomes[0].plantsPerHectare = 60f;
            s.biomes[0].plantScale = new Vector2(1f, 1f);
            var after = ScatterPlanner.Plan(Island(s, 77));
            var solid = after.FindAll(i => i.Kind != ScatterKind.Plant);
            Assert.That(after.Count, Is.GreaterThan(solid.Count), "plants were added");
            Assert.That(solid.Count, Is.EqualTo(before.Count), "trees and rocks are untouched");
            for (int i = 0; i < solid.Count; i++)
                Assert.That(solid[i].WorldPos.x, Is.EqualTo(before[i].WorldPos.x));
        }

        [Test]
        public void PlantCap_IsSeparateFromTreeCap()
        {
            var s = Settings(20f, 0f);
            s.biomes[0].plantsPerHectare = 200f;
            s.maxPlantInstances = 5;
            var plan = ScatterPlanner.Plan(Island(s, 3));
            Assert.That(plan.FindAll(i => i.Kind == ScatterKind.Plant).Count, Is.EqualTo(5));
            Assert.That(plan.FindAll(i => i.Kind == ScatterKind.Tree).Count, Is.GreaterThan(0));
        }

        [Test]
        public void Variant_IsNonNegativeAndStable()
        {
            var s = Settings(40f, 10f);
            var a = ScatterPlanner.Plan(Island(s, 9));
            var b = ScatterPlanner.Plan(Island(s, 9));
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(a[i].Variant, Is.GreaterThanOrEqualTo(0));
                Assert.That(b[i].Variant, Is.EqualTo(a[i].Variant));
            }
        }

        [Test]
        public void GroveContrast_ClumpsTreesIntoGrovesAndClearings()
        {
            // Same average density; with contrast the trees gather into groves, leaving open ground between.
            var even = Settings(trees: 60f, rocks: 0f);
            var clumped = Settings(trees: 60f, rocks: 0f);
            clumped.biomes[0].groveContrast = 1f;

            float evenSpread = 0f, clumpedSpread = 0f;
            int evenTrees = 0, clumpedTrees = 0;
            for (int seed = 1; seed <= 6; seed++)
            {
                evenSpread    += BlockSpread(ScatterPlanner.Plan(Island(even, seed)), out int a);
                clumpedSpread += BlockSpread(ScatterPlanner.Plan(Island(clumped, seed)), out int b);
                evenTrees += a; clumpedTrees += b;
            }
            Assert.Greater(clumpedSpread, evenSpread * 1.5f, "groves and clearings, not an even orchard");
            Assert.That(clumpedTrees, Is.InRange(evenTrees * 0.6f, evenTrees * 1.6f), "roughly as many trees overall");
        }

        // Variance of the tree count per 40 m block: high when trees clump together.
        private static float BlockSpread(List<ScatterInstance> plan, out int trees)
        {
            var counts = new Dictionary<(int, int), int>();
            trees = 0;
            foreach (var p in plan)
            {
                if (p.Kind != ScatterKind.Tree) continue;
                var key = (Mathf.FloorToInt(p.WorldPos.x / 40f), Mathf.FloorToInt(p.WorldPos.z / 40f));
                counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
                trees++;
            }
            // Blocks well inside the plateau (sea border is 4 cells of 10 m).
            float sum = 0f, sq = 0f; int n = 0;
            for (int x = 1; x < 7; x++)
            for (int z = 1; z < 7; z++)
            {
                counts.TryGetValue((x, z), out int c);
                sum += c; sq += c * c; n++;
            }
            float mean = sum / n;
            return sq / n - mean * mean;
        }
    }
}
