using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ProjectFossil.Core;
using ProjectFossil.Dinosaurs;

namespace ProjectFossil.Editor
{
    // One click turns the paid Asset Store packs (imported on this machine only, never committed) into game content:
    //   - the dinosaur pack: materials switched to URP Lit, clean model prefabs without the pack's own scripts,
    //     our own Animator controllers (Speed / Attack / Dead), one per species, plus a flying reptile for the sky
    //   - the forest pack and the dinosaur pack's tropical plants: trees, plants, rocks and ground textures per biome
    //   - the survivor pack: one player look per outfit, animated by our own (humanoid) survivor controller
    //   - the helicopter pack: the rescue helicopter, its rotors spun by our flight code
    // Everything it writes goes to Assets/_Project/Art/Bought (git-ignored), ending in Resources/BoughtArt.asset,
    // which the game reads at runtime. Machines without the packs keep the free models.
    // A report of what it found (animation names, shaders) goes to Logs/BoughtArtReport.txt.
    public static class BoughtPackSetup
    {
        private const string Out       = "Assets/_Project/Art/Bought";
        private const string ArtAsset  = Out + "/Resources/BoughtArt.asset";
        private const string Report    = "Logs/BoughtArtReport.txt";
        private const string ModelData = "Assets/_Project/Data/Models";

        // Our species → the creature in the dinosaur pack (prefab name in its creatures folder).
        private static readonly (string species, string creature)[] Species =
        {
            ("Raptor",    "Velociraptor"),
            ("Ironjaw",   "Tyrannosaurus Rex"),
            ("Hornback",  "Triceratops"),
            ("Spikeback", "Stegosaurus"),
            ("Crestback", "Parasaurolophus"),
            ("Longneck",  "Brachiosaurus"),
            ("Snapper",   "Compsognathus"),
        };
        private const string Flyer = "Pteranodon";

        private static readonly StringBuilder Log = new StringBuilder();

        [MenuItem("Project Fossil/Art/Set Up Bought Packs")]
        public static void Run()
        {
            Log.Clear();
            Log.AppendLine($"Bought art setup, {System.DateTime.Now:yyyy-MM-dd HH:mm}");

            string creatures = FindDir("Creatures/VOLI");
            string forest    = FindDir("Forest Environment Dynamic Nature");
            string dinoPack  = creatures != null ? Parent(Parent(creatures)) : null;
            string survivors = FindDir("Survivalist");
            string heliPack  = FindDir("OH-1_Basic");
            Log.AppendLine($"Dinosaur pack: {dinoPack ?? "not found"}");
            Log.AppendLine($"Forest pack:   {forest ?? "not found"}");
            Log.AppendLine($"Survivor pack: {survivors ?? "not found"}");
            Log.AppendLine($"Heli pack:     {heliPack ?? "not found"}");
            if (dinoPack == null && forest == null && survivors == null && heliPack == null)
            {
                EditorUtility.DisplayDialog("Bought packs", "None of the art packs is imported.", "OK");
                return;
            }
            if (forest != null && OfferForestUrpPackage(forest)) return;

            EnsureFolder(Out + "/Resources");
            EnsureFolder(Out + "/Models");
            EnsureFolder(Out + "/Animators");

            var art = AssetDatabase.LoadAssetAtPath<BoughtArt>(ArtAsset);
            if (art == null)
            {
                art = ScriptableObject.CreateInstance<BoughtArt>();
                AssetDatabase.CreateAsset(art, ArtAsset);
            }

            try
            {
                if (dinoPack != null)
                {
                    ConvertMaterials(dinoPack);
                    SetUpDinosaurs(art, creatures);
                }
                SetUpNature(art, forest, dinoPack);
                if (survivors != null) SetUpSurvivors(art, survivors);
                else art.survivors = new ModelDefinition[0];
                if (heliPack != null)
                {
                    ConvertMaterials(heliPack, "helicopter-pack", path => path.Contains("/specular/"));
                    SetUpHelicopter(art, heliPack);
                }
                else art.helicopter = null;
            }
            finally
            {
                EditorUtility.SetDirty(art);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                BoughtArt.Reload();
                Directory.CreateDirectory(Path.GetDirectoryName(Report));
                File.WriteAllText(Report, Log.ToString());
            }
            Debug.Log($"[BoughtPackSetup] Done. Press Play to see the bought art. Report: {Path.GetFullPath(Report)}");
        }

        // ── Materials ──────────────────────────────────────────────────────────

        // The dinosaur pack ships built-in pipeline materials. Its ReadMe says to use URP's default shader,
        // so every material that URP can't draw is switched to URP Lit, keeping its textures.
        private static void ConvertMaterials(string pack, string label = "dinosaur-pack", System.Func<string, bool> skip = null)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            int converted = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { pack }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/SceneFx/")) continue; // demo sky, water and particles: not used
                if (skip != null && skip(path)) continue;
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || IsUrp(mat.shader)) continue;

                var names    = mat.GetTexturePropertyNames();
                var baseTex  = FirstTexture(mat, names, "_BaseMap", "_MainTex", "_Albedo", "_Diffuse")
                            ?? TextureLike(mat, names, "main", "albedo", "diffuse", "base", "color", "skin");
                var normal   = FirstTexture(mat, names, "_BumpMap", "_NormalMap", "_Normal")
                            ?? TextureLike(mat, names, "bump", "normal");
                Color tint   = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                // Leaves and fronds in the plant atlas are cut out of the texture.
                bool cutout  = path.Contains("terrainDetails") || mat.name.ToLowerInvariant().Contains("atlas");
                bool glass   = mat.name.ToLowerInvariant().Contains("glass");
                var metal    = FirstTexture(mat, names, "_MetallicGlossMap") ?? TextureLike(mat, names, "metal");
                var occlusion = FirstTexture(mat, names, "_OcclusionMap") ?? TextureLike(mat, names, "occlusion", "_ao");
                string was   = mat.shader != null ? mat.shader.name : "missing";

                mat.shader = lit;
                if (baseTex != null) mat.SetTexture("_BaseMap", baseTex);
                tint.a = 1f;
                mat.SetColor("_BaseColor", tint);
                if (normal != null)
                {
                    MarkNormalMap(normal);
                    mat.SetTexture("_BumpMap", normal);
                    mat.EnableKeyword("_NORMALMAP");
                }
                mat.SetFloat("_Smoothness", cutout ? 0.1f : 0.3f);
                if (metal != null)
                {
                    mat.SetTexture("_MetallicGlossMap", metal);
                    mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                    mat.SetFloat("_Smoothness", 1f); // scales the map's own smoothness
                }
                if (occlusion != null)
                {
                    mat.SetTexture("_OcclusionMap", occlusion);
                    mat.EnableKeyword("_OCCLUSIONMAP");
                }
                if (glass)
                {
                    // Canopy glass: dark, glossy and mostly see-through.
                    mat.SetFloat("_Surface", 1f);
                    mat.SetFloat("_Blend", 0f);
                    mat.SetFloat("_ZWrite", 0f);
                    mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.SetColor("_BaseColor", new Color(tint.r * 0.3f, tint.g * 0.35f, tint.b * 0.4f, 0.35f));
                    mat.SetFloat("_Smoothness", 0.95f);
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    mat.SetOverrideTag("RenderType", "Transparent");
                }
                if (cutout)
                {
                    mat.SetFloat("_AlphaClip", 1f);
                    mat.SetFloat("_Cutoff", 0.5f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.SetFloat("_Cull", 0f);
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                    mat.SetOverrideTag("RenderType", "TransparentCutout");
                }
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                converted++;
                Log.AppendLine($"  material {path}: {was} -> URP Lit (base {(baseTex != null ? baseTex.name : "none")}, normal {(normal != null ? normal.name : "none")})");
            }
            Log.AppendLine($"Converted {converted} {label} materials to URP Lit.");
        }

        private static bool IsUrp(Shader s) =>
            s != null && s.isSupported && s.name != "Hidden/InternalErrorShader" &&
            (s.name.StartsWith("Universal Render Pipeline") || s.name.StartsWith("Shader Graphs") || s.name.Contains("URP"));

        private static Texture FirstTexture(Material m, string[] names, params string[] wanted)
        {
            foreach (var w in wanted)
                if (names.Contains(w) && m.GetTexture(w) != null) return m.GetTexture(w);
            return null;
        }

        private static Texture TextureLike(Material m, string[] names, params string[] parts)
        {
            foreach (var n in names)
            {
                string low = n.ToLowerInvariant();
                if (parts.Any(low.Contains) && m.GetTexture(n) != null) return m.GetTexture(n);
            }
            return null;
        }

        private static void MarkNormalMap(Texture tex)
        {
            var ti = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) as TextureImporter;
            if (ti == null || ti.textureType == TextureImporterType.NormalMap) return;
            ti.textureType = TextureImporterType.NormalMap;
            ti.SaveAndReimport();
        }

        // ── Dinosaurs ──────────────────────────────────────────────────────────

        private static void SetUpDinosaurs(BoughtArt art, string creatures)
        {
            var bySpecies = AssetDatabase.FindAssets("t:DinosaurSpecies")
                .Select(g => AssetDatabase.LoadAssetAtPath<DinosaurSpecies>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(s => s != null)
                .GroupBy(s => s.speciesName)
                .ToDictionary(g => g.Key, g => g.First());

            var looks = new List<BoughtArt.SpeciesLook>();
            foreach (var (speciesName, creature) in Species)
            {
                bySpecies.TryGetValue(speciesName, out var species);
                float walk = species != null ? species.walkSpeed : 3f, run = species != null ? species.runSpeed : 8f;
                var old = AssetDatabase.LoadAssetAtPath<ModelDefinition>($"{ModelData}/Model_{speciesName}.asset")
                       ?? (species != null ? species.model : null);
                float height = old != null ? old.height : 2f;

                var def = BuildCreature(creatures, creature, speciesName, height, flyer: false, walk, run);
                if (def != null) looks.Add(new BoughtArt.SpeciesLook { species = speciesName, model = def });
            }
            art.species = looks.ToArray();
            art.flyer   = BuildCreature(creatures, Flyer, "Flyer", 1.2f, flyer: true, 0f, 0f);
        }

        private static ModelDefinition BuildCreature(string creatures, string creature, string ourName, float height,
                                                     bool flyer, float walk, float run)
        {
            string prefabPath = $"{creatures}/{creature}.prefab";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (source == null) { Log.AppendLine($"{ourName}: {prefabPath} not found, skipped."); return null; }

            var srcAnimator = source.GetComponentInChildren<Animator>();
            string fbx = srcAnimator != null && srcAnimator.avatar != null ? AssetDatabase.GetAssetPath(srcAnimator.avatar) : null;
            if ((string.IsNullOrEmpty(fbx) || !fbx.ToLowerInvariant().EndsWith(".fbx")) && Directory.Exists($"{creatures}/{creature}"))
                fbx = Directory.GetFiles($"{creatures}/{creature}", "*.fbx").Select(p => p.Replace('\\', '/')).FirstOrDefault();
            else if (string.IsNullOrEmpty(fbx) || !fbx.ToLowerInvariant().EndsWith(".fbx")) fbx = null;
            if (fbx == null) { Log.AppendLine($"{ourName}: no model file next to {prefabPath}, skipped."); return null; }

            Log.AppendLine();
            Log.AppendLine($"{ourName} <- {creature} ({fbx})");

            var picked = PrepareClips(fbx, flyer);
            if (picked == null) return null;

            var model = CleanPrefab(source, $"{Out}/Models/{ourName}.prefab");
            var ctrl  = flyer ? FlyController(ourName, picked) : DinoController(ourName, picked, walk, run);

            string defPath = $"{Out}/Models/{ourName}.asset";
            var def = AssetDatabase.LoadAssetAtPath<ModelDefinition>(defPath);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<ModelDefinition>();
                AssetDatabase.CreateAsset(def, defPath);
            }
            def.model    = model;
            def.animator = ctrl;
            def.height   = height;
            def.yawOffset = 0f;
            def.faceHeadForward = !flyer;
            EditorUtility.SetDirty(def);
            return def;
        }

        // Picks idle, walk, run, attack and death (or fly) from the creature's animations by name, makes the moving
        // ones loop and keeps them in place (the AI moves the body; the animation only poses it).
        private static Dictionary<string, AnimationClip> PrepareClips(string fbx, bool flyer)
        {
            var imp = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (imp == null) { Log.AppendLine("  not a model file, skipped."); return null; }
            if (imp.animationType == ModelImporterAnimationType.Legacy || imp.animationType == ModelImporterAnimationType.None)
            {
                imp.animationType = ModelImporterAnimationType.Generic;
                imp.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.SaveAndReimport();
            }

            var clips = LoadClips(fbx);
            Log.AppendLine($"  animations: {string.Join(", ", clips.Select(c => c.name))}");
            var picked = new Dictionary<string, AnimationClip>();
            if (flyer)
            {
                picked["Fly"] = Best(clips, new[] { "flight", "glide", "fly", "flap", "soar" }) ?? Best(clips, new[] { "idle" });
            }
            else
            {
                picked["Idle"]   = Best(clips, new[] { "idle", "stand", "breath" });
                picked["Walk"]   = Best(clips, new[] { "walk" });
                picked["Run"]    = Best(clips, new[] { "run", "sprint", "gallop", "trot", "charge" }) ?? picked["Walk"];
                // The pack names attacks IdleAtk1, StepAtk1, AtkA...; a standing bite or swipe reads best.
                picked["Attack"] = Best(clips, new[] { "idleatk", "atka", "stepatk", "atk", "bite", "attack", "claw", "strike" });
                picked["Death"]  = Best(clips, new[] { "die", "death", "dead", "fall" });
                if (picked["Idle"] == null) picked["Idle"] = clips.FirstOrDefault();
                if (picked["Walk"] == null) picked["Walk"] = picked["Idle"];
                if (picked["Run"]  == null) picked["Run"]  = picked["Walk"];
            }
            foreach (var kv in picked) Log.AppendLine($"  {kv.Key,-6} = {(kv.Value != null ? kv.Value.name : "MISSING")}");
            if (picked.Values.All(c => c == null)) { Log.AppendLine("  no usable animations, skipped."); return null; }

            // Loop the moving clips and find the bone that carries the walk forward, if any.
            var loopNames = new HashSet<string>(new[] { "Idle", "Walk", "Run", "Fly" }
                .Where(picked.ContainsKey).Select(k => picked[k]).Where(c => c != null).Select(c => c.name));
            var moving = picked.TryGetValue("Run", out var r) && r != null ? r : picked.TryGetValue("Walk", out var w) ? w : null;
            string motionBone = moving != null ? TravellingBone(moving) : null;
            Log.AppendLine($"  moves forward through: {motionBone ?? "nothing (animations are in place)"}");

            var settings = imp.clipAnimations.Length > 0 ? imp.clipAnimations : imp.defaultClipAnimations;
            foreach (var c in settings)
            {
                string clean = c.name;
                if (loopNames.Contains(clean) || loopNames.Contains(clean.Substring(clean.LastIndexOf('|') + 1)))
                    c.loopTime = true;
                if (motionBone != null)
                {
                    // Root motion is pulled out of the travelling bone and then not applied: the animal walks on the spot.
                    c.lockRootRotation   = true;
                    c.lockRootHeightY    = true;
                    c.lockRootPositionXZ = false;
                    c.keepOriginalOrientation = true;
                    c.keepOriginalPositionY   = true;
                }
            }
            imp.clipAnimations = settings;
            if (motionBone != null)
            {
                // The importer names nodes by their path from the model's root; match the clip's bone path to one.
                string node = imp.transformPaths.Where(p => p == motionBone || p.EndsWith("/" + motionBone))
                                                .OrderBy(p => p.Length).FirstOrDefault();
                if (node != null) imp.motionNodeName = node;
                Log.AppendLine($"  root motion node: {node ?? "not found in " + string.Join(", ", imp.transformPaths.Take(6))}");
            }
            imp.SaveAndReimport();

            // Reload: reimporting replaced the clip objects.
            var fresh = LoadClips(fbx).ToDictionary(c => c.name, c => c);
            return picked.ToDictionary(kv => kv.Key,
                kv => kv.Value != null && fresh.TryGetValue(kv.Value.name, out var c) ? c : null);
        }

        private static List<AnimationClip> LoadClips(string fbx) =>
            AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>()
                         .Where(c => !c.name.StartsWith("__preview__")).ToList();

        // Words that mark a variant we don't want for the basic loop (turning, backwards, eating...).
        private static readonly string[] Variants =
        {
            "back", "left", "right", "turn", "jump", "swim", "land", "takeoff", "take off", "start", "end", "stop",
            "eat", "drink", "sleep", "limp", "growl", "call", "roar", "getup", "get up", "rise", "water", "air",
            "crouch", "strafe", "sit", "ground", "-",
        };

        private static AnimationClip Best(List<AnimationClip> clips, string[] words)
        {
            AnimationClip best = null;
            int bestScore = int.MaxValue;
            foreach (var c in clips)
            {
                string n = c.name.ToLowerInvariant();
                int wordIndex = System.Array.FindIndex(words, n.Contains);
                if (wordIndex < 0) continue;
                int score = wordIndex * 100 + Variants.Count(n.Contains) * 1000 + n.Length;
                if (score < bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        // The shallowest bone whose horizontal position travels more than half a body length during the clip.
        private static string TravellingBone(AnimationClip clip)
        {
            var travel = new Dictionary<string, float>();
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.type != typeof(Transform)) continue;
                if (b.propertyName != "m_LocalPosition.x" && b.propertyName != "m_LocalPosition.z") continue;
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                if (curve == null || curve.length < 2) continue;
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var k in curve.keys) { lo = Mathf.Min(lo, k.value); hi = Mathf.Max(hi, k.value); }
                travel.TryGetValue(b.path, out var t);
                travel[b.path] = Mathf.Max(t, hi - lo);
            }
            if (travel.Count == 0) return null;
            float most = travel.Values.Max();
            // Compare against how far bones normally swing: a real walk carries the root many times further.
            var sorted = travel.Values.OrderBy(v => v).ToList();
            float typical = sorted[sorted.Count / 2];
            if (most < 0.5f || most < typical * 6f) return null;
            return travel.Where(kv => kv.Value > most * 0.5f)
                         .OrderBy(kv => kv.Key.Count(ch => ch == '/')).ThenByDescending(kv => kv.Value)
                         .First().Key;
        }

        // A copy of the pack's prefab with only what draws and animates it: no scripts, physics or sound, which belong
        // to the pack's own AI and would fight ours.
        private static GameObject CleanPrefab(GameObject source, string path)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                for (int pass = 0; pass < 4; pass++)
                    // Scripts first: they're what require the physics and sound components.
                    foreach (var c in inst.GetComponentsInChildren<Component>(true).OrderBy(c => c is MonoBehaviour ? 0 : 1))
                    {
                        if (c == null || Keep(c)) continue;
                        Object.DestroyImmediate(c); // fails quietly for a component another one still requires; next pass
                    }
                foreach (var t in inst.GetComponentsInChildren<Transform>(true).Reverse())
                    if (t != null && t != inst.transform && t.GetComponentsInChildren<Component>(true).All(c => c is Transform) && t.childCount == 0
                        && !IsBone(inst, t))
                        Object.DestroyImmediate(t.gameObject);
                // Keep the pack's own root rotation and scale (models exported Z-up stand on it) under a plain
                // parent that ModelFit can turn and scale freely.
                var wrapper = new GameObject(Path.GetFileNameWithoutExtension(path));
                inst.transform.SetParent(wrapper.transform, false);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = source.transform.localRotation;
                inst.transform.localScale    = source.transform.localScale;
                try { return PrefabUtility.SaveAsPrefabAsset(wrapper, path); }
                finally { Object.DestroyImmediate(wrapper); inst = null; }
            }
            finally { if (inst != null) Object.DestroyImmediate(inst); }
        }

        private static bool Keep(Component c) =>
            c is Transform || c is Animator || c is SkinnedMeshRenderer || c is MeshRenderer || c is MeshFilter || c is LODGroup;

        private static bool IsBone(GameObject root, Transform t) =>
            root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(s => s.rootBone == t || s.bones.Contains(t));

        private static AnimatorController DinoController(string name, Dictionary<string, AnimationClip> clips, float walk, float run)
        {
            var ctrl = NewController(name);
            ctrl.AddParameter("Speed",  AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Dead",   AnimatorControllerParameterType.Bool);
            var sm = ctrl.layers[0].stateMachine;

            var loco = ctrl.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(clips["Idle"], 0f);
            tree.AddChild(clips["Walk"], Mathf.Max(0.5f, walk));
            tree.AddChild(clips["Run"],  Mathf.Max(walk + 0.5f, run));
            if (clips["Run"] == clips["Walk"])
            {
                // No run in the pack (the big theropod and the long-neck): a quicker walk instead of sliding feet.
                var children = tree.children;
                children[2].timeScale = Mathf.Clamp(run / Mathf.Max(0.5f, walk), 1f, 2.2f);
                tree.children = children;
            }
            sm.defaultState = loco;

            if (clips["Attack"] != null)
            {
                var attack = sm.AddState("Attack");
                attack.motion = clips["Attack"];
                var toAttack = sm.AddAnyStateTransition(attack);
                toAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
                toAttack.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
                toAttack.canTransitionToSelf = false;
                toAttack.duration = 0.1f;
                var back = attack.AddTransition(loco);
                back.hasExitTime = true; back.exitTime = 0.9f; back.duration = 0.15f;
            }
            if (clips["Death"] != null)
            {
                var death = sm.AddState("Death");
                death.motion = clips["Death"];
                var toDeath = sm.AddAnyStateTransition(death);
                toDeath.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
                toDeath.canTransitionToSelf = false;
                toDeath.duration = 0.15f;
            }
            return ctrl;
        }

        private static AnimatorController FlyController(string name, Dictionary<string, AnimationClip> clips)
        {
            var ctrl = NewController(name);
            var fly = ctrl.layers[0].stateMachine.AddState("Fly");
            fly.motion = clips["Fly"];
            ctrl.layers[0].stateMachine.defaultState = fly;
            return ctrl;
        }

        private static AnimatorController NewController(string name)
        {
            string path = $"{Out}/Animators/{name}.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);
            return AnimatorController.CreateAnimatorControllerAtPath(path);
        }

        // ── Survivors ──────────────────────────────────────────────────────────

        // Each outfit prefab of the survivor pack becomes a player look. The body is imported as a humanoid so the
        // survivor controller built from the free animation library (Set Up Model Packs) drives it.
        private static void SetUpSurvivors(BoughtArt art, string dir)
        {
            Log.AppendLine();
            Log.AppendLine($"Survivors <- {dir}");
            var avatar = HumanAvatar(dir);

            var template = AssetDatabase.LoadAssetAtPath<ModelDefinition>($"{ModelData}/Model_Survivor.asset");
            if (template == null || template.animator == null)
                Log.AppendLine("  Model_Survivor has no animations yet: run Project Fossil > Art > Set Up Model Packs, then this again.");

            string prefabDir = $"{dir}/Prefab";
            var sources = Directory.Exists(prefabDir)
                ? Directory.GetFiles(prefabDir, "Survivalist*.prefab").Select(p => p.Replace('\\', '/')).OrderBy(p => p).ToList()
                : new List<string>();
            if (sources.Count == 0) { Log.AppendLine($"  no Survivalist prefabs in {prefabDir}, skipped."); art.survivors = new ModelDefinition[0]; return; }

            EnsureFolder(Out + "/Survivors");
            var defs = new List<ModelDefinition>();
            for (int i = 0; i < sources.Count; i++)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(sources[i]);
                if (source == null) continue;
                string name = $"Survivor_{i + 1}";
                string modelPath = $"{Out}/Survivors/{name}.prefab";
                CleanPrefab(source, modelPath);
                FixSurvivor(modelPath, dir, avatar);

                string defPath = $"{Out}/Survivors/{name}.asset";
                var def = AssetDatabase.LoadAssetAtPath<ModelDefinition>(defPath);
                if (def == null)
                {
                    def = ScriptableObject.CreateInstance<ModelDefinition>();
                    AssetDatabase.CreateAsset(def, defPath);
                }
                def.model           = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                def.animator        = template != null ? template.animator : null;
                def.height          = template != null ? template.height : 1.8f;
                def.yawOffset       = 0f;     // Unity-made prefabs face +Z
                def.faceHeadForward = false;
                def.attachments     = new GameObject[0];
                def.trimMeshes      = new string[0];
                def.keepBones       = new string[0];
                EditorUtility.SetDirty(def);
                defs.Add(def);
                Log.AppendLine($"  {name} <- {sources[i]}");
            }
            art.survivors = defs.ToArray();
        }

        // The pack's body model, imported as a humanoid. Returns its avatar.
        private static Avatar HumanAvatar(string dir)
        {
            string fbx = Directory.Exists($"{dir}/Basemesh")
                ? Directory.GetFiles($"{dir}/Basemesh", "*.fbx").Select(p => p.Replace('\\', '/')).FirstOrDefault()
                : null;
            if (fbx == null) { Log.AppendLine("  no body model in Basemesh; keeping the prefabs' own avatars."); return null; }
            var imp = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (imp != null && imp.animationType != ModelImporterAnimationType.Human)
            {
                Log.AppendLine($"  {fbx}: rig {imp.animationType} -> Humanoid");
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup   = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.SaveAndReimport();
            }
            var avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().FirstOrDefault();
            Log.AppendLine($"  avatar: {(avatar == null ? "none" : avatar.name + (avatar.isHuman ? " (humanoid)" : " (NOT humanoid)"))}");
            return avatar;
        }

        // URP versions of the outfit materials, and the humanoid avatar on the Animator.
        private static void FixSurvivor(string prefabPath, string dir, Avatar avatar)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null || IsUrp(m.shader)) continue;
                        string bare = m.name.StartsWith("HDRP_") ? m.name.Substring(5) : m.name.StartsWith("URP_") ? m.name.Substring(4) : m.name;
                        var urp = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/Materials URP/URP_{bare}.mat");
                        if (urp != null) mats[i] = urp;
                        else Log.AppendLine($"  {r.name}: {m.name} ({(m.shader != null ? m.shader.name : "no shader")}) has no URP version");
                    }
                    r.sharedMaterials = mats;
                }
                foreach (var an in root.GetComponentsInChildren<Animator>(true))
                {
                    if (avatar != null && (an.avatar == null || !an.avatar.isHuman)) an.avatar = avatar;
                    an.runtimeAnimatorController = null; // ours is set at runtime
                    an.applyRootMotion = false;
                }
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ── Helicopter ─────────────────────────────────────────────────────────

        private static void SetUpHelicopter(BoughtArt art, string dir)
        {
            Log.AppendLine();
            var prefabs = Directory.GetFiles(dir, "*.prefab", SearchOption.AllDirectories)
                                   .Select(p => p.Replace('\\', '/'))
                                   .OrderBy(p => p.Contains("/metallic/") ? 0 : 1).ThenBy(p => p).ToList();
            var source = prefabs.Select(AssetDatabase.LoadAssetAtPath<GameObject>).FirstOrDefault(g => g != null);
            if (source == null) { Log.AppendLine($"Helicopter: no prefab in {dir}, skipped."); art.helicopter = null; return; }
            Log.AppendLine($"Helicopter <- {AssetDatabase.GetAssetPath(source)}");

            string path = $"{Out}/Models/Helicopter.prefab";
            CleanPrefab(source, path);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                // Our flight code spins the rotors; the pack's demo animation would fight it.
                foreach (var an in root.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(an);
                var b = new Bounds();
                bool any = false;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                Log.AppendLine($"  size {b.size.x:0.0} x {b.size.y:0.0} x {b.size.z:0.0} m (x, y, z); parts:");
                foreach (var t in root.GetComponentsInChildren<Transform>(true).Take(120))
                {
                    int depth = 0;
                    for (var p = t.parent; p != null; p = p.parent) depth++;
                    Log.AppendLine($"  {new string(' ', depth * 2)}{t.name}{(t.GetComponent<Renderer>() != null ? " [mesh]" : "")}");
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            art.helicopter = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // ── Nature ─────────────────────────────────────────────────────────────

        private static void SetUpNature(BoughtArt art, string forest, string dinoPack)
        {
            string tropics = dinoPack != null ? $"{dinoPack}/Scene/terrainDetails" : null;
            GameObject[] F(string folder, params string[] prefixes) => Prefabs(forest, folder, prefixes);
            GameObject[] T(params string[] names) =>
                tropics == null ? new GameObject[0]
                : names.Select(n => AssetDatabase.LoadAssetAtPath<GameObject>($"{tropics}/{n}.prefab")).Where(g => g != null).ToArray();
            GameObject[] All(params GameObject[][] lists) => lists.SelectMany(l => l).ToArray();

            var beech    = F("Beech Trees/Prefabs", "prefab_beech_tree_0");
            var oldBeech = F("Beech Trees/Prefabs", "prefab_beech_tree_00");
            var young    = F("Beech Trees/Prefabs", "prefab_beech_plant");
            var ferns    = F("Foliage and Grass/Prefabs", "prefab_fern");
            var plants   = F("Foliage and Grass/Prefabs", "prefab_plant", "prefab_ivy");
            var grass    = F("Foliage and Grass/Prefabs", "prefab_grass", "prefab_shaggy", "prefab_lily");
            var bushes   = F("Bushes/Prefabs", "Prefab_Forest_black_cherry");
            var stones   = F("Rocks/Prefabs", "prefab_beech_forest_stones");
            var mossy    = F("Rocks/Prefabs Leaves", "prefab_");
            var logs     = F("Stumps Roots and Branches/Prefabs", "prefab_dead_log", "prefab_beech_forest_stump", "prefab_Beech_forest_stump", "prefab_beech_old_roots");
            var boughs   = F("Stumps Roots and Branches/Prefabs", "prefab_beech_dry_bough");
            var shrooms  = F("Mushrooms/Prefabs", "prefab_");
            var palms    = T("PalmA", "PalmB", "PalmC");
            var jungleTrees = T("BanyanTree", "ThinTree");
            var tropicalPlants = T("BananaPlant", "Mimosa");
            var tropicalRocks  = T("RockA", "RockB", "RockC");

            // Mature beeches are fine as jungle canopy giants; young beeches pass for undergrowth.
            art.biomes = new[]
            {
                Biome("Jungle",   All(jungleTrees, jungleTrees, palms, beech), All(stones, mossy, tropicalRocks, logs),
                                  All(ferns, ferns, tropicalPlants, plants, bushes, young), Layer(forest, "Terrain_Layer5_Leaves")),
                Biome("Plains",   All(beech),                                 All(stones, tropicalRocks),
                                  All(grass, grass, bushes),                    Layer(forest, "Terrain_Layer4_Grass_Plants")),
                Biome("Swamp",    All(jungleTrees, oldBeech),                 All(logs, mossy, stones),
                                  All(ferns, plants, shrooms, young),           Layer(forest, "Terrain_Layer3_Soil_Wet")),
                Biome("Beach",    All(palms),                                 All(tropicalRocks, stones),
                                  All(tropicalPlants, grass),                   Layer(forest, "Terrain_Layer1_Sand") ?? Layer(dinoPack, "layer_sand")),
                Biome("Volcanic", All(boughs),                                All(tropicalRocks, stones, logs),
                                  All(shrooms),                                 Layer(forest, "Terrain_Layer8_Stones") ?? Layer(dinoPack, "layer_rock")),
            };
            art.cliff = Layer(dinoPack, "layer_cliff") ?? Layer(forest, "Terrain_Layer8_Stones");
            art.dirt  = Layer(forest, "Terrain_Layer2_Soil");
            art.moss  = Layer(forest, "Terrain_Layer6_Moss");

            string windDir = FindDir("NatureManufacture Wind");
            art.windZone = windDir != null ? AssetDatabase.LoadAssetAtPath<GameObject>($"{windDir}/Prefab_Wind.prefab") : null;

            Log.AppendLine();
            foreach (var b in art.biomes)
                Log.AppendLine($"{b.biome}: {b.trees.Length} trees, {b.rocks.Length} rocks, {b.plants.Length} plants, ground {(b.ground != null ? b.ground.name : "none")}");
            Log.AppendLine($"Cliff {Name(art.cliff)}, dirt {Name(art.dirt)}, moss {Name(art.moss)}, wind {(art.windZone != null ? art.windZone.name : "none")}");
            if (forest != null) ReportShaders(forest);
        }

        private static string Name(Object o) => o != null ? o.name : "none";

        private static BoughtArt.BiomeLook Biome(string name, GameObject[] trees, GameObject[] rocks, GameObject[] plants, TerrainLayer ground) =>
            new BoughtArt.BiomeLook { biome = name, trees = trees, rocks = rocks, plants = plants, ground = ground };

        private static GameObject[] Prefabs(string root, string folder, params string[] prefixes)
        {
            if (root == null) return new GameObject[0];
            string dir = $"{root}/{folder}";
            if (!Directory.Exists(dir)) { Log.AppendLine($"  missing folder {dir}"); return new GameObject[0]; }
            return Directory.GetFiles(dir, "*.prefab")
                .Select(p => p.Replace('\\', '/'))
                .Where(p => prefixes.Any(Path.GetFileName(p).StartsWith))
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(g => g != null)
                .ToArray();
        }

        private static TerrainLayer Layer(string root, string fileStart)
        {
            if (root == null) return null;
            foreach (var guid in AssetDatabase.FindAssets("t:TerrainLayer", new[] { root }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(p).StartsWith(fileStart)) return MatteCopy(AssetDatabase.LoadAssetAtPath<TerrainLayer>(p));
            }
            return null;
        }

        // The packs' ground layers carry mask maps packed for their own terrain shaders. Our URP terrain reads them
        // as wet and metallic, so the forest floor glitters white and blue. A copy with colour and bumps only is matte,
        // and a larger tile hides the repeat grid.
        private const float MinGroundTile = 6f;

        private static TerrainLayer MatteCopy(TerrainLayer source)
        {
            if (source == null) return null;
            EnsureFolder(Out + "/Ground");
            string path = $"{Out}/Ground/{source.name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            bool isNew = layer == null;
            if (isNew) layer = new TerrainLayer();

            layer.diffuseTexture   = source.diffuseTexture;
            layer.normalMapTexture = source.normalMapTexture;
            layer.normalScale      = Mathf.Min(source.normalScale, 1f);
            layer.maskMapTexture   = null;
            layer.smoothness       = 0f; // URP terrain: smoothness = albedo alpha × this
            layer.metallic         = 0f;
            layer.specular         = Color.black;
            layer.diffuseRemapMin  = Vector4.zero;
            layer.diffuseRemapMax  = Vector4.one;
            layer.tileOffset       = Vector2.zero;
            layer.tileSize         = new Vector2(Mathf.Max(source.tileSize.x, MinGroundTile), Mathf.Max(source.tileSize.y, MinGroundTile));

            if (isNew) AssetDatabase.CreateAsset(layer, path);
            else EditorUtility.SetDirty(layer);
            Log.AppendLine($"  ground {source.name}: tile {source.tileSize.x:0.#} -> {layer.tileSize.x:0.#} m, mask map {(source.maskMapTexture != null ? "dropped" : "none")}");
            return layer;
        }

        // The forest pack ships for the built-in pipeline, with its URP version as a package inside it. Until that's
        // imported, every plant shows magenta. Returns true when the import was started (run the setup again after).
        private static bool OfferForestUrpPackage(string forest)
        {
            int builtIn = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { forest }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && !IsUrp(m.shader) && !m.shader.name.StartsWith("Particles") && !m.shader.name.StartsWith("Legacy")) builtIn++;
            }
            if (builtIn == 0) return false;
            string package = Directory.GetFiles(forest, "*.unitypackage", SearchOption.AllDirectories)
                                      .Where(p => Path.GetFileName(p).StartsWith("URP"))
                                      .OrderByDescending(p => p).FirstOrDefault();
            if (package == null)
            {
                Log.AppendLine($"{builtIn} forest materials use built-in shaders and no URP package was found in the pack.");
                return false;
            }
            if (!EditorUtility.DisplayDialog("Forest pack for URP",
                    $"{builtIn} forest materials are built for the old render pipeline and show pink.\n\n" +
                    $"The pack includes its URP version ({Path.GetFileName(package)}). Import it now? " +
                    "Click Import in the next window, then run Set Up Bought Packs again.",
                    "Import", "Skip")) return false;
            AssetDatabase.ImportPackage(package, true);
            return true;
        }

        // The forest pack's own shaders: anything URP can't draw shows magenta, so list it.
        private static void ReportShaders(string forest)
        {
            var counts = new Dictionary<string, int>();
            int broken = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { forest }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null) continue;
                string n = m.shader == null ? "missing" : m.shader.name + (m.shader.isSupported ? "" : " (NOT SUPPORTED)");
                if (m.shader == null || !m.shader.isSupported || m.shader.name == "Hidden/InternalErrorShader") broken++;
                counts.TryGetValue(n, out var c);
                counts[n] = c + 1;
            }
            Log.AppendLine();
            Log.AppendLine("Forest pack shaders:");
            foreach (var kv in counts.OrderByDescending(k => k.Value)) Log.AppendLine($"  {kv.Value,4}  {kv.Key}");
            var packages = Directory.GetFiles(forest, "*.unitypackage", SearchOption.AllDirectories);
            if (packages.Length > 0) Log.AppendLine("Render pipeline packages inside the pack: " + string.Join(", ", packages.Select(Path.GetFileName)));
            if (broken > 0)
                Debug.LogWarning($"[BoughtPackSetup] {broken} forest materials use shaders URP can't draw (magenta). See {Report}.");
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        // First folder under Assets whose path ends with `tail` (packs land wherever their authors put them).
        private static string FindDir(string tail)
        {
            string last = tail.Contains('/') ? tail.Substring(tail.LastIndexOf('/') + 1) : tail;
            return Directory.GetDirectories("Assets", last, SearchOption.AllDirectories)
                            .Select(d => d.Replace('\\', '/'))
                            .Where(d => d.EndsWith("/" + tail) && !d.StartsWith(Out))
                            .OrderBy(d => d.Length)
                            .FirstOrDefault();
        }

        private static string Parent(string dir) => dir.Substring(0, dir.LastIndexOf('/'));

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Parent(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
    }
}
