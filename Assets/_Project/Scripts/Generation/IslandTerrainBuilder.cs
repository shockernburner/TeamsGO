using UnityEngine;

namespace ProjectFossil.Generation
{
    // Converts IslandData into a Unity Terrain scene object.
    // Call from an editor window or from a Match bootstrap MonoBehaviour at runtime.
    public static class IslandTerrainBuilder
    {
        public static GameObject Build(IslandData data, Transform parent = null)
        {
            var s   = data.Settings;
            int res = data.Resolution;

            var td = new TerrainData();
            td.heightmapResolution = res;
            td.size = new Vector3(s.worldSize, s.maxHeight, s.worldSize);
            td.SetHeights(0, 0, data.Heightmap);

            var go      = Terrain.CreateTerrainGameObject(td);
            go.name     = $"Island_{data.Seed}";
            if (parent != null) go.transform.SetParent(parent, false);

            // Spawn debug markers for POIs
            foreach (var poi in data.PointsOfInterest)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = $"POI_{poi.Type}_{poi.GridPos.x}_{poi.GridPos.y}";
                marker.transform.position = poi.WorldPos + Vector3.up * 3f;
                marker.transform.localScale = Vector3.one * 6f;
                marker.transform.SetParent(go.transform, true);
                Object.DestroyImmediate(marker.GetComponent<Collider>());

                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = poi.Type switch
                {
                    POIType.ExtractionZone => new Color(0f, 1f, 0.2f),
                    POIType.LootCache      => new Color(1f, 0.85f, 0f),
                    POIType.Ruins          => new Color(0.6f, 0.6f, 0.6f),
                    _                      => Color.white
                };
                marker.GetComponent<Renderer>().sharedMaterial = mat;
            }

            return go;
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
