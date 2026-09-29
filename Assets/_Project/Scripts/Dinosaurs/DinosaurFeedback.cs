using System.Collections.Generic;
using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Dinosaurs
{
    // Combat readability: the body glows red while a bite charges, rears up slightly,
    // and flashes white when hit. Presentation only; DinosaurAI works without it.
    public class DinosaurFeedback : MonoBehaviour
    {
        public Color windupColor = new Color(1f, 0.15f, 0.1f);
        public Color hitColor    = Color.white;
        public float hitFlashSeconds = 0.12f;
        public float rearHeight      = 0.25f;

        private DinosaurAI _ai;
        private Material[] _materials;
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
            var visual = GetComponent<DinosaurVisual>();
            if (visual != null) visual.Build(); // so the imported model's renderers are the ones we tint
            bool hasModel = visual != null && visual.ModelRoot != null;

            var mats = new List<Material>();
            foreach (var r in GetComponentsInChildren<Renderer>())
                if (r.enabled) mats.AddRange(r.materials); // instances the materials, per dinosaur
            _materials  = mats.ToArray();
            _baseColors = new Color[_materials.Length];
            // Placeholder bodies take the species colour so each kind reads at a distance; models keep their own.
            bool useSpecies = !hasModel && _ai != null && _ai.species != null;
            for (int i = 0; i < _materials.Length; i++)
            {
                _baseColors[i] = useSpecies ? _ai.species.debugColor : _materials[i].color;
                _materials[i].color = _baseColors[i];
            }

            // Rear the visual rather than the root, so the NavMeshAgent keeps control of the root.
            var v = hasModel ? visual.ModelRoot : FirstChildRenderer();
            if (v != null && v != transform) { _visual = v; _visualBasePos = v.localPosition; }

            if (_ai != null)
            {
                _ai.AttackWindupStarted += OnWindup;
                if (_ai.Health != null) _ai.Health.Damaged += OnDamaged;
            }
        }

        private Transform FirstChildRenderer()
        {
            var r = GetComponentInChildren<Renderer>();
            return r != null ? r.transform : null;
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
            if (_materials == null) return;
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
            for (int i = 0; i < _materials.Length; i++)
                _materials[i].color = Color.Lerp(_baseColors[i], tint, t);

            if (_visual != null)
                _visual.localPosition = _visualBasePos + Vector3.up * (rearHeight * rear);
        }
    }
}
