using System;

namespace ProjectFossil.Match
{
    public enum MatchPhase  { Active, Ended }
    public enum MatchResult { None, Extracted, Died, Stranded }

    // Pure match flow: drop in -> survive -> extract or die -> results. No Unity scene dependencies.
    public class MatchState
    {
        public MatchPhase  Phase   { get; private set; } = MatchPhase.Active;
        public MatchResult Result  { get; private set; } = MatchResult.None;
        public float       Elapsed { get; private set; }

        public float Duration           { get; }
        public float ExtractionOpensAt  { get; }
        public float ExtractionHoldTime { get; }

        public float Remaining          => Math.Max(0f, Duration - Elapsed);
        public bool  IsExtractionOpen   => Elapsed >= ExtractionOpensAt;
        public float ExtractionProgress { get; private set; } // seconds held inside a zone
        public bool  IsExtracting       => ExtractionProgress > 0f && Phase == MatchPhase.Active;

        public event Action              ExtractionOpened;
        public event Action<int>         SurvivalPayout;
        public event Action<MatchResult> Ended;

        private readonly float _payoutInterval;
        private readonly int   _payoutAmount;
        private float _payoutTimer;
        private bool  _openAnnounced;

        public MatchState(float duration, float extractionOpensAt, float extractionHoldTime,
                          float payoutInterval = 0f, int payoutAmount = 0)
        {
            Duration           = duration;
            ExtractionOpensAt  = extractionOpensAt;
            ExtractionHoldTime = extractionHoldTime;
            _payoutInterval    = payoutInterval;
            _payoutAmount      = payoutAmount;
        }

        public MatchState(MatchRules rules)
            : this(rules.matchDuration, rules.extractionOpensAt, rules.extractionHoldTime,
                   rules.survivalPayoutInterval, rules.survivalPayoutAmount) { }

        public void Tick(float deltaTime, bool playerInExtractionZone)
        {
            if (Phase != MatchPhase.Active || deltaTime <= 0f) return;

            Elapsed += deltaTime;

            if (!_openAnnounced && IsExtractionOpen)
            {
                _openAnnounced = true;
                ExtractionOpened?.Invoke();
            }

            if (_payoutInterval > 0f && _payoutAmount > 0)
            {
                _payoutTimer += deltaTime;
                while (_payoutTimer >= _payoutInterval)
                {
                    _payoutTimer -= _payoutInterval;
                    SurvivalPayout?.Invoke(_payoutAmount);
                }
            }

            if (IsExtractionOpen && playerInExtractionZone)
            {
                ExtractionProgress += deltaTime;
                if (ExtractionProgress >= ExtractionHoldTime)
                {
                    End(MatchResult.Extracted);
                    return;
                }
            }
            else
            {
                ExtractionProgress = 0f;
            }

            if (Elapsed >= Duration) End(MatchResult.Stranded);
        }

        public void ReportPlayerDied() => End(MatchResult.Died);

        private void End(MatchResult result)
        {
            if (Phase == MatchPhase.Ended) return;
            Phase  = MatchPhase.Ended;
            Result = result;
            Ended?.Invoke(result);
        }
    }

    public class MatchStats
    {
        public int         Seed;
        public MatchResult Result;
        public float       TimeSurvived;
        public int         CoinsEarned;
        public int         DinosKilled;
        public int         ThreatsFaced;
    }
}
