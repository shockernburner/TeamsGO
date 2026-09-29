using UnityEngine;

namespace ProjectFossil.Director
{
    // SpawnHunters: dinosaurs that hunt the target down.
    // Stampede: a panicked herd charges through the target's position and tramples whatever is in the way.
    public enum ThreatKind { SpawnHunters, Stampede }

    // One purchasable threat. New threat = new asset. The AI director and (later) rival teams buy from the same catalog.
    [CreateAssetMenu(menuName = "Project Fossil/Threat", fileName = "Threat_New")]
    public class ThreatDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string threatId     = "threat";
        public string displayName  = "Threat";
        [TextArea]
        public string announcement = "Something is coming...";

        [Header("Purchase rules")]
        public int   cost       = 50;
        public float cooldown   = 60f; // seconds before the same buyer can buy it again
        public float unlockTime = 0f;  // seconds into the match before it can be bought
        public float aiWeight   = 1f;  // how often the AI director picks it among affordable threats

        [Header("Effect")]
        public ThreatKind       kind          = ThreatKind.SpawnHunters;
        public GameObject       spawnPrefab;          // empty = the match's default dinosaur prefab
        public ScriptableObject speciesOverride;      // e.g. a DinosaurSpecies; empty = prefab's own
        public int              spawnCount    = 1;
        public float            spawnDistance = 45f;  // spawn this far from the target, out of sight
        public float            trampleDamage = 25f;  // Stampede only: damage to anyone a runner hits
    }
}
