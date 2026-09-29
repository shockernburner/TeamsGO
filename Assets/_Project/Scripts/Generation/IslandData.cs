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

    // A river's centre line, source to mouth. Point y is the water surface in world units; it only ever falls.
    public class RiverPath
    {
        public readonly List<Vector3> Points     = new List<Vector3>();
        public readonly List<float>   HalfWidths = new List<float>();

        // Rounds off the grid steps so the water ribbon doesn't zigzag. Keeps the ends and the falling surface.
        public void Smooth(int radius)
        {
            if (Points.Count < 3 || radius <= 0) return;
            var smoothed = new List<Vector3>(Points.Count);
            for (int i = 0; i < Points.Count; i++)
            {
                float x = 0f, z = 0f; int n = 0;
                for (int k = i - radius; k <= i + radius; k++)
                {
                    if (k < 0 || k >= Points.Count) continue;
                    x += Points[k].x; z += Points[k].z; n++;
                }
                smoothed.Add(new Vector3(x / n, Points[i].y, z / n));
            }
            Points.Clear();
            Points.AddRange(smoothed);
        }
    }

    // A round lake. Center y is the water surface in world units.
    public struct Lake
    {
        public Vector3 Center;
        public float   Radius;
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
        public IReadOnlyList<RiverPath> Rivers { get; }
        public IReadOnlyList<Lake> Lakes { get; }

        public IslandData(int seed, IslandSettings settings, float[,] heightmap, int[,] biomeMap,
            bool[,] landMask, List<PointOfInterest> pois, List<SpawnZone> spawnZones,
            List<RiverPath> rivers = null, List<Lake> lakes = null)
        {
            Seed = seed;
            Settings = settings;
            Resolution = heightmap.GetLength(0);
            Heightmap = heightmap;
            BiomeMap = biomeMap;
            LandMask = landMask;
            PointsOfInterest = pois.AsReadOnly();
            SpawnZones = spawnZones.AsReadOnly();
            Rivers = (rivers ?? new List<RiverPath>()).AsReadOnly();
            Lakes = (lakes ?? new List<Lake>()).AsReadOnly();
        }
    }
}
