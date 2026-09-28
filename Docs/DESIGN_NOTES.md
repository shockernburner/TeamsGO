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
