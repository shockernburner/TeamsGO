using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using ProjectFossil.Core;

namespace ProjectFossil.EditorTools
{
    // Fetches a photographed sky for each time of day and weather from Poly Haven (every asset there is CC0), and
    // builds the SkyLibrary the game's sky reads. The skies are downloaded on this machine and stay out of git
    // (they're large); anyone can fetch the same ones with this menu. Runs once by itself when the library is
    // missing.
    public static class SkyDownload
    {
        private const string Out      = "Assets/_Project/Art/Skies";
        private const string Library  = Out + "/Resources/" + SkyLibrary.ResourceName + ".asset";
        private const string Api      = "https://api.polyhaven.com";
        private const string Resolution = "2k";

        // What each slot needs from Poly Haven's categories, and how bright its horizon should look in the game.
        private static readonly (string key, string[] all, string[] any, string[] none, string[] prefer, float horizon)[] Slots =
        {
            ("Clear",    new[] { "clear" },           new[] { "midday", "morning-afternoon" }, new[] { "night", "sunrise-sunset", "overcast" }, new[] { "clear" },   0.85f),
            ("Cloudy",   new[] { "partly cloudy" },   new[] { "midday", "morning-afternoon" }, new[] { "night", "sunrise-sunset", "overcast" }, new[] { "cloud" },   0.8f),
            ("Overcast", new[] { "overcast" },        new string[0],                           new[] { "night" },                               new[] { "overcast" }, 0.62f),
            ("Dawn",     new[] { "sunrise-sunset" },  new string[0],                           new[] { "night" },                               new[] { "sunrise", "dawn", "morning" }, 0.6f),
            ("Dusk",     new[] { "sunrise-sunset" },  new string[0],                           new[] { "night" },                               new[] { "sunset", "dusk", "evening" }, 0.55f),
            ("Night",    new[] { "night" },           new string[0],                           new string[0],                                   new[] { "moon", "stars", "clear" }, 0.06f),
        };

        [MenuItem("Project Fossil/Art/Download Skies (Poly Haven, CC0)")]
        public static void Run()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Download Skies", "Asking Poly Haven for its skies...", 0f);
                var list = Json.Parse(Get($"{Api}/assets?t=hdris")) as Dictionary<string, object>;
                if (list == null) throw new Exception("Poly Haven's asset list didn't come back.");

                Directory.CreateDirectory(Out + "/Resources");
                var lib = AssetDatabase.LoadAssetAtPath<SkyLibrary>(Library);
                if (lib == null)
                {
                    lib = ScriptableObject.CreateInstance<SkyLibrary>();
                    AssetDatabase.CreateAsset(lib, Library);
                }
                var skies = new List<SkyLibrary.Sky>();
                var used = new HashSet<string>();
                for (int i = 0; i < Slots.Length; i++)
                {
                    var slot = Slots[i];
                    string id = Pick(list, slot.all, slot.any, slot.none, slot.prefer, used);
                    if (id == null) { Debug.LogWarning($"[Skies] No Poly Haven sky fits '{slot.key}'."); continue; }
                    used.Add(id);
                    EditorUtility.DisplayProgressBar("Download Skies", $"{slot.key}: {id}", (i + 0.5f) / Slots.Length);
                    var sky = Fetch(slot.key, id, slot.horizon);
                    if (sky != null) skies.Add(sky);
                }
                lib.skies = skies.ToArray();
                EditorUtility.SetDirty(lib);
                AssetDatabase.SaveAssets();
                SkyLibrary.Reload();
                Debug.Log($"[Skies] {skies.Count} photographed skies ready: {string.Join(", ", skies.Select(s => $"{s.key} = {s.source}"))}. " +
                          "All CC0 from polyhaven.com. Press Play to see them.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Skies] Couldn't download the skies ({e.Message}). The game keeps its drawn sky; try the menu again later.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // The most downloaded sky-only HDRI with the slot's categories (sky-only "puresky" ones first: no ground or
        // buildings in the picture).
        private static string Pick(Dictionary<string, object> list, string[] all, string[] any, string[] none, string[] prefer,
                                   HashSet<string> used)
        {
            var scored = new List<(string id, int rank, double downloads)>();
            foreach (var kv in list)
            {
                if (used.Contains(kv.Key) || !(kv.Value is Dictionary<string, object> a)) continue;
                var cats = Strings(a, "categories");
                var tags = Strings(a, "tags");
                if (!cats.Contains("skies") && !kv.Key.Contains("puresky")) continue;
                if (!all.All(cats.Contains)) continue;
                if (any.Length > 0 && !any.Any(cats.Contains)) continue;
                if (none.Any(cats.Contains)) continue;
                int rank = (kv.Key.Contains("puresky") ? 0 : 2)
                         + (prefer.Any(p => kv.Key.Contains(p) || tags.Any(t => t.Contains(p))) ? 0 : 1);
                double downloads = a.TryGetValue("download_count", out var d) && d is double dd ? dd : 0;
                scored.Add((kv.Key, rank, downloads));
            }
            return scored.OrderBy(x => x.rank).ThenByDescending(x => x.downloads).ThenBy(x => x.id)
                         .Select(x => x.id).FirstOrDefault();
        }

        private static HashSet<string> Strings(Dictionary<string, object> a, string key)
        {
            var set = new HashSet<string>();
            if (a.TryGetValue(key, out var v) && v is List<object> l)
                foreach (var o in l) if (o is string s) set.Add(s.ToLowerInvariant());
            return set;
        }

        private static SkyLibrary.Sky Fetch(string key, string id, float horizonTarget)
        {
            var files = Json.Parse(Get($"{Api}/files/{id}")) as Dictionary<string, object>;
            string url = Dig(files, "hdri", Resolution, "hdr", "url") as string ?? Dig(files, "hdri", "1k", "hdr", "url") as string;
            if (url == null) { Debug.LogWarning($"[Skies] {id} has no HDR file."); return null; }

            string path = $"{Out}/{key}_{id}.hdr";
            if (!File.Exists(path)) File.WriteAllBytes(path, GetBytes(url));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            // Read it uncompressed once to find the sun and the horizon, then store it compressed.
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureShape       = TextureImporterShape.Texture2D;
            imp.mipmapEnabled      = false; // mips smear a seam where the picture's edges meet
            imp.wrapModeU          = TextureWrapMode.Repeat;
            imp.wrapModeV          = TextureWrapMode.Clamp;
            imp.maxTextureSize     = 4096;
            imp.isReadable         = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Analyse(tex, out bool hasSun, out Vector3 sunDir, out Color horizon);
            imp.isReadable         = false;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

            float lum = horizon.r * 0.2126f + horizon.g * 0.7152f + horizon.b * 0.0722f;
            float exposure = Mathf.Clamp(horizonTarget / Mathf.Max(1e-4f, lum), 0.05f, 8f);

            string matPath = $"{Out}/Sky_{key}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            var shader = Shader.Find("Skybox/Panoramic");
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = shader;
            mat.SetTexture("_MainTex", tex);
            mat.SetFloat("_Exposure", exposure);
            mat.SetFloat("_Rotation", 0f);
            mat.SetFloat("_Mapping", 1f);   // latitude-longitude
            mat.SetFloat("_ImageType", 0f); // 360 degrees
            mat.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
            mat.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
            EditorUtility.SetDirty(mat);

            var haze = horizon * exposure;
            haze = new Color(Mathf.Clamp01(haze.r), Mathf.Clamp01(haze.g), Mathf.Clamp01(haze.b), 1f).gamma;
            return new SkyLibrary.Sky { key = key, material = mat, hasSun = hasSun, sunDirection = sunDir, horizon = haze, source = id };
        }

        // The sun is the brightest spot above the horizon, if it stands far above the rest of the sky. The horizon
        // colour is the average of a band just above it.
        private static void Analyse(Texture2D tex, out bool hasSun, out Vector3 sunDir, out Color horizon)
        {
            int w = tex.width, h = tex.height;
            var px = tex.GetPixels();
            float best = 0f; int bx = 0, by = 0;
            var lums = new List<float>();
            Color sum = Color.black; int count = 0;
            for (int y = h / 2; y < h; y++)
                for (int x = 0; x < w; x += 2)
                {
                    var c = px[y * w + x];
                    float l = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
                    if ((x & 15) == 0) lums.Add(l);
                    if (l > best) { best = l; bx = x; by = y; }
                    if (y < h / 2 + h / 18) { sum += c; count++; }
                }
            lums.Sort();
            float median = lums.Count > 0 ? lums[lums.Count / 2] : 0f;
            hasSun = best > Mathf.Max(1e-3f, median) * 25f;
            horizon = count > 0 ? sum / count : Color.grey;

            // Unity's panoramic sky: u = 0.5 - atan2(z, x) / 2pi, v = 1 - acos(y) / pi (v = 0 at the bottom row).
            float u = (bx + 0.5f) / w, v = (by + 0.5f) / h;
            float lat = (1f - v) * Mathf.PI, lon = (0.5f - u) * 2f * Mathf.PI;
            float yy = Mathf.Cos(lat), r = Mathf.Sin(lat);
            sunDir = new Vector3(Mathf.Cos(lon) * r, yy, Mathf.Sin(lon) * r).normalized;
        }

        private static object Dig(object o, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (!(o is Dictionary<string, object> d) || !d.TryGetValue(k, out o)) return null;
            }
            return o;
        }

        private static string Get(string url) => Encoding.UTF8.GetString(GetBytes(url));

        private static byte[] GetBytes(string url)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("User-Agent", "ProjectFossil-SkyDownload");
                var op = req.SendWebRequest();
                while (!op.isDone) System.Threading.Thread.Sleep(20);
                if (req.result != UnityWebRequest.Result.Success) throw new Exception($"{url}: {req.error}");
                return req.downloadHandler.data;
            }
        }

        // ── A minimal JSON reader (objects, arrays, strings, numbers, true/false/null) ──
        private static class Json
        {
            public static object Parse(string s) { int i = 0; return Value(s, ref i); }

            private static void Skip(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            private static object Value(string s, ref int i)
            {
                Skip(s, ref i);
                if (i >= s.Length) return null;
                char c = s[i];
                if (c == '{')
                {
                    var d = new Dictionary<string, object>(); i++;
                    while (true)
                    {
                        Skip(s, ref i);
                        if (s[i] == '}') { i++; return d; }
                        string k = Str(s, ref i); Skip(s, ref i); i++; // ':'
                        d[k] = Value(s, ref i); Skip(s, ref i);
                        if (s[i] == ',') i++;
                    }
                }
                if (c == '[')
                {
                    var l = new List<object>(); i++;
                    while (true)
                    {
                        Skip(s, ref i);
                        if (s[i] == ']') { i++; return l; }
                        l.Add(Value(s, ref i)); Skip(s, ref i);
                        if (s[i] == ',') i++;
                    }
                }
                if (c == '"') return Str(s, ref i);
                int start = i;
                while (i < s.Length && ",}] \t\r\n".IndexOf(s[i]) < 0) i++;
                string word = s.Substring(start, i - start);
                if (word == "true") return true;
                if (word == "false") return false;
                if (word == "null") return null;
                return double.TryParse(word, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0.0;
            }

            private static string Str(string s, ref int i)
            {
                var sb = new StringBuilder(); i++; // opening quote
                while (s[i] != '"')
                {
                    if (s[i] == '\\')
                    {
                        i++;
                        char e = s[i];
                        if (e == 'u') { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                        else sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'r' ? '\r' : e == 'b' ? '\b' : e == 'f' ? '\f' : e);
                    }
                    else sb.Append(s[i]);
                    i++;
                }
                i++;
                return sb.ToString();
            }
        }
    }

    // Fetches the skies once when this machine has none yet.
    [InitializeOnLoad]
    internal static class SkyAutoDownload
    {
        private const string Done = "ProjectFossil.SkyAutoDownload";

        static SkyAutoDownload()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(Done, false)) return;
                SessionState.SetBool(Done, true);
                var lib = AssetDatabase.LoadAssetAtPath<SkyLibrary>("Assets/_Project/Art/Skies/Resources/" + SkyLibrary.ResourceName + ".asset");
                if (lib != null && lib.skies != null && lib.skies.Length > 0) return;
                Debug.Log("[Skies] No photographed skies on this machine yet: downloading them from Poly Haven (CC0).");
                SkyDownload.Run();
            };
        }
    }
}
