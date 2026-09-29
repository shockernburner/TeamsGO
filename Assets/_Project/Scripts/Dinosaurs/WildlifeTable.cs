using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Dinosaurs
{
    // Which animals live on the island and how many. The match spawns groups from this at the start and keeps
    // topping the population up as animals die, so the island never empties out. New animal = new entry.
    [CreateAssetMenu(menuName = "Project Fossil/Wildlife Table", fileName = "Wildlife_New")]
    public class WildlifeTable : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public DinosaurSpecies species;
            [Min(0f)] public float weight = 1f;
            [Min(1)]  public int   groupMin = 1;
            [Min(1)]  public int   groupMax = 1;
            [Tooltip("Seconds into the match before this animal starts appearing in top-ups")]
            public float fromTime = 0f;
        }

        public List<Entry> entries = new List<Entry>();

        [Header("Population")]
        [Tooltip("Groups placed in each spawn zone at the start (the player's drop zone stays empty)")]
        public int   groupsPerZone    = 2;
        [Tooltip("Animals alive at the start of the match stays the floor; this many more are added per minute")]
        public float growthPerMinute  = 1.5f;
        public int   maxAlive         = 45;
        public float topUpInterval    = 20f;
        [Tooltip("Top-ups arrive out of sight: at least this far from the player")]
        public float minSpawnDistance = 70f;
        public float maxSpawnDistance = 140f;

        // Weighted pick among entries available at this time. Null when nothing qualifies.
        public Entry Pick(RNGService rng, float matchTime)
        {
            float total = 0f;
            foreach (var e in entries)
                if (Available(e, matchTime)) total += e.weight;
            if (total <= 0f) return null;

            float roll = rng.NextFloat() * total;
            Entry last = null;
            foreach (var e in entries)
            {
                if (!Available(e, matchTime)) continue;
                last = e;
                roll -= e.weight;
                if (roll <= 0f) return e;
            }
            return last;
        }

        public static int GroupSize(Entry e, RNGService rng)
        {
            int lo = Math.Max(1, e.groupMin), hi = Math.Max(lo, e.groupMax);
            return lo + rng.Next(hi - lo + 1);
        }

        // How many animals should be alive at this point in the match.
        public int TargetAlive(int startCount, float matchTime, float countMultiplier)
        {
            float target = (startCount + growthPerMinute * (matchTime / 60f)) * Math.Max(0.1f, countMultiplier);
            return Math.Min(maxAlive, (int)target);
        }

        private static bool Available(Entry e, float matchTime) =>
            e != null && e.species != null && e.weight > 0f && matchTime >= e.fromTime;
    }
}
