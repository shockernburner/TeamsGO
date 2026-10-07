# Releasing TETHER: Primal on itch.io (while Steam reviews)

## Is it allowed?

Yes. Steam has no exclusivity, and itch.io is happy to host games that are also (or later) on Steam. Two things to
keep in mind:

- **Price.** Valve asks that Steam players never get a worse deal than elsewhere. If the game is paid on itch, use
  the same price you plan for Steam, or release on itch as a free early alpha / demo instead.
- **Licences.** Every asset in a build must allow commercial distribution in a game. The Asset Store packs
  (Muelsa dinosaurs, NatureManufacture forest, Survivalist, OH-1, Sky Den axes, Rain System VFX), Quaternius
  (CC0), Poly Haven skies (CC0) and Mixkit sounds all allow this inside a built game. Never include the Muelsa
  pack's demo sounds. See `Docs/ASSET_LICENSES.md`.

Recommended: **free early alpha on itch** ("In development"), with a "Wishlist on Steam" link at the top of the
page. It builds a player base and feedback without competing with the Steam launch price.

## What itch builds differ in

- **No Steam inside.** Do not ship `steam_appid.txt` in an itch build. App 480 is Valve's test app, so players
  would show up as playing "Spacewar". Without it the game runs without Steam: Steam names, invites and Steam
  voice relay are off.
- **Co-op on itch** is LAN (same Wi-Fi) or direct IP: the host forwards **UDP port 7770** on their router, or both
  players join the same virtual LAN (Tailscale, ZeroTier or Radmin VPN, all free) and use that address.
- **Quit** closes the game and returns the player to the itch app (or desktop).

## Step by step

1. **Make the builds** (on the Mac, in Unity):
   - `Project Fossil > Release > Build for itch (Windows + Mac)` gives `Builds/itch/windows/` and
     `Builds/itch/mac/TETHER Primal.app`, both without `steam_appid.txt`.
2. **Create the itch page:** itch.io > Dashboard > Create new project.
   - Title: TETHER: Primal. Project URL: `tether-primal`. Kind of project: Downloadable.
   - Classification: Games. Release status: In development. Pricing: No payments (or Donate).
   - Visibility: **Draft** until everything is uploaded and checked.
3. **Install butler** (itch's uploader): download from https://itchio.itch.io/butler, then in Terminal run
   `butler login` once.
4. **Upload** (replace `vantward` with your itch username):

   ```
   butler push "Builds/itch/windows" vantward/tether-primal:windows --userversion 0.1.0
   butler push "Builds/itch/mac" vantward/tether-primal:mac --userversion 0.1.0
   ```

   Each later update: the same command with a higher `--userversion`. butler only uploads what changed.
5. **Fill the page** with the copy below, the cover image, 5 screenshots and the trailer link, then set
   Visibility to **Public**.

## Warnings players will see (put these in the description)

- **Mac:** the app is not notarized yet, so macOS says it "can't be opened". Fix: open System Settings > Privacy &
  Security, scroll down, press **Open Anyway** next to TETHER Primal. (Notarizing needs an Apple Developer
  account, US$99 a year; worth doing before Steam.)
- **Windows:** SmartScreen may say "Windows protected your PC". Press **More info > Run anyway**.

## Page copy

**Short description (shown under the title):**
Co-op dinosaur survival where the dinosaurs can hear you. Drop in, salvage, survive, get out together.

**Description:**

> In some parallel universe, the dinosaurs never died out.
>
> The dinosaurs never left. Humanity pushed them back to a ring of islands beyond the Cordon and called it peace.
> On the islands, they changed. They learned our engines. Our lights. Our voices.
>
> You are a TETHER crew: one to four survivors dropped onto a lost island to salvage what the dead outposts left
> behind, then fire a flare and climb the helicopter ladder before the island takes you.
>
> **They can hear you.** Your real voice carries in the world. Whisper and a raptor walks past your hiding spot.
> Talk too loud and something turns its head. Shout to pull a hunter off a teammate.
>
> - A new island every match, generated from a seed: jungle, plains, swamp and volcanic ground, rivers and lakes.
> - Ironjaw, an apex hunter that tracks your scent.
> - An island that fights back: stampedes, packs, storms and the hunter sent at you as the clock runs down.
> - Co-op for 1 to 4, proximity voice, revive your downed teammates.
> - Solo with Easy, Medium and Hard; co-op plays it Hard.
> - Scores, Survivor Rank and leaderboards for players and crews.
>
> Early alpha: expect rough edges. Tell us what breaks in the comments.
>
> Stay quiet. Stay close. Leave no one.

**Genre:** Survival. **Tags:** co-op, dinosaurs, survival, multiplayer, procedural-generation, horror, stealth,
3d, first-person, extraction. **Made with:** Unity. **Inputs:** keyboard and mouse. **Multiplayer:** local
network, online (direct IP), 1–4 players, voice chat. **Languages:** English. **Accessibility:** configurable
controls are planned.

**Cover image (630x500):** `Assets/_Project/Art/Brand/Resources/Brand/TetherPrimal.png` over a dark in-game
screenshot. **Icon:** `Icon1024.png`.
