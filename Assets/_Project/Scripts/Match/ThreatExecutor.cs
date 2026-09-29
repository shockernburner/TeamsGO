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

        public ThreatExecutor(GameObject defaultDinosaurPrefab, Transform parent)
        {
            _defaultDinosaurPrefab = defaultDinosaurPrefab;
            _parent                = parent;
        }

        public void Execute(ThreatEvent e)
        {
            switch (e.Threat.kind)
            {
                case ThreatKind.SpawnHunters: SpawnHunters(e); break;
            }
        }

        private void SpawnHunters(ThreatEvent e)
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

            for (int i = 0; i < Mathf.Max(1, def.spawnCount); i++)
            {
                Vector2 jitter = Random.insideUnitCircle * 4f;
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
                if (e.Target.Focus != null) ai.Hunt(e.Target.Focus);
            }
        }
    }
}
