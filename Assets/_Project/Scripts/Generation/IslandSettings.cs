using System.Collections.Generic;
using UnityEngine;

namespace ProjectFossil.Generation
{
    [CreateAssetMenu(menuName = "Project Fossil/Island Settings", fileName = "IslandSettings")]
    public class IslandSettings : ScriptableObject
    {
        [Header("Terrain")]
        [Tooltip("Heightmap resolution — must be 2^n+1 (e.g. 65, 129, 257, 513).")]
        public int resolution = 257;
        public float worldSize = 1000f;
        public float maxHeight = 150f;

        [Header("Noise")]
        public float noiseScale = 0.003f;
        [Range(0.3f, 0.49f)]
        public float islandRadiusFraction = 0.42f;
        public AnimationCurve heightRemap = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("Biomes")]
        public List<BiomeDefinition> biomes = new();

        [Header("Points of Interest")]
        public int extractionZoneCount = 2;
        public int lootCacheCount = 8;
        public int ruinsCount = 4;
        public float minPOISpacing = 120f;

        [Header("Spawning")]
        public int spawnZoneCount = 6;

        [Header("Water")]
        [Tooltip("Sea level as a fraction of maxHeight. Land starts at 0.05.")]
        [Range(0f, 0.2f)] public float seaLevel = 0.045f;

        [Header("Scatter (trees and rocks)")]
        [Tooltip("One candidate per cell; smaller cells = denser possible placement.")]
        public float scatterCellSize = 7f;
        public int   maxScatterInstances = 2000;
        [Tooltip("Metres kept clear around points of interest and the player spawn.")]
        public float scatterClearance = 14f;
        [Tooltip("Max rise over run for trees (0.7 is about 35 degrees). Rocks allow twice this.")]
        public float maxTreeSlope = 0.7f;

        private void OnEnable()
        {
            if (heightRemap == null || heightRemap.keys.Length == 0)
                heightRemap = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }
    }
}
