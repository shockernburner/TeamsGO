using System.Collections.Generic;
using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Generation
{
    public enum ScatterKind { Tree, Rock, Plant }

    public struct ScatterInstance
    {
        public ScatterKind Kind;
        public Vector3     WorldPos;  // y is the heightmap estimate; builders snap to the real terrain
        public float       Scale;
        public float       Yaw;       // degrees
        public int         BiomeIndex;
        public int         Variant;   // stable per cell; picks which model of the biome's list to use
    }

    // Decides where trees and rocks go. Pure and deterministic: same island data and seed, same result.
    // Jittered grid (one candidate per cell) keeps things evenly spread without clumping into walls. Biomes with
    // grove contrast then modulate the chance with a slow noise field, so forests come as thick groves (cover)
    // separated by clearings (danger) instead of an even orchard.
    public static class ScatterPlanner
    {
        private const int   SaltSeed   = 0x5CA7;
        private const float GroveScale = 70f; // metres across a typical grove or clearing

        public static List<ScatterInstance> Plan(IslandData data)
        {
            var s      = data.Settings;
            var rng    = new RNGService(unchecked(data.Seed * 31 + SaltSeed));
            var result = new List<ScatterInstance>();
            int solid = 0, plants = 0; // trees+rocks and plants have separate caps
            var biomes = s.biomes;
            if (biomes == null || biomes.Count == 0 || s.scatterCellSize <= 0f) return result;

            float cell      = s.scatterCellSize;
            float cellArea  = cell * cell;
            int   cells     = Mathf.FloorToInt(s.worldSize / cell);
            float clear2    = s.scatterClearance * s.scatterClearance;
            float minHeight = Mathf.Max(s.seaLevel, 0.05f) + 0.01f; // keep off the waterline

            // Clearings: every point of interest plus the player spawn (spawn zone 0).
            var groveRng = new RNGService(unchecked(data.Seed * 31 + SaltSeed + 1));
            float groveX = groveRng.NextFloat() * 1000f, groveZ = groveRng.NextFloat() * 1000f;

            var clearings = new List<Vector3>();
            foreach (var poi in data.PointsOfInterest) clearings.Add(poi.WorldPos);
            if (data.SpawnZones.Count > 0) clearings.Add(data.SpawnZones[0].WorldCenter);

            for (int cz = 0; cz < cells; cz++)
            {
                for (int cx = 0; cx < cells; cx++)
                {
                    // Always draw the same number of values per cell so one change doesn't reshuffle the island.
                    float jx = rng.NextFloat(), jz = rng.NextFloat();
                    float roll = rng.NextFloat();
                    float scaleT = rng.NextFloat();
                    float yaw = rng.NextFloat() * 360f;

                    float x = (cx + jx) * cell;
                    float z = (cz + jz) * cell;

                    float h = SampleHeight(data, x, z);
                    if (h < minHeight || !IsLand(data, x, z)) continue; // not in the sea, rivers or lakes

                    int biome = SampleBiome(data, x, z);
                    var b = biome >= 0 && biome < biomes.Count ? biomes[biome] : null;
                    if (b == null) continue;

                    float grove = b.groveContrast > 0f ? GroveDensity(x, z, groveX, groveZ, b.groveContrast) : 1f;
                    float pTree = b.treesPerHectare * cellArea / 10000f * grove;
                    float pRock = b.rocksPerHectare * cellArea / 10000f;
                    float pPlant = b.plantsPerHectare * cellArea / 10000f * Mathf.Lerp(1f, grove, 0.5f);
                    ScatterKind kind;
                    if (roll < pTree) kind = ScatterKind.Tree;
                    else if (roll < pTree + pRock) kind = ScatterKind.Rock;
                    else if (roll < pTree + pRock + pPlant) kind = ScatterKind.Plant;
                    else continue;

                    if (kind == ScatterKind.Plant ? plants >= s.maxPlantInstances : solid >= s.maxScatterInstances) continue;

                    float slope = SampleSlope(data, x, z);
                    float maxSlope = kind == ScatterKind.Rock ? s.maxTreeSlope * 2f : s.maxTreeSlope;
                    if (slope > maxSlope) continue;

                    if (InClearing(clearings, x, z, clear2)) continue;

                    float scale = kind == ScatterKind.Tree  ? Mathf.Lerp(b.treeScale.x, b.treeScale.y, scaleT)
                                : kind == ScatterKind.Plant ? Mathf.Lerp(b.plantScale.x, b.plantScale.y, scaleT)
                                : Mathf.Lerp(0.6f, 2.2f, scaleT * scaleT); // mostly small rocks, a few boulders
                    if (kind == ScatterKind.Plant) plants++; else solid++;

                    result.Add(new ScatterInstance
                    {
                        Kind       = kind,
                        WorldPos   = new Vector3(x, h * s.maxHeight, z),
                        Scale      = scale,
                        Yaw        = yaw,
                        BiomeIndex = biome,
                        Variant    = CellHash(cx, cz),
                    });
                }
            }
            return result;
        }

        // Density multiplier around 1 on average: up to ~2x inside groves, ~0.15x in clearings at full contrast.
        public static float GroveDensity(float x, float z, float offX, float offZ, float contrast)
        {
            float n = Mathf.PerlinNoise(x / GroveScale + offX, z / GroveScale + offZ);
            float t = Mathf.Clamp01((n - 0.35f) / 0.3f);
            return Mathf.Lerp(1f, Mathf.Lerp(0.15f, 2f, t), Mathf.Clamp01(contrast));
        }

        // Stable, non-negative hash of a cell, so model choice doesn't consume RNG draws.
        private static int CellHash(int cx, int cz)
        {
            unchecked
            {
                uint h = (uint)cx * 73856093u ^ (uint)cz * 19349663u;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                return (int)(h & 0x7fffffff);
            }
        }

        private static bool InClearing(List<Vector3> clearings, float x, float z, float clear2)
        {
            foreach (var c in clearings)
            {
                float dx = c.x - x, dz = c.z - z;
                if (dx * dx + dz * dz < clear2) return true;
            }
            return false;
        }

        // ── Heightmap sampling (heightmap is [row = z, col = x], normalized) ──

        public static float SampleHeight(IslandData data, float worldX, float worldZ)
        {
            int   res = data.Resolution;
            float gx  = Mathf.Clamp(worldX / data.Settings.worldSize * (res - 1), 0f, res - 1.001f);
            float gz  = Mathf.Clamp(worldZ / data.Settings.worldSize * (res - 1), 0f, res - 1.001f);
            int   x0  = (int)gx, z0 = (int)gz;
            float tx  = gx - x0, tz = gz - z0;
            var   hm  = data.Heightmap;
            float a   = Mathf.Lerp(hm[z0, x0],     hm[z0, x0 + 1],     tx);
            float b   = Mathf.Lerp(hm[z0 + 1, x0], hm[z0 + 1, x0 + 1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        public static bool IsLand(IslandData data, float worldX, float worldZ)
        {
            int res = data.Resolution;
            int gx  = Mathf.Clamp(Mathf.RoundToInt(worldX / data.Settings.worldSize * (res - 1)), 0, res - 1);
            int gz  = Mathf.Clamp(Mathf.RoundToInt(worldZ / data.Settings.worldSize * (res - 1)), 0, res - 1);
            return data.LandMask[gz, gx];
        }

        public static int SampleBiome(IslandData data, float worldX, float worldZ)
        {
            int res = data.Resolution;
            int gx  = Mathf.Clamp(Mathf.RoundToInt(worldX / data.Settings.worldSize * (res - 1)), 0, res - 1);
            int gz  = Mathf.Clamp(Mathf.RoundToInt(worldZ / data.Settings.worldSize * (res - 1)), 0, res - 1);
            return data.BiomeMap[gz, gx];
        }

        // Rise over run in world units, from the steepest of the two axes.
        public static float SampleSlope(IslandData data, float worldX, float worldZ)
        {
            float step = data.Settings.worldSize / (data.Resolution - 1);
            float hx = SampleHeight(data, worldX + step, worldZ) - SampleHeight(data, worldX - step, worldZ);
            float hz = SampleHeight(data, worldX, worldZ + step) - SampleHeight(data, worldX, worldZ - step);
            float rise = Mathf.Max(Mathf.Abs(hx), Mathf.Abs(hz)) * data.Settings.maxHeight;
            return rise / (2f * step);
        }
    }
}
