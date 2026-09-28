using System;

namespace ProjectFossil.Director
{
    public class ThreatDirector
    {
        public event Action<string> OnThreatTriggered;

        public bool CanAfford(string threatId) => false;

        public bool Purchase(string threatId, object target, object buyer) => false;
    }
}
