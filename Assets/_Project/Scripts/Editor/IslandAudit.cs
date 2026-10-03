using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Unity.Pipeline.Commands;
using ProjectFossil.Core;
using ProjectFossil.Dinosaurs;
using ProjectFossil.Match;
using ProjectFossil.Player;

namespace ProjectFossil.Editor
{
    // Plays the island the way a tester would, for a list of seeds, and writes down what went wrong.
    // For each seed: generate the island, walk the survivor from the spawn to every extraction along the NavMesh
    // (logging where it gets stuck), watch every actor for falling under the ground and every dinosaur for standing
    // in deep water, time the frames, and save pictures from fixed viewpoints and from the player's camera.
    //
    // Runs in Play mode (the match needs its MonoBehaviours). From the terminal:
    //   unity cmd editor_play
    //   unity cmd fossil_audit --seeds 1,2,3
    //   unity cmd fossil_audit_status          (until "done")
    // Report and pictures land in Logs/FossilAudit/<time>/.
    public static class IslandAudit
    {
        public static AuditRunner Runner { get; internal set; }

        [MenuItem("Project Fossil/Audit/Run Island Audit (seeds 1-4)")]
        private static void RunFromMenu()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[Audit] Enter Play mode first."); return; }
            Start("1,2,3,4", 3f, 0);
        }

        [CliCommand("fossil_audit", "Project Fossil: in Play mode, generate each seed, walk the survivor to every " +
                    "extraction, check actors against IslandWorld ground and deep water, log fps and save " +
                    "screenshots to Logs/FossilAudit. Returns at once; poll fossil_audit_status.",
                    Tags = new[] { "tests" })]
        public static object Start(
            [CliArg("seeds", "Comma-separated seeds")] string seeds = "1,2,3,4",
            [CliArg("time_scale", "Game speed while walking (1 = real time)")] float timeScale = 3f,
            [CliArg("walks", "Extractions to walk to per seed (0 = all)")] int walks = 0)
        {
            if (!EditorApplication.isPlaying)
                return new { started = false, error = "Not in Play mode. Run `unity cmd editor_play` first." };
            if (Runner != null && Runner.Running)
                return new { started = false, error = "An audit is already running.", status = Runner.Status() };
            var bootstrap = Object.FindFirstObjectByType<MatchBootstrap>();
            if (bootstrap == null) return new { started = false, error = "No MatchBootstrap in the scene." };

            var list = seeds.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Select(int.Parse).ToList();
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "FossilAudit",
                                      System.DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(dir);

            // Keep the game ticking while the Editor is in the background (this Play session only; the project
            // setting is untouched): unfocused, the walk froze until someone clicked on Unity.
            Application.runInBackground = true;
            var go = new GameObject("IslandAudit");
            Object.DontDestroyOnLoad(go);
            Runner = go.AddComponent<AuditRunner>();
            Runner.Begin(bootstrap, list, Mathf.Max(0.25f, timeScale), walks, dir);
            return new { started = true, seeds = list, output = dir };
        }

        [CliCommand("fossil_audit_status", "Project Fossil: progress of the running island audit, and the report " +
                    "summary once it is done.", MainThreadRequired = false, Tags = new[] { "tests" })]
        public static object Status()
        {
            if (Runner == null) return new { running = false, note = "No audit started in this Play session." };
            return Runner.Status();
        }

        [CliCommand("fossil_audit_stop", "Project Fossil: stop the running island audit and write what it has.",
                    Tags = new[] { "tests" })]
        public static object Stop()
        {
            if (Runner == null) return new { stopped = false };
            Runner.Abort();
            return Runner.Status();
        }

        [CliCommand("fossil_slope_check", "Project Fossil: generate each seed with the game's island settings (no " +
                    "Play mode needed) and report the steepest dry ground step, how many steps are steeper than 45 " +
                    "degrees and how many are walls (over 56 degrees), with where the worst one is.",
                    Tags = new[] { "tests" })]
        public static object SlopeCheck(
            [CliArg("seeds", "Comma-separated seeds")] string seeds = "0,1,2,3,4,5,6,7",
            [CliArg("settings", "IslandSettings asset path")] string settings = "Assets/_Project/Data/Island/IslandSettings_Default.asset")
        {
            var s = AssetDatabase.LoadAssetAtPath<ProjectFossil.Generation.IslandSettings>(settings);
            if (s == null) return new { error = $"No IslandSettings at {settings}" };
            var rows = new List<object>();
            int[] dx = { 1, 0, 1, -1 }, dz = { 0, 1, 1, 1 };
            foreach (int seed in seeds.Split(',').Select(t => int.Parse(t.Trim())))
            {
                var world = ProjectFossil.Generation.WaterField.Build(new ProjectFossil.Generation.IslandGenerator(seed, s).Generate());
                int n = world.Size, steep = 0, walls = 0, wx = 0, wz = 0;
                float worst = 0f;
                for (int z = 0; z < n; z++)
                    for (int x = 0; x < n; x++)
                    {
                        if (world.WetVertex(x, z)) continue;
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = x + dx[k], nz = z + dz[k];
                            if (nx < 0 || nz >= n || nx >= n || world.WetVertex(nx, nz)) continue;
                            float run = world.Cell * (dx[k] != 0 && dz[k] != 0 ? 1.41421356f : 1f);
                            float grade = Mathf.Abs(world.GroundAtVertex(nx, nz) - world.GroundAtVertex(x, z)) / run;
                            if (grade > ProjectFossil.Generation.WaterField.MaxGroundGrade + 0.02f) steep++;
                            if (grade > 1.5f) walls++;
                            if (grade > worst) { worst = grade; wx = x; wz = z; }
                        }
                    }
                rows.Add(new { seed, worstDegrees = Mathf.Round(Mathf.Atan(worst) * Mathf.Rad2Deg), steeperThan45 = steep, walls,
                               worstAt = $"{wx * world.Cell:0},{wz * world.Cell:0}" });
            }
            return rows;
        }

        [CliCommand("fossil_snapshot", "Project Fossil: in Play mode, save the player camera (and optionally a " +
                    "top-down view of the island) and return the player's state: position, ground, water, health, " +
                    "nearby dinosaurs, fps.", Tags = new[] { "capture" })]
        public static object Snapshot(
            [CliArg("save_dir", "Folder to write PNGs into (project-relative)")] string saveDir = "Logs/FossilPlay",
            [CliArg("name", "File name prefix")] string name = "shot",
            [CliArg("overview", "Also save the top-down island view")] bool overview = false,
            [CliArg("eye", "Save the player camera")] bool eye = true)
        {
            if (!EditorApplication.isPlaying) return new { error = "Not in Play mode." };
            Directory.CreateDirectory(saveDir);
            var match = Object.FindFirstObjectByType<MatchManager>();
            var player = match != null ? match.Player : GameObject.FindWithTag("Player");
            var files = new List<string>();
            var cam = player != null ? player.GetComponentInChildren<Camera>() : Camera.main;
            if (eye && cam != null) files.Add(AuditCamera.SaveFrom(cam, Path.Combine(saveDir, name + "_eye.png")));
            if (overview)
                files.Add(AuditCamera.SaveOverview(Path.Combine(saveDir, name + "_top.png"),
                                                   player != null ? player.transform.position : (Vector3?)null));
            return new { files, state = AuditRunner.Describe(player, match) };
        }
    }

    // Renders a camera into a PNG without touching the Game view.
    internal static class AuditCamera
    {
        public const int Width = 1280, Height = 720;

        public static string SaveFrom(Camera cam, string path)
        {
            var rt = RenderTexture.GetTemporary(Width, Height, 24);
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = old;
            Write(rt, path);
            RenderTexture.ReleaseTemporary(rt);
            return path;
        }

        // A free camera at a fixed pose (fog off, so the far side of the island shows).
        public static string SaveFrom(Vector3 position, Vector3 lookAt, float fov, string path)
        {
            var go = new GameObject("AuditCamera");
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position));
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 5000f;
            var main = Camera.main;
            if (main != null) { cam.clearFlags = main.clearFlags; cam.backgroundColor = main.backgroundColor; }
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            try { return SaveFrom(cam, path); }
            finally { RenderSettings.fog = fog; Object.DestroyImmediate(go); }
        }

        // The whole island from above, with a red post where the survivor stands.
        public static string SaveOverview(string path, Vector3? marker)
        {
            var world = IslandWorld.Current;
            if (world == null) return null;
            Vector3 c = world.Origin + new Vector3(world.Extent * 0.5f, 0f, world.Extent * 0.5f);
            GameObject pin = null;
            if (marker.HasValue)
            {
                pin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(pin.GetComponent<Collider>());
                pin.transform.position = marker.Value + Vector3.up * 40f;
                pin.transform.localScale = new Vector3(12f, 40f, 12f);
                var r = pin.GetComponent<Renderer>();
                r.sharedMaterial = new Material(r.sharedMaterial) { color = Color.red };
            }
            try { return SaveFrom(c + new Vector3(0f, world.Extent * 1.05f, -world.Extent * 0.02f), c, 55f, path); }
            finally { if (pin != null) Object.DestroyImmediate(pin); }
        }

        private static void Write(RenderTexture rt, string path)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }

    public class AuditRunner : MonoBehaviour
    {
        // How far below the drawn ground an actor may sit before it counts as fallen through (terrain triangles
        // and the bilinear IslandWorld height differ a little on steep ground).
        private const float UnderGroundTolerance = 0.6f;
        // Seconds (game time) without this much progress count as stuck.
        private const float StuckWindow   = 3f;
        private const float StuckDistance = 1f;
        private const int   MaxStuckShotsPerSeed = 4;

        public bool Running { get; private set; }

        private MatchBootstrap _bootstrap;
        private List<int> _seeds;
        private float _timeScale;
        private int _walks;
        private string _dir;
        private bool _abort;
        private string _phase = "starting";
        private int _seedIndex;
        private readonly List<SeedReport> _reports = new List<SeedReport>();
        private SeedReport _current;

        private class Issue
        {
            public string kind, detail;
            public Vector3 at;
            public override string ToString() => $"{kind} at ({at.x:0},{at.y:0.0},{at.z:0}): {detail}";
        }

        private class SeedReport
        {
            public int seed;
            public int extractions, reached, unreachable, stuck, teleports, dinos;
            public float fpsAvg, fpsLow, worstFrameMs;
            public readonly List<float> frameMs = new List<float>();
            public readonly List<string> legs = new List<string>();
            public readonly List<Issue> issues = new List<Issue>();
            public readonly HashSet<(string, Transform)> seen = new HashSet<(string, Transform)>();
            public readonly List<string> shots = new List<string>();
        }

        public void Begin(MatchBootstrap bootstrap, List<int> seeds, float timeScale, int walks, string dir)
        {
            _bootstrap = bootstrap; _seeds = seeds; _timeScale = timeScale; _walks = walks; _dir = dir;
            Running = true;
            StartCoroutine(Run());
        }

        public void Abort() => _abort = true;

        // Read off the main thread (so it answers while the game is busy): a list changing underfoot just means
        // the summary waits for the next poll.
        public object Status()
        {
            object seeds;
            try { seeds = _reports.ToArray().Select(Summary).ToList(); }
            catch (System.InvalidOperationException) { seeds = "busy"; }
            return new
            {
                running = Running,
                phase = _phase,
                seed = _current?.seed,
                progress = $"{_seedIndex}/{_seeds?.Count ?? 0}",
                output = _dir,
                seeds,
            };
        }

        private static object Summary(SeedReport r) => new
        {
            r.seed, r.extractions, r.reached, r.unreachable, r.stuck, r.teleports, r.dinos,
            fpsAvg = Mathf.Round(r.fpsAvg), fpsLow = Mathf.Round(r.fpsLow), worstFrameMs = Mathf.Round(r.worstFrameMs),
            issueCount = r.issues.Count,
            issues = r.issues.ToArray().Take(25).Select(i => i.ToString()).ToList(),
            legs = r.legs.ToArray(),
        };

        private IEnumerator Run()
        {
            try
            {
                for (_seedIndex = 0; _seedIndex < _seeds.Count && !_abort; _seedIndex++)
                {
                    var routine = AuditSeed(_seeds[_seedIndex]);
                    while (true)
                    {
                        bool more;
                        try { more = routine.MoveNext(); }
                        catch (System.Exception e)
                        {
                            Debug.LogException(e);
                            _current?.issues.Add(new Issue { kind = "exception", detail = e.Message });
                            break;
                        }
                        if (!more) break;
                        yield return routine.Current;
                    }
                    if (_current != null) { Finish(_current); _reports.Add(_current); WriteReport(); }
                }
            }
            finally
            {
                Time.timeScale = 1f;
                var pc = Player();
                if (pc != null) pc.ScriptedMove = null;
                _phase = _abort ? "aborted" : "done";
                Running = false;
                WriteReport();
                Debug.Log($"[Audit] {_phase}. Report: {Path.Combine(_dir, "report.md")}");
            }
        }

        private PlayerController Player()
        {
            var m = _bootstrap != null ? _bootstrap.Match : null;
            return m != null ? m.PlayerController : null;
        }

        private IEnumerator AuditSeed(int seed)
        {
            _current = new SeedReport { seed = seed };
            var r = _current;
            _phase = $"seed {seed}: generating";
            Time.timeScale = 1f;
            _bootstrap.GenerateAndSpawn(seed);
            yield return null;
            yield return new WaitForSecondsRealtime(2f); // let the terrain, NavMesh and actors settle

            var match = _bootstrap.Match;
            var pc = Player();
            var world = IslandWorld.Current;
            if (match == null || pc == null || world == null)
            {
                r.issues.Add(new Issue { kind = "setup", detail = "No match, player or IslandWorld after generation" });
                yield break;
            }
            // The audit survivor can't die: hits are swallowed so the walk finishes.
            pc.Health.Redirect = _ => true;
            int count = 0; IslandWorld.ForEach(IslandWorld.ActorKind.Dinosaur, _ => count++);
            r.dinos = count;

            // ── Fixed viewpoints ──
            _phase = $"seed {seed}: screenshots";
            string P(string n) => Path.Combine(_dir, $"seed{seed}_{n}.png");
            Vector3 c = world.Origin + new Vector3(world.Extent * 0.5f, 0f, world.Extent * 0.5f);
            Vector3 spawn = pc.transform.position;
            r.shots.Add(AuditCamera.SaveOverview(P("00_top"), spawn));
            float e = world.Extent;
            Vector3[] corners = { new Vector3(-0.1f, 0, -0.1f), new Vector3(1.1f, 0, -0.1f), new Vector3(1.1f, 0, 1.1f), new Vector3(-0.1f, 0, 1.1f) };
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 from = world.Origin + new Vector3(corners[i].x * e, e * 0.28f, corners[i].z * e);
                r.shots.Add(AuditCamera.SaveFrom(from, c, 50f, P($"0{i + 1}_corner")));
            }
            Vector3 back = spawn - pc.transform.forward * 25f + Vector3.up * 14f;
            r.shots.Add(AuditCamera.SaveFrom(back, spawn, 55f, P("05_spawn_out")));
            var eye = pc.GetComponentInChildren<Camera>();
            if (eye != null) r.shots.Add(AuditCamera.SaveFrom(eye, P("06_spawn_eye")));

            // ── Caches: resting on the ground, not hanging off a slope or perched on a tree ──
            int cacheShots = 0;
            var shotKinds = new HashSet<string>();
            foreach (var cache in Object.FindObjectsByType<ProjectFossil.Economy.LootContainer>(FindObjectsSortMode.None))
            {
                var col = cache.GetComponent<Collider>();
                if (col == null) continue;
                var bounds = col.bounds;
                float gap = float.MaxValue;
                for (int i = 0; i < 4; i++)
                {
                    var corner = new Vector3(i < 2 ? bounds.min.x : bounds.max.x, 0f, i % 2 == 0 ? bounds.min.z : bounds.max.z);
                    gap = Mathf.Min(gap, bounds.min.y - world.GroundAt(corner));
                }
                if (gap > 0.25f) Note(r, "cache-floating", cache.transform.position, $"{cache.name}: lowest corner {gap:0.00} m above the ground");
                if (cacheShots < 2 && shotKinds.Add(cache.name)) // one of each kind: supply cache, ruin stash
                {
                    cacheShots++;
                    Vector3 cp = cache.transform.position, side = new Vector3(6f, 0f, 3f);
                    r.shots.Add(AuditCamera.SaveFrom(cp + side + Vector3.up * (world.GroundAt(cp + side) - cp.y + 1.7f), cp, 60f, P($"07_cache{cacheShots}")));
                }
            }

            // ── Walks ──
            var zones = match.ExtractionZones.Where(z => z != null).Select(z => z.transform.position).ToList();
            r.extractions = zones.Count;
            int stuckShots = 0, walked = 0;
            Time.timeScale = _timeScale;
            while (zones.Count > 0 && !_abort && (_walks <= 0 || walked < _walks))
            {
                Vector3 from = pc.transform.position;
                zones.Sort((a, b) => (a - from).sqrMagnitude.CompareTo((b - from).sqrMagnitude));
                Vector3 goal = zones[0];
                zones.RemoveAt(0);
                walked++;
                _phase = $"seed {seed}: walking to extraction {walked}/{r.extractions}";

                // A long path can come back partial only because the search ran out of nodes; walk to its end and
                // plan again from there. It is unreachable only when a new plan gets no closer.
                var path = new NavMeshPath();
                NavMeshHit b = default;
                bool ok = NavMesh.SamplePosition(from, out var a, 6f, NavMesh.AllAreas)
                       && NavMesh.SamplePosition(goal, out b, 12f, NavMesh.AllAreas)
                       && NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path)
                       && path.status != NavMeshPathStatus.PathInvalid && path.corners.Length > 1;
                if (!ok)
                {
                    r.unreachable++;
                    Note(r, "unreachable", goal, $"no NavMesh path at all from ({from.x:0},{from.z:0}) (status {path.status})");
                    r.legs.Add($"extraction {walked}: UNREACHABLE by NavMesh");
                    continue;
                }
                Vector3 navGoal = b.position;
                int partials = path.status == NavMeshPathStatus.PathPartial ? 1 : 0;
                bool dead = false;

                float length = 0f;
                for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                float timeout = length / pc.walkSpeed * 2.5f + 30f;
                float startTime = Time.time;
                var line = path.corners;
                int corner = 1;
                Vector3 lastCheckPos = pc.transform.position;
                float lastCheckTime = Time.time, lastRepath = Time.time, nextActorCheck = 0f;
                int stuckHere = 0;
                bool arrived = false;

                while (!_abort)
                {
                    if (pc == null || !pc.isActiveAndEnabled) { Note(r, "player-lost", goal, "player disabled or destroyed mid-walk"); break; }
                    Vector3 p = pc.transform.position;
                    if (Flat(p - goal) < 7f) { arrived = true; break; }
                    if (Time.time - startTime > timeout) break;

                    while (corner < line.Length - 1 && Flat(p - line[corner]) < 1.5f) corner++;
                    // At the end of a partial plan: plan again from here, and give up if that gets no closer.
                    if (corner >= line.Length - 1 && Flat(p - line[line.Length - 1]) < 2f && Flat(p - navGoal) > 7f)
                    {
                        float before = Flat(line[line.Length - 1] - navGoal);
                        bool more = NavMesh.SamplePosition(p, out var at, 6f, NavMesh.AllAreas) &&
                                    NavMesh.CalculatePath(at.position, navGoal, NavMesh.AllAreas, path) &&
                                    path.corners.Length > 1 && Flat(path.corners[path.corners.Length - 1] - navGoal) < before - 3f;
                        if (!more)
                        {
                            dead = true;
                            r.unreachable++;
                            Note(r, "unreachable", p, $"NavMesh ends {Flat(p - navGoal):0} m short of extraction {walked}; " + StuckDetail(pc, world, p));
                            if (eye != null) r.shots.Add(AuditCamera.SaveFrom(eye, P($"1{walked}_deadend_eye")));
                            break;
                        }
                        partials++;
                        line = path.corners; corner = 1; lastRepath = Time.time;
                    }
                    Vector3 to = line[Mathf.Min(corner, line.Length - 1)] - p; to.y = 0f;
                    if (to.sqrMagnitude > 0.01f) pc.transform.rotation = Quaternion.LookRotation(to);
                    pc.ScriptedMove = Vector2.up;

                    if (Time.time >= nextActorCheck) { CheckActors(r, world); nextActorCheck = Time.time + 0.5f; }
                    r.frameMs.Add(Time.unscaledDeltaTime * 1000f);

                    if (Time.time - lastCheckTime >= StuckWindow)
                    {
                        if (Flat(p - lastCheckPos) < StuckDistance)
                        {
                            r.stuck++; stuckHere++;
                            Note(r, "stuck", p, StuckDetail(pc, world, p));
                            if (stuckShots++ < MaxStuckShotsPerSeed && eye != null)
                            {
                                r.shots.Add(AuditCamera.SaveFrom(eye, P($"stuck{stuckShots}_eye")));
                                r.shots.Add(AuditCamera.SaveFrom(p - pc.transform.forward * 8f + Vector3.up * 6f, p, 60f, P($"stuck{stuckShots}_out")));
                            }
                            // Get going again: put the survivor on the next corner of the path.
                            Vector3 next = line[Mathf.Min(corner + (stuckHere > 1 ? 1 : 0), line.Length - 1)];
                            Teleport(pc, next + Vector3.up * 0.5f);
                            r.teleports++;
                            if (corner < line.Length - 1) corner++;
                        }
                        lastCheckPos = pc.transform.position;
                        lastCheckTime = Time.time;
                    }
                    // Re-plan now and then: the walk drifts off the line on slopes and in water.
                    if (Time.time - lastRepath > 12f)
                    {
                        lastRepath = Time.time;
                        if (NavMesh.SamplePosition(p, out var here, 6f, NavMesh.AllAreas) &&
                            NavMesh.CalculatePath(here.position, navGoal, NavMesh.AllAreas, path) &&
                            path.corners.Length > 1)
                        { line = path.corners; corner = 1; }
                    }
                    yield return null;
                }
                if (pc != null) pc.ScriptedMove = Vector2.zero;
                float took = Time.time - startTime;
                if (arrived) r.reached++;
                else if (!dead) Note(r, "timeout", pc != null ? pc.transform.position : goal, $"did not reach extraction {walked} in {took:0}s (path {length:0} m)");
                r.legs.Add($"extraction {walked}: {(arrived ? "reached" : "NOT reached")} in {took:0}s game time, path {length:0} m, stuck {stuckHere}x" +
                           (partials > 0 ? $", re-planned {partials}x after partial paths" : ""));
                if (eye != null && arrived) r.shots.Add(AuditCamera.SaveFrom(eye, P($"1{walked}_arrive_eye")));
            }
            Time.timeScale = 1f;
            if (pc != null) pc.ScriptedMove = null;

            // A few seconds in real time, standing still, for an honest frame-rate sample.
            _phase = $"seed {seed}: sampling fps";
            r.frameMs.Clear();
            float until = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < until)
            {
                r.frameMs.Add(Time.unscaledDeltaTime * 1000f);
                CheckActors(r, world);
                yield return null;
            }
        }

        private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        private static void Teleport(PlayerController pc, Vector3 to)
        {
            var cc = pc.GetComponent<CharacterController>();
            cc.enabled = false;
            pc.transform.position = to;
            cc.enabled = true;
        }

        private static string StuckDetail(PlayerController pc, IslandWorld world, Vector3 p)
        {
            float c = world.Cell;
            float gx = (world.GroundAt(p + Vector3.right * c) - world.GroundAt(p - Vector3.right * c)) / (2f * c);
            float gz = (world.GroundAt(p + Vector3.forward * c) - world.GroundAt(p - Vector3.forward * c)) / (2f * c);
            float slope = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
            var hits = Physics.OverlapCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.6f, 0.8f, ~0, QueryTriggerInteraction.Ignore)
                              .Where(h => h.transform.root != pc.transform.root)
                              .Select(h => h.name).Distinct().Take(4);
            return $"slope {slope:0}°, feet {p.y - world.GroundAt(p):+0.00;-0.00} m vs ground, water depth {world.WaterDepthAt(p):0.0} m, " +
                   $"grounded {pc.IsGrounded}, swimming {pc.IsSwimming}, touching [{string.Join(", ", hits)}]";
        }

        private static void Note(SeedReport r, string kind, Vector3 at, string detail)
        {
            r.issues.Add(new Issue { kind = kind, at = at, detail = detail });
            Debug.LogWarning($"[Audit] seed {r.seed} {kind} at ({at.x:0},{at.y:0.0},{at.z:0}): {detail}");
        }

        private static void CheckActors(SeedReport r, IslandWorld world)
        {
            IslandWorld.ForEach(IslandWorld.ActorKind.Survivor, t => CheckActor(r, world, t, false));
            IslandWorld.ForEach(IslandWorld.ActorKind.Dinosaur, t => CheckActor(r, world, t, true));
        }

        private static void CheckActor(SeedReport r, IslandWorld world, Transform t, bool dino)
        {
            if (!t.gameObject.activeInHierarchy) return;
            Vector3 p = t.position;
            float ground = world.GroundAt(p);
            // A dinosaur's body is lifted onto the ground where the NavMesh runs under it; judge what is drawn.
            var feedback = dino ? t.GetComponent<DinosaurFeedback>() : null;
            if (feedback != null) p.y += feedback.GroundLift;
            if (p.y < ground - UnderGroundTolerance && r.seen.Add(("under", t)))
                Note(r, dino ? "dino-under-ground" : "survivor-under-ground", p, $"{t.name} is {ground - p.y:0.00} m below IslandWorld ground");
            if (!dino) return;
            var ai = t.GetComponent<DinosaurAI>();
            if (ai != null && ai.CurrentState == DinosaurAI.State.Dead) return;
            float depth = world.WaterDepthAt(p);
            if (depth > MatchBootstrap.DinosaurWadeDepth + 0.3f && p.y < world.WaterSurfaceAt(p) - 0.3f &&
                r.seen.Add(("deep", t)))
                Note(r, "dino-in-deep-water", p, $"{t.name} ({ai?.CurrentState}) stands in {depth:0.0} m of water");
        }

        private static void Finish(SeedReport r)
        {
            if (r.frameMs.Count == 0) return;
            var sorted = r.frameMs.OrderBy(x => x).ToList();
            float avgMs = sorted.Average();
            float p99 = sorted[Mathf.Clamp((int)(sorted.Count * 0.99f), 0, sorted.Count - 1)];
            r.fpsAvg = 1000f / Mathf.Max(0.01f, avgMs);
            r.fpsLow = 1000f / Mathf.Max(0.01f, p99);
            r.worstFrameMs = sorted[sorted.Count - 1];
        }

        private void WriteReport()
        {
            if (_dir == null) return;
            var sb = new StringBuilder();
            sb.AppendLine($"# Island audit {Path.GetFileName(_dir)}");
            sb.AppendLine($"Status: {_phase}. Time scale while walking: {_timeScale}. FPS is the Editor Game view standing still, not a player build.");
            sb.AppendLine();
            sb.AppendLine("| seed | extractions | reached | unreachable | stuck | teleports | dinos | fps avg | fps 1% low | worst frame ms | issues |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in _reports)
                sb.AppendLine($"| {r.seed} | {r.extractions} | {r.reached} | {r.unreachable} | {r.stuck} | {r.teleports} | {r.dinos} | {r.fpsAvg:0} | {r.fpsLow:0} | {r.worstFrameMs:0} | {r.issues.Count} |");
            foreach (var r in _reports)
            {
                sb.AppendLine();
                sb.AppendLine($"## Seed {r.seed}");
                foreach (var l in r.legs) sb.AppendLine($"- {l}");
                if (r.issues.Count > 0)
                {
                    sb.AppendLine();
                    foreach (var i in r.issues) sb.AppendLine($"- **{i.kind}** ({i.at.x:0}, {i.at.y:0.0}, {i.at.z:0}): {i.detail}");
                }
                sb.AppendLine();
                sb.AppendLine("Screenshots: " + string.Join(", ", r.shots.Where(s => s != null).Select(Path.GetFileName)));
            }
            File.WriteAllText(Path.Combine(_dir, "report.md"), sb.ToString());
        }

        // Where the player is and what is near, for play-testing from the terminal.
        public static object Describe(GameObject player, MatchManager match)
        {
            var world = IslandWorld.Current;
            if (player == null || world == null) return null;
            Vector3 p = player.transform.position;
            var pc = player.GetComponent<PlayerController>();
            var near = new List<string>();
            IslandWorld.ForEach(IslandWorld.ActorKind.Dinosaur, t =>
            {
                float d = Vector3.Distance(t.position, p);
                if (d < 60f) near.Add($"{t.name} {d:0}m {t.GetComponent<DinosaurAI>()?.CurrentState}");
            });
            return new
            {
                pos = $"{p.x:0.0},{p.y:0.0},{p.z:0.0}",
                feetAboveGround = p.y - world.GroundAt(p),
                waterDepth = world.WaterDepthAt(p),
                grounded = pc != null && pc.IsGrounded,
                swimming = pc != null && pc.IsSwimming,
                health = match != null && match.PlayerHealth != null ? match.PlayerHealth.Current : -1f,
                phase = match != null && match.State != null ? match.State.Phase.ToString() : null,
                elapsed = match != null && match.State != null ? match.State.Elapsed : 0f,
                fps = 1f / Mathf.Max(1e-4f, Time.unscaledDeltaTime),
                dinosNear = near,
            };
        }
    }
}
