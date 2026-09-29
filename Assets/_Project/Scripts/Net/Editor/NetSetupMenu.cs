using UnityEditor;
using UnityEngine;
using FishNet.Component.Transforming;
using FishNet.Managing.Object;
using FishNet.Object;
using ProjectFossil.Dinosaurs;

namespace ProjectFossil.Net.Editor
{
    // One click after installing FishNet: builds the two things the network can spawn and the list it spawns
    // them from. Safe to run again; it rebuilds all three.
    //   Resources/Net/NetDinosaur.prefab     a variant of Prefabs/Dinosaur (art and tuning still come from there)
    //   Resources/Net/Avatar.prefab          a teammate's body
    //   Resources/Net/NetworkPrefabs.asset   the spawnable list NetSession hands to the NetworkManager
    public static class NetSetupMenu
    {
        private const string Folder       = "Assets/_Project/Resources/Net";
        private const string BaseDinosaur = "Assets/_Project/Prefabs/Dinosaur.prefab";
        private const string DinosaurPath = Folder + "/NetDinosaur.prefab";
        private const string AvatarPath   = Folder + "/Avatar.prefab";
        private const string ListPath     = Folder + "/NetworkPrefabs.asset";

        [MenuItem("Project Fossil/Co-op/Set Up Networking")]
        public static void SetUp()
        {
            EnsureFolder(Folder);

            var dinosaur = BuildDinosaur();
            var avatar   = BuildAvatar();
            if (dinosaur == null || avatar == null) return;

            var list = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(ListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<SinglePrefabObjects>();
                AssetDatabase.CreateAsset(list, ListPath);
            }
            list.Clear();
            list.AddObject(avatar.GetComponent<NetworkObject>(), checkForDuplicates: true, initializeAdded: false);
            list.AddObject(dinosaur.GetComponent<NetworkObject>(), checkForDuplicates: true, initializeAdded: false);
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();

            Debug.Log("[Co-op] Networking set up: NetDinosaur and Avatar prefabs, and the spawn list, in " + Folder +
                      ". Press Play and choose Host or Join.");
        }

        private static GameObject BuildDinosaur()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(BaseDinosaur);
            if (source == null || source.GetComponent<DinosaurAI>() == null)
            {
                Debug.LogError($"[Co-op] {BaseDinosaur} with a DinosaurAI is needed.");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                instance.name = "NetDinosaur";
                Ensure<NetworkObject>(instance);
                var nt = Ensure<NetworkTransform>(instance);
                Configure(nt, clientAuthoritative: false);
                Ensure<NetDinosaur>(instance);
                // Saving a prefab instance makes a variant, so edits to Dinosaur.prefab carry over.
                return PrefabUtility.SaveAsPrefabAsset(instance, DinosaurPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static GameObject BuildAvatar()
        {
            var root = new GameObject("Avatar") { tag = "Player" };
            try
            {
                // Same body as the player prefab: feet at the pivot, 1.8 m capsule.
                var cc = root.AddComponent<CharacterController>();
                cc.center = new Vector3(0f, 0.9f, 0f);
                cc.height = 1.8f;
                cc.radius = 0.3f;

                // Placeholder body, shown until (or unless) the character model replaces it.
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body";
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                body.transform.localScale    = new Vector3(0.6f, 0.9f, 0.6f);

                root.AddComponent<NetworkObject>();
                var nt = root.AddComponent<NetworkTransform>();
                Configure(nt, clientAuthoritative: true);
                root.AddComponent<NetAvatar>();
                return PrefabUtility.SaveAsPrefabAsset(root, AvatarPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // Position and rotation only; scale is set locally from species data.
        private static void Configure(NetworkTransform nt, bool clientAuthoritative)
        {
            var so = new SerializedObject(nt);
            SetBool(so, "_clientAuthoritative", clientAuthoritative);
            SetBool(so, "_synchronizeScale", false);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(SerializedObject so, string name, bool value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.boolValue = value;
            else Debug.LogWarning($"[Co-op] NetworkTransform has no field {name}; left at its default.");
        }

        private static T Ensure<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
