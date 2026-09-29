using UnityEngine;
using UnityEngine.AI;
using ProjectFossil.Director;
using ProjectFossil.Dinosaurs;

namespace ProjectFossil.Match
{
    // Carries out threats the director sold. Kept separate from the director so the director stays pure
    // (and so a server can run this while clients only hear about it).
    public class ThreatExecutor
    {
        private readonly GameObject _defaultDinosaurPrefab;
        private readonly Transform  _parent;

        // Set by the match from the player's rank.
        public float HealthMultiplier { get; set; } = 1f;
        public float DamageMultiplier { get; set; } = 1f;

        public ThreatExecutor(GameObject defaultDinosaurPrefab, Transform parent)
        {
            _defaultDinosaurPrefab = defaultDinosaurPrefab;
            _parent                = parent;
        }

        // count: how many animals to send (the threat's own count, scaled by the director's intensity).
        public void Execute(ThreatEvent e, int count)
        {
            switch (e.Threat.kind)
            {
                case ThreatKind.SpawnHunters: Spawn(e, count, stampede: false); break;
                case ThreatKind.Stampede:     Spawn(e, count, stampede: true);  break;
            }
        }

        private void Spawn(ThreatEvent e, int count, bool stampede)
        {
            var def    = e.Threat;
            var prefab = def.spawnPrefab != null ? def.spawnPrefab : _defaultDinosaurPrefab;
            if (prefab == null)
            {
                Debug.LogWarning($"[ThreatExecutor] No prefab for threat '{def.threatId}'.");
                return;
            }

            // Pack arrives together from one direction, out of sight.
            float   baseAngle = Random.Range(0f, 360f);
            Vector3 center    = e.Target.Position + Quaternion.Euler(0f, baseAngle, 0f) * Vector3.forward * def.spawnDistance;

            // Hunters bunch up; a stampede comes as a wide wall of running animals.
            Vector3 side = Vector3.Cross(Vector3.up, (e.Target.Position - center).normalized);
            for (int i = 0; i < Mathf.Max(1, count); i++)
            {
                Vector2 jitter = Random.insideUnitCircle * 4f;
                if (stampede) jitter += new Vector2(side.x, side.z) * ((i - (count - 1) * 0.5f) * 3.5f);
                Vector3 pos    = center + new Vector3(jitter.x, 0f, jitter.y);
                if (!NavMesh.SamplePosition(pos, out var hit, def.spawnDistance, NavMesh.AllAreas))
                    continue;

                Vector3 look = e.Target.Position - hit.position;
                look.y = 0f;
                var rot = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity;

                var go = Object.Instantiate(prefab, hit.position, rot, _parent);
                go.name = $"Threat_{def.threatId}_{i}";

                var ai = go.GetComponent<DinosaurAI>();
                if (ai == null) continue;
                if (def.speciesOverride is DinosaurSpecies species) ai.species = species;
                ai.healthMultiplier = HealthMultiplier;
                ai.damageMultiplier = DamageMultiplier;

                if (stampede)
                {
                    // Aim each runner at its own point beside the target so the herd sweeps a wide lane.
                    Vector3 through = e.Target.Position + side * ((i - (count - 1) * 0.5f) * 3.5f);
                    ai.StartCoroutine(StampedeNextFrame(ai, through, def.trampleDamage));
                }
                else if (e.Target.Focus != null)
                {
                    // Predators with a nose stalk: they roam in and follow the scent trail instead of homing in.
                    if (ai.species != null && ai.species.smellRange > 0f) ai.Track(e.Target.Focus);
                    else ai.Hunt(e.Target.Focus);
                }
            }
        }

        // Start() sets the species up (speed, NavMesh agent), so the charge begins one frame after spawning.
        private static System.Collections.IEnumerator StampedeNextFrame(DinosaurAI ai, Vector3 through, float damage)
        {
            yield return null;
            if (ai != null) ai.Stampede(through, damage);
        }
    }
}
