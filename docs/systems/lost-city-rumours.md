# System: Lost City Rumours

| | |
|---|---|
| **Status** | In development — **placement + per-tile model + save (v25)** done; exploring a rumour to roll a reward is the next slice |
| **Last verified** | 2026-06-17 @ LCR placement (`86d3c9uex`) |
| **Code** | `game/src/GameLogic/World/LostCityRumourGenerator.cs`, `World/GameMap.cs` (`HasRumour`/`Rumours`/`AddRumour`/`RemoveRumour`), `GameSession/Game.cs` (`Game.New` placement step, `LcrStreamId`), `Persistence/SaveGame.cs` (`Rumours`, v25) |
| **Tests** | `game/tests/GameLogic.Tests/World/LostCityRumourTests.cs` |
| **FreeCol reference** | `SimpleMapGenerator.makeLostCityRumours`, `LostCityRumour.java`, `Tile.hasLostCityRumour`/`removeLostCityRumour`, `EuropeanStartingPositionsGenerator` (start-area removal) |
| **Related systems** | [map-terrain](map-terrain.md), [save-load](save-load.md), [fog-of-war](fog-of-war.md) |

## 1. How it works (plain English)

Scattered across the new world are **mysterious ruins** — Lost City Rumours. At the start of every game a handful of land tiles are quietly seeded with one (you won't see them through the fog until you get close). They sit out in the wilderness, never on a settlement, never on top of a unit, never right on your doorstep, and never up in the frozen polar fringe. **Walking a land unit onto a rumour tile investigates it** — and *that's* when the dice are rolled: it might be nothing, a gift of gold, a learned skill, a band of new colonists, a vanished expedition, a burial ground that enrages the natives, or — the dream — a city of gold and a treasure train. (That reward roll is the **next** slice; for now the rumours are placed, persist in saves, and are cleared when explored.)

The more land a map has, the more rumours appear (roughly one per 35 land tiles).

## 2. Detailed rules

- **How many:** target = `width × height × 45% / 35` (FreeCol's "a rumour every `rumourNumber`=35 land tiles", using our generator's ~45%-land estimate in place of FreeCol's `landMass` option). On the default 36×24 map that's **11**. The actual number can be fewer if eligible tiles run out (never more).
- **Where a rumour may sit:** dry **land**; not on a tile that already has a rumour; not on a **settlement** (colony or native); not on a tile holding a **unit**; not in the **polar rows** (FreeCol `Map.isPolar`: the top and bottom three rows); not in the player's **3×3 start area** (FreeCol removes rumours around a starting colony).
- **Placement is once, at game start**, and is **deterministic for a seed** — the same seed always produces the same rumour tiles.
- **Exploring** a rumour removes it (one-shot); a consumed rumour never comes back and is absent from later saves. *(The explore trigger + the reward table are the next slice.)*

**Deviations from original / FreeCol:** the **count estimate** uses our ~45%-land fraction rather than FreeCol's `landMass`=25% option (we have no such option; 45% matches the land our continents actually grow, so the count matches what the player sees — faithful to FreeCol's *intent*, a rumour per 35 land tiles, not to the 25 constant). We skip FreeCol's `SLOSH` edge-inset sampler — our maps already keep a watery margin, so uniform sampling over land tiles gives the same inset effect. A rumour is a **type-less flag** at this stage (FreeCol leaves the type undetermined until explored too); the FreeCol generation-time *MOUNDS* pre-set for native-owned tiles is deferred to the outcomes work.

## 3. Technical design

- **Placement** lives in `Game.New` (not `MapGenerator.Generate`): it runs **after** the map, the starting unit, native settlements and foreign powers are placed, so it can exclude every occupied tile and the start area. `LostCityRumourGenerator.Place(map, excluded, random)` returns the chosen positions (eligible land tiles, shuffled by the seeded RNG, take `target`); `Game.New` folds them in via `map.AddRumour`.
- **Determinism (ADR-009):** placement draws from a **dedicated stream** — `new Pcg32Random(seed, LcrStreamId)` with `LcrStreamId = 100`, a reserved id **above every per-player stream** (`Player.RngStreamId = playerId + 1`, so foreign powers occupy 2,3,4…). Because the scatter never touches the human's stream 0 (`_random`), every economy/combat/immigration draw — and the L5 soak's byte-stability — is unchanged. The stream is **gen-time only**: it is never saved or resumed (like map gen and native placement); a loaded game rebuilds rumours from the saved tile list, not by re-scattering.
- **Tile model:** `GameMap` holds a sparse `HashSet<Position> _rumours`, parallel to `_resources` — `HasRumour(p)`/`Rumours` (read) and `AddRumour`/`RemoveRumour` (internal: place at gen, consume on explore). A rumour is **not** stored on `TerrainType` (immutable rule-data).
- **Save (v25, additive):** `SaveGame.Rumours` is a row-major `int[]` of rumour tile indexes, **omitted when empty** so a rumour-free game stays byte-identical to v24 and pre-v25 saves load with none. `From` writes `game.Map.Rumours.Select(p => p.Y·W + p.X)`; `Restore` decodes them into the `GameMap` ctor's `rumours` param (no value needed — presence only).

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `LostCityRumourTests`: scatter on land at the target count, deterministic-for-seed, exclusions (water / polar rows / start 3×3 / settlements / units / no-dup), over-constrained map tolerated without throwing, `GameMap` add/has/remove, save round-trip (v25), rumour-free game omits the token + a v24-style save loads with none | ✅ |
| L2 Scenario | Always | the L5 soak (byte-stable across interrupted-vs-uninterrupted runs) confirms placement leaks no non-determinism and never shifts the human's stream 0 | ✅ |
| L3 Interaction | No UI yet | rumour markers on the map are a later presentation slice | — |
| L4 Visual | No screen yet | — | — |

## 5. Open issues / TODO

- [x] **Placement + per-tile flag + save** (`86d3c9uex`).
- [ ] **Explore trigger + outcome resolution** (`86d3c9uhj`): the move-onto-tile hook + the weighted reward table (nothing / vanish / tribal-chief gold / learn / colonist / burial-ground / ruins / cibola / fountain-of-youth), drawn per-player (`RandomFor(owner)`), with scout / Hernando de Soto good-outcome bias.
- [ ] **Fountain of Youth** (`86d3c9ujx`): an immigration burst onto the Europe dock.
- [ ] **Strange-mounds prompt** (`86d3c9umy`) + the generation-time MOUNDS pre-set for native-owned tiles.
- [ ] **Treasure trains** (`86d3c9ryj`/`86d3c9rzu`/`86d3c9t1e`): the treasure-train unit (save bump for the carried amount), spawn-on-sack replacing instant plunder, and cashing in (King's transport cut + tax, or sail it home).
- [ ] Map **rumour markers** (presentation).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-17 | **LCR placement + model + save** (`86d3c9uex`): `LostCityRumourGenerator` scatters ~`land/35` rumours at `Game.New` from a dedicated RNG stream (`LcrStreamId`=100, off the human's stream 0) clear of settlements/units/start/polar; `GameMap` gains `HasRumour`/`Rumours`/`AddRumour`/`RemoveRumour`; save **v25** adds the additive `Rumours` index list (omitted when none). +7 L1; 684 + soak green (byte-stable). Outcomes/treasure are the next slices. | Phase 5 (`86d3c9uex`) |
