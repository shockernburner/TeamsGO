using System;
using UnityEngine;

namespace ProjectFossil.Core
{
    // Presentation-only happenings that the sound layer voices: nothing in gameplay listens to these.
    public static class AmbientEvents
    {
        // A flying reptile cried out high overhead here.
        public static event Action<Vector3> SkyCall;

        public static void RaiseSkyCall(Vector3 position) => SkyCall?.Invoke(position);
    }
}
