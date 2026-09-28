using System.Collections.Generic;
using UnityEngine;

namespace ProjectFossil.Generation
{
    public static class IslandValidator
    {
        public class ValidationResult
        {
            public bool IsValid = true;
            public readonly List<string> Errors = new();
            public readonly List<string> Warnings = new();
        }

        public static ValidationResult Validate(IslandData data)
        {
            var r = new ValidationResult();
            var s = data.Settings;

            // Must have at least one extraction zone
            int extractions = 0;
            foreach (var poi in data.PointsOfInterest)
                if (poi.Type == POIType.ExtractionZone) extractions++;

            if (extractions == 0)
            {
                r.Errors.Add("No extraction zones placed.");
                r.IsValid = false;
            }

            // Must have spawn zones
            if (data.SpawnZones.Count == 0)
            {
                r.Errors.Add("No spawn zones placed.");
                r.IsValid = false;
            }

            // All POIs must be on land
            for (int i = 0; i < data.PointsOfInterest.Count; i++)
            {
                var poi = data.PointsOfInterest[i];
                int gx = poi.GridPos.x;
                int gy = poi.GridPos.y;
                if (gx < 0 || gx >= data.Resolution || gy < 0 || gy >= data.Resolution ||
                    !data.LandMask[gy, gx])
                {
                    r.Errors.Add($"POI {poi.Type} #{i} is in water at grid {poi.GridPos}.");
                    r.IsValid = false;
                }
            }

            // Warn if extraction zones are close together
            var exList = new List<PointOfInterest>();
            foreach (var poi in data.PointsOfInterest)
                if (poi.Type == POIType.ExtractionZone) exList.Add(poi);

            for (int i = 0; i < exList.Count; i++)
                for (int j = i + 1; j < exList.Count; j++)
                {
                    float dist = Vector3.Distance(exList[i].WorldPos, exList[j].WorldPos);
                    if (s != null && dist < s.minPOISpacing)
                        r.Warnings.Add($"Extraction zones {i}&{j} only {dist:F0}m apart (min {s.minPOISpacing}m).");
                }

            // Must have at least 5% land coverage
            int landCount = 0;
            int total = data.Resolution * data.Resolution;
            for (int y = 0; y < data.Resolution; y++)
                for (int x = 0; x < data.Resolution; x++)
                    if (data.LandMask[y, x]) landCount++;

            float landFraction = (float)landCount / total;
            if (landFraction < 0.05f)
            {
                r.Errors.Add($"Only {landFraction * 100f:F1}% land — island too small or seed degenerate.");
                r.IsValid = false;
            }

            return r;
        }
    }
}
