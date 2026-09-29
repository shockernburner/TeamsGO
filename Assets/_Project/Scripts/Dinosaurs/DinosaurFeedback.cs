using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Dinosaurs
{
    // Placeholder combat readability: the body glows red while a bite charges, rears up slightly,
    // and flashes white when hit. Presentation only; DinosaurAI works without it.
    public class DinosaurFeedback : MonoBehaviour
    {
        public Color windupColor = new Color(1f, 0.15f, 0.1f);
        public Color hitColor    = Color.white;
        public float hitFlashSeconds = 0.12f;
        public float rearHeight      = 0.25f;

        private DinosaurAI _ai;
        private Renderer[] _renderers;
        private Color[]    _baseColors;
        private float _windupTotal, _windupLeft, _hitLeft;
        private Transform _visual;
        private Vector3   _visualBasePos;

        private void Awake()
        {
            _ai = GetComponent<DinosaurAI>();
        }

        private void Start()
        {
            _renderers  = GetComponentsInChildren<Renderer>();
            _baseColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
                _baseColors[i] = _renderers[i].material.color; // instances the material; fine for placeholders

            // Rear the first child mesh rather than the root, so the NavMeshAgent keeps control of the root.
            var r = _renderers.Length > 0 ? _renderers[0].transform : null;
            if (r != null && r != transform) { _visual = r; _visualBasePos = r.localPosition; }

            if (_ai != null)
            {
                _ai.AttackWindupStarted += OnWindup;
                if (_ai.Health != null) _ai.Health.Damaged += OnDamaged;
            }
        }

        private void OnDestroy()
        {
            if (_ai == null) return;
            _ai.AttackWindupStarted -= OnWindup;
            if (_ai.Health != null) _ai.Health.Damaged -= OnDamaged;
        }

        private void OnWindup(float seconds)
        {
            _windupTotal = Mathf.Max(0.01f, seconds);
            _windupLeft  = _windupTotal;
        }

        private void OnDamaged(DamageInfo info)
        {
            _hitLeft    = hitFlashSeconds;
            _windupLeft = 0f; // the AI cancels the bite on a hit
        }

        private void Update()
        {
            if (_renderers == null) return;
            if (_ai != null && _ai.CurrentState == DinosaurAI.State.Dead) { Apply(0f, Color.clear, 0f); enabled = false; return; }

            float windup = 0f;
            if (_windupLeft > 0f)
            {
                _windupLeft -= Time.deltaTime;
                windup = 1f - Mathf.Clamp01(_windupLeft / _windupTotal);
            }
            if (_hitLeft > 0f) _hitLeft -= Time.deltaTime;

            if (_hitLeft > 0f) Apply(1f, hitColor, 0f);
            else               Apply(windup, windupColor, windup);
        }

        private void Apply(float t, Color tint, float rear)
        {
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null)
                    _renderers[i].material.color = Color.Lerp(_baseColors[i], tint, t);

            if (_visual != null)
                _visual.localPosition = _visualBasePos + Vector3.up * (rearHeight * rear);
        }
    }
}
