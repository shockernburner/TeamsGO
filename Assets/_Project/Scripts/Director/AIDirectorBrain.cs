using System.Collections.Generic;
using ProjectFossil.Core;

namespace ProjectFossil.Director
{
    // The AI "player" of the Threat Director: earns budget over time and spends it through the same
    // Purchase API a rival team would use. Deterministic for a given seed and tick sequence.
    public class AIDirectorBrain
    {
        public ThreatBuyer Buyer { get; }
        public float LastThreatTime { get; private set; } = float.NegativeInfinity;

        private readonly ThreatDirector   _director;
        private readonly DirectorSettings _settings;
        private readonly RNGService       _rng;
        private readonly float            _matchDuration;

        private float _incomeRemainder;
        private float _nextDecision;

        public AIDirectorBrain(ThreatDirector director, DirectorSettings settings, ThreatBuyer buyer,
                               RNGService rng, float matchDuration)
        {
            _director      = director;
            _settings      = settings;
            Buyer          = buyer;
            _rng           = rng;
            _matchDuration = matchDuration > 0f ? matchDuration : 1f;
            _nextDecision  = settings.gracePeriod;

            if (settings.startingBudget > 0) buyer.Wallet.Earn(settings.startingBudget);
        }

        // Call once per frame (or fixed step) with the match clock. Returns the purchased threat, if any.
        public ThreatDefinition Tick(float deltaTime, float matchTime, ThreatTarget? target)
        {
            _director.SetTime(matchTime);
            EarnIncome(deltaTime, matchTime);

            if (matchTime < _nextDecision) return null;
            _nextDecision = matchTime + _settings.decisionInterval;

            if (target == null) return null;
            if (matchTime - LastThreatTime < _settings.minSecondsBetween) return null;

            var choice = Choose(target.Value, matchTime);
            if (choice == null) return null;

            if (_director.Purchase(choice.threatId, target.Value, Buyer) != PurchaseResult.Success) return null;
            LastThreatTime = matchTime;
            return choice;
        }

        private void EarnIncome(float deltaTime, float matchTime)
        {
            _incomeRemainder += _settings.IncomeAt(matchTime / _matchDuration) * deltaTime;
            int whole = (int)_incomeRemainder;
            if (whole <= 0) return;
            _incomeRemainder -= whole;
            Buyer.Wallet.Earn(whole);
        }

        private ThreatDefinition Choose(ThreatTarget target, float matchTime)
        {
            var affordable = _director.Purchasable(target, Buyer);
            if (affordable.Count == 0) return null;

            // Escalation: if something bigger is unlocked but not affordable yet, sometimes save for it.
            int maxAffordable = 0;
            foreach (var t in affordable) if (t.cost > maxAffordable) maxAffordable = t.cost;
            foreach (var t in _director.Threats)
            {
                bool unlocked = matchTime >= t.unlockTime;
                if (unlocked && t.cost > maxAffordable && _director.CooldownRemaining(t.threatId, Buyer) <= 0f)
                {
                    if (_rng.NextFloat() < _settings.saveUpChance) return null;
                    break;
                }
            }

            return WeightedPick(affordable);
        }

        private ThreatDefinition WeightedPick(List<ThreatDefinition> options)
        {
            float total = 0f;
            foreach (var t in options) total += t.aiWeight > 0f ? t.aiWeight : 0f;
            if (total <= 0f) return options[_rng.Next(options.Count)];

            float pick = _rng.NextFloat() * total;
            foreach (var t in options)
            {
                pick -= t.aiWeight > 0f ? t.aiWeight : 0f;
                if (pick <= 0f) return t;
            }
            return options[options.Count - 1];
        }
    }
}
