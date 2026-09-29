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
