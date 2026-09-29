using System;
using System.Collections.Generic;

namespace ProjectFossil.Director
{
    // Standalone service: validates and executes threat purchases from any buyer (AI now, rival teams later).
    // It never spawns anything itself; listeners of OnThreatTriggered carry the threat out.
    public class ThreatDirector
    {
        public float Time { get; private set; }
        public IReadOnlyList<ThreatDefinition> Threats => _threats;

        public event Action<ThreatEvent> OnThreatTriggered;

        private readonly List<ThreatDefinition> _threats = new List<ThreatDefinition>();
        private readonly Dictionary<string, ThreatDefinition> _byId = new Dictionary<string, ThreatDefinition>();
        private readonly Dictionary<(string buyer, string threat), float> _readyAt = new Dictionary<(string, string), float>();

        public ThreatDirector(IEnumerable<ThreatDefinition> threats)
        {
            foreach (var t in threats)
            {
                if (t == null) continue;
                if (_byId.ContainsKey(t.threatId))
                    throw new ArgumentException($"Duplicate threatId '{t.threatId}'");
                _byId[t.threatId] = t;
                _threats.Add(t);
            }
        }

        // Match time in seconds. Called by the match every frame.
        public void SetTime(float time) => Time = time;

        public ThreatDefinition Find(string threatId) =>
            threatId != null && _byId.TryGetValue(threatId, out var t) ? t : null;

        public bool CanAfford(string threatId, ThreatBuyer buyer)
        {
            var threat = Find(threatId);
            return threat != null && buyer != null && buyer.Wallet.CanAfford(threat.cost);
        }

        public float CooldownRemaining(string threatId, ThreatBuyer buyer)
        {
            if (buyer == null || !_readyAt.TryGetValue((buyer.Id, threatId), out var readyAt)) return 0f;
            return Math.Max(0f, readyAt - Time);
        }

        // Every check a purchase goes through, without spending anything.
        public PurchaseResult Evaluate(string threatId, ThreatTarget target, ThreatBuyer buyer)
        {
            var threat = Find(threatId);
            if (threat == null || buyer == null) return PurchaseResult.UnknownThreat;
            if (Time < threat.unlockTime) return PurchaseResult.Locked;
            if (CooldownRemaining(threatId, buyer) > 0f) return PurchaseResult.OnCooldown;
            if (target.TeamId != null && target.TeamId == buyer.TeamId) return PurchaseResult.InvalidTarget;
            if (!buyer.Wallet.CanAfford(threat.cost)) return PurchaseResult.CannotAfford;
            return PurchaseResult.Success;
        }

        public PurchaseResult Purchase(string threatId, ThreatTarget target, ThreatBuyer buyer)
        {
            var result = Evaluate(threatId, target, buyer);
            if (result != PurchaseResult.Success) return result;

            var threat = _byId[threatId];
            buyer.Wallet.TrySpend(threat.cost);
            _readyAt[(buyer.Id, threatId)] = Time + threat.cooldown;

            OnThreatTriggered?.Invoke(new ThreatEvent
            {
                Threat = threat,
                Target = target,
                Buyer  = buyer,
                Time   = Time,
            });
            return PurchaseResult.Success;
        }

        // Threats this buyer could buy right now against this target.
        public List<ThreatDefinition> Purchasable(ThreatTarget target, ThreatBuyer buyer)
        {
            var list = new List<ThreatDefinition>();
            foreach (var t in _threats)
                if (Evaluate(t.threatId, target, buyer) == PurchaseResult.Success)
                    list.Add(t);
            return list;
        }
    }
}
