# CLAUDE.md — Project Context

This file gives Claude Code the full context for this project. Read it at the start of every session.

## Who I am

- Solo developer. My tools: Claude Code, Claude (chat), Cursor, Unity.
- I can do some 3D work in Blender/Maya/Unity but have **very little time for art**.
- Strong preference for **automatic/procedural generation**: scenes, levels, islands, spawns, and encounters should come from code and data, not hand placement.
- Goal: a game that can **keep earning for years** through updates, not a one-off launch.

## The game (working title: "Project Fossil" — placeholder, final name TBD)

A **co-op dinosaur survival game** for PC (Steam first, other platforms later).

- Teams of 1–4 players drop onto a **procedurally generated prehistoric island** (new layout every match, generated from a seed).
- They scavenge, craft short-term defenses, survive escalating dinosaur threats, and reach an **extraction point** in ~20–30 minutes.
- **In-match currency** (earned by looting, objectives, and surviving) buys weapons, tools, bigger inventory, and **threats**.
- **Signature hook — the Threat Director:** threats (stampedes, raptor packs, an apex predator, storms, etc.) can be *purchased and triggered*.
  - Phase 1: an AI director buys and triggers threats against the players.
  - Phase 2: rival player teams buy and trigger threats against each other (team vs team vs dinos).
  - The same system must serve both. Design it that way from day one.
- **Voice chat throughout the match**, ideally proximity-based (later milestone).

### Design pillars

1. **Systemic over handcrafted.** Rules and generators create the content.
2. **Every match is different.** Seeds, weighted pools, and constrained randomness, not pure noise.
3. **Tension and clip-worthy moments.** Hearing a roar you know a rival paid for is the core feeling.
4. **Fair.** No real-money pay-to-win. Gameplay purchases use **in-match currency only**.

### Hard rules (legal and business)

- **Never** use "Jurassic Park", "Jurassic World", "Hunger Games", or any other trademarked names, characters, logos, or look-alike designs in code, assets, UI text, file names, or store copy. Invent original names.
- Real-money monetization is limited to: the game price, cosmetics (skins, emotes, armor looks), and map/content DLC.
- Only use assets with licenses that allow commercial use (e.g. Asset Store packs, CC0). Record the source and license of every third-party asset in `Docs/ASSET_LICENSES.md`.

## Roadmap

### Milestone 1 — Core offline prototype (current focus)

Build single-player first, but structure code so networking can be added cleanly (keep game state separate from presentation).

1. **Project skeleton:** folder structure, assembly definitions, URP setup, input system.
2. **Procedural Island Generator:** seed → heightmap terrain → biomes (jungle, plains, swamp, volcanic) → rivers/lakes → vegetation and rock scattering → points of interest (loot caches, ruins, extraction zones) → dinosaur spawn zones. Must be deterministic for a given seed. Include an Editor window or menu item to regenerate with a new seed.
3. **Player controller:** third-person or first-person (TBD), sprint, crouch, stamina, interact.
4. **Dinosaur AI (basic):** wander, detect by sight and sound, chase, attack, flee. Species defined as data.
5. **Threat Director (AI-controlled):** currency budget over time, a threat catalog as data, escalation curve, triggering. Build it as a service that could later accept purchase requests from players.
6. **Loot, currency, inventory:** pickups, in-match shop, inventory slot upgrades.
7. **Match loop:** drop in → survive → extract or die → results screen.

### Milestone 2 — Online co-op
- Steam integration, lobbies with friends, host/client via Steam relay (no dedicated servers needed).
- Server-authoritative game state. Sync the island by **seed only**, never by sending terrain data.
- Voice chat (proximity).

### Milestone 3 — Steam release (Early Access)
- Build and upload pipeline to Steam (SteamPipe/steamcmd), store page assets, demo build for Steam Next Fest.

### Milestone 4+ — Longevity
- Team-vs-team mode using the same Threat Director with players purchasing threats.
- New biome presets, dinosaur species, seasonal events, cosmetics, daily/weekly seeded challenges.

## Tech stack

- **Engine:** Unity 6 LTS (confirm the installed version with the Unity CLI before creating or upgrading anything). Render pipeline: URP.
- **Language:** C#.
- **Networking (Milestone 2, proposed, confirm with me before adding):** FishNet + Steam transport (Steamworks.NET), or Netcode for GameObjects + a Steam transport.
- **Voice (proposed):** Dissonance or Steam voice API.
- **Art approach:** stylized/low-poly; purchased animated dinosaur packs; modular environment kits; shaders and particles for polish. Use placeholder primitives until the gameplay is fun.

## Architecture rules

- **Data-driven everything.** Dinosaurs, weapons, items, threats, biomes, and loot tables are `ScriptableObject` definitions (or JSON). New content = new data, no new code.
- **Determinism.** All procedural generation takes an explicit seed and uses its own `System.Random` (or a custom RNG) instance. Never use `UnityEngine.Random` in generation code.
- **Constrained generation.** Use rules and guarantees (minimum distance between extraction points, guaranteed loot tiers, reachable spawns) and validate outputs. Add automated validation tests for generator output across many seeds.
- **Separate simulation from presentation.** Game logic should not depend on MonoBehaviour visuals, which keeps networking and testing possible.
- **The Threat Director is a standalone service** with a clear API: `CanAfford`, `Purchase(threatId, target, buyer)`, and events. The buyer may be the AI or a player team.
- Prefer composition and small components. No god classes.
- Write EditMode tests for pure logic (generation, economy, director) and PlayMode tests where needed.

## Suggested folder structure

```
Assets/
  _Project/
    Scripts/
      Core/            (bootstrap, services, events, RNG)
      Generation/      (island, biomes, scattering, POIs)
      Dinosaurs/       (AI, species data)
      Director/        (threat director, threat definitions)
      Economy/         (currency, shop, inventory)
      Player/
      Match/           (match flow, extraction, results)
      UI/
      Editor/          (editor windows, generators, tools)
    Data/              (ScriptableObject assets)
    Prefabs/
    Scenes/
    Art/               (placeholder and imported art)
    Tests/
      EditMode/
      PlayMode/
Docs/
  ASSET_LICENSES.md
  DESIGN_NOTES.md
```

Namespaces follow folders: `ProjectFossil.Generation`, `ProjectFossil.Director`, etc. Each Scripts subfolder gets an assembly definition.

## Tooling: Unity CLI and working in the Editor

- Unity's standalone **Unity CLI** (`unity` command) is installed. It manages Editors, modules, projects, builds, and tests from the terminal. With the experimental **`com.unity.pipeline`** package, it can drive a running Editor, including executing C# inside it.
- The CLI is in beta and its commands change between releases. **Do not guess commands.** Run `unity --help` and `unity <command> --help` to discover the exact syntax for the installed version, and prefer JSON output for parsing.
- Useful capabilities to use once verified: listing installed Editors, opening the project, running EditMode/PlayMode tests, building, and running C# in the Editor to execute generator tools.
- Custom project commands can be exposed to the CLI with the `[CliCommand]` attribute. Consider exposing commands like "regenerate island with seed X" and "run generator validation".
- A Unity MCP server (Unity's official one or CoplayDev's MCP for Unity) is an optional alternative if the CLI can't do something.

## Workflow rules for Claude Code

- **Ask before:** upgrading the Unity version, installing or removing packages, changing project settings globally, changing source-control setup, or deleting assets.
- After editing scripts, trigger a refresh/compile and **check the console for errors** before moving on.
- Work in small, testable steps. After each step, summarize what changed and how I can see it in the Editor.
- Keep `Docs/DESIGN_NOTES.md` updated with decisions made along the way.
- Commit to git at each working step with clear messages (ask me to set up git if the project isn't a repo yet).
- Use placeholder art (primitives, simple materials) until mechanics are proven.
