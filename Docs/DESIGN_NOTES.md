# Design Notes

Running log of decisions made during development.

---

## 2026-09-28 — Milestone 1 skeleton

- **Project root:** `Assets/_Project/` (leading underscore keeps it at the top of the Project window).
- **Unity version:** 6000.5.5f1 (Unity 6.5 LTS).
- **Render pipeline:** URP (`com.unity.render-pipelines.universal 17.5.0`).
- **Input:** New Input System (`com.unity.inputsystem 1.19.0`). Legacy input disabled.
- **Pathfinding:** AI Navigation (`com.unity.ai.navigation 2.0.14`) wired into `ProjectFossil.Dinosaurs`.
- **Pipeline automation:** `com.unity.pipeline 0.8.0-exp.1` already in manifest; enables `unity command` / `unity pipeline` CLI integration.

### Assembly dependency graph

```
Core  ←  Generation
Core  ←  Dinosaurs (+ Unity.AI.Navigation)
Core  ←  Director
Core  ←  Economy
Core  ←  Player (+ Unity.InputSystem)
Core + Director + Economy  ←  Match
Core + Economy + Match  ←  UI
Core + Generation  ←  Editor (editor-only)
```

### RNG rule
All procedural generation uses `RNGService` (wraps `System.Random` with an explicit seed).
**Never use `UnityEngine.Random` in generation code.** This keeps outputs deterministic and testable.

### Threat Director API contract
The director exposes three surface points regardless of whether the buyer is AI or a player team:
- `CanAfford(threatId)`
- `Purchase(threatId, target, buyer)`
- `OnThreatTriggered` event

---

## 2026-09-28 — Island Generator (Milestone 1, step 2)

### Algorithm
- **Heightmap:** 5-octave fractal Perlin noise (persistence=0.5, lacunarity=2) with RNG-seeded offsets. Island forced by a smooth radial falloff (SmoothStep) beyond `islandRadiusFraction`.
- **Biome map:** separate moisture noise pass; classify by (height, moisture) thresholds in `BiomeDefinition`. Order: Beach → Swamp → Jungle → Plains → Volcanic (ordered most-specific first; fallback = nearest biome by height midpoint).
- **POI placement:** rejection sampling with minimum grid-spacing constraint. Extraction zones placed first (higher priority, wider spacing), then caches and ruins.
- **Spawn zones:** rejection-sampled with 3× radius exclusion between zones.

### Biome thresholds
| Biome    | Height       | Moisture    |
|----------|--------------|-------------|
| Beach    | 0.05 – 0.18  | any         |
| Swamp    | 0.05 – 0.25  | 0.65 – 1.0  |
| Jungle   | 0.10 – 0.62  | 0.45 – 1.0  |
| Plains   | 0.15 – 0.45  | 0.00 – 0.52 |
| Volcanic | 0.55 – 1.00  | any         |

### Validation
`IslandValidator` checks on every generation: ≥1 extraction zone, ≥1 spawn zone, all POIs on land, ≥5% land coverage. 30/30 seeds pass in tests.

### Editor tooling
- **Project Fossil → Island Generator (F5):** generates + builds terrain live in the active scene.
- POIs visualized as colored spheres (green=extraction, yellow=loot, grey=ruins).

### Runtime surface for Match integration
`IslandTerrainBuilder.Build(IslandData, parent)` — call from `MatchManager` with the match seed. Never send heightmap over the network; send seed only.

---
