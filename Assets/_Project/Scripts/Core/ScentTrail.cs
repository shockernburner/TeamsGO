using System.Collections.Generic;
using UnityEngine;

namespace ProjectFossil.Core
{
    // The trail a player leaves behind: a mark every couple of seconds that fades after a while. Predators with a
    // sense of smell follow it mark by mark toward the freshest one, so a player who stood still, doubled back or
    // waded through water can lose them. Pure; the match drops the marks and the AI reads them.
    public class ScentTrail
    {
        // The trail of the current match (one player for now; co-op will keep one per player).
        public static ScentTrail Active { get; set; }

        public float Lifetime     { get; }
        public float DropInterval { get; }
        public int   Count => _marks.Count;

        private struct Mark
        {
            public Vector3 Position;
            public float   Time;
        }

        private readonly List<Mark> _marks = new List<Mark>();
        private float _lastDrop = float.NegativeInfinity;

        public ScentTrail(float lifetime = 90f, float dropInterval = 2f)
        {
            Lifetime     = lifetime > 0f ? lifetime : 1f;
            DropInterval = dropInterval > 0f ? dropInterval : 0.1f;
        }

        // Call every frame with where the player is. `leavesScent` false (in water) drops nothing: water breaks it.
        public void Tick(Vector3 position, float now, bool leavesScent = true)
        {
            int expired = 0;
            while (expired < _marks.Count && now - _marks[expired].Time > Lifetime) expired++;
            if (expired > 0) _marks.RemoveRange(0, expired);

            if (!leavesScent || now - _lastDrop < DropInterval) return;
            _marks.Add(new Mark { Position = position, Time = now });
            _lastDrop = now;
        }

        // The freshest mark within `range` of `from` that is newer than `newerThan`. Following these one after
        // another walks a predator along the trail toward the player.
        public bool TryFindFreshest(Vector3 from, float range, float newerThan, out Vector3 position, out float time)
        {
            float r2 = range * range;
            for (int i = _marks.Count - 1; i >= 0; i--) // newest first
            {
                var m = _marks[i];
                if (m.Time <= newerThan) break;
                float dx = m.Position.x - from.x, dz = m.Position.z - from.z;
                if (dx * dx + dz * dz > r2) continue;
                position = m.Position;
                time     = m.Time;
                return true;
            }
            position = default;
            time     = 0f;
            return false;
        }

        public void Clear()
        {
            _marks.Clear();
            _lastDrop = float.NegativeInfinity;
        }
    }
}
