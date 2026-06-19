# System: Map & terrain

| | |
|---|---|
| **Status** | Implemented (climate-band generation + isometric FreeCol-art rendering + bonus resources with yield effects + geographic regions + **selectable world size & land mass**) |
| **Last verified** | 2026-06-20 @ P5 world-size & land-mass options (86d3c9w9c) |
| **Code** | `game/src/GameLogic/World/` (incl. `WorldSizeOptions.cs`) · rendering: `game/presentation/MapView.cs` · new-game options: `game/presentation/NewGameDialog.cs` |
| **Tests** | `game/tests/GameLogic.Tests/World/WorldTests.cs`, `WorldSizeOptionsTests.cs`, `RegionTests.cs`, `RegionGeneratorTests.cs` |
| **FreeCol reference** | map model: `freecol/src/net/sf/freecol/common/model/Map.java`, `Tile.java`; regions: `Region.java`, `server/model/ServerRegion.java`, `server/generator/TerrainGenerator.java` |
| **Related systems** | [ruleset-data](ruleset-data.md), [units-movement](units-movement.md) |

## 1. How it works (plain English)

The world is a rectangular grid of square tiles, each with one terrain type from the rules data. For now the map is a placeholder: a 2-tile ocean border around one landmass whose terrain is randomly scattered (but always the same for the same game seed). On screen each terrain is a flat colour — ocean blue, plains tan, forests green, mountains grey.

**What the player sees and does:** the coloured map; pan with middle/right-mouse drag, zoom with the wheel.

**Choosing the world (new game).** When you start a new game the menu first asks two things: the **world size** (Small 30×20, Standard 36×24, Large 56×38, Huge 80×52) and how much of it is **land** (Sparse 30%, Normal 45%, Dense 50%). A bigger map gives more room to expand and explore; more land means larger continents and less sea. Leave them on the defaults (Standard / Normal) and you get exactly the world the game has always generated. The same seed plus the same two choices always makes the same world.

**Regions.** Behind the scenes the map is also carved into named *regions*, exactly as the original game does. The frozen top and bottom rows are the **arctic** and **antarctic**; the western sea is the **Pacific** and the eastern sea the **Atlantic**, each split into a north and south half; every separate continent is its own **land region** (a very big one is chopped into ~75-tile pieces so no single region swallows a whole continent); and runs of hills and mountains become **mountain regions**. Every tile belongs to exactly one region. Each region carries a *score* — the points a player will earn for discovering it once exploration is added (a faraway sea like the Pacific is worth more; bigger land regions are worth more). Regions are computed purely from the finished terrain, so the same map always produces the same regions, on every machine.

## 2. Detailed rules

- Coordinates: (0,0) top-left; X east, Y south. Diagonals count as adjacent (8 neighbours), as in the original game.
- Same seed → identical map, every time, every machine (ADR-009).
- **Climate-band generation (Phase 2a):** a continent grown from seeded blobs (a configurable land fraction — default 45% — with watery margins); per-tile climate triple — temperature from latitude (40 °C equator → −20 °C poles, jittered), humidity from smoothed noise (0–100), altitude rolled (lowland 84%, hills 10%, mountains ~4–6%); terrain picked among ruleset types whose `<gen>` envelope contains the triple (forest-vs-clear is a separate ~45% roll; off-envelope triples take the climate-nearest type). Outermost map columns are **high seas** (the future route to Europe).
- Result: hot wet equator → savannah/tropical forest, dry bands → desert/scrub, poles → arctic/tundra/boreal.

**World-size & land-mass options (`86d3c9w9c`).** Map width/height and the land fraction are **new-game options** (FreeCol's `model.option.mapWidth`/`mapHeight`/`landMass`). `MapGenerator.Generate` takes a `landMassFraction` (the share of tiles grown into land); `Game.New` takes `mapWidth`/`mapHeight`/`landMassFraction`. The offered presets live in `WorldSizeOptions` ("data not code", like the game variants): sizes Small 30×20 / Standard 36×24 / Large 56×38 / Huge 80×52, land Sparse 30% / Normal 45% / Dense 50% — bounds mirror FreeCol (width 30–200, height 20–200, land 15–50%). **The defaults equal the shipped world (36×24, 45%)**, and the default-valued call uses the identical arithmetic as before, so a default new game is **byte-identical** (ADR-009) — the visual goldens and soak baseline are untouched; only a *non-default* choice changes the map. The land fraction only sets how many tiles `GrowContinent` grows (the per-tile RNG draw order is unchanged), so a non-default world is still fully determined by `(seed, width, height, landMass)`.

**Deviations from original / FreeCol:** uses FreeCol's climate *data* and option *bounds* but not its exact algorithm; FreeCol's `LAND_GENERATOR_TYPE` (classic/continent/archipelago/islands), rivers, and lakes are still future work. Square grid topology as in the 1994 original.

### Regions (86d3c9w12)

After terrain is laid down, every tile is assigned to exactly one region, in this order (earlier passes win a tile; later passes skip already-claimed tiles):

1. **Polar bands** — land in rows `[0, POLAR_HEIGHT)` is **arctic**; land in rows `[Height − POLAR_HEIGHT − 1, Height)` is **antarctic** (`POLAR_HEIGHT = 2`, so 2 top rows / 3 bottom rows — the same asymmetry FreeCol's loops produce). Polar water is left for the ocean pass. Score 0.
2. **Oceans** — a **Pacific** (west) and **Atlantic** (east), each with a **north** and **south** quadrant. Each quadrant is flood-filled (8-direction) from the first water tile down its outer column, in three escalating bounds — own quadrant, own horizontal half, the whole map — so a region can overflow into its opposite quadrant when the geography demands. Tiles live in the four leaf quadrants; the parent ocean carries the discovery score (**Pacific = 100**, Atlantic = 0, `PACIFIC_SCORE_VALUE`). Any water the directional fill cannot reach (an enclosed body) becomes its own score-0 ocean region so no water tile is left unassigned.
3. **Mountain regions** — each contiguous block of hill/mountain (`IsElevation`) land not already in a polar band becomes one mountain region, scored `2 × tile-count`.
4. **Land regions** — each remaining contiguous landmass (8-direction) becomes one land region. A landmass larger than `LAND_REGION_MAX_SIZE = 75` is split into ~75-tile chunks (target 75, or half the remainder when it is below 150). Score = `max((int)(regionSize / totalLandTiles × 1000), 5)` (`LAND_REGIONS_SCORE_VALUE`, `LAND_REGION_MIN_SCORE`); the denominator is *all* land tiles, polar and mountain included.

**Determinism:** region assignment draws **no randomness** (no RNG parameter at all) — it is a pure function of the finished terrain, mirroring [native land claims](natives.md). It therefore perturbs no RNG stream (ADR-009) and recomputes byte-identically at game start and on load.

**Faithful subset / deviations:** FreeCol grows mountain *ranges* with a generation-time random walk; our altitude is per-tile noise, so we derive mountain regions from the resulting hill/mountain terrain instead (same type and `2 × size` score, different tile source). FreeCol's nine "geographic thirds" virtual bounding boxes (used only to seed native settlement placement) and the RIVER / LAKE / COAST / DESERT region types are **deferred** until rivers and that placement hook exist. Region **discovery** mechanics (discoverable/prediscovered flags, per-player discovery, naming, the explorer's-map UI and the score award) are **P6** — regions carry a `ScoreValue` now but no discovery state.

## 3. Technical design

- `Position` (record struct): adjacency + neighbour enumeration.
- `GameMap`: immutable terrain grid (row-major array), bounds checks throw off-map.
- `MapGenerator.Generate(ruleset, w, h, IGameRandom, landMassFraction = 0.45)`: pure function of its inputs; `landMassFraction` (default `MapGenerator.DefaultLandMassFraction`) is the `targetLand` ceiling in `GrowContinent` (replaces the former `0.45` literal); after building terrain it calls `RegionGenerator.Assign` and `GameMap.SetRegions` (no RNG consumed by that step). `Game.New(ruleset, seed, mapWidth = 36, mapHeight = 24, …, landMassFraction = 0.45)` forwards all three world-shape params.
- **World-size options:** `WorldSizeOptions` (static, GameLogic) holds the immutable `WorldSize(Name, Width, Height)` and `LandMass(Name, Fraction)` preset lists + the default indices; no save change (map dimensions persist since save v2). Presentation: `NewGameDialog` (programmatic overlay, ADR-006) lets the player pick before starting and forwards the choice via `GameController.PendingWorldSize`/`PendingLandMass` (statics that survive the menu→game scene change, like `PendingLoadPath`) into `GameController.StartNewGame(seed, size, landMass)` → `Game.New`. The parameterless `StartNewGame(seed)` (tests/goldens) uses the shipped defaults, so it is unchanged.
- **Regions:** `RegionType` (enum `Ocean`/`Land`/`Mountain`/`River`, serialized by ordinal — `River` reserved), `Region` (record: `Id`, `Type`, `ScoreValue`, `Key?`, `ParentId?` — `Id` equals the index into `GameMap.Regions`; fixed regions carry a `model.region.*` key, dynamic land/mountain regions a null key; ocean quadrants link to their parent ocean via `ParentId`). `GameMap` holds a dense row-major `int[]` region layer (`RegionIdAt` → id or `GameMap.NoRegion`; `RegionOf` → `Region?`; `Regions` table) installed via the internal `SetRegions`; the ctor takes optional `(regionIds, regions)` for save restore, following the resource/rumour sparse-layer convention. `RegionGenerator.Assign(GameMap) → (int[], IReadOnlyList<Region>)` is `static`, RNG-free, deterministic (fixed y-then-x seed scan + `Direction.values()` neighbour order); the eight fixed regions always exist (ids 0 arctic, 1 antarctic, 2 pacific +3/4, 5 atlantic +6/7) for stable ids, then mountain then land regions follow. Flood fill mirrors FreeCol `Map.floodFillBool` (including the bounded carve used to split oversized landmasses).
- Rendering (`MapView`, ADR-014): **isometric diamonds with FreeCol art** — the unchanged square grid projects 45° (`screen = ((x−y)·64, (x+y)·32)`, tiles 128×64, `TileW`/`TileH`). Base diamond per terrain (2 variants picked by position hash — no RNG draws); forest/hills/mountains render as base + overlay (base mapping mirrors the climate pairs); fog uses FreeCol's `unexplored` art; unmapped terrain renders a magenta diamond. Conversions in `TileCentre`/`TileAt` (exact diamond picking via rounding in grid space). Art provenance: `game/assets/freecol/PROVENANCE.md`. Not yet adopted: beach/river transitions, 16-variant forest connection bitmasks, hi-res `.size9` art.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `PositionTests` (adjacency table, 8 neighbours), `GameMapTests` (row-major reads, bounds), `MapGeneratorTests` (seed determinism, border/interior shape, **default land-mass byte-identity, higher land-mass grows more land, non-default size determinism + size-relative margin**); `WorldSizeOptionsTests` (presets within FreeCol bounds, defaults match the shipped world + ordering, default-sized `Game.New` matches the parameterless default); regions: `RegionTests`, `RegionGeneratorTests` | ✅ |
| L2 Scenario | Always | walking-skeleton scenarios traverse generated maps with invariants; `RegionGeneratorTests.GeneratedMap_CoversEveryTile_AndIsRegionStablePerSeed`; `WorldSizeOptionsTests.ANonDefaultSizedGame_RoundTripsThroughSave_WithNoVersionBump` (a 30×20/50% game saves+restores byte-identically, save still v44) | ✅ |
| L3 Interaction | Yes (pan/zoom; new-game options) | camera covered indirectly by main-scene load test (dedicated pan/zoom tests TODO); `MainMenuTests` — the **New Game button opens the world-options dialog**, and the **dialog forwards the chosen size + land mass** | ✅ options / ⚠️ partial camera |
| L4 Visual | Yes (map screen) | `VisualGoldenTests.MapView_SeededWorld_MatchesGolden` — golden `map-seed424242`, **unchanged** (default world byte-identical, no regen) | ✅ |

- **FreeCol cross-check:** n/a for the placeholder generator (will apply to the Phase 2 generator).

## 5. Open issues / TODO

- [x] **World-size & land-mass options** (`86d3c9w9c`): map width/height + land fraction are new-game options (`WorldSizeOptions` presets, `NewGameDialog`); default = the shipped world (byte-identical). Follow-up: FreeCol's `LAND_GENERATOR_TYPE` (continent/archipelago/islands) and persisting the chosen presets per save-variant.
- [ ] Rivers, lakes, multiple landmass styles (FreeCol generator features). Rivers/lakes will add RIVER/LAKE regions (currently deferred) — note: rivers draw RNG *inside* map-gen stream 0, so that slice will deliberately regenerate the map goldens + soak baseline (this options commit does not).
- [x] Geographic regions (polar/ocean/mountain/land) assigned to every tile with FreeCol score values (86d3c9w12).
- [ ] **Region persistence (save v35)** — regions are currently re-derived on every load (deterministic, like native claims); the planned next slice persists them additively so saved region ids stay stable across algorithm changes.
- [ ] **Region discovery (P6)** — discoverable/prediscovered state, per-player discovery, region naming, the score award on discovery, the explorer's-map UI.
- [ ] Native settlement placement consuming region bounds (the nine geographic-thirds regions); currently `NativeSettlementGenerator` uses settlement-number bands.
- [x] L4 map golden — `map-seed424242` (`VisualGoldenTests.MapView_SeededWorld_MatchesGolden`). [ ] L3 camera (pan/zoom) input tests still TODO.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Grid model, placeholder generator, flat-colour rendering | Phase 1 skeleton |
| 2026-06-13 | Climate-band generation from spec `<gen>` data; high-seas edges; fog rendering | Phase 2a |
| 2026-06-13 | Isometric rendering with FreeCol terrain art (ADR-014); temperate-biased start tile | Phase 2c |
| 2026-06-13 | Bonus resources: ~8% of tiles host a resource from the terrain's spec table (weighted), rendered with FreeCol bonus icons, persisted (save v8). Yield effects pending (spec modifiers — with expert/modifier system) | Phase 2/3 |
| 2026-06-13 | Bonus-resource **yield effects** now applied in production (`TileYield`); see [colonies](colonies.md). Expert-scoped bonuses deferred (no per-colonist identity) | Phase 4 slice 8 |
| 2026-06-19 | **Geographic regions**: `RegionGenerator` assigns every tile a region (arctic/antarctic polar bands, Atlantic/Pacific oceans split N/S, mountain blocks, per-landmass land split at 75 tiles) with FreeCol score values; RNG-free, re-derived on load. Discovery + persistence deferred to later slices | P5 (86d3c9w12) |
| 2026-06-20 | **World-size & land-mass options** (`86d3c9w9c`, FreeCol `MapGeneratorOptions`): `MapGenerator.Generate` gains a `landMassFraction` param (replaces the hard-coded `0.45`); `Game.New` forwards `mapWidth`/`mapHeight`/`landMassFraction`; new `WorldSizeOptions` preset registry (Small/Standard/Large/Huge × Sparse/Normal/Dense, FreeCol bounds); `NewGameDialog` overlay (opened by the menu's New Game) lets the player pick, forwarded via `GameController.PendingWorldSize`/`PendingLandMass`. **Default (36×24/45%) byte-identical → no golden regen, no soak shift, no save bump (dims persist since v2).** +8 L1/L2 (`MapGeneratorTests`, `WorldSizeOptionsTests`) + 2 L3 (`MainMenuTests`); 1184 + 5 goldens + soak green | P5 (86d3c9w9c) |
