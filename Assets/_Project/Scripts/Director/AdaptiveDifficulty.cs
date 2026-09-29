using System;
using System.Collections.Generic;

namespace ProjectFossil.Director
{
    // Reads how the match is going and turns it into one number, Intensity, that the AI director spends by.
    // 1 = normal. A player who kills fast at full health pushes it up; a badly hurt one pulls it down.
    // The baseline comes from the player's Survivor Rank, so better players start every island harder.
    // Pure and deterministic: same inputs, same output.
    public class AdaptiveDifficulty
    {
        public float Baseline  { get; }
        public float Intensity { get; private set; }

        private readonly DirectorSettings _settings;
        private readonly Queue<float> _recentKills = new Queue<float>();
        private float _lastHurtTime = float.NegativeInfinity;

        public AdaptiveDifficulty(DirectorSettings settings, float baseline = 1f)
        {
            _settings = settings;
            Baseline  = Math.Max(0.1f, baseline);
            Intensity = Baseline;
        }

        public int RecentKills => _recentKills.Count;

        public void ReportKill(float matchTime) => _recentKills.Enqueue(matchTime);

        public void ReportPlayerHurt(float matchTime) => _lastHurtTime = matchTime;

        // Call every frame. healthFraction is the player's current health, 0..1.
        public float Tick(float deltaTime, float matchTime, float healthFraction)
        {
            while (_recentKills.Count > 0 && matchTime - _recentKills.Peek() > _settings.killWindow)
                _recentKills.Dequeue();

            Intensity = Approach(Intensity, Target(matchTime, healthFraction), _settings.intensityChangePerSecond * deltaTime);
            return Intensity;
        }

        public float Target(float matchTime, float healthFraction)
        {
            float boost = 0f;

            // Killing easily: up to +killBoost at killsForFullBoost kills inside the window.
            if (_settings.killsForFullBoost > 0)
                boost += _settings.killBoost * Math.Min(1f, (float)_recentKills.Count / _settings.killsForFullBoost);

            // Untouched for a while at high health: the island isn't scary enough yet.
            if (healthFraction >= 0.8f && matchTime - _lastHurtTime > _settings.calmSeconds)
                boost += _settings.calmBoost;

            // Badly hurt: give some room to heal and run.
            if (healthFraction < _settings.hurtThreshold)
                boost -= _settings.hurtRelief;

            float target = Baseline * (1f + boost);
            return Clamp(target, _settings.minIntensity, _settings.maxIntensity);
        }

        private static float Approach(float from, float to, float step)
        {
            if (step <= 0f) return from;
            if (from < to) return Math.Min(to, from + step);
            return Math.Max(to, from - step);
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
