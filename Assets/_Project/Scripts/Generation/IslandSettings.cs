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
        [Tooltip("The drawn terrain is this many times finer than the generator's grid (4 on a 257 grid over 1 km = "
               + "about 1 m). The generator's rules run on the coarse grid; the fine one smooths its 4 m facets.")]
        [Range(1, 4)] public int terrainDetail = 4;
        [Tooltip("Height of the small bumps added to open ground on the fine terrain, metres.")]
        [Range(0f, 1f)] public float terrainBumps = 0.35f;

        [Header("Noise")]
        public float noiseScale = 0.003f;
        [Range(0.3f, 0.49f)]
        public float islandRadiusFraction = 0.42f;
        public AnimationCurve heightRemap = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("Relief")]
        [Tooltip("Sharp ridges and mountain chains, where the mountain mask allows them")]
        [Range(0f, 1f)] public float ridgeStrength = 0.45f;
        [Tooltip("Ridge noise frequency, relative to the base noise")]
        public float ridgeScale = 2.5f;
        [Tooltip("Small bumps and gullies everywhere")]
        [Range(0f, 0.2f)] public float detailStrength = 0.06f;
        [Tooltip("> 1 keeps lowlands low and makes peaks steep; 1 = unchanged")]
        [Range(1f, 3f)] public float lowlandPower = 1.7f;

        [Header("Volcano")]
        [Range(0f, 1f)] public float volcanoHeight = 0.5f; // added at the summit, as a fraction of maxHeight
        [Tooltip("Base radius as a fraction of the world size")]
        [Range(0f, 0.3f)] public float volcanoRadius = 0.13f;
        [Range(0f, 0.3f)] public float craterDepth = 0.12f;

        [Header("Rivers and lakes")]
        public int   riverCount = 3;
        [Tooltip("River width in metres near the mouth; about half that at the source")]
        public float riverWidth = 12f;
        [Tooltip("Bank steepness (rise over run)")]
        public float riverBankSlope = 0.5f;
        [Tooltip("Steepest a river may run (drop over distance). Steeper stretches have no water: a flat sheet of water down a hillside looked like a blue tarp")]
        public float maxRiverGradient = 0.15f;
        [Tooltip("Rivers start between these normalized heights")]
        public Vector2 riverSourceHeight = new Vector2(0.3f, 0.6f);
        public int   lakeCount = 2;
        public Vector2 lakeRadius = new Vector2(25f, 50f); // metres
        [Tooltip("Water depth in rivers and lakes, metres")]
        public float waterDepth = 2.2f;
        // How far a drawn water surface reaches from its middle: out past where the bank rises above it, so its
        // edge tucks under the ground. The generator keeps the banks that high; the decorator draws this wide.
        public float RiverSurfaceHalfWidth(float channelHalfWidth) =>
            channelHalfWidth + (waterDepth + 0.4f) / Mathf.Max(0.05f, riverBankSlope) + 1f;
        public float LakeSurfaceRadius(float lakeRadius) => RiverSurfaceHalfWidth(lakeRadius);

        [Tooltip("How much wetter the ground is near rivers and lakes (feeds swamps)")]
        [Range(0f, 1f)] public float riverMoisture = 0.3f;

        [Header("Moisture")]
        [Tooltip("Stretches moisture noise away from 0.5 so dry plains and wet swamps both show up")]
        public float moistureContrast = 2.2f;

        [Header("Biomes")]
        public List<BiomeDefinition> biomes = new();

        [Header("Points of Interest")]
        public int extractionZoneCount = 2;
        public int lootCacheCount = 8;
        public int ruinsCount = 4;
        public float minPOISpacing = 120f;
        [Tooltip("Ruin stashes (a chest between standing columns) only go where the ground is this flat, in degrees; " +
                 "one that finds no such spot becomes a supply cache instead, so the loot total stays the same.")]
        public float ruinsMaxSlope = 15f;

        [Header("Spawning")]
        public int spawnZoneCount = 6;

        [Header("Water")]
        [Tooltip("Sea level as a fraction of maxHeight. Land starts at 0.05.")]
        [Range(0f, 0.2f)] public float seaLevel = 0.045f;

        [Header("Scatter (trees and rocks)")]
        [Tooltip("One candidate per cell; smaller cells = denser possible placement.")]
        public float scatterCellSize = 7f;
        public int   maxScatterInstances = 2000; // trees and rocks
        public int   maxPlantInstances   = 3000; // cosmetic ground cover, counted separately
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
