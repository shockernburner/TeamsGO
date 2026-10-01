using System.Collections.Generic;
using UnityEngine;

namespace ProjectFossil.Core
{
    // Anything that brushes plants aside as it walks through them: players and dinosaurs. The island wind reads
    // this list and bends nearby bushes and ferns away from each one. Presentation only.
    public class PlantPusher : MonoBehaviour
    {
        public static readonly List<PlantPusher> All = new List<PlantPusher>();

        [Tooltip("How far around the body plants get pushed, metres")]
        public float reach = 0.6f;

        private void OnEnable()  => All.Add(this);
        private void OnDisable() => All.Remove(this);

        public static PlantPusher Ensure(GameObject go, float reach)
        {
            var p = go.GetComponent<PlantPusher>();
            if (p == null) p = go.AddComponent<PlantPusher>();
            p.reach = reach;
            return p;
        }
    }
}
