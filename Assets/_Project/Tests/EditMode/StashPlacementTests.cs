using System.Collections.Generic;
using NUnit.Framework;
using ProjectFossil.Generation;
using UnityEngine;

namespace ProjectFossil.Tests.EditMode
{
    // Ruin stashes (a chest between standing columns) only on flat enough ground; on game-sized islands across many
    // seeds every one stays under IslandSettings.ruinsMaxSlope, and the loot total never drops (a stash that finds
    // no flat spot becomes a supply cache).
    public class StashPlacementTests
    {
        private static IslandSettings GameSettings()
        {
            var s = ScriptableObject.CreateInstance<IslandSettings>();
            s.resolution = 257; s.worldSize = 1000f; s.maxHeight = 150f; s.noiseScale = 0.0045f;
            s.islandRadiusFraction = 0.42f; s.heightRemap = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            s.extractionZoneCount = 2; s.lootCacheCount = 8; s.ruinsCount = 4; s.spawnZoneCount = 3; s.minPOISpacing = 120f;
            s.riverCount = 5; s.lakeCount = 6; s.terrainDetail = 4;
            var b = ScriptableObject.CreateInstance<BiomeDefinition>();
            b.biomeName = "Everywhere"; b.minHeight = 0f; b.maxHeight = 1f; b.minMoisture = 0f; b.maxMoisture = 1f;
            s.biomes = new List<BiomeDefinition> { b };
            return s;
        }

        [Test]
        public void RuinStashes_StandOnFlatGround_AndLootTotalsHold()
        {
            var s = GameSettings();
            float cell = s.worldSize / (s.resolution - 1);
            int ruins = 0, caches = 0, fallbacks = 0;
            float steepest = 0f, steepestDrawn = 0f;
            for (int seed = 0; seed < 24; seed++)
            {
                var data = new IslandGenerator(seed, s).Generate();
                int r = 0, c = 0;
                foreach (var poi in data.PointsOfInterest)
                {
                    if (poi.Type == POIType.LootCache) c++;
                    if (poi.Type != POIType.Ruins) continue;
                    r++;
                    float slope = IslandGenerator.SlopeDegrees(data.Heightmap, poi.GridPos.x, poi.GridPos.y, cell, s.maxHeight);
                    steepest = Mathf.Max(steepest, slope);
                    Assert.LessOrEqual(slope, s.ruinsMaxSlope + 1e-3f, $"seed {seed}: ruin stash on a {slope:0} degree slope");
                }
                Assert.AreEqual(s.lootCacheCount + s.ruinsCount, r + c, $"seed {seed}: loot spots went missing");
                fallbacks += s.ruinsCount - r;
                ruins += r; caches += c;

                // And on the ground as drawn (rivers, lakes and banks reshape it a little): no stash on a hillside.
                if (seed < 6)
                {
                    var world = WaterField.Build(data);
                    foreach (var poi in data.PointsOfInterest)
                    {
                        if (poi.Type != POIType.Ruins) continue;
                        steepestDrawn = Mathf.Max(steepestDrawn, DrawnSlope(world, poi.WorldPos));
                    }
                }
            }
            Assert.Greater(ruins, 24 * s.ruinsCount / 2, "most islands should still have ruin stashes");
            Assert.LessOrEqual(steepestDrawn, s.ruinsMaxSlope + 10f, "the drawn ground should agree with the placement");
            Debug.Log($"[StashPlacementTests] {ruins} ruin stashes (steepest {steepest:0.0} deg, drawn {steepestDrawn:0.0}), " +
                      $"{caches} supply caches, {fallbacks} stashes became caches");
        }

        private static float DrawnSlope(ProjectFossil.Core.IslandWorld world, Vector3 at)
        {
            float steepest = 0f, r = IslandGenerator.StashReach;
            foreach (var d in new[] { new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(0.707f, 0, 0.707f), new Vector3(0.707f, 0, -0.707f) })
                steepest = Mathf.Max(steepest, Mathf.Abs(world.GroundAt(at + d * r) - world.GroundAt(at - d * r)) / (2f * r));
            return Mathf.Atan(steepest) * Mathf.Rad2Deg;
        }
    }
}
