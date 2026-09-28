using System.Collections.Generic;
using UnityEngine;

namespace ProjectFossil.Generation
{
    public enum POIType { LootCache, Ruins, ExtractionZone }

    public struct PointOfInterest
    {
        public Vector2Int GridPos;
        public Vector3 WorldPos;
        public POIType Type;
    }

    public struct SpawnZone
    {
        public Vector2Int Center;
        public Vector3 WorldCenter;
        public float WorldRadius;
        public int BiomeIndex;
        public float Weight;
    }

    public class IslandData
    {
        public int Seed { get; }
        public int Resolution { get; }
        public IslandSettings Settings { get; }

        // [row, col] = [y, x], normalized 0–1
        public float[,] Heightmap { get; }
        public int[,] BiomeMap { get; }
        public bool[,] LandMask { get; }

        public IReadOnlyList<PointOfInterest> PointsOfInterest { get; }
        public IReadOnlyList<SpawnZone> SpawnZones { get; }

        public IslandData(int seed, IslandSettings settings, float[,] heightmap, int[,] biomeMap,
            bool[,] landMask, List<PointOfInterest> pois, List<SpawnZone> spawnZones)
        {
            Seed = seed;
            Settings = settings;
            Resolution = heightmap.GetLength(0);
            Heightmap = heightmap;
            BiomeMap = biomeMap;
            LandMask = landMask;
            PointsOfInterest = pois.AsReadOnly();
            SpawnZones = spawnZones.AsReadOnly();
        }
    }
}
