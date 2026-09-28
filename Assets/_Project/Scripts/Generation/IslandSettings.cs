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

        private void OnEnable()
        {
            if (heightRemap == null || heightRemap.keys.Length == 0)
                heightRemap = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }
    }
}
