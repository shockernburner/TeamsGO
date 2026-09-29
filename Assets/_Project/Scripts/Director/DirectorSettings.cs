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

        public float IncomeAt(float normalizedTime)
        {
            float t = Mathf.Clamp01(normalizedTime);
            return Mathf.Lerp(startIncomePerSecond, endIncomePerSecond, Mathf.Pow(t, Mathf.Max(0.01f, escalationExponent)));
        }
    }
}
