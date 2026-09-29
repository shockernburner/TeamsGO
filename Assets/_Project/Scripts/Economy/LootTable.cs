using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Economy
{
    public struct LootDrop
    {
        public ItemDefinition Item;   // null = currency
        public int            Amount;

        public bool IsCurrency => Item == null;
    }

    [CreateAssetMenu(menuName = "Project Fossil/Loot Table", fileName = "Loot_New")]
    public class LootTable : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public ItemDefinition item;          // leave empty for a currency drop
            public int            minAmount = 1;
            public int            maxAmount = 1;
            public float          weight    = 1f;
        }

        [Header("Always dropped")]
        public int guaranteedCurrencyMin = 5;
        public int guaranteedCurrencyMax = 15;

        [Header("Weighted rolls")]
        public int rolls = 1;
        [Range(0f, 1f)]
        public float emptyRollChance = 0.3f; // chance each roll gives nothing
        public List<Entry> entries = new List<Entry>();

        // Deterministic for a given RNG state. Currency drops are merged into one entry.
        public List<LootDrop> Roll(RNGService rng)
        {
            var drops = new List<LootDrop>();
            int currency = RollRange(rng, guaranteedCurrencyMin, guaranteedCurrencyMax);

            float totalWeight = 0f;
            foreach (var e in entries) totalWeight += Mathf.Max(0f, e.weight);

            for (int r = 0; r < rolls; r++)
            {
                if (rng.NextFloat() < emptyRollChance || totalWeight <= 0f) continue;

                float pick = rng.NextFloat() * totalWeight;
                Entry chosen = null;
                foreach (var e in entries)
                {
                    pick -= Mathf.Max(0f, e.weight);
                    if (pick <= 0f) { chosen = e; break; }
                }
                if (chosen == null) chosen = entries[entries.Count - 1];

                int amount = RollRange(rng, chosen.minAmount, chosen.maxAmount);
                if (amount <= 0) continue;

                if (chosen.item == null) currency += amount;
                else drops.Add(new LootDrop { Item = chosen.item, Amount = amount });
            }

            if (currency > 0) drops.Insert(0, new LootDrop { Item = null, Amount = currency });
            return drops;
        }

        private static int RollRange(RNGService rng, int min, int max)
        {
            if (max < min) max = min;
            return rng.Next(min, max + 1);
        }
    }
}
