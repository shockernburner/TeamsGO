using System;
using UnityEngine;

namespace ProjectFossil.Core
{
    // Looks from paid art packs, which live only on the machines that bought them (they're kept out of git).
    // The editor tool "Project Fossil > Art > Set Up Bought Packs" writes this asset into a git-ignored
    // Resources folder. Where it's missing, everything falls back to the free models in the repository.
    public class BoughtArt : ScriptableObject
    {
        [Serializable]
        public class SpeciesLook
        {
            public string          species;
            public ModelDefinition model;
        }

        [Serializable]
        public class BiomeLook
        {
            [Tooltip("Matched against the end of the BiomeDefinition asset's name, e.g. Jungle for BiomeDef_Jungle")]
            public string       biome;
            public GameObject[] trees;
            public GameObject[] rocks;
            public GameObject[] plants;
            public TerrainLayer ground;
        }

        public SpeciesLook[] species;
        public BiomeLook[]   biomes;

        [Header("Ground")]
        public TerrainLayer cliff;
        public TerrainLayer dirt;
        public TerrainLayer moss;

        [Tooltip("Wind zone prefab that bought foliage shaders sway with")]
        public GameObject windZone;

        [Header("Sky")]
        public ModelDefinition flyer;

        [Header("Sizes")]
        [Tooltip("Height of a tree at scatter scale 1, in metres. Bought trees are real-sized, much taller than the old ones.")]
        public float treeHeight = 16f;

        private static BoughtArt _current;
        private static bool      _loaded;

        public static BoughtArt Current
        {
            get
            {
                if (!_loaded)
                {
                    _loaded  = true;
                    _current = Resources.Load<BoughtArt>("BoughtArt");
                }
                return _current;
            }
        }

        // Editor tool: forget the cached asset after rewriting it.
        public static void Reload() => _loaded = false;

        public static ModelDefinition ModelFor(string speciesName)
        {
            var art = Current;
            if (art == null || art.species == null || string.IsNullOrEmpty(speciesName)) return null;
            foreach (var s in art.species)
                if (s != null && s.model != null && s.model.HasModel &&
                    string.Equals(s.species, speciesName, StringComparison.OrdinalIgnoreCase))
                    return s.model;
            return null;
        }

        public static BiomeLook BiomeFor(string biomeAssetName)
        {
            var art = Current;
            if (art == null || art.biomes == null || string.IsNullOrEmpty(biomeAssetName)) return null;
            foreach (var b in art.biomes)
                if (b != null && !string.IsNullOrEmpty(b.biome) &&
                    biomeAssetName.EndsWith(b.biome, StringComparison.OrdinalIgnoreCase))
                    return b;
            return null;
        }

        // A list with at least one model still present (a pack deleted after setup leaves empty slots).
        public static bool Has(GameObject[] models)
        {
            if (models == null) return false;
            foreach (var m in models) if (m != null) return true;
            return false;
        }
    }
}
