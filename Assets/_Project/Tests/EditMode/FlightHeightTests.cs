using System.Collections.Generic;
using NUnit.Framework;
using ProjectFossil.Core;
using ProjectFossil.Generation;
using UnityEngine;

namespace ProjectFossil.Tests.EditMode
{
    // Flying things clear the island: on game-sized islands across many seeds, a flock circling and drifting the
    // way Environment/Pterosaurs flies (wanting to sit low) never comes closer than FlightHeight.HardMinimum to
    // the ground below it, and is back at full clearance once over level ground.
    public class FlightHeightTests
    {
        private static IslandSettings GameSettings()
        {
            var s = ScriptableObject.CreateInstance<IslandSettings>();
            s.resolution = 257; s.worldSize = 1000f; s.maxHeight = 150f; s.noiseScale = 0.0045f;
            s.islandRadiusFraction = 0.42f; s.heightRemap = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            s.extractionZoneCount = 2; s.lootCacheCount = 4; s.ruinsCount = 2; s.spawnZoneCount = 3; s.minPOISpacing = 50f;
            s.riverCount = 5; s.lakeCount = 6; s.terrainDetail = 4;
            var b = ScriptableObject.CreateInstance<BiomeDefinition>();
            b.biomeName = "Everywhere"; b.minHeight = 0f; b.maxHeight = 1f; b.minMoisture = 0f; b.maxMoisture = 1f;
            s.biomes = new List<BiomeDefinition> { b };
            return s;
        }

        [Test]
        public void Flocks_NeverFlyThroughTheIsland()
        {
            var s = GameSettings();
            const float dt = 1f / 30f;
            int checks = 0;
            float closest = float.MaxValue;
            for (int seed = 0; seed < 6; seed++)
            {
                var world = WaterField.Build(new IslandGenerator(seed, s).Generate());
                var rng = new System.Random(seed);
                for (int flock = 0; flock < 6; flock++)
                {
                    // Low on purpose: only the ground rule keeps it up.
                    var centre = new Vector3((float)rng.NextDouble() * 800f + 100f, 0f, (float)rng.NextDouble() * 800f + 100f);
                    float radius = 25f + (float)rng.NextDouble() * 40f, speed = 12f, angle = 0f;
                    var drift = new Vector3((float)rng.NextDouble() * 3f - 1.5f, 0f, (float)rng.NextDouble() * 3f - 1.5f);
                    float wanted = world.GroundAt(centre) + 5f, y = float.NaN;
                    for (int step = 0; step < 30 * 40; step++) // 40 seconds
                    {
                        angle  += speed / radius * dt;
                        centre += drift * dt;
                        var pos = centre + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                        var heading = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                        y = FlightHeight.Step(world, pos, heading, y, wanted, dt);
                        float above = y - Mathf.Max(world.GroundAt(pos), world.WaterSurfaceAt(pos));
                        closest = Mathf.Min(closest, above);
                        checks++;
                        Assert.GreaterOrEqual(above, FlightHeight.HardMinimum - 1e-3f,
                            $"seed {seed} flock {flock} at {pos}: {above:0.0} m over the ground");
                    }
                }
            }
            Assert.Greater(checks, 1000);
            Debug.Log($"[FlightHeightTests] {checks} positions, closest {closest:0.0} m over the ground");
        }

        [Test]
        public void Floor_LooksAheadAlongTheHeading()
        {
            // A wall of ground 40 m ahead counts before the flyer reaches it; behind it doesn't.
            int n = 33; float cell = 5f;
            var ground = new float[n * n];
            var water = new float[n * n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    ground[z * n + x] = x >= 24 ? 80f : 0f;
                    water[z * n + x] = float.NaN;
                }
            var world = new IslandWorld(n, cell, -5f, ground, water);
            var pos = new Vector3(14 * cell, 0f, 16 * cell); // 50 m short of the wall
            Assert.Greater(FlightHeight.Floor(world, pos, Vector3.right), 70f);
            Assert.Less(FlightHeight.Floor(world, pos, Vector3.left), 1f);
        }
    }
}
