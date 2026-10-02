using UnityEngine;

namespace ProjectFossil.Generation
{
    public enum TreeShape { Round, Tall, Dead }

    [CreateAssetMenu(menuName = "Project Fossil/Biome Definition", fileName = "BiomeDef_")]
    public class BiomeDefinition : ScriptableObject
    {
        public string biomeName = "Unnamed";

        [Header("Classification thresholds (normalized 0–1)")]
        [Range(0f, 1f)] public float minHeight = 0f;
        [Range(0f, 1f)] public float maxHeight = 1f;
        [Range(0f, 1f)] public float minMoisture = 0f;
        [Range(0f, 1f)] public float maxMoisture = 1f;

        [Header("Look")]
        public Color groundColor  = new Color(0.35f, 0.45f, 0.2f);
        public Color foliageColor = new Color(0.2f, 0.45f, 0.15f);
        public Color rockColor    = new Color(0.45f, 0.43f, 0.4f);
        [Tooltip("Colour of the short grass drawn on the terrain (the blades' tint; patches vary towards a drier shade).")]
        public Color grassColor   = new Color(0.42f, 0.52f, 0.22f);
        [Tooltip("How thickly short grass covers open ground here: 0 = none (sand, ash), 1 = a full meadow. "
               + "Thinner under trees and gone on cliffs.")]
        [Range(0f, 1f)] public float grassCover = 0.5f;

        [Header("Scatter (instances per hectare, 100 m x 100 m)")]
        [Min(0f)] public float treesPerHectare = 0f;
        [Min(0f)] public float rocksPerHectare = 0f;
        [Tooltip("Tree size range (uniform scale; 1 = about 7 m tall).")]
        public Vector2 treeScale = new Vector2(0.8f, 1.3f);
        [Tooltip("Tall thin trees (plains/volcanic) vs round bushy crowns (jungle/swamp).")]
        public TreeShape treeShape = TreeShape.Round;
        [Min(0f)] public float plantsPerHectare = 0f; // ferns, bushes, tall grass; cosmetic, no collision
        [Tooltip("Plant size range (uniform scale; 1 = about 1.2 m tall).")]
        public Vector2 plantScale = new Vector2(0.8f, 1.6f);
        [Tooltip("0 = trees spread evenly. 1 = packed into thick groves with open clearings between them, at the same "
               + "average count. Groves are where players hide; clearings are what they have to cross.")]
        [Range(0f, 1f)] public float groveContrast = 0f;

        [Header("Models (empty = placeholder primitives; plants need models)")]
        public GameObject[] treeModels;
        public GameObject[] rockModels;
        public GameObject[] plantModels;

        [Header("Debug")]
        public Color debugColor = Color.green;
    }
}
