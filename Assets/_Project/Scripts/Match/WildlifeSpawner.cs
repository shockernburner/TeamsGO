using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ProjectFossil.Core;
using ProjectFossil.Dinosaurs;
using ProjectFossil.Generation;

namespace ProjectFossil.Match
{
    // Keeps the island populated: groups from the wildlife table in every spawn zone at the start, then
    // top-ups out of the player's sight as animals die and as the match goes on.
    public class WildlifeSpawner
    {
        public int Alive => CountAlive();
        public int StartCount { get; private set; }

        private readonly WildlifeTable _table;
        private readonly GameObject    _prefab;
        private readonly Transform     _parent;
        private readonly IslandData    _island;
        private readonly RNGService    _rng;
        private readonly float         _countMultiplier;
        private readonly float         _healthMultiplier;
        private readonly float         _damageMultiplier;
        private readonly List<DinosaurAI> _spawned = new List<DinosaurAI>();
        private float _nextTopUp;

        public WildlifeSpawner(WildlifeTable table, GameObject prefab, Transform parent, IslandData island,
                               float countMultiplier, float healthMultiplier, float damageMultiplier)
        {
            _table            = table;
            _prefab           = prefab;
            _parent           = parent;
            _island           = island;
            _rng              = new RNGService(unchecked(island.Seed * 13 + 5));
            _countMultiplier  = countMultiplier;
            _healthMultiplier = healthMultiplier;
            _damageMultiplier = damageMultiplier;
        }

        public void SpawnInitial()
        {
            if (_table == null || _prefab == null) return;

            // Spawn zone 0 is the player's drop point: keep it clear.
            int groups = Mathf.Max(1, Mathf.RoundToInt(_table.groupsPerZone * _countMultiplier));
            for (int zi = 1; zi < _island.SpawnZones.Count; zi++)
            {
                var zone = _island.SpawnZones[zi];
                for (int g = 0; g < groups; g++)
                {
                    var entry = _table.Pick(_rng, 0f);
                    if (entry == null) return;
                    Vector2 offset = InsideCircle() * zone.WorldRadius * 0.8f;
                    SpawnGroup(entry, zone.WorldCenter + new Vector3(offset.x, 0f, offset.y), zone.WorldRadius, null);
                }
            }
            StartCount = CountAlive();
            _nextTopUp = _table.topUpInterval;
        }

        public void Tick(float matchTime, Vector3? playerPos)
        {
            if (_table == null || _prefab == null || matchTime < _nextTopUp) return;
            _nextTopUp = matchTime + _table.topUpInterval;

            int target = _table.TargetAlive(StartCount, matchTime, 1f); // StartCount already includes the rank bonus
            int missing = target - CountAlive();
            // One or two groups per top-up keeps new arrivals gradual.
            for (int i = 0; i < 2 && missing > 0; i++)
            {
                var entry = _table.Pick(_rng, matchTime);
                if (entry == null) return;
                if (!TryFindTopUpPoint(playerPos, out var at)) return;
                missing -= SpawnGroup(entry, at, 8f, playerPos);
            }
        }

        private int SpawnGroup(WildlifeTable.Entry entry, Vector3 center, float spread, Vector3? lookAt)
        {
            int size = WildlifeTable.GroupSize(entry, _rng);
            int made = 0;
            for (int i = 0; i < size; i++)
            {
                Vector2 j = InsideCircle() * Mathf.Max(3f, spread * 0.4f);
                Vector3 pos = center + new Vector3(j.x, 0f, j.y);
                if (!NavMesh.SamplePosition(pos, out var hit, Mathf.Max(6f, spread), NavMesh.AllAreas)) continue;

                float yaw = _rng.NextFloat() * 360f;
                var go = Object.Instantiate(_prefab, hit.position, Quaternion.Euler(0f, yaw, 0f), _parent);
                go.name = $"{entry.species.speciesName}_{_spawned.Count}";

                var ai = go.GetComponent<DinosaurAI>();
                if (ai == null) { Object.Destroy(go); continue; }
                ai.species          = entry.species;
                ai.healthMultiplier = _healthMultiplier;
                ai.damageMultiplier = _damageMultiplier;
                DinosaurAI.AnnounceCreated(ai);
                _spawned.Add(ai);
                made++;
            }
            return made;
        }

        // A spawn zone out of the player's sight if one fits, otherwise any point on a ring around the player.
        private bool TryFindTopUpPoint(Vector3? playerPos, out Vector3 point)
        {
            float min = _table.minSpawnDistance, max = _table.maxSpawnDistance;
            if (playerPos == null)
            {
                point = _island.SpawnZones.Count > 1 ? _island.SpawnZones[1 + _rng.Next(_island.SpawnZones.Count - 1)].WorldCenter
                                                      : Vector3.zero;
                return _island.SpawnZones.Count > 1;
            }

            Vector3 p = playerPos.Value;
            int zones = _island.SpawnZones.Count;
            int start = zones > 0 ? _rng.Next(zones) : 0;
            for (int k = 0; k < zones; k++)
            {
                var z = _island.SpawnZones[(start + k) % zones];
                float d = Vector2.Distance(new Vector2(z.WorldCenter.x, z.WorldCenter.z), new Vector2(p.x, p.z));
                if (d >= min && d <= max * 1.8f) { point = z.WorldCenter; return true; }
            }

            for (int tries = 0; tries < 8; tries++)
            {
                float angle = _rng.NextFloat() * Mathf.PI * 2f;
                float dist  = Mathf.Lerp(min, max, _rng.NextFloat());
                Vector3 c = p + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
                if (NavMesh.SamplePosition(c, out var hit, 15f, NavMesh.AllAreas)) { point = hit.position; return true; }
            }
            point = default;
            return false;
        }

        private int CountAlive()
        {
            int n = 0;
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                var ai = _spawned[i];
                if (ai == null || ai.CurrentState == DinosaurAI.State.Dead) { _spawned.RemoveAt(i); continue; }
                n++;
            }
            return n;
        }

        private Vector2 InsideCircle()
        {
            float a = _rng.NextFloat() * Mathf.PI * 2f;
            float r = Mathf.Sqrt(_rng.NextFloat());
            return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }
    }
}
