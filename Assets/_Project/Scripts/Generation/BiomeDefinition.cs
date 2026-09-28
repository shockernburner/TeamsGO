using UnityEngine;

namespace ProjectFossil.Generation
{
    [CreateAssetMenu(menuName = "Project Fossil/Biome Definition", fileName = "BiomeDef_")]
    public class BiomeDefinition : ScriptableObject
    {
        public string biomeName = "Unnamed";

        [Header("Classification thresholds (normalized 0–1)")]
        [Range(0f, 1f)] public float minHeight = 0f;
        [Range(0f, 1f)] public float maxHeight = 1f;
        [Range(0f, 1f)] public float minMoisture = 0f;
        [Range(0f, 1f)] public float maxMoisture = 1f;

        [Header("Debug")]
        public Color debugColor = Color.green;
    }
}
