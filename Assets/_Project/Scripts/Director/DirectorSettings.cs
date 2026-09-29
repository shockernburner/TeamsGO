using UnityEngine;

namespace ProjectFossil.Director
{
    // Tuning for the AI-controlled director. Escalation comes from income rising over the match
    // and from stronger threats unlocking later (ThreatDefinition.unlockTime).
    [CreateAssetMenu(menuName = "Project Fossil/Director Settings", fileName = "DirectorSettings")]
    public class DirectorSettings : ScriptableObject
    {
        [Header("Budget")]
        public int   startingBudget      = 0;
        public float startIncomePerSecond = 0.5f;
        public float endIncomePerSecond   = 3f;
        [Tooltip("1 = linear ramp; >1 = slow start, sharp finish")]
        public float escalationExponent   = 1.5f;

        [Header("Pacing")]
        public float gracePeriod         = 45f; // no threats before this
        public float minSecondsBetween   = 40f; // breathing room between threats
        public float decisionInterval    = 5f;  // how often the director considers buying
        [Range(0f, 1f)]
        [Tooltip("Chance to hold money when a pricier threat is unlocked but not yet affordable")]
        public float saveUpChance        = 0.5f;

        [Header("Adaptive difficulty (see AdaptiveDifficulty)")]
        public float minIntensity             = 0.6f;
        public float maxIntensity             = 2.5f;
        public float intensityChangePerSecond = 0.05f; // how fast it follows the player's form
        public float killWindow               = 120f;  // kills inside this many seconds count as "recent"
        public int   killsForFullBoost        = 6;
        public float killBoost                = 0.6f;
        public float calmSeconds              = 60f;   // unhurt this long at high health...
        public float calmBoost                = 0.2f;  // ...adds this
        [Range(0f, 1f)]
        public float hurtThreshold            = 0.35f; // below this health fraction...
        public float hurtRelief               = 0.4f;  // ...ease off by this much

        [Header("Threat size")]
        [Tooltip("Extra hunters per threat at the top intensity, as a fraction of the threat's own count")]
        public float extraSpawnAtMax          = 0.6f;

        // How many of a threat's hunters to send at a given intensity. Never fewer than the threat asks for.
        public int ScaledSpawnCount(int baseCount, float intensity)
        {
            if (baseCount <= 0) return 1;
            float t = Mathf.Clamp01((intensity - 1f) / Mathf.Max(0.01f, maxIntensity - 1f));
            return baseCount + Mathf.FloorToInt(baseCount * extraSpawnAtMax * t + 0.001f);
        }

        public float IncomeAt(float normalizedTime)
        {
            float t = Mathf.Clamp01(normalizedTime);
            return Mathf.Lerp(startIncomePerSecond, endIncomePerSecond, Mathf.Pow(t, Mathf.Max(0.01f, escalationExponent)));
        }
    }
}
