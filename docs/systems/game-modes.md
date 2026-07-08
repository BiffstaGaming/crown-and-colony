# System: Game modes / variants (transposability)

| | |
|---|---|
| **Status** | In development (selection layer + Classic variant; **scenario selector surfaced at New Game**; **Australian Federation variant skeleton registered (P8) — selectable and playable on the authored Australia map; six named colony regions + six selectable colony-start scenarios (logic API); content reskin in progress**) |
| **Last verified** | 2026-07-08 @ Australia six colony regions + colony-start API (P8, `86d3mm1xr`/`86d3mm2ug`) |
| **Code** | `game/src/GameLogic/Specification/GameVariant.cs` (`ClassicAmerica` + `Australia`), `Ruleset.LoadEmbedded`; the fixed maps in `game/src/GameLogic/World/FixedMap.cs` (`MapSource.America`/`Australia`); the colony-start API in `game/src/GameLogic/World/AustraliaColonyStart.cs` (`AustraliaColony` + `AustraliaColonyStart`); selection + `Pending*` threading in `game/presentation/GameController.cs`; the New-Game UI in `game/presentation/NewGameDialog.cs` |
| **Tests** | `game/tests/GameLogic.Tests/Specification/GameVariantTests.cs` + `AustraliaVariantTests.cs` (L1/L2); `game/presentation/tests/MainMenuTests.cs` + `MainSceneTests.cs` + `NewGameSetupUiTests.cs` (L3 — dialog forwarding + map dropdown + applied-to-started-game) |
| **FreeCol reference** | FreeCol ships multiple rulesets (`data/rules/classic`, `data/rules/freecol`) + mods (`data/mods/`) — same "data selects the world" idea; the New-Game UI mirrors FreeCol's `NewPanel` (the "rules" dropdown) + `GameOptionsDialog` (option grouping) + `MapGeneratorOptionsDialog` |
| **Related systems** | [ruleset-data](ruleset-data.md), [save-load](save-load.md), [founding-fathers](founding-fathers.md), [natives](natives.md), [fog-of-war](fog-of-war.md), [custom-house](custom-house.md), [independence](independence.md) (victory conditions), [map-terrain](map-terrain.md) |

## 1. How it works (plain English)

The game can tell more than one story. "Colonial America" is the one we ship today; an "Australia" setting (and others later) will follow. Each of these is a **variant** — a self-contained world with its *own* nations, Founding Fathers (each with their own perks), starting countries, units and goods.

The important promise: **picking a variant is the only thing that changes the data.** All the game's rules — how colonies produce, how combat resolves, how turns advance — are written once and read whatever the chosen variant defines. So adding a new setting is a *content* job (write its data, add it to the list), not a rewrite. American Founding Fathers and Australian Founding Fathers can both exist; only the selected variant's set is used.

**Worked example:**
> In the Classic variant you might recruit Thomas Jefferson (+50% bells). A future Australia variant would define its own historical figures with their own perks in its own data file — the election screen, the bonus maths, and everything else work unchanged, because they just apply whatever the selected variant's people grant.

**Australia — where you start (colony-start scenarios).** The Australian Federation variant adds a second axis of replayability *within* the one British-Australia campaign: **which of the six colonies you begin in**. The authored Australia map names its land as the six historical colonies — New South Wales, Victoria, Queensland, South Australia, Tasmania, Western Australia — and you can choose which one your first settlers land in. New South Wales is the default (Sydney Cove, the First Fleet's 1788 landing); pick Western Australia and you start on the far western coast instead, Tasmania and you start on the southern island, and so on. It's the same continent and the same rules every time — only your **starting coast** changes, which changes the frontier you face (isolation, terrain, neighbours). *(This is currently the engine/data layer — the code that resolves a chosen colony to its landing spot and boots the game there. The New-Game screen dropdown to pick it is a later task.)*

**What the player sees and does:** the **New Game** screen opens with a **Scenario** dropdown at the top — the world the game tells. It now lists **"Colonial America (Classic)"** and **"Australian Federation"** (the P8 skeleton — a copy-of-classic ruleset being reskinned to Australian content, played on the authored Australia continent map). Picking Australian Federation was *one registry line* of change to add — no other change to the screen or the code. Below the scenario, the same screen lets the player tune the world before starting:

- **Scenario** — which variant (ruleset) the game plays (Classic today).
- **Map** — a procedurally generated random New World, or FreeCol's fixed America map.
- **World size / Land mass / Landmass** — how big the map is, how much of it is land, and whether the land is one continent, a few big islands, or many small ones. (These apply only to the random map — a fixed map sets its own shape, so they grey out when America is chosen.)
- **Difficulty** — the five classic levels (Discoverer … Viceroy); Conquistador is the historical default.
- **Nation** — which European power the player leads (or "No nation" for the classic nation-less start).
- **Game options**, grouped the way FreeCol groups them:
  - *Victory conditions* — which ways of winning are switched on: defeat the Royal Expeditionary Force (on), be the last European power standing (on), be the last human standing (off).
  - *Map options* — **Fog of war** (on): explored land you can't currently see is remembered but its contents hidden; turn it off to keep everywhere you've ever been permanently in view.
  - *Colony options* — **Custom house sells boycotted goods** (on): a colony's custom house smuggles goods even when they're under boycott; turn it off and it skips boycotted goods.

**Every option is pre-set to its current default, so a player who just presses Start gets exactly the game they got before this screen existed** (the default game is unchanged, bit for bit).

> **Why only these options?** The screen shows only the options the engine actually *acts on*. FreeCol's setup dialogs expose dozens more (exploration points, amphibious moves, customs-on-coast, and map-generator dials like river/mountain/rumour counts). Ours doesn't yet read most of those — a switch that did nothing would be a lie to the player — so each is added here only once the engine honours it. The map-generator counts in particular are still fixed constants inside the map generator, not yet wired to a setting, so they aren't shown.

## 2. Detailed rules

- A **variant** has a stable `Id` (e.g. `classic`), a display name, a description, and the embedded specification it loads.
- The shipped variants live in one registry (`GameVariants.All`); `GameVariants.Default` is used for a new game or a legacy save.
- A **save records its variant id** so it always reloads under the matching ruleset. A pre-v15 save has none → it resolves to the default (Classic).
- Loading a variant whose id isn't installed fails loudly (you can't play a world whose data you don't have).

**The transposability contract (what a variant must honour):**
- The engine references a small set of **well-known ids** that every variant is expected to define — core goods (`model.goods.food`, `bells`, `crosses`, `grain`), the high-seas/ocean tiles, and the core abilities (`navalUnit`, `foundColony`, `person`). These are the *contract*, the same way FreeCol's own rulesets keep them. A variant changes the *content* (which nations, which fathers, which perks), not these structural anchors.
- Founding-Father perks expressed as `<modifier>`/`<ability>` (ADR-017) need **no code** — any variant's fathers can grant bonuses to goods, etc. A genuinely *novel* mechanic perk (one not expressible as a modifier/ability) needs a small handler keyed to its ability id.

**Deviations from original / FreeCol:** none in spirit — FreeCol is itself ruleset-driven with mods; we adopt the same "data selects the world" model. We do not (yet) support mod *overlays* that patch a base ruleset — each variant is a whole spec.

## 3. Technical design

**Domain model:** `Specification.GameVariant` (id, display name, description, embedded spec resource; `LoadRuleset()`), `Specification.GameVariants` (the registry: `ClassicAmerica`, `All`, `Default`, `ById`, `Resolve(id?)`).

**Data sources:** each variant points at an embedded `specification.xml`. `Ruleset.LoadEmbedded(resource)` reads it; `Ruleset.LoadClassic()` is now a convenience for the classic variant. The generic `Ruleset.Load(Stream)` parses *any* spec — the engine is variant-agnostic by construction.

**Integration points:** `GameController` holds the selected `_variant`; a new game loads `_variant.LoadRuleset(difficulty.Id)`. On save it records `_variant.Id`; on load it reads the save's variant, resolves it (`GameVariants.Resolve`), and restores under that ruleset.

**New-Game options surface (`86d3e4bu0`):** `NewGameDialog` (presentation, built programmatically) collects the picks and threads them to the started game through the existing **`GameController.Pending*` static pattern** (statics survive the menu→game scene change). The `onStart` callback carries the world-options (`WorldSize` / `LandMass` / `DifficultyLevel` / `MapSource`) that `MainMenu` stores on `PendingWorldSize` / `PendingLandMass` / `PendingDifficulty` / `PendingMapSource`; the dialog sets the remaining picks directly on their own statics:

| Pick | Static | Applied in `GameController` | Honoured by | Persisted? |
|---|---|---|---|---|
| Scenario / variant | `PendingVariant` (`GameVariant?`) | `NewGame()` sets `_variant` before `StartNewGame` | `_variant.LoadRuleset` (ADR-018) | **Yes** — save records the variant id (existing field, no bump) |
| Human nation | `PendingNation` (`string?`) | forwarded to `Game.New(humanNationId:)` | national advantage + colony names | yes (player state) |
| Landmass style | `PendingLandStyle` (`LandStyleOption?`) | forwarded to `Game.New(landStyle:)` | `MapGenerator` (random map only) | n/a (map baked into save) |
| Victory conditions | `PendingVictoryConditions` (`(bool,bool,bool)?`) | `Ruleset.WithVictoryConditions` | `Game.Winner` | **No** — session-only override |
| Fog of war | `PendingFogOfWar` (`bool?`) | `Ruleset.WithFogOfWar` | `Game.CurrentlyVisible` / `IsVisible` | **No** — session-only override |
| Custom-house smuggling | `PendingCustomIgnoreBoycott` (`bool?`) | `Ruleset.WithCustomIgnoreBoycott` | custom-house auto-sell | **No** — session-only override |

The three `Ruleset.With*` overrides are configuration seams, not rules changes (ADR-006): each is applied to the freshly-parsed, never-shared ruleset instance right after `LoadRuleset`, and **a `null` pick leaves the spec default untouched** — so the dialog's pre-selected defaults reproduce the byte-identical default game (ADR-009). The victory/fog/custom-house overrides are deliberately **not** persisted (a saved override would bump the save format — see [save-load](save-load.md)); a reload re-derives them from the variant's spec. The variant id *is* persisted (the save already carries it), so a saved variant game reloads under the right ruleset.

**Honoured-but-omitted (and why), per ADR-009 "don't surface an inert option":** the other `gameOptions.map`/`.colony` toggles FreeCol shows (`explorationPoints`, `amphibiousMoves`, `enhancedMissionaries`, `customsOnCoast`, …) are not consulted by the engine yet. The map-generator **counts** FreeCol exposes (`model.option.riverNumber` / `mountainNumber` / `rumourNumber` / `bonusNumber`) are still hard-coded constants in `MapGenerator` / `LostCityRumourGenerator`, **not plumbed through `Game.New`** — so they are not surfaced (a dial here would be inert; the only wired map-generator options — size / land mass / landmass style — *are* surfaced). The per-level difficulty option *values* (FreeCol's editable `DifficultyDialog`) are not surfaced — the engine reads them from the chosen level's spec, not a live override; the difficulty *level* dropdown is.

**Persistence:** save format **v15** adds the variant id (`SaveGame.Variant`); `SaveGame.From(game, variantId)` records it. Pre-v15 saves load as Default.

**Australia colony-start scenarios (`86d3mm2ug`, design doc `04_*` Mode 3).** `World.AustraliaColony` is the six-value enum (NSW / Vic / Qld / SA / Tas / WA); `World.AustraliaColonyStart` resolves it. Each colony maps to a **coastal start tile** on `australia.txt` and the **region key** naming it — the six start tiles are the *same* coastal seeds the region generator (`scripts/generate-australia-regions.py`, `86d3mm1xr`) partitions the six colony regions around, so a colony's start always sits inside its own named region. The API is deliberately minimal and engine-level (no Godot UI — that is a later task):

| Member | Returns | Use |
|---|---|---|
| `AustraliaColonyStart.All` | the six colonies in Federation order | enumerate the choices (a future dropdown) |
| `AustraliaColonyStart.Default` | `NewSouthWales` | the historical First-Fleet start |
| `StartTile(colony)` | the coastal `Position` | inspect / test the landing tile |
| `RegionKey(colony)` | `model.region.newSouthWales` … | the region the start lies in |
| `ImportFor(colony, ruleset)` | a `MapImportResult` | pass to `Game.New(importOverride:)` |

`ImportFor` imports the Australia map (terrain + the six-colony region layer) and swaps only its `HumanStart` (`imported with { HumanStart = StartTile(colony) }`) — reusing the **exact seam** a scenario `[starts] human X Y` uses, so `Game.New` honours it with no code change (`imported?.HumanStart` at the coastal-start heuristic). RNG-free and engine-free (ADR-006/009): choosing a colony only relocates the human's landfall, drawing no RNG, so a game started in any colony keeps the same stream sequence. For `NewSouthWales` the override equals the plain import (the map already bakes NSW as the First-Fleet landfall), so the default colony boots byte-identically to an ordinary Australia game. The per-colony **difficulty** hints in the design doc (Tas/WA "Hard", etc.) are *not* wired here — difficulty stays the New-Game dropdown; this API only sets *where* you land.

**Still hard-coded (tracked migrations toward full per-variant data):**
- Colony names: **per-nation lists are now ruleset data** (`EuropeanNation.ColonyNames`, parsed in FP-3a — see [ruleset-data](ruleset-data.md)/[players](players.md)). `Game.ColonyNames` remains the fallback used by the nation-less human until `FoundColony` adopts per-nation names when the human is assigned a nation (FP-3b).
- Starting unit type (`Game.StartingUnitTypeId`) and a few father-effect handlers keyed to specific ability ids.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `GameVariantTests`: registry/`Default`/`ById`/`Resolve`; classic loads the American world | ✅ |
| L2 Scenario | Always | `GameVariantTests.DifferentRuleset_YieldsDifferentFathersAndNations` — the transposability proof (a custom spec → a custom father with its own perk, no shared content); `SoakTests` byte-identical default (option defaults don't shift the default game); **`AustraliaVariantTests` colony-start scenarios (`86d3mm2ug`)** — the six colonies' starts each boot the human on that colony's coast (theory over all six), the default colony equals the map's own First-Fleet start, and the registry lists all six | ✅ |
| L3 Interaction | New-Game UI + save/load | `MainMenuTests` — the dialog forwards the chosen scenario/variant + game options onto their `Pending*` statics (the **write** side); `NewGameBridgeTests` (`86d3fy56v`) — the **consume** side of the same seam: booting the game scene with the five setup-dial statics set (rival count / start year / map options / rumour number / national advantages) clears every static and threads the picks into the started game (asserted through the read-only `GameController.CurrentGame`), plus the no-picks path starts the classic default (1492, 3 rivals); `MainSceneTests.NewGame_AppliesAChosenNonDefaultOptionSet_ToTheStartedGame` — a non-default option set reaches the started game's ruleset (+ a companion default-byte-identical check); `InputTests` F5/F9 round-trip drives the variant-aware load path | ✅ |
| L4 Visual | No golden yet | — (the New-Game dialog has no visual golden; the menu golden is unaffected) | — |

- **FreeCol cross-check:** conceptual — FreeCol is ruleset+mod driven and groups its New-Game options into `gameOptions.{map,colony,victoryConditions,years}` (`GameOptions.java`) shown via `GameOptionsDialog`, with the ruleset chosen in `NewPanel`; we match the "data selects the world" model and mirror the group taxonomy for the honoured subset (without mod overlays yet).

## 5. Open issues / TODO

- [x] Variant-select screen (UI) — the **Scenario** dropdown is now on the New-Game dialog (`86d3e4bu0`); a future variant is a registry entry, not a dialog change.
- [ ] Surface more honoured game options as the engine grows to read them (the omitted `gameOptions.map`/`.colony` toggles), and wire + surface the map-generator counts (`riverNumber`/`mountainNumber`/`rumourNumber`/`bonusNumber`) through `Game.New` (today they are fixed constants in `MapGenerator`/`LostCityRumourGenerator`).
- [ ] Optional: an editable difficulty (FreeCol's `model.difficulty.custom` + `DifficultyDialog`) — today only the five preset *levels* are offered.
- [ ] Migrate the remaining hard-coded America-specific data into nation/ruleset data (colony names via `<nation>` parsing; review the well-known-id contract).
- [ ] Decide on mod-overlay support (patch a base ruleset) vs. whole-spec variants (the Australia variant is currently a whole spec — a copy of classic being reskinned).
- [x] **Australia variant skeleton** (P8, `86d3kgbu2` epic `86d3b3r7h`): registered `GameVariants.Australia` + the authored Australia continent map (`MapSource.Australia`, `australia.txt`); selectable and playable. **Remaining:** reskin the spec to Australian content — nations/units/goods/**Australian Pioneers** (the Founding-Father equivalent)/display labels (the other `[P8]` tasks). See `docs/australian_federation_mode_md/IMPLEMENTATION_PLAN.md`.
- [x] **Australia six colony regions + start sites** (P8, `86d3mm1xr`): the six colonies as named map regions + the First-Fleet NSW landfall (see [map-terrain](map-terrain.md)).
- [x] **Australia colony-start scenarios — logic API** (P8, `86d3mm2ug`): `AustraliaColony`/`AustraliaColonyStart` (six selectable starts). **Remaining:** surface it in the New-Game UI (a "Colony" dropdown, Australia-only) — a later presentation task; wire per-colony difficulty hints if desired.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-07-08 | **Australia colony-start scenarios + six named colony regions** (`86d3mm2ug` / `86d3mm1xr`). The Australia map now carries the **six historical colonies as named regions** (map-terrain change — see [map-terrain](map-terrain.md) changelog same date: `[regions]` overlay on `australia.txt`, one keyed ocean + six keyed `Land` colonies, generated by `scripts/generate-australia-regions.py`). On top of that, `World.AustraliaColony` + `World.AustraliaColonyStart` give a **logic-level six-start selection API**: `StartTile`/`RegionKey`/`ImportFor`/`All`/`Default` resolve a chosen colony to its coastal landing tile and to a `MapImportResult` fed to `Game.New(importOverride:)` (swapping only `HumanStart` — the same seam a scenario `[starts]` uses). Engine/data only — **no Godot UI** (the New-Game dropdown to pick a colony is a later task); RNG-free/engine-free, so any colony start keeps the byte-stable stream sequence and NSW (the default) boots identically to a plain Australia game. `Game.PredefinedRegionLabel` now humanises non-ocean keyed region keys (`newSouthWales` → "New South Wales") — inert for the default game (soak green). +11 L1/L2 (`AustraliaVariantTests`; full suite 2773 + soak 5 green). **Naming.cs untouched** (another stream owns it) — the humanisation runs through the existing `Naming.Humanize` from `PredefinedRegionLabel`. | (this commit) |
| 2026-07-08 | **Australian Pioneers roster** (`86d3kwtjb`): the variant's Founding-Father equivalent is authored — see [founding-fathers.md](founding-fathers.md) (changelog, same date) for the full roster + per-figure perk mapping. The electing body is titled the **Federation Convention** (`GameVariant.CongressName`). Data-only (ADR-018): the election machinery, saves and the classic variant are untouched. | (this commit) |
| 2026-07-08 | **New Zealand removed from the Australia map** (Chris: "Is that… New Zealand off the coast on the bottom right? This is for Australia only."). The FreeCol community source included NZ's northern tip — a separate 12-tile island (forest/mountains/plains, columns 51–57 / rows 33–36 of the de-staggered 60×40 grid), identified by flood-fill component analysis (mainland+Tasmania = one 889-tile component; the 1-tile Torres-Strait islet stays). Its land tiles are now `ocean` in the shipped `australia.txt`; the intentional edit of the GPL source is recorded in `data/maps/PROVENANCE.md`. Guarded by `AustraliaVariantTests.AustraliaMap_HasNoNewZealand_TheSouthEastIsOpenSea` (everything east of column 50 in the southern half must be water) so a re-conversion can't silently bring it back. Render-verified in-game. | (this commit) |
| 2026-07-08 | **Fixed maps de-staggered — Australia finally looks like Australia** (Chris playtest: "Australia is much wider than it is taller — this map feels stretched"). Root cause: FreeCol maps use a **staggered isometric lattice** whose stored `y` counts **half-rows** (N/S moves are `y±2`; odd rows sit half a tile right/lower), so importing FreeCol `(x,y)` verbatim rendered every FreeCol map **twice as tall** as authored. Both shipped grids re-converted with the lossless relabel `square (col,row) = (2x + y%2, y÷2)` (even columns = even half-rows, odd columns = the offset odd half-rows; same tile count, FreeCol's exact on-screen aspect): **America 40×180 → 80×90** (7200 tiles), **Australia 30×80 → 60×40** (2400 tiles — wider than tall, with Cape York / the Gulf of Carpentaria / the Bight / Tasmania all reading correctly; render-verified in-game). Conversion documented in `data/maps/PROVENANCE.md` (any future `.fsm`/`.fsg` conversion must apply it). Tests updated (`FixedMapTests` incl. re-derived spot-checks, `AmericaGameTests`, `MapImporterTests`, `AustraliaVariantTests`); full L1/L2 2752 green. Saves are unaffected (a save carries its own map). No goldens contain fixed maps. | (this commit) |
| 2026-07-07 | **Australian Federation variant skeleton + map** (P8, `86d3kwtf9`/`86d3kwtp5`): registered `GameVariants.Australia` (spec = copy of classic for now, root id `australia`, embedded) so "Australian Federation" is selectable in the Scenario dropdown; added `MapSource.Australia` + the authored **Australia continent map** (`data/maps/australia.txt`, 30×80) — converted from the FreeCol community map pack by Euzimar (GPL v2), all-standard terrain ids resolving 1:1 to our ruleset. `NewGameDialog` map dropdown gains "Australia (fixed)". +7 L1 (`AustraliaVariantTests`: variant/anchors/map-import/boot/save) + L3 map-dropdown update. Classic default byte-identical (soak green); no save bump (variant id already v15). Content reskin is the remaining `[P8]` tasks. | _this commit_ |
| 2026-07-03 | New-Game bridge L3 (`86d3fy56v`): `NewGameBridgeTests` pins the `Pending*` **read → clear → thread** seam from the consume side (five setup dials in, statics cleared, picks observable on the started game; plus the no-picks classic-default path); `GameController.CurrentGame` added as the read-only seam it asserts through. All cross-scene statics now reset in one helper (`ResetPendingStatics`) — extend it when adding a new `Pending*` static. Test + accessor only — no behaviour change | _this commit_ |
| 2026-06-22 | New-Game options surface (`86d3e4bu0`): a **Scenario/variant** dropdown (the seam future variants plug into) + the honoured base game options grouped FreeCol-style (victory conditions / fog of war / custom-house smuggling), threaded through `GameController.PendingVariant`/`PendingCustomIgnoreBoycott` (+ existing `Pending*`). Defaults pre-selected = byte-identical default game (soak green); no save change. L3 dialog-forwarding + applied-to-started-game tests | _this commit_ |
| 2026-06-13 | Variant/game-mode selection layer (`GameVariant`/`GameVariants`, `Ruleset.LoadEmbedded`), variant-aware saves (v15), transposability proof test (ADR-018) | Phase 5 (variant layer) |
