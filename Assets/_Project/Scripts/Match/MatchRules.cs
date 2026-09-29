using UnityEngine;

namespace ProjectFossil.Match
{
    [CreateAssetMenu(menuName = "Project Fossil/Match Rules", fileName = "MatchRules")]
    public class MatchRules : ScriptableObject
    {
        [Header("Timing")]
        public float matchDuration     = 1500f; // hard cap; still on the island at the end = stranded
        public float extractionOpensAt = 300f;  // extraction beacons light up after this
        public float extractionHoldTime = 8f;   // seconds inside a zone to extract

        [Header("Extraction")]
        public float extractionRadius = 8f;

        [Header("Survival income")]
        public float survivalPayoutInterval = 60f;
        public int   survivalPayoutAmount   = 10;

        [Header("Combat income")]
        [Tooltip("Coins per point of damage dealt to dinosaurs, paid on every hit (kills pay their own reward on top)")]
        public float coinsPerDamage = 0.15f;
    }
}
