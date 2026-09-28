using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ProjectFossil.Generation;

namespace ProjectFossil.Editor
{
    public class IslandGeneratorWindow : EditorWindow
    {
        private int _seed = 42;
        private IslandSettings _settings;
        private IslandData _lastData;
        private double _lastGenMs;

        [MenuItem("Project Fossil/Island Generator _F5")]
        public static void Open() => GetWindow<IslandGeneratorWindow>("Island Generator");

        private void OnGUI()
        {
            GUILayout.Label("Island Generator", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            _seed     = EditorGUILayout.IntField("Seed", _seed);
            _settings = (IslandSettings)EditorGUILayout.ObjectField(
                "Settings", _settings, typeof(IslandSettings), false);

            EditorGUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_settings == null))
            {
                if (GUILayout.Button("Generate  (F5)", GUILayout.Height(28)))
                    RunGeneration();
            }
            if (GUILayout.Button("Random Seed", GUILayout.Height(28)))
            {
                _seed = UnityEngine.Random.Range(0, int.MaxValue);
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            if (_settings == null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(
                    "Create a Project Fossil → Island Settings asset and assign it here.",
                    MessageType.Info);
            }

            if (_lastData != null)
            {
                EditorGUILayout.Space(8);
                GUILayout.Label("Last Result", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Seed",        _lastData.Seed.ToString());
                EditorGUILayout.LabelField("Resolution",  $"{_lastData.Resolution}×{_lastData.Resolution}");
                EditorGUILayout.LabelField("Generated in", $"{_lastGenMs:F0} ms");
                EditorGUILayout.LabelField("POIs",        _lastData.PointsOfInterest.Count.ToString());
                EditorGUILayout.LabelField("Spawn Zones", _lastData.SpawnZones.Count.ToString());

                EditorGUILayout.Space(4);

                var validation = IslandValidator.Validate(_lastData);
                if (validation.IsValid)
                    EditorGUILayout.HelpBox("Validation passed.", MessageType.Info);
                else
                {
                    foreach (var e in validation.Errors)
                        EditorGUILayout.HelpBox(e, MessageType.Error);
                }
                foreach (var w in validation.Warnings)
                    EditorGUILayout.HelpBox(w, MessageType.Warning);
            }
        }

        private void RunGeneration()
        {
            IslandTerrainBuilder.DestroyExisting();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var gen = new IslandGenerator(_seed, _settings);
            _lastData = gen.Generate();
            sw.Stop();
            _lastGenMs = sw.Elapsed.TotalMilliseconds;

            IslandTerrainBuilder.Build(_lastData);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log($"[IslandGenerator] Seed {_seed} | {_lastData.Resolution}×{_lastData.Resolution} " +
                      $"| {_lastData.PointsOfInterest.Count} POIs | {_lastData.SpawnZones.Count} spawn zones " +
                      $"| {_lastGenMs:F0} ms");

            Repaint();
        }
    }
}
