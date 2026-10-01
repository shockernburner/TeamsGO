using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ProjectFossil.Core;
using ProjectFossil.Dinosaurs;
using ProjectFossil.Generation;
using ProjectFossil.Player;

namespace ProjectFossil.Editor
{
    // One click turns the imported Quaternius packs (CC0) into game-ready content:
    //   - import settings (rig type, clip names and looping) for dinosaurs, the survivor and the animation library
    //   - URP materials with the right textures (cutout, double-sided foliage) remapped onto every model
    //   - Animator controllers for each dinosaur species and the player
    //   - fills the ModelDefinition assets and each biome's tree/rock/plant model lists
    // Safe to run again: it rebuilds what it generated and reassigns the references.
    public static class ModelPackSetup
    {
        private const string Pack      = "Assets/_Project/Art/ThirdParty/Quaternius";
        private const string Generated = "Assets/_Project/Art/Generated";
        private const string ModelData = "Assets/_Project/Data/Models";
        private const string IslandDir  = "Assets/_Project/Data/Island";
        private const string PlayerPrefab = "Assets/_Project/Prefabs/Player.prefab";

        // ModelDefinition asset → the model file it shows.
        private static readonly Dictionary<string, string> Models = new Dictionary<string, string>
        {
            { "Model_Raptor",   "Dinosaurs/Dino_Raptor.fbx" },
            { "Model_Ironjaw",  "Dinosaurs/Dino_Ironjaw.fbx" },
            { "Model_Hornback", "Dinosaurs/Dino_Triceratops.fbx" },
            { "Model_Spikeback", "Dinosaurs/Dino_Stegosaurus.fbx" },
            { "Model_Crestback", "Dinosaurs/Dino_Parasaurolophus.fbx" },
            { "Model_Longneck", "Dinosaurs/Dino_Apatosaurus.fbx" },
            { "Model_Survivor", "Characters/Survivor_Male.fbx" },
        };

        // Source material name → (base colour texture, normal map, cutout foliage).
        private static readonly Dictionary<string, (string baseTex, string normal, bool cutout)> Textures =
            new Dictionary<string, (string, string, bool)>
        {
            { "Bark_NormalTree",    ("Bark_NormalTree",  "Bark_NormalTree_Normal",  false) },
            { "Bark_TwistedTree",   ("Bark_TwistedTree", "Bark_TwistedTree_Normal", false) },
            { "Bark_DeadTree",      ("Bark_DeadTree",    "Bark_DeadTree_Normal",    false) },
            { "Leaves_NormalTree",  ("Leaves_NormalTree_C",  null, true) },
            { "Leaves_TwistedTree", ("Leaves_TwistedTree",   null, true) }, // white mask, tinted below (the _C version is autumn red)
            { "Leaves_Pine",        ("Leaf_Pine_C",          null, true) },
            { "Leaves",             ("Leaves",               null, true) },
            { "Grass",              ("Grass",                null, true) },
            { "Flowers",            ("Flowers",              null, true) },
            { "Rocks",              ("Rocks_Diffuse",        null, false) },
            { "Mushrooms",          ("Mushrooms",            null, false) },
            { "MI_Superhero_Male",   ("T_Superhero_Male_Dark",             "T_Superhero_Male_Normal",   false) },
            { "MI_Superhero_Female", ("T_Superhero_Female_Dark_BaseColor", "T_Superhero_Female_Normal", false) },
            { "MI_Eyes",             ("T_Eye_Brown",        "T_Eye_Normal",    false) },
            { "MI_Hair_1",           ("T_Hair_1_BaseColor", "T_Hair_1_Normal", false) },
            { "MI_Ranger",           ("T_Ranger_BaseColor", "T_Ranger_Normal", false) },
            { "MI_Regular_Male",     ("T_Regular_Male_Dark_BaseColor", "T_Regular_Male_Normal", false) },
            { "Atlas",               ("Atlas_Pirate", null, false) }, // pirate kit props: one shared colour atlas
        };

        // Plant_7 is left out: its leaves sit on the purple part of the atlas.
        private static readonly string[] Ferns  = { "Fern_1", "Plant_1", "Plant_1_Big" };

        // White leaf masks get a colour here instead of from the texture.
        private static readonly Dictionary<string, Color> Tints = new Dictionary<string, Color>
        {
            { "Leaves_TwistedTree", new Color(0.36f, 0.55f, 0.2f) },
        };
        private static readonly string[] Rocks  = { "Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3" };

        [MenuItem("Project Fossil/Art/Set Up Model Packs")]
        public static void Run()
        {
            if (!AssetDatabase.IsValidFolder(Pack))
            {
                EditorUtility.DisplayDialog("Model packs", $"Nothing to set up: {Pack} is missing.", "OK");
                return;
            }
            EnsureFolder(Generated);
            EnsureFolder(Generated + "/Materials");
            EnsureFolder(Generated + "/Animators");

            try
            {
                AssetDatabase.StartAssetEditing();
                MarkNormalMaps();
            }
            finally { AssetDatabase.StopAssetEditing(); }

            SetUpDinosaurs();
            SetUpSurvivor();
            SetUpNature();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ModelPackSetup] Done. Press Play: dinosaurs, the survivor and the island now use the imported models.");
        }

        // ── Dinosaurs ──────────────────────────────────────────────────────────

        private static void SetUpDinosaurs()
        {
            foreach (var path in Files("Dinosaurs", "*.fbx"))
            {
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                imp.animationType = ModelImporterAnimationType.Generic;
                imp.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.importAnimation = true;
                imp.clipAnimations = RenameClips(imp.defaultClipAnimations, loop: n => n == "Idle" || n == "Walk" || n == "Run");
                imp.SaveAndReimport();
                RemapMaterials(path, "Dinosaurs/" + System.IO.Path.GetFileNameWithoutExtension(path) + "_");
            }

            foreach (var species in LoadAll<DinosaurSpecies>())
            {
                var def = species.model;
                if (def == null || !Models.TryGetValue(def.name, out var file)) continue;
                string path = $"{Pack}/{file}";
                var clips = Clips(path);
                var ctrl = BuildDinoController(System.IO.Path.GetFileNameWithoutExtension(path), clips, species);
                Assign(def, path, ctrl);
            }
        }

        private static AnimatorController BuildDinoController(string name, Dictionary<string, AnimationClip> clips,
                                                               DinosaurSpecies species)
        {
            var ctrl = NewController(name);
            ctrl.AddParameter("Speed",  AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Dead",   AnimatorControllerParameterType.Bool);
            var sm = ctrl.layers[0].stateMachine;

            var loco = ctrl.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(Clip(clips, "Idle"), 0f);
            tree.AddChild(Clip(clips, "Walk"), species.walkSpeed);
            tree.AddChild(Clip(clips, "Run"),  species.runSpeed);
            sm.defaultState = loco;

            var attack = sm.AddState("Attack");
            attack.motion = Clip(clips, "Attack");
            var toAttack = sm.AddAnyStateTransition(attack);
            toAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            toAttack.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
            toAttack.canTransitionToSelf = false;
            toAttack.duration = 0.1f;
            var back = attack.AddTransition(loco);
            back.hasExitTime = true; back.exitTime = 0.9f; back.duration = 0.15f;

            var death = sm.AddState("Death");
            death.motion = Clip(clips, "Death");
            var toDeath = sm.AddAnyStateTransition(death);
            toDeath.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
            toDeath.canTransitionToSelf = false;
            toDeath.duration = 0.15f;
            return ctrl;
        }

        // ── Survivor (player) ──────────────────────────────────────────────────

        private static void SetUpSurvivor()
        {
            foreach (var path in Files("Characters", "*.fbx"))
            {
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                imp.animationType   = ModelImporterAnimationType.Human;
                imp.avatarSetup     = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.importAnimation = false;
                imp.isReadable      = true; // ModelFit trims the body down to the head under the outfit
                imp.SaveAndReimport();
                RemapMaterials(path, "Characters/");
            }
            foreach (var path in Files("Characters/Hair", "*.fbx").Concat(Files("Characters/Outfit", "*.fbx")))
            {
                // Hair and beards are skinned to the same skeleton; ModelFit binds them to the body by bone name.
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                imp.animationType   = ModelImporterAnimationType.Generic;
                imp.avatarSetup     = ModelImporterAvatarSetup.NoAvatar;
                imp.importAnimation = false;
                imp.SaveAndReimport();
                RemapMaterials(path, "Characters/");
            }

            string animPath = $"{Pack}/Animations/UAL1_Standard.fbx";
            var animImp = AssetImporter.GetAtPath(animPath) as ModelImporter;
            if (animImp == null) { Debug.LogWarning("[ModelPackSetup] Animation library missing: " + animPath); return; }
            animImp.animationType   = ModelImporterAnimationType.Human;
            animImp.avatarSetup     = ModelImporterAvatarSetup.CreateFromThisModel;
            animImp.importAnimation = true;
            animImp.materialImportMode = ModelImporterMaterialImportMode.None; // clips only
            var list = RenameClips(animImp.defaultClipAnimations, loop: n => n.EndsWith("_Loop"));
            foreach (var c in list)
            {
                // In place: the character controller moves the body, the clips only pose it.
                c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true;
            }
            animImp.clipAnimations = list;
            animImp.SaveAndReimport();

            var def = AssetDatabase.LoadAssetAtPath<ModelDefinition>($"{ModelData}/Model_Survivor.asset");
            if (def == null) { Debug.LogWarning("[ModelPackSetup] Model_Survivor.asset missing."); return; }

            var ctrl = BuildSurvivorAnimator("Survivor");
            Assign(def, $"{Pack}/{Models["Model_Survivor"]}", ctrl);
            def.yawOffset       = 180f;  // the pack's characters face -Z in Unity
            def.faceHeadForward = false;
            // Ranger outfit (without its hood, so the hair shows) over the base character's head.
            def.attachments = new[] { "Hair/Hair_SimpleParted", "Hair/Hair_Beard",
                                      "Outfit/Male_Ranger_Body", "Outfit/Male_Ranger_Arms",
                                      "Outfit/Male_Ranger_Legs", "Outfit/Male_Ranger_Feet_Boots" }
                .Select(n => AssetDatabase.LoadAssetAtPath<GameObject>($"{Pack}/Characters/{n}.fbx"))
                .Where(g => g != null).ToArray();
            bool dressed = def.attachments.Any(a => a.name.Contains("Ranger"));
            def.trimMeshes = dressed ? new[] { "SuperHero_Male" } : new string[0];
            def.keepBones  = new[] { "head", "neck_01", "neck_02" };
        }

        // The player's animator from the Quaternius library. `overrides` swaps in other humanoid clips by name
        // (a character pack's own idle, walk and run), each with its natural speed in m/s so it plays in step.
        internal static AnimatorController BuildSurvivorAnimator(string name,
            Dictionary<string, (AnimationClip clip, float speed)> overrides = null)
        {
            var clips = Clips($"{Pack}/Animations/UAL1_Standard.fbx");
            var pc = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab)?.GetComponent<PlayerController>();
            float walk = pc != null ? pc.walkSpeed : 5f, run = pc != null ? pc.runSpeed : 9f;
            float crouch = pc != null ? pc.crouchSpeed : 2.5f, swim = pc != null ? pc.swimSpeed : 2.2f;
            return BuildSurvivorController(name, clips, overrides, walk, run, crouch, swim);
        }

        private static AnimatorController BuildSurvivorController(string name, Dictionary<string, AnimationClip> clips,
            Dictionary<string, (AnimationClip clip, float speed)> overrides, float walk, float run, float crouch, float swim)
        {
            var ctrl = NewController(name);
            ctrl.AddParameter("Speed",  AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Armed",  AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Hit",    AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Dead",   AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Swim",   AnimatorControllerParameterType.Bool);

            // Base layer: whole-body movement.
            var sm = ctrl.layers[0].stateMachine;
            var stand = ctrl.CreateBlendTreeInController("Stand", out var standTree, 0);
            standTree.blendParameter = "Speed";
            standTree.useAutomaticThresholds = false;
            if (overrides != null && overrides.TryGetValue("Idle", out var oi) && oi.clip != null
                && overrides.TryGetValue("Walk", out var ow) && ow.clip != null
                && overrides.TryGetValue("Run", out var or) && or.clip != null)
            {
                // A pack's own locomotion: the run clip sped up to the jog and sprint speeds so the feet keep pace.
                standTree.AddChild(oi.clip, 0f);
                standTree.AddChild(ow.clip, ow.speed);
                standTree.AddChild(or.clip, walk);
                standTree.AddChild(or.clip, run);
                var kids = standTree.children;
                kids[2].timeScale = Mathf.Clamp(walk / Mathf.Max(0.5f, or.speed), 0.6f, 1.6f);
                kids[3].timeScale = Mathf.Clamp(run  / Mathf.Max(0.5f, or.speed), 0.6f, 1.6f);
                standTree.children = kids;
            }
            else
            {
                standTree.AddChild(Clip(clips, "Idle_Loop"), 0f);
                standTree.AddChild(Clip(clips, "Walk_Loop"), 1.8f);
                standTree.AddChild(Clip(clips, "Jog_Fwd_Loop"), walk);
                standTree.AddChild(Clip(clips, "Sprint_Loop"), run);
            }
            sm.defaultState = stand;

            var low = ctrl.CreateBlendTreeInController("Crouch", out var lowTree, 0);
            lowTree.blendParameter = "Speed";
            lowTree.useAutomaticThresholds = false;
            lowTree.AddChild(Clip(clips, "Crouch_Idle_Loop"), 0f);
            lowTree.AddChild(Clip(clips, "Crouch_Fwd_Loop"), crouch);

            var down = stand.AddTransition(low);
            down.AddCondition(AnimatorConditionMode.If, 0f, "Crouch");
            down.hasExitTime = false; down.duration = 0.2f;
            var up = low.AddTransition(stand);
            up.AddCondition(AnimatorConditionMode.IfNot, 0f, "Crouch");
            up.hasExitTime = false; up.duration = 0.2f;

            // Swimming in rivers and lakes: treading water, or a front crawl when moving.
            var water = ctrl.CreateBlendTreeInController("Swim", out var swimTree, 0);
            swimTree.blendParameter = "Speed";
            swimTree.useAutomaticThresholds = false;
            swimTree.AddChild(Clip(clips, "Swim_Idle_Loop"), 0f);
            swimTree.AddChild(Clip(clips, "Swim_Fwd_Loop"), swim);
            var dive = sm.AddAnyStateTransition(water);
            dive.AddCondition(AnimatorConditionMode.If, 0f, "Swim");
            dive.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
            dive.canTransitionToSelf = false; dive.duration = 0.3f;
            var ashore = water.AddTransition(stand);
            ashore.AddCondition(AnimatorConditionMode.IfNot, 0f, "Swim");
            ashore.hasExitTime = false; ashore.duration = 0.3f;


            var death = sm.AddState("Death");
            death.motion = Clip(clips, "Death01");
            var toDeath = sm.AddAnyStateTransition(death);
            toDeath.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
            toDeath.canTransitionToSelf = false;
            toDeath.duration = 0.2f;

            // Upper-body layer: punches, swings and flinches play over running legs.
            ctrl.AddLayer("Actions");
            var layers = ctrl.layers;
            layers[1].avatarMask    = UpperBodyMask();
            layers[1].defaultWeight = 1f;
            layers[1].blendingMode  = AnimatorLayerBlendingMode.Override;
            ctrl.layers = layers;

            var act = ctrl.layers[1].stateMachine;
            var empty = act.AddState("Empty");
            empty.writeDefaultValues = false;
            act.defaultState = empty;

            AddAction(act, empty, "Punch", Clip(clips, "Punch_Jab"),    1.3f, "Attack", armed: false);
            AddAction(act, empty, "Swing", Clip(clips, "Sword_Attack"), 1.2f, "Attack", armed: true);
            AddAction(act, empty, "Flinch", Clip(clips, "Hit_Chest"),   1.2f, "Hit",    armed: null);
            return ctrl;
        }

        private static void AddAction(AnimatorStateMachine sm, AnimatorState empty, string name, Motion motion,
                                      float speed, string trigger, bool? armed)
        {
            var s = sm.AddState(name);
            s.motion = motion;
            s.speed  = speed;
            s.writeDefaultValues = false;
            var t = sm.AddAnyStateTransition(s);
            t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            if (armed.HasValue) t.AddCondition(armed.Value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, "Armed");
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
            t.canTransitionToSelf = true;
            t.duration = 0.05f;
            var back = s.AddTransition(empty);
            back.hasExitTime = true; back.exitTime = 0.85f; back.duration = 0.15f;
        }

        private static AvatarMask UpperBodyMask()
        {
            string path = $"{Generated}/Animators/Survivor_UpperBody.mask";
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (mask == null) { mask = new AvatarMask(); AssetDatabase.CreateAsset(mask, path); }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                bool upper = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head ||
                             part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                             part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers;
                mask.SetHumanoidBodyPartActive(part, upper);
            }
            EditorUtility.SetDirty(mask);
            return mask;
        }

        // ── Nature ─────────────────────────────────────────────────────────────

        private static void SetUpNature()
        {
            var models = new Dictionary<string, GameObject>();
            foreach (var path in Files("Nature", "*.fbx"))
            {
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                imp.animationType   = ModelImporterAnimationType.None;
                imp.importAnimation = false;
                imp.addCollider     = false; // the island decorator adds simple colliders itself
                imp.SaveAndReimport();
                RemapMaterials(path, "Nature/");
                models[System.IO.Path.GetFileNameWithoutExtension(path)] = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }

            GameObject[] Get(params string[] names) => names.Where(models.ContainsKey).Select(n => models[n]).ToArray();
            string[] Series(string prefix) => Enumerable.Range(1, 5).Select(i => $"{prefix}_{i}").ToArray();

            var common  = Series("CommonTree");
            var twisted = Series("TwistedTree");
            var dead    = Series("DeadTree");
            var pines   = Series("Pine");

            foreach (var path in Files("Props", "*.fbx"))
            {
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                imp.animationType   = ModelImporterAnimationType.None;
                imp.importAnimation = false;
                imp.addCollider     = false;
                imp.SaveAndReimport();
                RemapMaterials(path, "Props/");
                models[System.IO.Path.GetFileNameWithoutExtension(path)] = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            AssignStatic("Model_SupplyCache", "Props/Prop_Chest_Closed.fbx");
            AssignStatic("Model_RuinStash",   "Props/Prop_Chest_Gold.fbx");

            var palms = new[] { "Environment_PalmTree_1", "Environment_PalmTree_2", "Environment_PalmTree_3" };
            var bones = Rocks.Append("Environment_LargeBones").ToArray();

            SetBiome("BiomeDef_Jungle",   Get(common.Concat(twisted).ToArray()), Get(Rocks), Get(Ferns.Append("Bush_Common").ToArray()));
            SetBiome("BiomeDef_Plains",   Get(common),                            Get(bones), Get("Grass_Common_Tall", "Grass_Wispy_Tall", "Bush_Common", "Bush_Common_Flowers"));
            SetBiome("BiomeDef_Swamp",    Get(dead.Concat(twisted).ToArray()),    Get(Rocks), Get("Fern_1", "Plant_1", "Grass_Wispy_Tall", "Mushroom_Common"));
            SetBiome("BiomeDef_Beach",    Get(models.ContainsKey(palms[0]) ? palms : pines), Get(Rocks), Get("Grass_Wispy_Tall"));
            SetBiome("BiomeDef_Volcanic", Get(dead),                              Get(bones.Append("Environment_Skulls").ToArray()), Get("Mushroom_Common"));
        }

        private static void AssignStatic(string defName, string modelFile)
        {
            var def = AssetDatabase.LoadAssetAtPath<ModelDefinition>($"{ModelData}/{defName}.asset");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Pack}/{modelFile}");
            if (def == null || model == null) { Debug.LogWarning($"[ModelPackSetup] Can't set {defName} to {modelFile}."); return; }
            def.model = model;
            def.animator = null;
            EditorUtility.SetDirty(def);
        }

        private static void SetBiome(string asset, GameObject[] trees, GameObject[] rocks, GameObject[] plants)
        {
            var b = AssetDatabase.LoadAssetAtPath<BiomeDefinition>($"{IslandDir}/{asset}.asset");
            if (b == null) { Debug.LogWarning($"[ModelPackSetup] {asset} not found."); return; }
            b.treeModels  = trees;
            b.rockModels  = rocks;
            b.plantModels = plants;
            EditorUtility.SetDirty(b);
        }

        // ── Materials ──────────────────────────────────────────────────────────

        // Gives every material slot of a model a URP Lit material we own, with its textures set explicitly
        // (the packs reference textures by Windows paths from the artist's machine, which don't resolve here).
        private static void RemapMaterials(string modelPath, string matPrefix)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            var names = new HashSet<string>();
            var embedded = new Dictionary<string, Material>();
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>())
            {
                names.Add(m.name);
                embedded[m.name] = m;
            }
            foreach (var kv in imp.GetExternalObjectMap())
                if (kv.Key.type == typeof(Material)) names.Add(kv.Key.name);

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            bool changed = false;
            foreach (var n in names)
            {
                string matPath = $"{Generated}/Materials/{matPrefix}{Sanitize(n)}.mat";
                EnsureFolder(System.IO.Path.GetDirectoryName(matPath).Replace('\\', '/'));
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(lit);
                    if (embedded.TryGetValue(n, out var src))
                    {
                        if (src.HasProperty("_BaseColor"))  mat.SetColor("_BaseColor", src.GetColor("_BaseColor"));
                        else if (src.HasProperty("_Color")) mat.SetColor("_BaseColor", src.GetColor("_Color"));
                        var embeddedTex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap")
                                        : src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
                        if (embeddedTex != null) mat.SetTexture("_BaseMap", embeddedTex);
                    }
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                ConfigureMaterial(mat, n);
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), mat);
                changed = true;
            }
            if (changed) imp.SaveAndReimport();
        }

        private static void ConfigureMaterial(Material mat, string sourceName)
        {
            mat.enableInstancing = true;
            mat.SetFloat("_Smoothness", 0.1f);
            if (!Textures.TryGetValue(sourceName, out var t)) { EditorUtility.SetDirty(mat); return; }

            var tex = FindTexture(t.baseTex);
            if (tex != null)
            {
                mat.SetTexture("_BaseMap", tex);
                mat.SetColor("_BaseColor", Tints.TryGetValue(sourceName, out var tint) ? tint : Color.white);
            }
            var nrm = t.normal != null ? FindTexture(t.normal) : null;
            if (nrm != null) { mat.SetTexture("_BumpMap", nrm); mat.EnableKeyword("_NORMALMAP"); }

            if (t.cutout)
            {
                // Leaves and grass: alpha-tested and visible from both sides.
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.5f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_Cull", 0f);
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                mat.SetOverrideTag("RenderType", "TransparentCutout");
            }
            EditorUtility.SetDirty(mat);
        }

        private static Texture2D FindTexture(string fileName)
        {
            foreach (var guid in AssetDatabase.FindAssets(fileName + " t:Texture2D", new[] { Pack }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == fileName)
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            Debug.LogWarning($"[ModelPackSetup] Texture {fileName} not found under {Pack}.");
            return null;
        }

        private static void MarkNormalMaps()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Pack }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (!p.EndsWith("_Normal.png")) continue;
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                if (ti.textureType == TextureImporterType.NormalMap) continue;
                ti.textureType = TextureImporterType.NormalMap;
                ti.SaveAndReimport();
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        // "Armature|TRex_Attack" → "Attack"; "Armature|Jog_Fwd_Loop" → "Jog_Fwd_Loop" (library clips keep their names).
        private static ModelImporterClipAnimation[] RenameClips(ModelImporterClipAnimation[] clips, System.Func<string, bool> loop)
        {
            bool shortNames = clips.Length <= 8; // the dinosaur files: <Species>_<Action>
            foreach (var c in clips)
            {
                string n = c.name;
                int bar = n.LastIndexOf('|');
                if (bar >= 0) n = n.Substring(bar + 1);
                if (shortNames) { int us = n.LastIndexOf('_'); if (us >= 0) n = n.Substring(us + 1); }
                c.name = n;
                c.loopTime = loop(n);
            }
            return clips;
        }

        private static Dictionary<string, AnimationClip> Clips(string path)
        {
            var d = new Dictionary<string, AnimationClip>();
            foreach (var c in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                if (!c.name.StartsWith("__preview__")) d[c.name] = c;
            return d;
        }

        private static AnimationClip Clip(Dictionary<string, AnimationClip> clips, string name)
        {
            if (clips.TryGetValue(name, out var c)) return c;
            Debug.LogWarning($"[ModelPackSetup] Animation '{name}' not found. Have: {string.Join(", ", clips.Keys)}");
            return null;
        }

        private static AnimatorController NewController(string name)
        {
            string path = $"{Generated}/Animators/{name}.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path); // ours, regenerated
            return AnimatorController.CreateAnimatorControllerAtPath(path);
        }

        private static void Assign(ModelDefinition def, string modelPath, RuntimeAnimatorController ctrl)
        {
            def.model    = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            def.animator = ctrl;
            EditorUtility.SetDirty(def);
        }

        private static IEnumerable<string> Files(string sub, string pattern)
        {
            string dir = $"{Pack}/{sub}";
            if (!System.IO.Directory.Exists(dir)) return Enumerable.Empty<string>();
            return System.IO.Directory.GetFiles(dir, pattern).Select(p => p.Replace('\\', '/')).OrderBy(p => p);
        }

        private static IEnumerable<T> LoadAll<T>() where T : Object =>
            AssetDatabase.FindAssets("t:" + typeof(T).Name)
                         .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
                         .Where(a => a != null);

        private static string Sanitize(string s)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace('|', '_');
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
