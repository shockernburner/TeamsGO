using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Dinosaurs
{
    [CreateAssetMenu(menuName = "Project Fossil/Dinosaur Species", fileName = "Species_New")]
    public class DinosaurSpecies : ScriptableObject
    {
        [Header("Identity")]
        public string speciesName = "Unknown";
        public Color  debugColor  = Color.green;
        public float  bodyScale   = 1f;   // uniform scale applied to the spawned body
        public ModelDefinition model;     // imported look; empty = placeholder capsule

        [Header("Movement")]
        public float walkSpeed   = 3f;
        public float runSpeed    = 8f;
        public float turnSpeed   = 120f; // degrees/sec for steering

        [Header("Wander")]
        public float wanderRadius   = 20f;
        public float wanderWaitMin  = 2f;
        public float wanderWaitMax  = 6f;

        [Header("Detection")]
        public float sightRange   = 25f;
        [Range(0f, 180f)]
        public float sightAngle   = 110f; // half-angle FOV
        public float hearingRange = 15f;

        [Header("Chase / Combat")]
        public float chaseRange   = 40f;  // give-up distance
        public float attackRange  = 2.5f;
        public float attackDamage = 20f;
        public float attackCooldown = 1.5f;
        public float attackWindup   = 0.45f; // telegraph before the bite lands; stepping out of reach dodges it
        public float hitStagger     = 0.35f; // pause after taking a hit, so fighting back buys time

        [Header("Health / Flee")]
        public float maxHealth          = 60f;
        [Range(0f, 1f)]
        public float fleeHealthFraction = 0.25f; // flee when health drops below this fraction (0 = never)
        public float fleeDistance       = 30f;
        public float fleeDuration       = 6f;

        [Header("Economy")]
        public int killReward = 15; // in-match currency paid to whoever lands the killing blow
    }
}
