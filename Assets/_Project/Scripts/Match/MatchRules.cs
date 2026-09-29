using UnityEngine;

namespace ProjectFossil.Match
{
    [CreateAssetMenu(menuName = "Project Fossil/Match Rules", fileName = "MatchRules")]
    public class MatchRules : ScriptableObject
    {
        [Header("Timing")]
        public float matchDuration     = 1500f; // hard cap; still on the island at the end = stranded
        public float extractionOpensAt = 300f;  // extraction beacons light up after this
        public float extractionHoldTime = 15f;  // seconds holding the pad while the helicopter takes you aboard

        [Header("Extraction")]
        public float extractionRadius = 8f;
        [Tooltip("When extraction opens and every beacon is farther than this, a rescue flare adds one near the player")]
        public float flareIfFartherThan = 150f;
        [Tooltip("How far from the player the rescue flare lands (min, max metres)")]
        public Vector2 flareDistance = new Vector2(70f, 110f);

        [Header("The stalker")]
        [Tooltip("Threat sent early on to stalk the player by scent (empty = none). Free: it's the island's opening "
               + "act, not a director purchase.")]
        public string stalkerThreatId = "ironjaw";
        public float  stalkerAt       = 110f;  // seconds into the match
        public float  stalkerDistance = 130f;  // metres from the player it starts

        [Header("Final stand")]
        [Tooltip("Sent at whoever starts boarding the helicopter (empty = none); the stalker joins in too")]
        public string finalWaveThreatId = "raptor_pack";
        public float  finalWaveDistance = 45f;

        [Header("Survival income")]
        public float survivalPayoutInterval = 60f;
        public int   survivalPayoutAmount   = 10;

        [Header("Combat income")]
        [Tooltip("Coins per point of damage dealt to dinosaurs, paid on every hit (kills pay their own reward on top)")]
        public float coinsPerDamage = 0.15f;
    }
}
