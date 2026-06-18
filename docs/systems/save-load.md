# System: Save & load

| | |
|---|---|
| **Status** | Implemented (full game state: multi-player `Players[]`, per-player RNG streams, diplomacy stances/tensions, native settlements + interaction + missions, ship repair, per-colony liberty, unit standing orders, colony build queues, Lost City Rumours, native land claims, treasure trains, custom-house export settings, escalated Europe unit prices, per-colonist worker types + on-the-job experience + school training; save v33) |
| **Last verified** | 2026-06-18 @ missionaries (save v33: a settlement's resident missionary) |
| **Code** | `game/src/GameLogic/Persistence/SaveGame.cs` · UI: `GameController` F5/F9 |
| **Tests** | `game/tests/GameLogic.Tests/Persistence/SaveGameTests.cs`, `Scenarios/` |
| **FreeCol reference** | n/a — our own format (FreeCol's .fsg is not a compatibility goal) |
| **Related systems** | [randomness](randomness.md) (RNG state is part of the save), [players](players.md) (player-scoped state) |

## 1. How it works (plain English)

Press **F5** to save, **F9** to load. A save captures everything — the map, the units, whose turn it is, and even the game's hidden "dice position" — so a loaded game continues *exactly* as if you'd never stopped: the same future battles, the same future maps. Saves are readable JSON files (handy for debugging and bug reports).

## 2. Detailed rules

- A save restores: turn, map (terrain per tile), every unit (id, **type**, position, movement left), **explored tiles (fog of war)**, RNG state.
- Loading an interrupted game then continuing produces **identical outcomes** to never having saved (tested).
- Saves carry a format `Version` (currently **27**); older saves still load with sensible defaults for fields that didn't exist yet (see the changelog), and v1 saves default units to free colonists and reveal fog around units.
- **Player-scoped state (v20+)** — treasury/tax, the per-player market, liberty/Congress, immigration + the Europe dock, the per-player RNG stream, and explored fog — is stored in a `Players[]` array (the human + the foreign powers + native nations; see [players](players.md)). The load path is chosen by version: a v20+ save reads `Players[]`; a v19-and-earlier save **folds** the old flat top-level fields into a single human player. As of **FP-7** a v20 save writes player state **only** in `Players[]`; the legacy flat top-level fields are no longer written (the format version stays **20** because the v20 load path was already `Players[]`-only, so new v20 saves are just smaller). The flat field *properties* remain on the record (read-only) so ≤v19 saves still fold them into one human player, and pre-FP-7 v20 saves (which carry both) still load via the `Players[]` arm — the flat fields are ignored whenever `Players[]` is present. From **FP-5** these per-player fields actually diverge in real games (each foreign power moves its own market, banks its own gold/liberty/immigration, fills its own dock, advances its own RNG) and all of it round-trips byte-identically — the soak proves a 200-turn active-economy game re-saves identically. On load, each colonial player's Europe dock is **topped up** to its full set: a no-op for a full FP-5 dock (no RNG drawn, so byte-stable), a fresh draw for an older save's empty foreign dock (a foreign power loaded from a pre-FP-5 save can then recruit).
- Each unit also restores its **owner and equipment** (v18+): the owning native nation (null = the player) and its military role + role count; pre-v18 saves load every unit player-owned and unarmed (tested). Native braves persist through the unit list via the owner field — no separate collection — so a saved game's garrisons come back intact. A ship **damaged in combat** restores its **repair countdown** (v21+): turns left repairing in Europe (0 = healthy); pre-v21 ships load healthy.
- The map restores its **Lost City Rumours** (v25+): the tiles holding an unexplored rumour, as a row-major index list (`SaveGame.Rumours`), omitted when none so a rumour-free game stays byte-identical to v24; pre-v25 saves load with no rumours. Placement is gen-time only (a dedicated RNG stream, never resumed), so the save just lists the surviving tiles. See [lost-city-rumours](lost-city-rumours.md).
- The map restores its **tiles bought/taken from the natives** (v26+): a row-major index list (`SaveGame.ClaimedTiles`), omitted when none so a no-purchase game stays byte-identical to v25; pre-v26 saves load with none. Native tile *ownership* is otherwise **derived** (recomputed from the settlements, not saved); this claimed-tiles override is the one piece that persists, so the re-derivation on load never re-claims a tile you already bought. See [natives](natives.md).
- A colony restores its **ordered build queue** (v24+): the queue **front** is the long-standing `CurrentBuild` field; the **tail** (everything after the front) rides in `SavedColony.BuildQueueRest`, omitted entirely when the queue holds ≤ 1 item — so a single-building or idle colony stays byte-identical to v23. A pre-v24 save loads its lone `CurrentBuild` as a one-item queue. See [colonies](colonies.md).
- A save also restores all **native settlements** (v14+): id, owning nation type, settlement type, capital flag, position, size, taught skill, plus their **interaction state** (v16+): alarm, visited flag, skill-consumed flag, and **wanted goods** (v17+). A settlement **destroyed by assault (v19+)** is simply absent from the saved list — there is no new field; its plunder is already folded into the saved gold.
- A save records its **game variant** (v15+; e.g. `classic`) so it reloads under the matching ruleset (ADR-018); pre-v15 saves resolve to the default variant. See [game-modes](game-modes.md).
- Saves reference terrain by ruleset id — loading needs the matching ruleset; unknown ids fail loudly.

**Deviations:** our own JSON format by design; no FreeCol save compatibility planned.

## 3. Technical design

- `SaveGame` (record): pure DTO snapshot; `From(game)` / `Restore(ruleset)` / `ToJson()` / `FromJson()`. System.Text.Json, indented output.
- RNG round-trip via `RandomState` (ADR-009) — the linchpin of resume-identical behaviour.
- Quicksave path: `user://quicksave.json` (Godot user dir); file I/O lives in presentation (`GameController`), serialization in GameLogic — keeps GameLogic free of file-system concerns.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | JSON round-trip preserves all fields; RNG state preserved; unknown terrain id throws | ✅ |
| L2 Scenario | Always | save-mid-game acid test: interrupted vs uninterrupted runs end byte-identical | ✅ |
| L3 Interaction | Yes (F5/F9) | `InputTests.QuickSaveF5_ThenF9_RestoresTheTurn` (simulated keys) | ✅ |
| L4 Visual | No screen | — | — |

## 5. Open issues / TODO

- [ ] Save slots / save dialog UI (later phase); L3 hotkey test.
- [ ] Versioned migration once format changes post-1.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | JSON save format v1, F5/F9 quicksave, resume-identical guarantee | Phase 1 skeleton |
| 2026-06-13 | Format v2: unit type ids + explored tiles; v1 loads with defaults (tested) | Phase 2a |
| 2026-06-13 | Format v3: colonies; pre-v3 loads with none (tested) | Phase 2b |
| 2026-06-13 | Format v4: colony goods stores; pre-v4 colonies load with empty stores (tested) | Phase 3 |
| 2026-06-13 | Format v5: colony tile workers; pre-v5 loads with none (tested) | Phase 3 |
| 2026-06-13 | Format v6: colony buildings + staffing; pre-v6 re-derives free base buildings (tested). Legacy raw-grain stores normalize to food on load | Phase 3 |
| 2026-06-13 | Format v7: colony construction target (CurrentBuild) | Phase 3 |
| 2026-06-13 | Format v8: map bonus resources (sparse index list) | Phase 2/3 |
| 2026-06-13 | Format v9: treasury (gold, tax) + moved-market inventories (sparse); pre-v9 loads 0 gold/tax, market reseeded (tested) | Phase 4 |
| 2026-06-13 | Format v10: liberty, Congress, current/offered fathers; pre-v10 loads empty (tested) | Phase 4 |
| 2026-06-13 | Format v11: unit location + sail turns + cargo hold; pre-v11 units load on-map empty (tested) | Phase 4 |
| 2026-06-13 | Format v12: immigration (pool, target) + Europe recruitment dock (slots, escalating base price/floor); pre-v12 loads classic defaults + a fresh dock (tested) | Phase 4 slice 4 |
| 2026-06-13 | Format v13: unit carrier ids (colonists aboard ships); pre-v13 units load not-aboard (tested) | Phase 4 slice 5 |
| 2026-06-13 | Format v14: native settlements (id, nation type, settlement type, capital, position, size, skill); pre-v14 loads with none (tested) | Phase 5 slice 1 |
| 2026-06-13 | Format v15: game variant id (which ruleset/variant the game plays under); pre-v15 resolves to the default variant (tested) | Phase 5 (variant layer) |
| 2026-06-14 | Format v16: native settlement interaction state (alarm, visited, skill-consumed); pre-v16 settlements load peaceful/unvisited (tested) | Phase 5 slice 3 |
| 2026-06-14 | Format v17: native settlement wanted goods; pre-v17 settlements load with none (tested) | Phase 5 slice 4 |
| 2026-06-14 | Format v18: unit owner nation + role/roleCount (native braves, armed soldiers); pre-v18 units load player-owned and unarmed (tested); default-role player units serialize identically to v17 | Phase 5 slice 5b |
| 2026-06-14 | Format v19: settlement assault — a destroyed settlement is absent from the list, plunder folds into gold (no new field; a marker only). Older saves load unchanged; a sacked-settlement game round-trips (tested) | Phase 5 slice 5c |
| 2026-06-14 | Format v20: player-scoped state moved into a `Players[]` array (one human player); load keyed on version (v20+ reads `Players[]`, ≤v19 folds the flat fields into one human player — tested `V19Save_LoadsAsSingleHumanPlayer`). Flat fields still written in v20 (dropped at FP-7). See [players](players.md) (ADR-019) | FP-1 |
| 2026-06-14 | Format v20 (additive): optional unit/colony owner ids (`SavedUnit.OwnerId`, `SavedColony.OwnerId`); null = the human (id 0), omitted so human-only saves stay byte-stable; pre-FP-2 saves load every unit/colony human-owned (tested) | FP-2 |
| 2026-06-14 | Format v20 (multi-player): `Players[]` now persists the human **and** the inert foreign powers + native nations (and the foreign powers' Europe units); a pre-FP-3b save (only the human, or ≤v19) loads with no rivals (the fold path still yields a single human). See [players](players.md) | FP-3b |
| 2026-06-14 | Format v20 (additive): each non-human player's own PCG stream (`SavedPlayer.RngState`/`RngIncrement`) — so an active foreign power's AI resumes its exact sequence; null/omitted for the human (stream 0 stays top-level). Foreign powers' on-map units + founded colonies round-trip via the existing unit/colony save. A pre-FP-4 save (rivals had no stream) re-derives one deterministically on load | FP-4 |
| 2026-06-14 | FP-5: per-player market/gold/liberty/immigration/dock/RNG now diverge per foreign power and round-trip byte-identically (no new field — the v20 `Players[]` fields are exercised for real; soak round-trips 200 active-economy turns). On load each colonial player's dock is topped up (no-op for a full FP-5 dock; fresh draw for an empty pre-FP-5 foreign dock). Recruited/bought units persist their owner id | FP-5 |
| 2026-06-14 | FP-6a (v20 additive): per-player diplomacy `Stances`/`Tensions` maps on `SavedPlayer` (keyed by other player id; omitted via `WhenWritingNull` when empty, so a no-contact game is byte-identical; older saves load Uncontacted/0). See [diplomacy](diplomacy.md) | FP-6a |
| 2026-06-15 | FP-6b: `Stance.CeaseFire` (=3) is a new possible value in the existing `Stances` map — no format change; FP-6a ordinals (Peace=1/War=2) stay stable, so it round-trips and older saves are unaffected | FP-6b |
| 2026-06-15 | **FP-7: save-format v20 consolidation** — the legacy flat top-level player fields (gold/tax/market/liberty/Congress/fathers/immigration/dock/explored) are **no longer written**; player state lives only in `Players[]`. Format version stays 20 (its load path was already `Players[]`-only); new v20 saves are smaller. The flat properties remain (read-only) so ≤v19 saves still fold and pre-FP-7 v20 saves still load — verified by `NewSave_OmitsLegacyFlatPlayerFields`, `LegacyV20Save_WithFlatFieldsAndPlayers_LoadsFromPlayersIgnoringFlatFields`, `OldSaveVersion_WithFlatFields_FoldsToOneHuman` (v9/12/19), `HumanState_RoundTripsThroughPlayersOnly` | FP-7 |
| 2026-06-15 | Format **v21** (additive): `SavedUnit.RepairTurns` — a damaged ship's turns-left-repairing (1c-3b). Omitted via `WhenWritingNull` when 0, so an undamaged fleet is byte-identical to v20; pre-v21 saves load every ship healthy. A damaged ship's repair state round-trips (tested `DamagedShip_RepairState_SurvivesSaveRoundTrip`). See [combat](combat.md) | Phase 5 slice 1c-3b |
| 2026-06-16 | Format **v22** (additive): `SavedColony.Liberty` — a colony's accumulated Sons-of-Liberty points. Omitted via `WhenWritingNull` when 0, so a no-liberty colony is byte-identical to v21; ≤v21 saves load 0 (SoL 0%). Round-trips (tested `SonsOfLibertyTests.Liberty_SurvivesSaveLoad` + omitted-when-0 + pre-v22-loads-0). See [sons-of-liberty](sons-of-liberty.md) | Phase 5 (Sons of Liberty) |
| 2026-06-18 | Format **v30** (additive): per-colonist worker unit types — `SavedWorker.UnitTypeId` (a tile worker), `SavedColony.BuildingWorkerTypes` (a building's non-free occupants) and `SavedColony.IdleWorkerTypes` (non-free idle colonists). All omitted when the worker is a free colonist, so a free-colonist-only game is byte-identical to v29; pre-v30 saves load every worker free. Round-trip + omit-when-all-free tested (`ColonyWorkerTypeTests`). See [colonies](colonies.md) | Phase 3 (`86d3b6nrz` slice 2) |
| 2026-06-18 | Format **v31** (additive): `SavedWorker.Experience` — a free colonist's accrued on-the-job experience toward an expert upgrade. Omitted when 0, so a game with no accrued experience is byte-identical to v30; pre-v31 saves load every worker at 0. Round-trip + omit-when-0 tested (`ExperienceUpgradeTests`). See [colonies](colonies.md) | Phase 5 (`86d3c9pgj`) |
| 2026-06-18 | Format **v32** (additive): `SavedColony.SchoolTraining` — per school building, the accrued training turns toward its current student. Omitted when no school is mid-training, so a non-teaching game is byte-identical to v31; pre-v32 saves load with no training in progress. Round-trip + omit-when-empty tested (`SchoolTeachingTests`). See [education-schools](education-schools.md) | Phase 5 (`86d3c9p7f`) |
| 2026-06-18 | Format **v33** (additive): `SavedNativeSettlement.MissionOwnerId` + `MissionIsExpert` — a settlement's resident missionary (the owning colonial player + whether it's a jesuit). Both omitted when the settlement has no mission, so a mission-free game is byte-identical to v32; pre-v33 saves load with no missions. Round-trip + omit-when-none tested (`NativeMissionTests`). See [natives](natives.md) | Phase 5 (`86d3c9t6e` slice 1) |
| 2026-06-17 | Format **v29** (additive): `SavedPlayer.UnitPrices` — a player's escalated Europe purchase prices by unit-type id (artillery). Omitted when none have escalated, so a game where no one has bought artillery is byte-identical to v28; pre-v29 saves load with the ruleset base prices. Round-trip + omit-when-empty tested (`EuropePurchaseTests`). See [europe](europe.md) | Phase 5 (`86d3c9qgy`) |
| 2026-06-17 | Format **v28** (additive): `SavedColony.Exports` (per-good `{Exported, Level}`, omitted when empty) + top-level `AutoExportMode` (omitted when `PerGood`). A game with no export toggles and the default mode is byte-identical to v27 (only the version differs); pre-v28 saves load with no exports and `PerGood`. Round-trip + omit-when-default tested (`CustomHouseTests`). See [custom-house](custom-house.md) | Phase 5 (`86d3c9ru3`) |
| 2026-06-17 | Format **v27** (additive): `SavedUnit.TreasureAmount` — the gold carried by a treasure train. Omitted when 0, so every non-treasure unit (and a treasure-free game) is byte-identical to v26; pre-v27 saves load with 0. Round-trip + omit-when-empty tested (`TreasureTrainTests`). See [treasure-train](treasure-train.md) | Phase 5 (`86d3c9ryj`) |
| 2026-06-17 | Format **v26** (additive): `SaveGame.ClaimedTiles` — the row-major tile indexes the player has bought or taken from the natives. Omitted when none, so a no-purchase game is byte-identical to v25; pre-v26 saves load with none. Native ownership is otherwise derived (not saved); this override is honoured by the re-derivation so a bought tile never reverts. Round-trip + omit-when-empty tested (`NativeLandPurchaseTests`). See [natives](natives.md) | Phase 5 (`86d3c9tha`) |
| 2026-06-17 | Format **v25** (additive): `SaveGame.Rumours` — the row-major tile indexes holding an unexplored Lost City Rumour. Omitted when none, so a rumour-free game is byte-identical to v24; pre-v25 saves load with no rumours. Placement is gen-time only (a dedicated RNG stream, never resumed), so only the surviving tiles persist. Round-trip + omit-when-empty tested (`LostCityRumourTests`). See [lost-city-rumours](lost-city-rumours.md) | Phase 5 (`86d3c9uex`) |
| 2026-06-16 | Format **v24** (additive): `SavedColony.BuildQueueRest` — a colony's build-queue tail (everything after `CurrentBuild`). Omitted for a ≤1-item queue, so a single/idle colony is byte-identical to v23; pre-v24 saves load the lone `CurrentBuild` as a one-item queue. Round-trip tested (`BuildQueueTests`: multi-item round-trips, ≤1-item omits the token). See [colonies](colonies.md) | Phase 5 (`86d3c9nxe`) |
| 2026-06-16 | Format **v23** (additive): `SavedUnit.Orders` — a unit's fortify/sentry standing order. Omitted when *Active*, so a no-orders game is byte-identical to v22; pre-v23 saves load every unit Active. A *Fortified* defender's +50% bonus round-trips (tested `UnitOrdersTests.OrderState_RoundTripsThroughSaveLoad` + all-active-omits-token + pre-v23-loads-Active). See [units-movement](units-movement.md) | Phase 5 (`86d3c9pfh`) |
