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

## Audible apex roar (2026-09-29)

- Playtest: no big roar was ever heard. Two causes. The Ironjaw threat unlocks at 10:00 and matches were ending near 6:00, and the old roar put ~73% of its energy below 250 Hz, which laptop speakers barely reproduce.
- The roar recipe now lives in throat formants (~380 Hz, 1 kHz sweeping down, 1.8 kHz rasp) with a detuned growl; the sub rumble is a headphone bonus. A test keeps at least half the energy above 250 Hz.
- Distant roars: from 1:15, every 60–110 s, a roar plays ~140 m from the player in a random direction, unless a big dinosaur is already being tracked. Foreshadowing only; nothing spawns.
- When a threat whose species is big (maxHealth ≥ 200) is bought, a roar follows the sting from where it will come.
- Ironjaw unlock time is unchanged (economy call, not audio).

## Low-poly models (2026-09-29)

- Chose the free CC0 Quaternius packs over the paid polyperfect dinosaurs: same art style for dinosaurs, player and nature, and swapping later is cheap because looks are data.
- New `ModelDefinition` asset (model, animator, target height, facing). Species, the player (via `GameContent.playerModel`) and biomes point at data, never at code. An empty model keeps the placeholder primitives, so the game still runs before the art is set up.
- `ModelFit` sizes any model by its measured bounds (feet on the ground, target height), so export units don't matter. Dinosaur models are turned so their head bone points forward.
- `DinosaurVisual` / `PlayerVisual` are presentation only. They read the NavMeshAgent / controller and drive Animator parameters: dinosaurs `Speed`, `Attack` (on bite windup), `Dead`; player `Speed`, `Crouch`, `Attack` + `Armed`, `Hit`, `Dead`. Player attacks and flinches play on an upper-body layer so running continues. Prone reuses the crouch animations (the free library has no crawl).
- Scatter gained a cosmetic `Plant` kind (ferns, bushes, grass) with its own cap, drawn from the same roll so trees and rocks are unchanged for a given seed. `Variant` (a cell hash) picks which model of a biome's list to use. Trees get a trunk capsule, rocks a box collider, plants none.
- `Project Fossil ▸ Art ▸ Set Up Model Packs` (Editor) configures importers, makes URP materials (cutout, double-sided foliage), builds the Animator controllers and fills the ModelDefinitions and biome model lists. Needs running once in Unity after pulling; safe to rerun.
- Real-world trademark rule: the source files named after a famous theropod are renamed `Dino_Ironjaw`; clip names are shortened to `Idle/Walk/Run/Attack/Death`.
- Playtest after the models: the twisted trees and common bushes came out autumn red (the pack's `Leaves_TwistedTree_C` texture), and `Plant_7` leaves use the purple part of the leaf atlas. The setup tool now uses the white `Leaves_TwistedTree` mask tinted green, and drops `Plant_7` from the biome lists. Rerun the menu item to apply.
- Player faced the camera while walking: the Quaternius characters face -Z in Unity, so `Model_Survivor.yawOffset` is 180. `PlayerVisual` now turns the model (on a "Visual" pivot) toward the direction of travel, so backing up or strafing shows the character running that way; an attack snaps it back to the aim for half a second.
- Hair and beard: `ModelDefinition.attachments` binds extra skinned meshes on the same skeleton to the model by bone name (`ModelFit.Attach`). The survivor gets `Hair_SimpleParted` and `Hair_Beard` from the base-character pack. Outfits will use the same mechanism.
- Collisions: with a model, the dinosaur's placeholder capsule is replaced by a box around the visible body, and the NavMeshAgent radius matches its width. NavMeshAgents ignore CharacterControllers, so `DinosaurAI.KeepOutOfPeople` pushes a dinosaur back out along the ground whenever its body overlaps a person's capsule. The player's CharacterController already stops at the box.
- Outfit: the survivor wears the Ranger outfit (body, arms, legs, boots; no hood so hair shows). The outfit pack expects only the base character's head, so `ModelDefinition.trimMeshes`/`keepBones` cut the full-body mesh down to triangles skinned to `head`/`neck` at spawn (`ModelFit.TrimToBones`, cached per mesh; the character FBX is imported readable).
- Loot: `GameContent.cacheModel` / `ruinsModel` show pirate-kit chests (closed chest for supply caches, gold chest for ruin stashes) in place of the cube; the cube's collider stays for interaction and its facing comes from the position, not the loot RNG, so loot rolls don't depend on art. Beaches get palm trees; plains and volcanic ground get large bones (and skull piles on volcanic) among the rocks.

## Harder, wilder island (2026-09-29)

Playtest feedback: the map looked like a garden, dinosaurs were scarce and easy, kills paid only at the end, 5 quiet minutes scored like 10 busy ones, weapons weren't visible, and nothing adapted to the player.

- **Adaptive director.** `AdaptiveDifficulty` (Director, pure) turns recent kills, player health and time since last hurt into an `Intensity` (0.6 to 2.5, moves 0.05/s). The AI brain earns `Intensity` times faster and waits `minSecondsBetween / Intensity` between threats; threats send extra hunters at high intensity (`DirectorSettings.ScaledSpawnCount`). Tuning lives in `DirectorSettings`.
- **Survivor Rank.** `SurvivorRank` (Match, pure) keeps a 0 to 100 rating (10 per rank level) that moves after each match by how the score compares with `ExpectedScore(level)`, capped at +10/-6, +2 for extracting. The level sets the director baseline (+10% per level, also a shorter quiet start), dinosaur health (+6%), bite (+4%) and wildlife count (+8%). Stored locally in PlayerPrefs (`ProjectFossil.SurvivorRating`, `ProjectFossil.BestScore`); online profiles can replace this later.
- **Score.** `ScoreModel`: 2 points per second survived, the species' `scoreValue` per kill, 1 per point of damage dealt, 50 per threat faced; x1.5 for extracting, x0.75 for dying. Results screen shows the breakdown, best score and rank change; the HUD shows the live score and rank.
- **Coins per hit.** `CoinTrickle` pays `MatchRules.coinsPerDamage` (0.15) per point of damage, keeping fractions between hits; kill rewards were lowered to match (raptor 15 to 10).
- **Wildlife.** `WildlifeTable` (data) lists species with weights and group sizes; `WildlifeSpawner` places groups in every spawn zone except the drop zone and tops the population up out of sight (70 to 140 m) every 20 s, growing 1.5 animals a minute up to 45. `MatchBootstrap` only uses the old per-zone prefab spawn when there is no table.
- **Temperaments.** `DinosaurSpecies.temperament`: Predator hunts on sight; Territorial watches and charges anyone inside `territoryRadius` (or who hits it) and gives up at 3x that; Skittish flees on sight and bolts when hit, fighting back only when cornered below half health.
- **New species** (Quaternius models already in the repo): Snapper (small raptor, swarms), Crestback (skittish herds), Hornback and Spikeback (territorial tanks), Longneck (territorial giant). Raptors are tougher (85 HP, flee at 15%).
- **Bite slots.** At most two dinosaurs bite one person at once (`DinosaurAI.MaxAttackersPerTarget`); the rest circle just out of reach and step in when a slot frees up.
- **New threats.** Snapper Swarm (from 1:00), Stampede (from 2:00: a Crestback herd runs through your position; `ThreatKind.Stampede`, trample damage on contact), Hornback Rage (from 5:00). Raptor packs now from 2:30, Ironjaw from 8:00. Director income 0.6 to 3.2/s, grace 60 s, 40 s between threats at intensity 1.
- **Visible weapons.** `ItemDefinition.heldLook` (Spear, Club, Axe) flows through `WeaponStats.Look`; `PlayerVisual` builds the weapon from primitives in the right hand. The grip axis comes from the index and pinky knuckles in the rest pose, so it doesn't depend on how the rig's hand bone is oriented. New items: Stone Axe and Bandage.
- **HUD.** Hit messages replaced by health bars over dinosaurs you hit; small coin gains show as "+N" by the coin count; hotbar slots are wider; clicking the game re-locks the cursor if the Editor dropped it.
- **Island.** Relief = rolling base + ridged mountain chains under a slow mask + fine detail, raised to `lowlandPower` so lowlands stay low and peaks steep; the coastline is warped by noise. A volcano cone with a crater sits inland. Rivers start on hillsides, walk downhill with a pull toward the coast, and keep a water surface that only ever falls; the channel is cut below it with sloping banks. Lakes fill flat low hollows. `IslandData.Rivers/Lakes` carry the water surfaces; the decorator draws ribbons and discs with the sea material. Water cells are not land, so POIs, spawns and scatter avoid them; ground near water is wetter (more swamp).
- **Density and look.** Scatter cell 5 m; jungle 110 trees and 180 plants per hectare with trees up to 2.2x scale; plains are open grass with few trees; caps 5000 solid and 12000 plants. Every scattered model has a `LODGroup` that stops drawing it when tiny on screen. Ground gets dirt and moss patches over the biome colours, and the haze is denser, humid and greener.

## Finding the exit, clear view, no duplicate weapons (2026-09-29)

Playtest: the player couldn't find a beacon once extraction opened, big plants filled the camera, the shop sold a second Stone Axe, and after dying on the beach the camera sat under the sea.

- **Beacon marker.** Once extraction opens, the HUD marks the nearest beacon with its distance. When it's off screen or behind you, the marker sits on the screen edge with an arrow pointing the way to turn (`ScreenMarker.Place`, Core, pure).
- **Camera rig.** `CameraRig` (Player, added by `PlayerController` at start) sphere-casts from the head to the shoulder camera and slides in front of terrain, trunks and rocks (not the player, animals or moving bodies), easing back out when clear. Plants have no colliders, so the decorator registers each plant's rough sphere in `ViewBlockers` (Core, a small grid rebuilt per island); plants near the line of sight stop rendering until the view clears.
- **Water.** `ViewBlockers.WaterHeight` is the sea surface. The camera stays above it, and the player can wade to `PlayerController.maxWadeDepth` (1.1 m) but no deeper, instead of walking along the seabed.
- **One of each weapon.** The shop answers `AlreadyOwned` for a weapon you carry and shows "Owned". A duplicate weapon found in a cache is scrapped for its `ItemDefinition.scrapValue` in coins (default 10) instead of taking a slot.

## Rescue flare (2026-09-29)

Playtest: extraction opened with the nearest of the two beacons 313 m away and a raptor pack on top of the player.

- When extraction opens and no beacon is within `MatchRules.flareIfFartherThan` (150 m), a rescue flare adds an open beacon 70 to 110 m from the player (`flareDistance`) on NavMesh ground above the waterline. The spot comes from its own seeded RNG stream (seed x 41 + 11). The HUD marker already points at the nearest beacon, so it picks the flare up.
- `CameraRig` also hides plants within 1.2 m of the camera, which were filling the edges of the frame.

## Stealth and atmosphere (2026-09-29)

Firdous's direction: keep it hard; the game is team stealth survival. The big predator sniffs around, the team hides and runs between shelters, and a helicopter takes them off the island. Full plan: `/mnt/project-files/plans/co-op-vision.md`.

- **Wind.** `IslandWind` (Generation, on the island root) bends plants and leans trees within 45 m of the camera, with slow gusts and a per-island wind direction from the seed. Trees sway through a child "Sway" pivot so their trunk collider never moves. It uses the `ViewBlockers` plant grid, which now also stores each plant's root and rest rotation. A shader-based wind can replace it later; this version needs no new assets.
- **Hiding in plants.** Plants at least 1.1 m wide and 0.7 m tall register as cover. Inside one, `PlayerController.VisibilityMultiplier` is scaled by 0.6 standing and 0.4 crouching or crawling (for example, a crouching player in a bush is seen from 30% of the normal sight range). The HUD says "HIDDEN" or "In cover (crouch to hide)".
- **Scent.** `ScentTrail` (Core, pure) drops a mark every 2 s at the player's feet. Marks fade after 90 s, and none are dropped while wading in the sea, a river or a lake (`ViewBlockers.InWater`). A species with `smellRange > 0` (Ironjaw: 60 m) follows the freshest mark it can smell at 1.3x walk speed and loses the trail where it breaks. Threats with such a species now `Track` instead of `Hunt`: the predator roams toward the target's area and finds them by scent, sight or sound, instead of homing in. The first pickup in 30 s announces "Something has your scent".
- **Helicopter extraction.** Each extraction zone has a rescue helicopter (placeholder primitives) that flies in over 10 s when extraction opens and lands on the pad. Boarding (the 8 s hold) only counts once it has landed. While landed, it makes noise every 6 s: wandering dinosaurs within 70 m come to look (`DinosaurAI.NoiseAt`), and skittish ones bolt.

## Forest, stalker and the last stand (2026-09-29)

From the 8:39 PM playtest: big trees didn't sway, one small bush was enough to hide, the Ironjaw never showed up (it unlocks at 8:00 and matches end around 5:30), the camera ended up inside the helicopter, and the end needed to be tense with a real payoff.

- **Tree sway.** Most imported trees are one mesh on the model's root, so the old "Sway" pivot found nothing to move. `SwayPivot` now moves that mesh onto the pivot too; the root keeps only the trunk collider. Trees lean up to about 4 degrees in gusts, rocking slowly (about 0.4 Hz) while plants flutter faster.
- **Denser forest, in groves.** Scatter cells are 4 m (were 5). Jungle is 200 trees and 260 plants per hectare (was 110 and 180), swamp 70 and 180, plains 22 and 100. The new `BiomeDefinition.groveContrast` packs trees into groves about 70 m across, with clearings between them (up to 2x density inside, 0.15x outside), so there is cover to hide in and open ground to cross. Caps are 9,000 trees and rocks, and 16,000 plants. A typical island now has about 7,000 trees (was 3,500); if frame rate suffers on the Mac, lower jungle `treesPerHectare` first.
- **Hiding takes a forest.** `ViewBlockers.Concealment(pos)` scores trees within 7 m and big plants within 3 m (more if you're inside one) from 0 to 1. At 0.3 or more you're in cover; at 0.7 or more and crouching or crawling you're HIDDEN. A lone bush scores about 0.5: thin cover, not hidden. Visibility scales smoothly with concealment, down to 0.3x when crouching deep in the forest. The HUD shows "Thin cover (find thicker forest)", "Deep forest (crouch to hide)" or "HIDDEN".
- **Scent under cover.** A HIDDEN player leaves no scent marks: the undergrowth smothers them. A tracker reaching the end of the trail stops and casts about for 5 s, swinging its head (and its sight cone) around. That's the moment to hold still, or to slip away while it faces the other way. Holding still also cuts your noise to 0.4x, so a crawling player who stops moving can only be heard from about 2.5 m.
- **The stalker.** At 1:50 the match sends one Ironjaw from 130 m away to stalk the player by scent (`MatchRules.stalkerThreatId`, free, outside the director's budget). A distant roar marks where it starts. The HUD pulses "SOMETHING HAS YOUR SCENT", or "STAY LOW. DON'T MOVE." when you're hidden, and you hear it sniffing within 60 m.
- **Fear feedback.** `DinosaurAI.DangerAt(pos)` rates danger from nearby predators (charging, sniffing, or a big one wandering close). It drives a heartbeat (70 to 150 bpm), a red screen-edge pulse, and the post-processing: colour drains and the vignette tightens and reddens.
- **Helicopter and the last stand.** The chopper is rebuilt: rounded cabin, glass nose, open side door, tapered tail with fin, skids, four-blade rotor, blinking red, green and strobe lights, a sweeping searchlight, rotor dust, and a looping rotor sound. It no longer lands. It hovers 5.5 m over the pad with a rope ladder down, and a box collider keeps the camera out of it. Boarding takes 15 s (was 8). The moment boarding starts, a raptor pack comes in from 45 m, the stalker switches to a straight hunt, and the HUD shows "BOARDING: HOLD THE PAD" with a progress bar. Step off the pad and the count resets.
- **The payoff.** On extraction the player grabs the ladder and the helicopter climbs away with them hanging off it. A fanfare plays, the screen flashes, and "YOU MADE IT OUT!" holds for 5.5 s with the score before the results.
- **Film look.** `CinematicLook` (UI) adds a global URP volume at runtime: ACES tone mapping, a bit more contrast and saturation, bloom on highlights, and a soft vignette. It turns on post-processing for the player camera. The UI assembly now references the URP runtime assemblies (already in the project; no new packages).

## Less red, brighter forest (2026-09-29, 9:31 PM playtest)

The red danger screen was on most of the match and the jungle floor was close to black. Small hunters (snappers, raptors) chasing now count at most 0.55 danger (the big one still reaches 1), and wandering small predators count 0.15. The screen-edge overlay is half as strong, the vignette tops out at 0.34 with a darker red, and colour drains to -15 saturation instead of -35. The film look uses +0.55 exposure and +8 contrast (was +0.3 and +18), and the ambient light is about 20% brighter, so the forest floor under the canopy reads.

## Online co-op, step 1: host and join (2026-09-29)

Firdous approved FishNet and Steamworks.NET. Step 1 adds FishNet 4.7.3 (UPM git package) with its Tugboat UDP transport, so a team can play by IP: two windows on one Mac, or two computers on the same Wi-Fi. Steam lobbies and the Steam relay (Steamworks.NET) are step 2; proximity voice is step 3.

- **Start menu.** `NetSession` (new `ProjectFossil.Net` assembly) adds itself next to the `MatchBootstrap` and holds the match at a menu: Play solo, Host a co-op game, or Join (address, default 127.0.0.1; port 7770; team of up to 4). Solo play is unchanged and never touches the network.
- **Host is the authority.** The host generates the island and runs the director, wildlife, stalker and every dinosaur's brain. A joiner gets only the seed and the match clock (`IslandMessage`), builds the same island itself, and fast-forwards its timer (`MatchState.FastForward`). The island is never sent, as CLAUDE.md requires.
- **Shared dinosaurs.** Online, the host spawns `Resources/Net/NetDinosaur` (a prefab variant of `Prefabs/Dinosaur` with NetworkObject, NetworkTransform and `NetDinosaur`). Spawners call `DinosaurAI.AnnounceCreated` right after configuring an animal, and the host shares it before its first frame. On joiners it's a puppet (`DinosaurAI.IsRemote`, set from `NetRole.IsFollower`): no NavMesh agent and no thinking. The host sends species, health, state, sniffing and bite windups. A joiner's hit goes to the host (`Health.Redirect`) and lands on the real animal as coming from that joiner, so it turns on them. The host passes back that joiner's coins, score and kills.
- **Teammates.** Everyone keeps their own local player, loot, coins and score. The host spawns a `NetAvatar` for each player. It follows its owner's player, is hidden on the owner's screen, and shows everyone else the character model with a name tag and distance ("Survivor 2  34 m", or "is down"). On the host, a teammate's body is tagged Player, so dinosaurs see, hear, chase, bite and trample it (with the owner's stealth numbers), and each bite is passed to the owner's real health. Team weapons skip teammates (`IFriendly`).
- **Director and the finale with a team.** The director takes turns between living teammates (45 s each), and a threat sent at a teammate hunts them directly (the scent trail is the host's). When a joiner starts boarding, their pad gets its own final wave. Threat and stalker announcements are shared.
- **Restarts.** Only the host starts the next island, which pulls the whole team along. A joiner's results screen says "Waiting for the host to start the next island".
- **Setup.** After the package installs, run **Project Fossil > Co-op > Set Up Networking** once. It builds the NetDinosaur and Avatar prefabs and the `NetworkPrefabs` spawn list in `Resources/Net`. It's safe to run again.
- **Known limits in step 1.** The stalker and the scent trail follow the host only. The rescue flare only drops for the host. If the host dies or extracts, the director stops for everyone until the host starts a new island (animals already out keep hunting). Each player's rank and best score are their own.

## Magenta ground in builds (2026-10-01 co-op playtest)

The first co-op test worked: host in the Editor, joiner in a Mac build, both saw each other, fought the same dinosaurs, and the host extracted. But the build's ground was magenta. The terrain used the render pipeline's default terrain material, which only exists in the Editor. `Resources/Shaders/IslandTerrain.mat` (URP Terrain/Lit) is now assigned as the terrain's material, and `Resources/Shaders/RotorDust.mat` keeps the URP particle shader that the helicopter dust looks up at runtime in builds.

## Teamwork, first person and names (2026-10-01, 9:56 AM playtest)

- **Grey shells are gone.** On the host, FishNet turns every renderer of a shared object back on when it spawns, which brought back the placeholder capsules hidden inside dinosaurs and teammates. `Placeholder.RemoveRenderers` now destroys those placeholder renderers instead of disabling them.
- **Down, not dead.** Online, while at least one teammate is still on their feet, a killing blow knocks you down instead (`Health.CanGoDown`, 1 HP). Down, you crawl (forced prone, 0.7 m/s), can't fight, jump, run or heal yourself, and bleed out after 60 s. Any further hit finishes you. A teammate presses E beside you and stays within 2.6 m for 4 s to get you up with 35% health. If nobody on the team is still standing, you die at once (team wipe).
- **Patching up a teammate.** E on a hurt teammate uses one of your own bandages or medkits on them. Other item sharing is left for later.
- **Leaving together.** Each teammate on the pad with you makes boarding 50% faster. When anyone finishes boarding, everyone standing under that helicopter leaves with them (`LiftOffMessage`, `MatchManager.TeamLiftOff`). Each teammate aboard adds +25% to everyone's score (`ScoreModel.TeamFactor`). A player who escapes alone gets no bonus, and the rest keep playing until they extract at another pad, die, or run out of time.
- **Team messages.** Anyone's news (a teammate down, threats, someone dropping in, making it out, not making it, or leaving the game) goes through the host to everyone else.
- **First person.** The camera sits in the head (`FirstPersonView`). Your own body only casts its shadow; you see a pair of arms and the weapon in your hand, which sway with the view, bob with your stride, chop or jab when you attack and jolt when you're hit. In first person a swing only hits what's in front of you (about 140 degrees) and never turns the view for you. Leaves and grass within 45 cm of your eyes hide. V (or d-pad down) switches to the over-the-shoulder camera and back; the choice is saved. When you die the camera pulls out.
- **Names.** The start menu has a name field (saved, random default such as Kestrel or Juniper). The host makes names unique ("Kestrel 2") and shows them over teammates' heads, in team messages and on the top bar.

## Builds draw everything, gloved hands, hit direction (2026-10-01, 11:27 AM playtest)

- **Magenta in builds.** In a player build, `CreatePrimitive` gives Unity's built-in default material, which URP can't draw, so the beacon pillar, the landing pad and the loot poles were magenta. `Placeholder.Primitive` and `Placeholder.Lit` start every code-made object from `Resources/Shaders/PlainLit.mat` (URP Lit). `PlainLitGlow` and `PlainLitClear` ship so the emissive and transparent variants (helicopter lights, water) stay in the build too.
- **Hands.** The first-person arms were long tan tubes. They're now short olive sleeves with leather cuffs and gloved fists, low in the corners of the view. The field of view is 80 degrees (was 70).
- **What bit me?** A red wedge around the middle of the screen points toward whatever just hit you, for 1.4 s. In first person, small hunters at your feet or behind you were invisible behind the red danger edges.
- **Finding a downed teammate.** A pulsing red light column 50 m tall stands over a downed teammate. Their tag stays whole when pinned to the screen edge, with an arrow toward them. Bleed-out is 60 s (was 45).
- **Frame rate.** The Editor and development builds show fps in the bottom-right corner. Running the host Editor and a joining build on one Mac roughly halves each one's frame rate, which looks like dinosaurs lagging.

## Sky, weather, birds and shared rescue flares (2026-10-01, 12:36 PM playtest)

- **Shared rescue flares.** A rescue flare used to land only on the host's island, so a joiner standing with a teammate had no helicopter to board. Any player's flare now goes through the host to everyone (`FlareMessage`, `MatchManager.AddSharedPad`); late joiners get the pads already down. Teammates can board the same helicopter and leave together for the team bonus.
- **Time of day and weather.** The start menu has a Time (Random, Dawn, Day, Dusk, Night) and a Weather (Random, Clear, Cloudy, Rain, Storm, Fog) choice, saved between sessions. Random is rolled from the island seed (`WorldConditions.Resolve`), so it's deterministic, with midday and clear skies most likely. Online, the host's result travels in `IslandMessage` and joiners use it as an override.
- **Weather is systemic.** Dinosaur sight range is multiplied by `WorldConditions.Sight` (night 0.55, dawn and dusk 0.85; fog 0.6, storm 0.75, rain 0.85) and hearing range by `Hearing` (storm 0.6, rain 0.8). Bad weather and darkness help stealth, but you see less too.
- **The sky** (`Environment/SkyDirector`) sets the sun or moon angle and colour, ambient light, procedural skybox, and fog per time of day, then dims and greys it for weather. `SkyEffects` adds drifting particle clouds (more in bad weather), stars on clear nights, and rain around the camera. Storms strike lightning every 7 to 22 s, flashing the sky; thunder follows at the speed of sound. Rain, thunder, night insects and bird calls are synthesized (`SoundSynth`).
- **Head lamp.** At night a lamp on your head is on; L switches it. Dinosaurs don't react to it yet.
- **Flying reptiles.** Flocks of placeholder pterosaurs circle 45 to 85 m up (four flocks on a clear day, fewer in rain and fog, none at night or in storms) and cry when within 260 m. They are scenery only and will get a real animated model with the art pass.

## Bought art packs (2026-10-01)

- Firdous bought NatureManufacture's Forest Environment and Muelsa's dinosaur pack Vol. I. Paid packs stay out of git (the repo is public and the Asset Store licence forbids redistributing them), so they're git-ignored.
- **Project Fossil > Art > Set Up Bought Packs** (`Editor/BoughtPackSetup`) turns them into content on the machine that has them:
  - Switches the dinosaur pack's built-in materials to URP Lit, as its ReadMe advises.
  - Copies each creature prefab without the pack's own scripts, physics and sound, which would fight our AI.
  - Builds our Speed/Attack/Dead Animator controllers, picking idle, walk, run, attack and death by animation name. It keeps the walk in place by pulling root motion out of whichever bone travels.
  - Assigns trees, plants, rocks and ground textures per biome. The dinosaur pack's palms, banyan, banana and mimosa make the jungle and beach tropical.
  - Writes everything to `Art/Bought` (git-ignored), ending in `Resources/BoughtArt.asset`, and a report to `Logs/BoughtArtReport.txt`.
- At runtime `BoughtArt.Current` is read first, and the free Quaternius models are the fallback. A machine without the packs, such as a joiner's, still plays the same island from the same seed; only the looks differ.
- Bought trees are real-sized: 16 m at scatter scale 1 (was 7 m). Models that bring their own LODGroup keep it.
- First run (3:52 PM recording): the dinosaurs showed with their real skins, but the forest was pink because the forest pack ships for the built-in pipeline. Its URP version is a `.unitypackage` inside the pack, and the setup now offers to import it. Two more fixes:
  - The creature prefabs had lost their root rotation, so a raptor stood on its tail. The copy now keeps the pack's root transform under a plain parent.
  - The pack names attacks `IdleAtk1`/`AtkA`, and the flight loop is `Flight`. The big theropod and the long-neck have no run clip, so their walk plays up to 2.2× faster at a run.
- `SkyParticle.shader` sat next to `SkyParticle.mat` in Resources, so `Resources.Load<Material>("Shaders/SkyParticle")` tried to read the shader as a material ("isMapping" assertion), which broke clouds, stars and rain. The shader moved to `Art/Shaders`.

## Matte ground, smaller leaves, real clouds (2026-10-01, 4:59 PM playtest)

- The forest looked right after the URP import. Four things still stood out:
  - The "isMapping" console error was still there at match start. The game's Resources materials were written as text outside Unity, and one of them reads badly in 6.5. `Editor/MaterialRepair` has Unity load and resave them once per project copy, which rewrites them in its own format. It is also on the menu as **Project Fossil > Maintenance > Resave Game Materials**.
  - The forest floor glittered white and blue in a visible grid. The forest pack's ground layers carry mask maps packed for its own terrain shader, and URP read them as wet metal. The setup now makes matte copies of the ground layers (colour and bumps, no mask map, smoothness 0) with tiles of at least 6 m.
  - Crawling through the jungle filled the screen with leaves metres wide. Low, spreading plants (banana, big ferns) were scaled up to plant height, so they got very wide. Plants are now capped at 1.8 m across at scale 1. The camera's plant lookup also reaches the widest plant registered, so a big plant whose centre is a few metres away still hides when it is in your face.
  - Clouds were round balls. They are now wide, flat-bottomed cumulus quads, 150–320 m across and 55–95 m tall, kept level.

## Grounded plants, storm skies, visible dinosaurs (2026-10-01, 5:26 and 5:30 PM playtests)

- Ferns hung in the air and tree roots stood above the ground. The bought plants and trees are modelled with the pivot at ground level and roots or stems running a little below it. The decorator lifted every model so its lowest point touched the ground, which raised them by that much. Trees and plants whose pivot sits near their base now keep the pivot on the ground. On slopes they also sink to the lowest ground under their base: fully for trees, and 60% for plants, whose leaves can bend. Rocks are unchanged.
- During rain and storms the sky stayed blue with white clouds and a sun. The procedural skybox's blue comes from its scattering, so greying its tint did nothing. In rain, storms and fog the camera now clears to a flat overcast colour matched to the fog, and lightning brightens it. Rain clouds are darker.
- Raindrops passing by the lens drew as thick white bars. Their on-screen width is now capped.
- Dinosaurs sometimes disappeared. Two culling causes: skinned-mesh bounds that don't follow a lunge, and detail-level groups dropping the model too early. The bounds are padded, and the last detail level now stays until the body is a speck.
- Attacks, for reference: a swing checks a sphere around the body (weapon range + 0.4 m: 2 m for bare hands, 3 m for the spear). It takes the damageable thing that is most straight ahead, with being in front counting more than being a few centimetres closer. Third person accepts up to 120° either side and turns you to face the hit. First person only accepts about 72° either side of where you look.

## Toward ad-quality footage (2026-10-01, 5:51 and 5:55 PM playtests)

- Bushes still tipped above the ground. Our wind turned each plant and tree as a rigid whole, on top of the forest pack's own shader wind. Bought foliage now sways only in its shader, driven by the pack's wind zone. Our rigid sway is kept for the free models, which have no shader wind.
- MSAA is off in the pipeline asset, so leaves shimmered. The match camera now uses SMAA High.
- Rain was a wall of white sticks. Drops are thinner and fainter, and there are fewer of them.
- The danger wash and damage bars painted the screen red, which ruins footage. Danger is now darkness at the edges. A hit flushes the edges dark red, stronger on the side it came from.
- The helicopter is matte olive with blurred rotor discs, and its blades only ghost through. The rotor loop is a deep 9 Hz "whop" with breathing wash instead of high, fast slaps.
- The roadmap to jaw-dropping is in `/mnt/project-files/plans/jaw-dropping-plan.md`: recorded sound, a realistic player and helicopter, sky and fog, smarter packs, co-op voice, then trailer capture. Shadow distance (50 → 120 m) and Unity Recorder wait on Firdous's OK.
- Approved by Firdous (2026-10-01): the PC pipeline's shadow distance went from 50 m to 120 m, so the far forest keeps its depth. Unity Recorder (`com.unity.recorder`) was added for 4K ad capture, under Window > General > Recorder. 5.1.1 failed to compile on Unity 6.5 (`GetInstanceID` is now an error), so it is pinned to 5.1.7, which Firdous installed and which compiles.

## Real survivor, real helicopter, recorded sound (2026-10-01, evening)

- Firdous added two free Asset Store packs, Survivalist Character and OH-1 helicopter, plus 18 Mixkit sound effects. All stay out of git like the paid packs.
- "Set Up Bought Packs" now also does the following:
  - It turns each Survivalist outfit prefab into a player look, imported as a humanoid with its URP materials. It is animated by the same survivor controller as before, built from the free animation library.
  - Solo and the host get outfit 1. Each friend who joins gets the next outfit, so a team reads at a glance.
  - It turns the helicopter prefab into a clean copy without the pack's demo animation. Our flight code finds the rotors by name and spins them, and adds a faint blur disc over the main rotor.
  - The searchlight, rope ladder and collider are placed from the model's size. If the model flies sideways, `helicopterYaw` on BoughtArt turns it, and `helicopterLength` scales it.
- Recorded sounds load from any `Resources/SoundLibrary` folder by name (`Roar_1`, `Growl_2`, `RainLoop` and so on). Anything missing falls back to the synthesized sound.
- How the recordings are used:
  - Real roars for the big animals.
  - Screeches for raptors.
  - Growls for the idle chatter.
  - The big predator's breathing, close in, alternates with its sniffing.
  - Separate loops for rain and storm.
  - A wind layer in storms.
  - Three thunder strikes.
  - A recorded helicopter loop.
- The clips were trimmed, normalised and made into seamless loops with ffmpeg. They are mono for positional one-shots and stereo for the weather beds.

## Big predator reach, getting unstuck, a filmic ending (2026-10-01, 8:58 PM playtest)

- The 8:58 PM playtest still showed the free survivor, the primitive helicopter and synthesized sound. The likely cause is that Set Up Bought Packs hadn't run since the new packs arrived, because the Recorder compile error blocked the Editor. The setup now runs by itself once per Editor session whenever an imported pack isn't in use yet.
- To make this checkable from a screenshot, the Console says what's in use at match start: which player look, how many bought survivors, which helicopter, and whether recorded sounds were found.
- Ironjaw walked until its middle was within bite range, which put its whole body over the player. The player could then hit its belly from underneath while it stood still. Fixes:
  - Bite reach is now measured from the body's surface, at half the species range, so a big animal bites from its snout.
  - Circling keeps the body length out of the way.
  - The agent no longer stops early at 90% of the range.
- A chase that makes no headway for 1.2 s now detours to another side of its target, 50–130° around, before trying straight again. "No headway" means barely moving while out of reach, or a path that ends short (the player up on a rock or behind a wall of trees).
- How the match ends:
  - When it's over, the edges close in and the picture fades to black. Then the results and menu come up on the black.
  - On extraction this follows the "YOU MADE IT OUT!" banner.
  - On death it takes 2.5 s.
  - The rotor sound fades away behind it.

## Bought-pack setup never stops early

- The forest pack's "import the URP version" question used to end the setup, every run, because a few forest
  materials stay non-URP even after that import. So the survivor and helicopter were never set up. Now the question
  is asked once per project and the setup always carries on and always writes `Logs/BoughtArtReport.txt`.
- Recorded sounds must sit in `Assets/SoundLibrary/Resources/SoundLibrary`. The setup (and the automatic run on
  Editor start) now moves our clips (Roar_1, RainLoop, ...) there from anywhere under `Assets/SoundLibrary` and
  reports how many are ready.
- Every everyday sound can now come from a recording: Step, SoftStep, Bite, Swing, Hit, Hurt, DayAmbience and
  NightAmbience join the recorded sets, each falling back to its synthesized version. The synthesized day ambience
  (wind plus whistled bird chirps) is the main "cartoon" sound in playtests, so a real forest recording replaces it first.

## Facing, a sadder death, the pilot's call

- Humanoid models are turned by their own shoulders (forward = right x up) instead of a fixed yaw, because the
  Survivalist prefabs face the other way from the Quaternius ones; the survivor walked backwards.
- Death plays `Lament` (slowing heartbeats under a low A-minor string swell), or a recorded `Death` clip.
- When the helicopter arrives: radio static, then a recorded `Radio_*` pilot line if present, and the subtitle
  "PILOT: Your escape helicopter has arrived. Go to the extraction zone." The bell chime is gone.
- The coin ding on every hit made fights sound like an arcade: now soft and at most every 2 s.
- Flyer calls use the recorded screeches pitched up when the sound library is present.

## 2026-10-01 — Movie_003 fixes: facing, swimming, posture, crawl, first-person arms

- **Survivor facing:** the bind-pose shoulder check didn't match what humanoid retargeting shows, so it is gone. PlayerVisual now measures the animated shoulders a few frames after spawning and turns the model by the nearest quarter turn if it faces more than 30° away from the way it walks. It logs "[PlayerVisual] … turned it …" when it does.
- **Swimming:** rivers and lakes used to let the player sink to the bed. `ViewBlockers.SurfaceAt` gives the highest water surface over a spot. Deeper than `swimDepth` (1.3 m), the player floats with the head above the surface, moves at `swimSpeed`, and can't jump or sprint. The animator gets a Swim blend tree (Quaternius Swim_Idle / Swim_Fwd).
- **Posture:** the Quaternius jog and sprint clips keep the fists up. With the bought survivalist pack, its own StarterAssets idle, walk and run clips drive the "Survivor_Survivalist" animator instead, with relaxed arms.
- **Crawl removed:** there is no crawl animation, so Z does nothing and the hint no longer mentions it.
- **First-person arms:** these borrow the survivor model's skin and shirt materials when their names say which is which. The bought-pack report now lists the FPS_Survivalist prefab so the real arms can be wired next.

## 2026-10-02 — Movie_004 fixes: survivalist first-person arms, weapon in view, plants that part

- **Weapon missing in first person:** MatchBootstrap adds PlayerCombat after PlayerController has already created FirstPersonView. The view never found it, so it never showed the held weapon, never swung, and never told combat it was in first person. The view now looks again until PlayerCombat turns up.
- **First-person arms:** the survivor pack's FPS_Survivalist prefab is stored as `BoughtArt.firstPersonArms`. `FirstPersonRig` keeps only its FPS arm and sleeve meshes and curls the fingers into a fist with HumanPoseHandler. Each frame it bends each arm with two-bone IK onto the wrist targets that FirstPersonView already animates (rest pose, bob, sway, swing). The weapon rides the right wrist target, so it stays in the hand. Without the pack, the simple arms remain.
- **Giant poles on rocky slopes:** the volcanic biome used dry boughs as its "trees", so they were scaled to tree height and stood up as huge diagonal logs. They now count as rocks (scaled by width), and old beeches are the trees.
- **Red hit flash:** the side flush was a stretched vignette and left a hard vertical edge. It is now a smooth sideways fade.
- **Plants part around you:** `PlantPusher` goes on players and dinosaurs. IslandWind leans bushes and ferns away from any pusher inside them, and they spring back once it has passed. Bought foliage now also leans with gusts (half strength) on top of its shader ripple.

## 2026-10-02 — Movie_005 fixes: relaxed arms, URP first-person skin, saplings

- **Fists up in third person:** the upper-body "Actions" layer sat at full weight with an empty state between attacks, and with Write Defaults off it kept holding the arms in the last action's pose. The layer now starts at weight 0. PlayerVisual raises it while a punch, swing or flinch plays and fades it out afterwards, so idle, walk and run show the pack's natural arms. Needs Set Up Bought Packs (or Set Up Model Packs) to rebuild the controller.
- **Pink first-person hands:** the pack's FPS prefab uses materials the URP can't draw. Setup now saves a clean copy (Art/Bought/Survivors/FirstPersonArms.prefab) with the pack's "Materials URP" versions, as for the bodies. FirstPersonRig also swaps any undrawable material for a plain lit one as a safety net.
- **First-person hands:** relaxed and low when walking, swinging with the stride, pumping as loose fists when sprinting, and up in a guard for 1.6 s after an attack. With a weapon, the right hand carries it low on the right, tilted away from the view. The fingers blend between an open hand and a fist. Which sign of the humanoid finger muscles closes the hand is found at runtime by trying both.
- **Saplings:** young beeches are small trees. Shrunk to plant height, they looked like the crown of a buried tree swaying on the ground, so they're now in the tree lists.

## 2026-10-02 — Screenshot fixes: grounded foliage, extraction stakes, bought axe

- **Floating leaves:** PR #34 turned bought (shader-wind) plants as a whole for pushing and gusts. Their leaves then rotated about the root and lifted into the air. Bought foliage is no longer turned, as before, and only keeps its shader ripple. The Quaternius plants still part and lean, with a gentler push bend (14° instead of 38°).
- **Leaves over lakes:** plants are no longer placed where the water surface is above the ground. Lakes register their full mesh radius (+4 m), so their edges count too.
- **Red disc and pole:** the closed extraction pad was a flat disc that cut into slopes, plus a 40 m red pillar. It is now a ring of short stakes with coloured flags, each set on the terrain, and a slimmer beacon standing on the ground beside it.
- **Axe pack:** Sky Den Games' Mid poly Axes Collection (`Assets/Skyden_Games/Axe Package`) is detected by Set Up Bought Packs. The first axe becomes `BoughtArt.axe`, and the held axe uses it (longest axis up, gripped near the bottom). Without the pack, the primitive axe remains.
- **Rain pack:** AIK Studio's Rain System VFX (`Assets/Rainy VFX`) is detected and listed in the setup report. It isn't wired yet; the report shows which prefabs and shaders it uses.

## 2026-10-02 — Movie_007 fixes: sapling poles, swimming backwards, lakes

- **"Floating leaves" at 0:30, 0:47, 1:05:** these were young beeches, which PR #35 moved to the tree lists. Every tree is scaled to the same height, so a 3 m sapling was stretched about four times. Its thin twigs became long bare poles across the view, with a few leaves hanging off the ends. Bought trees now keep roughly their real size (never enlarged more than 1.25×).
- **Swimming backwards:** the swim clips come from the Quaternius library while walking uses the pack's own clips, and the two face the body opposite ways. The facing check that runs at spawn now runs again whenever swimming starts or stops, once the blend has settled.
- **Water vanished with the camera under it:** the third-person camera was only kept above the sea, not above lakes and rivers. It now stays above whatever water is under it. The inland water surface is also drawn from both sides.
- **Unseen dinosaurs biting from under the water:** dinosaurs walked the lake bottom. Deep lakes are now cut out of their NavMesh, so they wait at the shore and a lake is a place to hide. Rivers stay crossable so the island isn't split in two.

## 2026-10-02 — Screenshot fixes: real rivers, bought axes, crouch facing

- **Rivers as blue sheets on hillsides:** most of a river's course ran down slopes of 30–40 %, and the ribbon of water lay on the slope like a tarp. Where it crossed a slope, its downhill edge hung in the air. Now rivers only have water where they run gently (`maxRiverGradient`, 15 %). Steeper stretches stay dry ground, and the river starts again below them. Beside the water the ground is built up into a bank a little above the surface, so water never hangs over lower ground. Water is deeper (2.2 m), and the ribbon reaches out to where the bank rises above it. Gentle stretches are rarer, so the default island tries 5 rivers and 4 lakes.
- **Forest pack water:** setup picks the forest pack's river and swamp water materials (`BoughtArt.riverWater`, `lakeWater`). Rivers carry UVs along their flow, and lakes get planar UVs. Without the pack, the plain see-through water remains.
- **Weapons from the axe pack:** the shortest axe in the pack is the Hatchet (was the Bone Club, 25 coins), and the longest is the Felling Axe (was the Stone Axe, 45). The simple fallback for the hatchet is now a small axe too.
- **Crouch flipped the face:** like swimming, crouching uses clips from another library. The body-facing check now runs again on every base-layer state change, once its blend has settled (not after death).

## 2026-10-02 — Minimap

- A 200 px map in the bottom-right corner (`UI/MiniMap`), north up. It is drawn once per island from the generator's data: the sea in deep blue, rivers and lakes lighter, open ground in sand that turns to rock on steep slopes, and forest in greens from light to dark by how many trees stand within about 12 m. Hills are shaded from the north-west.
- On top of it: the player as an arrow pointing where the camera looks, unsearched supply caches as pulsing yellow dots, and, once extraction opens, every extraction beacon blinking green.
- Dinosaurs are not on the map. Finding them is the player's job.

## 2026-10-02 — Graphical glitch pass (Movie_008)

- **Snowy, wet-looking ground and "rivers" on hillsides:** URP terrain takes a layer's smoothness from its colour texture's alpha whenever the texture has one, and ignores the layer's own smoothness setting. The forest pack stores gloss there, so the ground mirrored the sky. From above it looked like wet snow; seen low, it looked like sheets of white water. You could "walk into the river" and, once the view angle changed (first person), the river was gone. Setup now gives each bought ground layer its own colour texture with no alpha (`Art/Bought/Ground/*_Colour.jpg`), so the terrain is matte. Re-run *Set Up Bought Packs* once.
- **Leaves and mushroom caps floating and spinning:** plants' far detail levels in the bought packs are flat cards that turn to face the camera (cross, billboard and impostor shaders). Plants and rocks now drop those levels and keep their real mesh until they cull. A plant made only of such cards is not placed. Trees keep theirs, since a whole forest of full meshes at range would cost too much.
- **Camera skimming the ground:** pulled in on a slope, the third-person camera could sit right on the ground. The view then filled with ground, and the player seemed to sink into it. The camera now stays at least 0.6 m above the terrain.
- **Survivor turned backwards while running:** the body-facing check now turns the model only when six settled frames in a row agree. A single odd reading in a stride (167° in the log) no longer spins the runner.

## 2026-10-02 — Wow pass: fine terrain, grass, light and mist

- **Terrain detail, 4 m to 1 m:** the generator's rules still run on its 257 grid, where rivers, lakes, POIs and spawns are worked out and tested. The terrain you see and walk on is built from it by `TerrainDetail.Refine`: four times finer (`IslandSettings.terrainDetail`), smoothed about one coarse cell wide so hills curve instead of breaking into 4 m facets, with small bumps on open land (`terrainBumps`, 0.35 m). Smoothing never lowers ground more than 0.2 m, so river and lake banks stay above their water. It is deterministic for a seed, so co-op still syncs by seed alone. Tests cover size, determinism and banks. The ground textures' cliff blend now follows the fine slopes, at about 2 m a texel.
- **Grass:** short grass drawn by the terrain, one layer per biome with its own tint (`BiomeDefinition.grassColor`) and cover (`grassCover`: plains 0.85, swamp 0.6, jungle 0.45, beach 0.05, volcanic 0). It grows in patches, thins under trees, and is absent on cliffs, sand, ash and under water. The blades are fixed crossed cards that sway in the wind, never camera-facing ones. The blade texture is drawn in code, so no asset is needed. It is drawn out to 70 m.
- **Sun shafts:** in forest, slanted shafts of sunlight stand in gaps beside trees near the camera, leaning towards the sun. They are strongest at dawn and dusk, faint at midday, weak under cloud, and absent in rain, fog and at night. A new shader (`ProjectFossil/SoftVolume`) softens them where they meet the ground and fades crossed cards seen edge-on. The camera now renders a depth texture for this.
- **Ground mist:** mist lies on water and in hollows, and at dawn, in fog and rain it lies everywhere. It drifts with the wind.
- **Colour per time of day:** split toning gives warm light and cool shade by day, rose and violet at dawn, amber and blue at dusk, and moonlit blue at night. Rain and fog grey it.

## 2026-10-02 — Water that never floats (5.46 PM recording)

- **Flat water sheets hanging in the forest:** a drawn surface reaches past its channel so its edge tucks under the bank, but several things left that edge over lower ground: river ends cut off square over the hollow their channel ended in, lakes with no bank on their downhill side, one water's hollow cutting into another's bank, and rivers that climbed out of a hollow with their surface still at the hollow's level (a dry trench with water floating in it). Now:
  - Rivers end where the ground over their course stands more than 3 m above the surface, and have rounded ends.
  - Lakes get banks like rivers and keep clear of rivers.
  - Once all water is in place, a low rim (0.4 m) is raised just past every surface's edge, easing down outside it, without filling any channel.
  - Any surface whose edge still hangs is lowered until it tucks under the ground or steps down at most 0.3 m onto other water. A stretch that would need more than 1.5 m stays dry. This repeats until nothing changes, and the water map is rebuilt from what is left.
  - The fine terrain never smooths ground near water below the generator's ground, so the edges stay tucked under on the drawn terrain too.
  - `WaterShape` holds the shape of the drawn surfaces, shared by the generator and the tests. A new test checks every river edge and lake rim against the fine terrain across eight game-sized islands, and that most rivers survive.
- **White, foamy rivers:** rivers now use the forest pack's calm swamp water, like lakes.
- **Brown blobs in fog:** in fog weather there are no clouds; the sky is a grey wash there anyway.
- **Red "IsMapping" errors at match start:** the mist and sun shafts now load their shader by name, and only fall back to the Resources material.

## 2026-10-02 — Rivers that drain, grey overcast, bought rain (6.41 PM recording)

- **The water sheets were rivers lost in a hollow.** Rivers walked downhill over the bare ground, so a river that reached a hollow wandered round inside it. Three rivers on one island coiled within about 30 m, a knot of water ribbons at different heights with dry ground and tilted steps between. Rivers now follow how water really drains: over the ground with every hollow filled to its brim (priority flood), so each one crosses hollows and runs on to the sea. A river ends where it meets another. Where lowering a surface would leave a steep step between two points, the river breaks there.
- **Steep courses:** traced honestly, almost every course on these islands runs at 25–50 %, far steeper than the 15 % a flat ribbon of water can show without looking like a tilted sheet. The island now tries 6 lakes instead of 4.
- **Overcast looked like a clear day:** under cloud the sky is now a grey lid instead of blue, with more cloud, a dimmer sun and softer shadows.
- **Bought rain:** *Set Up Bought Packs* now picks the rain pack's falling-rain effects by name (steady rain, and heavy rain for storms), skipping splashes, ripples, drops on glass and anything whose shaders don't draw under URP. It runs again by itself once for this. Rain and storms play that effect around the camera; without it the simple streaks remain.
- **Mountain streams (Firdous chose these next):** on slopes a stream is a run of level pools, each cut about 2 m into the slope below the lip of the one above, with a fall between. Where the ground is gentle the water falls smoothly with it, as before. Streams narrow on the slopes; there is no water on cliffs steeper than 70 %. A fall is drawn as a short steep curtain at the end of the upper pool, with white spray at its foot and a looping rush (synthesized, or `WaterfallLoop` from the sound library) on the three nearest falls. Across eight test islands there are now about 900 points of running water with about 300 falls, plus lakes.

## 2026-10-02 — One world: IslandWorld (8.03 PM recording)

The 8.03 PM clip still showed water sheets on ridgetops, terraced pools with vertical walls, big white blocks
(the fall spray), dinosaurs biting from under lakes, and distant leaves spinning. Root cause: no single source of
truth. About ten places each worked out ground and water their own way (coarse map, drawn terrain, raycasts,
circles round each lake), so water was drawn over one ground, walked on over another and swum in over a third.

- `ProjectFossil.Core.IslandWorld` is now the one answer. It holds the fine ground grid (exactly what the terrain
  draws), the water surface per vertex, the sea level, fast-stream spots for sound, and a registry of survivors and
  dinosaurs. Terrain, drawn water, grass, scatter, NavMesh, swimming, wading, scent, camera, mist, light shafts,
  birds, extraction and the minimap all read it.
- `WaterField.Build` makes it from the generator's plan: lakes (wobbly shores) and streams laid on the fine grid,
  surfaces limited to a 0.2 grade (no falls, curtains or tilted sheets), beds shaped by distance to shore (streams
  at most 0.7 m deep, lakes 2.4 m), gentle banks and levees, every dry vertex beside water at least 0.15 m above
  it, deep cuts and puddles left out. Tested on 8 game-sized seeds.
- The water is one chunked mesh over the wet vertices plus one ring past them, so every edge tucks under a bank.
  River ribbons, lake discs and the fall spray are gone.
- Dinosaurs: the NavMesh excludes water deeper than 0.9 m (strips built from IslandWorld), so they wait on the
  shore; streams stay crossable.
- Trees lose camera-facing far LODs (cross, billboard, impostor) like plants did; they keep their last real mesh
  and fade out in the haze.
- Terrain pixel error 3 so far shores don't sink under their water.

## 2026-10-02 — Spinning cards, findable lakes, photographed skies, Windows co-op (8.52, 8.59, 9.04 PM recordings)

- The "leaves rotating round their centre with no branch or root" were bought plants' far LODs: a few crossed
  quads drawn with the same leaf shader as the near mesh, so the shader-name test missed them. Any renderer of 20
  triangles or fewer now counts as a card; a far level with any card in it is dropped (shared renderers stay on),
  and a model made only of cards is left out, trees included.
- Lakes were too small and too rare to find (16–30 m, and the picker refused hollows). Lakes are now 25–50 m and
  sit in hollows or on the flat; tests require swimming water on at least 7 of 8 islands.
- Photographed skies: `Project Fossil > Art > Download Skies` (also runs once by itself) picks the most
  downloaded sky-only CC0 HDRI from Poly Haven for clear, cloudy, overcast, dawn, dusk and night, finds its sun
  and horizon colour, and fills `SkyLibrary`. The match uses the photo for the skybox, aims the sun at the
  photo's sun, takes the haze colour from its horizon and lights the island from it. The drawn clouds and stars
  are off under a photo. Without the download the drawn sky stays.
- The Console now says at match start which sky and which rain are in use.
- `Project Fossil > Co-op > Build for Windows` builds a Windows copy on the Mac for a friend's laptop.

## 2026-10-02 — No more walls on mountains (10.57 PM recording)

- The survivor got stuck halfway up a mountain with dinosaurs waiting above and below. Cause: the water pass cut
  stream banks down to a gentle slope for 14 m and left the hillside beyond untouched, so a sheer step (up to 80
  degrees) stood where they met. The ridge noise made a few more. Neither feet nor the NavMesh climb over 45
  degrees, so the fold between two such faces was a trap.
- Ground is now never steeper than about 45 degrees (`WaterField.MaxGroundGrade`): every step is eased back from
  the lowest ground up, keeping each bed and the levee the water needs. A test checks it across seeds.
- Ground steeper than the controller's slope limit now slides the survivor down instead of holding them.
- A dinosaur that has had no path to its target for 10 s gives up, wanders off and leaves that survivor alone for
  15 s, instead of camping at the foot of the slope.

## 2026-10-03 — Island audit: a tester that walks every seed

- `Project Fossil > Audit > Run Island Audit` (in Play mode), or from the terminal `unity cmd fossil_audit
  --seeds 1,2,3`, then `unity cmd fossil_audit_status`. For each seed it generates the island, walks the survivor
  from the spawn to every extraction along the NavMesh (scripted input through `PlayerController.ScriptedMove`),
  notes where it gets stuck, checks every actor against IslandWorld ground and every dinosaur against deep water,
  times the frames and saves pictures (top-down, four corners, behind the spawn, the player's eye, every arrival
  and every stuck spot). Report and pictures go to `Logs/FossilAudit/<time>/` (not in git).
  `unity cmd fossil_snapshot` saves the player's view and returns position, ground, water, health and nearby
  dinosaurs, for play-testing from the terminal.
- A long NavMesh path can come back partial only because the search ran out of nodes; the audit walks to its
  end and plans again, and calls a spot unreachable only when the new plan gets no closer.
- First run found dinosaurs grazing on the sea floor up to 7 m under the waves: only lakes and streams were
  fenced off from the NavMesh. The sea now counts, and a block is fenced off when any of its vertices is deeper
  than a dinosaur can wade (the centre alone let them wade chest-deep along every shore).
- Dinosaurs on sharp ridges and rounded hills sank up to 3 m into the ground: the NavMesh the agent walks on is
  simplified and runs under the real ground. The NavMesh now builds its height mesh, and the body is lifted onto
  IslandWorld ground wherever the agent is still below it (`DinosaurFeedback.GroundLift`).
- The survivor stopped against nothing in open forest. Bought models bring their own colliders (a banyan's is a
  4.5 m wide, 15 m tall capsule round a much thinner trunk; plants meant to be walk-through came with capsules).
  The decorator now strips them and adds only its own: a trunk capsule for trees, a box for rocks.
- Rocks lower than 0.75 m get no collider: the NavMesh climbs anything that low, so paths ran straight over
  knee-high stones and stumps the survivor (who steps 0.3 m) walked into.
- Bought trees whose LOD heights are above 1 made `SetLODs` refuse the whole group, a thousand Console errors per
  island, and those trees never got their far cut-off. The heights are clamped first.
- Caches stand on IslandWorld ground (a ray from above could land them on a tree or rock) and sink into a slope
  until the downhill corners touch, instead of hanging half in the air. The audit checks it.
- After these, seeds 1-16 and a random seed: every extraction reached, no stuck spots, no dinosaur in deep water
  or under the ground, no floating cache, no Console errors.
- The new climbable-ground test failed on seed 3: an 82-degree step was left where a levee holding a lake up
  (which may not be lowered) met hillside that had been cut down. Easing only lowered ground, so nothing could
  fix it. A second pass now raises dry ground below such a step until it is climbable. `unity cmd
  fossil_slope_check --seeds 0,1,2` reports the steepest step per seed with the game's own settings: before, seed
  3 had 80 walls and seed 4 had 8; after, seeds 0-11 top out at 44 degrees. All 123 EditMode tests pass.

## 2026-10-03 — Solo difficulty and the Esc menu

- "Play solo" now asks Easy, Medium or Hard (`Match/Challenge`). Hard is the game as tuned, and co-op always
  plays Hard. Easy and Medium scale the director's intensity, its quiet start (+90 s / +40 s), dinosaur damage and
  health, wildlife count and when Ironjaw comes out, on top of the Survivor Rank.
- Esc opens a menu at any point in a match: Resume, or Leave match (confirmed) back to the start menu. Solo, the
  island pauses while it's open; co-op can't pause, so it just blocks your input. The results screen also has
  "Main menu". Leaving takes the island down and drops the connection; the match doesn't count.
- Tested in Play mode: each difficulty starts (Easy shows fewer animals), Esc pauses (match clock, audio and input
  stop) and Esc resumes, Leave goes back to the start menu with the island, player and match gone, and a new solo
  game starts after it.
- On the start menu and after leaving there is no player camera, so Unity logged "no audio listeners" every
  frame. `MatchBootstrap` now keeps a stand-in listener that is on only while there is no player.

## 2026-10-03 — Real caches: a chest, and ruins

- Caches were yellow placeholder boxes: their model definitions pointed at the Pirate Kit chests only after a
  manual menu step, and those files' `.meta` were never committed, so the link could not survive in git.
- Supply caches are now a closed chest and ruin stashes a small ruin, both from Quaternius' CC0 Ultimate Modular
  Ruins Pack (the whole pack came from Poly Pizza as one GLB; Google Drive was over its download quota).
  `Tools/glb2obj.py` converts the pieces used to OBJ, which Unity imports without a package. Files, `.meta`,
  the `RuinStash` prefab and both model definitions are committed, so they work on every machine at once.
- A ruin stash: the chest in front of a broken overgrown arch, a short column, a fallen wall and broken pots.
  Stone pieces have box colliders. `GroundedParts` settles each piece onto the slope where the stash lands (on a
  ridge the arch half sinks in, like an old ruin). `Project Fossil > Art > Build Cache Models` rebuilds the prefab.
- A tree stump's box takes in its spreading roots, and on a slope the NavMesh climbed onto it from uphill: the
  survivor stuck on one. Rocks and stumps under 1 m now have no collider (was 0.75 m).

## 2026-10-03 — Console clean-up after merging #45

- Four red errors on Editor start ("Tried to get mapping information from scalar node", "Assertion failed on
  expression: 'IsMapping()'") came from `SoftVolume.mat` and `SkyParticle.mat`: each, written by hand, had one
  blank line inside its property block, which Unity 6.5's YAML reader rejects (Search indexed them on start). The
  blank lines are gone; no other committed asset has one.
- `Project Fossil > Audit > Run Island Audit` no longer needs Play mode first: it opens the Bootstrap scene if
  needed, enters Play mode, leaves the start menu for a solo match and then starts the audit.
- The audit side-steps a few metres each way when the survivor is blocked, as a player would round a stump; only
  a spot it can't walk out of counts as stuck.
- `BoughtPackSetup` ran on every Editor start ("New rain pack"): the rain pack has no prefab URP can draw (its
  effects use legacy particle shaders), so the rain slot stayed empty and looked new each time. That rerun
  reimported the bought dinosaurs and printed 38 "Transform 'Tric' has the same name as transform" warnings.
  `BoughtArt.rainPackChecked` remembers the folder, so it runs once. The warnings themselves come from node names
  inside the paid FBX files; silencing them would mean editing those files, so they stay, but now only appear when
  the packs really are set up again.
- The start menu's stand-in AudioListener exists from the first frame and steps aside the moment the player
  spawns: no more "no audio listeners" or "2 audio listeners" lines.

## 2026-10-03 — The rain pack, converted to URP

- The Rain System VFX pack draws with a legacy particle shader (pink under URP) and names every effect "Particle
  System", so the setup never used it. It now picks emitters by what they do: a wide box emitting steadily is the
  falling rain; an emitter that collides with the world and spawns a sub-emitter gives drops that splash on the
  ground (in the pack it is a lightning strike, so its jagged trails and noise are switched off).
- Copies of those, retuned, become `Art/Bought/Rain/Rain_Steady` and `Rain_Storm` (git-ignored, like the pack):
  URP Particles/Unlit, transparent, with a generated drop streak (the pack's soft dot all but vanished when
  stretched), drops over a 40 m square above the camera falling in world space, splashes within 14 m. The game
  centres them on the camera and leans the drops with the wind. Storm is denser and darker than steady rain.
- The project's base particle material saves no properties, so new materials made from it were opaque and drew
  rain as dark specks. The pack rain and the simple fallback streaks are both set to transparent now.
- Checked in Play mode in Rain and Storm: no pink, follows the camera, reads as slanted soft rain with splashes.
