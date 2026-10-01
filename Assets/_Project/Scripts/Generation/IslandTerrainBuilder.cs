using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Generation
{
    // Converts IslandData into a Unity Terrain scene object.
    // Call from an editor window or from a Match bootstrap MonoBehaviour at runtime.
    public static class IslandTerrainBuilder
    {
        // showPoiMarkers: big coloured debug spheres over POIs. Useful in the editor tool, noise in a match.
        // decorate: biome ground colours, sea, trees, rocks and haze (IslandDecorator).
        public static GameObject Build(IslandData data, Transform parent = null, bool showPoiMarkers = true,
                                       bool decorate = true)
        {
            var s   = data.Settings;
            int res = data.Resolution;

            var td = new TerrainData();
            td.heightmapResolution = res;
            td.size = new Vector3(s.worldSize, s.maxHeight, s.worldSize);
            td.SetHeights(0, 0, data.Heightmap);

            AddDefaultTerrainLayer(td);

            var go      = Terrain.CreateTerrainGameObject(td);
            go.name     = $"Island_{data.Seed}";
            // A build has no editor default terrain material (the ground renders magenta), so ship our own.
            var terrainMat = Resources.Load<Material>(TerrainMaterialPath);
            if (terrainMat != null) go.GetComponent<Terrain>().materialTemplate = terrainMat;
            if (parent != null) go.transform.SetParent(parent, false);

            if (decorate) IslandDecorator.Decorate(go, data);
            if (showPoiMarkers) AddPoiMarkers(data, go.transform);

            return go;
        }

        // Resources/Shaders also holds a material per runtime-only shader (rotor dust) so builds keep them.
        private const string TerrainMaterialPath = "Shaders/IslandTerrain";

        private static void AddPoiMarkers(IslandData data, Transform parent)
        {
            foreach (var poi in data.PointsOfInterest)
            {
                var marker = Placeholder.Primitive(PrimitiveType.Sphere);
                marker.name = $"POI_{poi.Type}_{poi.GridPos.x}_{poi.GridPos.y}";
                marker.transform.position = poi.WorldPos + Vector3.up * 3f;
                marker.transform.localScale = Vector3.one * 6f;
                marker.transform.SetParent(parent, true);
                Object.DestroyImmediate(marker.GetComponent<Collider>());

                var mat = new Material(Placeholder.LitBase);
                mat.color = poi.Type switch
                {
                    POIType.ExtractionZone => new Color(0f, 1f, 0.2f),
                    POIType.LootCache      => new Color(1f, 0.85f, 0f),
                    POIType.Ruins          => new Color(0.6f, 0.6f, 0.6f),
                    _                      => Color.white
                };
                marker.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }

        // Creates a single solid-color terrain layer so the terrain isn't checkerboard.
        // Replace with proper splat textures when art is ready.
        private static void AddDefaultTerrainLayer(TerrainData td)
        {
            const int size = 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px  = new Color32[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(100, 130, 70, 20); // earthy green; low alpha = matte in URP terrain
            tex.SetPixels32(px);
            tex.Apply();

            var layer = new TerrainLayer
            {
                diffuseTexture = tex,
                tileSize       = new Vector2(20f, 20f)
            };

            td.terrainLayers = new[] { layer };
        }

        public static void DestroyExisting(string namePrefix = "Island_")
        {
            var terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            foreach (var t in terrains)
                if (t.gameObject.name.StartsWith(namePrefix))
                    Object.DestroyImmediate(t.gameObject);
        }
    }
}
