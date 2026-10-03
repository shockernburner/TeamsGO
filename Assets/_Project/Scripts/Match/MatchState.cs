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

        // `boardingSpeed`: teammates holding the pad together board faster (1 = alone).
        public void Tick(float deltaTime, bool playerInExtractionZone, float boardingSpeed = 1f)
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
                ExtractionProgress += deltaTime * Math.Max(0f, boardingSpeed);
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

        // Joining a match already under way: take the host's clock. Payouts and the extraction announcement
        // pick up from there (nothing is paid for the time before joining).
        public void FastForward(float elapsed)
        {
            if (Phase != MatchPhase.Active || elapsed <= Elapsed) return;
            Elapsed = elapsed;
            if (_payoutInterval > 0f) _payoutTimer = elapsed % _payoutInterval;
        }

        public void ReportPlayerDied() => End(MatchResult.Died);

        // A teammate finished boarding the helicopter this player is standing under: everyone aboard leaves.
        public void ExtractNow()
        {
            if (Phase == MatchPhase.Active && IsExtractionOpen) End(MatchResult.Extracted);
        }

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
        public ChallengeLevel Challenge = ChallengeLevel.Hard;
        public MatchResult Result;
        public float       TimeSurvived;
        public int         CoinsEarned;
        public int         DinosKilled;
        public int         ThreatsFaced;

        // Score (filled in when the match ends)
        public int   SurvivalPoints;
        public int   KillPoints;
        public int   DamagePoints;
        public int   ThreatPoints;
        public float ResultMultiplier = 1f;
        public int   MatesAboard;      // teammates who left on the same helicopter
        public int   Score;
        public int   BestScore;
        public bool  NewBest;

        // Survivor Rank before and after this match
        public int   RankBefore;
        public int   RankAfter;
        public float RatingDelta;
    }
}
