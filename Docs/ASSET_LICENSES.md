# Asset Licenses

All third-party assets used in this project must be recorded here.
Only assets with licenses that permit commercial use are allowed.

| Asset | Source | License | Notes |
|---|---|---|---|
| Animated Dinosaur Pack (Quaternius) | https://quaternius.com/packs/animateddinosaurs.html | CC0 1.0 (public domain) | `Art/ThirdParty/Quaternius/Dinosaurs`. Files renamed to our species (Velociraptor → `Dino_Raptor`, the big theropod → `Dino_Ironjaw`). |
| Universal Base Characters, Standard (Quaternius) | https://quaternius.com/packs/universalbasecharacters.html | CC0 1.0 | `Art/ThirdParty/Quaternius/Characters`. Bodies renamed `Survivor_Male/Female`; textures downscaled to 1024. |
| Universal Animation Library, Standard (Quaternius) | https://quaternius.com/packs/universalanimationlibrary.html | CC0 1.0 | `Art/ThirdParty/Quaternius/Animations/UAL1_Standard.fbx` (in-place version). |
| Stylized Nature MegaKit, Standard (Quaternius) | https://quaternius.com/packs/stylizednaturemegakit.html | CC0 1.0 | `Art/ThirdParty/Quaternius/Nature`. Subset: trees, rocks, ferns, bushes, grass, mushrooms. Textures downscaled to 1024. |
| Modular Character Outfits - Fantasy, Standard (Quaternius) | https://quaternius.com/packs/modularcharacteroutfitsfantasy.html | CC0 1.0 | `Art/ThirdParty/Quaternius/Characters/Outfit` (Male Ranger body, arms, legs, boots). Textures downscaled to 1024. |
| Pirate Kit (Quaternius, via Poly Pizza) | https://poly.pizza/bundle/Pirate-kit-0q5ulmIYqQ | CC0 1.0 | `Art/ThirdParty/Quaternius/Props`: chests, barrel, palm trees, bones, skulls. `Atlas_Pirate.png` extracted from the FBX's embedded texture. |
| Forest Environment - Dynamic Nature 1.9.7 (NatureManufacture) | https://assetstore.unity.com/packages/3d/vegetation/forest-environment-dynamic-nature-150668 | Unity Asset Store EULA, Standard (single entity, commercial use allowed) | Bought by Firdous on 2026-10-01. **Not in git**: imported locally to `Assets/NatureManufacture Assets` (git-ignored). Ships inside the built game only. |
| Dinosaur pack Vol. I (Muelsa, Asset Store id 35660) | https://assetstore.unity.com/packages/slug/35660 | Unity Asset Store EULA, Standard (single entity, commercial use allowed) | Bought by Firdous on 2026-10-01. **Not in git** (git-ignored folder). Uses its models, textures, animations and tropical plants only; its scripts are stripped from our copies. Its demo sounds are personal/non-commercial only and are **not used**. Creatures are renamed to our species (the big theropod is Ironjaw). |
| Survivalist Character (Slayver, Asset Store id 181470) | https://assetstore.unity.com/packages/3d/characters/survivalist-character-181470 | Unity Asset Store EULA, Standard (free, commercial use allowed) | Added by Firdous on 2026-10-01. **Not in git** (`Assets/Survivalist`, git-ignored). The player model: four outfits, URP materials. Its Starter Assets demo controller is not used. |
| OH-1 Ninja JGSDF Basic Animation (Pukamakara, Asset Store id 351626) | https://assetstore.unity.com/packages/3d/vehicles/air/oh-1-ninja-jgsdf-basic-animation-444-351626 | Unity Asset Store EULA, Standard (free, commercial use allowed) | Added by Firdous on 2026-10-01. **Not in git** (`Assets/OH-1_Basic`, git-ignored). The rescue helicopter. The real aircraft and army names never appear in the game or store copy; it is only "the rescue helicopter". |
| Mixkit sound effects (18 clips: roars, growls, screeches, breath, rain, storm, wind, thunder, helicopter) | https://mixkit.co/free-sound-effects/ | Mixkit Sound Effects Free License (free in commercial projects, no attribution; the files may not be redistributed on their own) | Downloaded by Firdous on 2026-10-01. **Not in git**: cut and renamed into `Assets/SoundLibrary/Resources/SoundLibrary` (git-ignored), e.g. `Roar_1` = mixkit-dinosaur-monster-roar-1976. The original file names are listed in the README inside that folder. |
| FishNet: Networking Evolved 4.7.3 (FirstGearGames) | https://github.com/FirstGearGames/FishNet (Unity package from git, tag 4.7.3) | FishNet License: free, royalty-free use in games, including commercial | Code library, not art. Installed through `Packages/manifest.json`, not copied into Assets. The license text ships with the package (`LICENSE.txt`), plus its `THIRD PARTY NOTICE.md`. |

CC0 needs no attribution. The license text ships in `Art/ThirdParty/Quaternius/LICENSE.txt`.
Downloaded by Firdous on 2026-09-29.

Paid Asset Store packs are licensed to the buyer and must never be pushed to the repository, which is public. Before another developer opens the Unity project with them, check the licence: the store lists both as "Extension Asset", which may need a seat per user. Players get them inside the built game.
