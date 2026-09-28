using UnityEditor;
using UnityEngine;
using ProjectFossil.Generation;

namespace ProjectFossil.Editor
{
    public class IslandGeneratorWindow : EditorWindow
    {
        private int _seed;

        [MenuItem("Project Fossil/Island Generator")]
        public static void Open() => GetWindow<IslandGeneratorWindow>("Island Generator");

        private void OnGUI()
        {
            _seed = EditorGUILayout.IntField("Seed", _seed);

            if (GUILayout.Button("Regenerate"))
            {
                var generator = new IslandGenerator(_seed);
                Debug.Log($"[IslandGenerator] Regenerating with seed {_seed}");
            }

            if (GUILayout.Button("Random Seed"))
            {
                _seed = Random.Range(0, int.MaxValue);
                Repaint();
            }
        }
    }
}
