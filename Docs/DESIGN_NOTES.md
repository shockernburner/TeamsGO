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

## 2026-09-29 — Health, Threat Director, economy, match loop (Milestone 1, steps 4–7)

### Health and damage
- `HealthPool` (pure) + `Health` (MonoBehaviour wrapper) in Core. Anything hurtable implements `IDamageable`.
- Player and dinosaurs add a `Health` at runtime if the prefab lacks one, so no prefab edits were needed.
- Dinosaurs gained **Flee** and **Dead** states. They flee below `fleeHealthFraction`, retaliate when hit, and pay `killReward` to the killer.
- Player melee (`PlayerCombat`) uses the best weapon in the inventory through Core's `IWeaponProvider`, so Player doesn't depend on Economy.

### Threat Director
- `ThreatDirector` is pure and takes any `ThreatBuyer` (id, team, wallet, isAI). API: `CanAfford`, `Evaluate`, `Purchase(threatId, target, buyer)`, and `OnThreatTriggered`.
- Rules: unknown / locked (`unlockTime`) / per-buyer cooldown / can't target own team / can't afford. Nothing is spent on a rejection.
- `AIDirectorBrain` is the AI buyer. Income ramps from `startIncomePerSecond` to `endIncomePerSecond` (`escalationExponent` shapes the curve). It respects a grace period and minimum spacing, and sometimes saves up for a bigger unlocked threat. Seeded, so deterministic.
- The director never spawns anything. `ThreatExecutor` (Match) listens and spawns hunters out of sight. They ignore `chaseRange`.
- Starter catalog: Lone Stalker (1 raptor), Raptor Pack (3 raptors, unlocks at 2:30), Ironjaw (apex, unlocks at 10:00).
- Director depends on Economy for `CurrencySystem`, so AI and player-team wallets are the same type.

### Economy
- `CurrencySystem` tracks earned/spent. `Inventory` is slot-based with stacks and slot upgrades up to a max.
- `LootTable.Roll(RNGService)` always gives guaranteed coins, then weighted rolls. Loot is rolled from the match seed, so the same seed gives the same caches.
- `ShopService` checks everything before charging. The shop is available anywhere during the match (Tab) for now.
- Coins come from caches, dino kills, and a survival payout every minute.

### Match loop
- `MatchState` (pure): active → ended with Extracted / Died / Stranded. Extraction opens at `extractionOpensAt`. Hold inside a beacon for `extractionHoldTime`. The hard cap is `matchDuration`.
- `MatchManager` wires it together. `MatchBootstrap` restarts in place (new or same seed), so no scene needs to be in Build Settings.
- All match data lives in `Resources/GameContent.asset` (rules, director settings, threats, shop, loot tables).

### UI
- Placeholder IMGUI `UIManager` auto-creates next to any `MatchBootstrap`. It shows the HUD, announcements, interact prompt, shop and results screen. Replace it with real UI once the loop is fun.

### Input
- Added actions: Attack (LMB / RT), Shop (Tab / Select), UseItem (Q / D-pad up).

## 2026-09-29 — Movement, stamina and stealth pass (from playtest recording)

- **Input model:** `PlayerInput` uses Send Messages, which only reports button *presses*. The old hold-to-sprint and hold-to-crouch never saw the release, so sprint stuck on and drained stamina whenever the player moved. Run, crouch and crawl are now **toggles**: Shift runs, C or Ctrl crouches, Z crawls, and Space jumps or stands up.
- **Stamina** lives in a pure `StaminaModel` (Player). Running drains it. At 0 the player is **exhausted**: no running or jumping, and walking drops to 3 m/s until stamina is back above 30%. Standing still recovers 1.75× faster. A jump costs 10.
- **Stances:** standing, crouching (2.5 m/s) and prone crawl (1.2 m/s). Each resizes the CharacterController and the placeholder body, and lowers the camera.
- **Stealth:** the player exposes Core's `IStealthProfile`. Dinosaurs multiply hearing range by noise (run 1.5, crouch 0.5, crawl 0.25) and sight range by visibility (crouch 0.75, crawl 0.5).
- **Melee** reaches 240° around the player and snaps them to face the target. The recording showed raptors attacking from the sides while every swing missed.
- **POI debug spheres** are editor-only now (`IslandTerrainBuilder.Build(..., showPoiMarkers)`). Loot caches get a thin yellow pole that disappears once emptied.
- **Tuning:** Raptor Pack unlocks at 4:00 (was 2:30) and costs 60.

## 2026-09-29 — Combat readability pass (second playtest recording)

- **Telegraphed bites:** dinosaurs now charge a bite for `attackWindup` seconds (raptor 0.45, Ironjaw 0.8) before it lands. The body glows red and rears up while charging (`DinosaurFeedback`, presentation only). The bite only lands if the target is still within 1.25× attack range, so stepping back dodges it.
- **Hit stagger:** a hit pauses the dinosaur for `hitStagger` seconds (raptor 0.35, Ironjaw 0.15) and cancels a charging bite, so fighting back buys time. Hit dinosaurs flash white.
- **Raptor run speed 10 → 8.5.** Before, raptors outran a running player (9), so running away never worked.
- **Camera** sits over the right shoulder (camera local 0.7, 0.35, -3.2 under CameraTarget) so the body no longer blocks the middle of the screen. A small crosshair marks the centre.
- **HUD** is a compact top-left panel with one-line bars. Key hints show for the first 60 s (and while the shop is open) along the bottom. Missed swings no longer post messages. Taking damage flashes the screen edges red, stronger on the side the hit came from.

## 2026-09-29 — Third playtest tuning (first successful extraction)

- The playtest extracted at 6:35 with 4 kills and 7 threats faced, so the loop works end to end.
- **Stamina** drains 10/s (was 15) and regenerates 10/s (was 8). A full bar is now about 10 s of running instead of 6.5 s. The recording spent a lot of time "out of breath".
- **Camera** moved further back and up (0.9, 0.6, -4.2), because the placeholder capsule still filled the lower-left of the view.
- **Damage flash** is softer (lighter full-screen tint, thinner edges). The old one hid the fight.
- **Species colours:** placeholder dino bodies use `DinosaurSpecies.debugColor` (raptor sandy orange, Ironjaw purple), so they stand out from the terrain and from the red bite telegraph.

## 2026-09-29 — Island look, step 1 (ground, sea, trees, rocks)

- **Why the ground looked like snow:** URP terrain reads smoothness from the diffuse texture's alpha. The old placeholder layer had full alpha, so the ground was a mirror reflecting the sky. Terrain layers now use low alpha and are matte.
- **Ground colours:** `BiomeDefinition` gains `groundColor`, `foliageColor` and `rockColor`. `IslandDecorator.PaintTerrain` builds one terrain layer per biome (a small noisy texture, so it isn't flat plastic) plus a grey cliff layer that takes over on steep slopes. Neighbouring biomes blend over a few metres.
- **Sea:** a large transparent plane at `IslandSettings.seaLevel` (0.045 × maxHeight, just under where land starts). It has no collider.
- **Trees and rocks:** `ScatterPlanner` (pure, deterministic from the island seed) uses a jittered grid (`scatterCellSize` 7 m). Each biome sets `treesPerHectare`, `rocksPerHectare`, a tree scale range and a `TreeShape` (Round, Tall, Dead). Placement skips water, slopes over `maxTreeSlope` (rocks allow double), and anything within `scatterClearance` (14 m) of a point of interest or the player spawn. The total is capped at `maxScatterInstances` (2000).
  - Current densities per hectare (trees / rocks): jungle 40/4, swamp 12/2 dead trees, plains 4/5, beach 1.5/3, volcanic 0.8/14.
- **Placeholder models** are primitives: trunk plus sphere crowns, a tall capsule crown, a dead trunk with a branch, and tilted boxes for rocks. Trunks and rocks keep colliders, so they block the player and become NavMesh obstacles. Crowns have no colliders. Swapping in real prefabs only touches the `Make*` methods in `IslandDecorator`.
- **Haze:** exponential fog is turned on at runtime (play mode only) for depth. It isn't saved to the scene.
- Tests: `ScatterTests` covers determinism, sea, clearance, density, and the cap. `GenerationTests` checks scatter across 20 generated seeds.

## 2026-09-29 — Lighting fix (fourth playtest)

- The Bootstrap scene's "Directional Light" was really a **point light** (type 2, range 10) floating 330 m up, so nothing in the match was lit by a sun. The mirror-like ground used to hide this. Once the ground went matte, everything turned near-black.
- The scene light is now a directional sun at 50° down and -30° yaw, with soft shadows, and it's set as the scene's sun. Ambient is Trilight (sky, horizon and ground colours), because the scene has no baked lighting and skybox ambient came out almost black.
- `IslandDecorator.ApplyAtmosphere` enforces the same at runtime. It finds or creates a directional sun, raises one lying on the horizon, and sets the Trilight ambient, so a scene with a bad light still plays correctly.
- Ground colours are slightly brighter, and plains trees go from 4 to 8 per hectare.
- The bright cyan screen for the first seconds of play is Unity compiling the new terrain shader variants on first use. It stops after the first run.

## 2026-09-29 — Placeholder sound (generated in code)

- **No audio files yet.** `SoundSynth` (Scripts/Audio, pure C# with no UnityEngine) builds every sound from oscillators, noise, filters and envelopes. Each recipe is deterministic per variant and returns mono samples at 22.05 kHz. There's nothing to license and nothing for `ASSET_LICENSES.md`. Real recordings can replace any recipe later.
- **Sounds:** footsteps (normal and soft), raptor screech, big roar (species with maxHealth ≥ 200), bite, swing, hit, player hurt, coin, threat sting, extraction chime, and a 12 s wind-and-birds loop with a crossfaded seam.
- **`GameAudio`** is presentation only and auto-creates next to any `MatchBootstrap`, like the UI. It listens to existing events:
  - footsteps come from player distance travelled, with stride and volume set by stance;
  - `PlayerCombat.Attacked` plays the swing, plus a hit when something is struck;
  - `Health.Damaged` plays the hurt sound, and `CurrencySystem.BalanceChanged` plays the coin;
  - `ThreatDirector.OnThreatTriggered` plays the sting, and extraction opening plays the chime;
  - `DinosaurAI.AttackLanded` plays a bite, and `DinosaurAI.Killed` plays a death cry;
  - a dino switching into Alert/Chase calls out, with a 6 s cooldown per dino. Wandering dinos make quiet idle calls every 12–28 s, so you can hear what's nearby.
- Dino sounds are 3D with linear rolloff (screech up to 110 m, roar up to 220 m), so you can tell where a threat is. Nothing depends on the Audio assembly.
- WAV previews of every sound are in the shared project files under `audio-previews/`.
- Tests: `SoundSynthTests` checks length, no NaN, audible and unclipped levels, click-free endings, determinism, and the loop seam.
