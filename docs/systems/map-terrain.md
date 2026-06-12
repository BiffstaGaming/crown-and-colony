# System: Map & terrain

| | |
|---|---|
| **Status** | Implemented (skeleton: grid + placeholder generator; real generation is Phase 2) |
| **Last verified** | 2026-06-13 @ Phase 1 walking skeleton |
| **Code** | `game/src/GameLogic/World/` · rendering: `game/presentation/MapView.cs` |
| **Tests** | `game/tests/GameLogic.Tests/World/WorldTests.cs` |
| **FreeCol reference** | map model: `freecol/src/net/sf/freecol/common/model/Map.java`, `Tile.java` |
| **Related systems** | [ruleset-data](ruleset-data.md), [units-movement](units-movement.md) |

## 1. How it works (plain English)

The world is a rectangular grid of square tiles, each with one terrain type from the rules data. For now the map is a placeholder: a 2-tile ocean border around one landmass whose terrain is randomly scattered (but always the same for the same game seed). On screen each terrain is a flat colour — ocean blue, plains tan, forests green, mountains grey.

**What the player sees and does:** the coloured map; pan with middle/right-mouse drag, zoom with the wheel.

## 2. Detailed rules

- Coordinates: (0,0) top-left; X east, Y south. Diagonals count as adjacent (8 neighbours), as in the original game.
- Same seed → identical map, every time, every machine (ADR-009).
- **Climate-band generation (Phase 2a):** a continent grown from seeded blobs (~45% land, watery margins); per-tile climate triple — temperature from latitude (40 °C equator → −20 °C poles, jittered), humidity from smoothed noise (0–100), altitude rolled (lowland 84%, hills 10%, mountains ~4–6%); terrain picked among ruleset types whose `<gen>` envelope contains the triple (forest-vs-clear is a separate ~45% roll; off-envelope triples take the climate-nearest type). Outermost map columns are **high seas** (the future route to Europe).
- Result: hot wet equator → savannah/tropical forest, dry bands → desert/scrub, poles → arctic/tundra/boreal.

**Deviations from original / FreeCol:** uses FreeCol's climate *data* but not its exact algorithm (FreeCol layers landmass styles, rivers, bonus resources, lakes — future work). Square grid topology as in the 1994 original.

## 3. Technical design

- `Position` (record struct): adjacency + neighbour enumeration.
- `GameMap`: immutable terrain grid (row-major array), bounds checks throw off-map.
- `MapGenerator.Generate(ruleset, w, h, IGameRandom)`: pure function of its inputs.
- Rendering (`MapView`): `_Draw`-based flat-colour tiles, 32 px (`MapView.TileSize`); palette keyed by `TerrainType.ShortName`, unknown terrain renders magenta to be unmissable. Map-space↔tile conversions live in `MapView.TileCentre`/`TileAt`.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `PositionTests` (adjacency table, 8 neighbours), `GameMapTests` (row-major reads, bounds), `MapGeneratorTests` (seed determinism, border/interior shape) | ✅ |
| L2 Scenario | Always | walking-skeleton scenarios traverse generated maps with invariants | ✅ |
| L3 Interaction | Yes (pan/zoom) | camera covered indirectly by main-scene load test; dedicated input tests TODO | ⚠️ partial |
| L4 Visual | Yes (map screen) | TODO with the visual harness | ⬜ |

- **FreeCol cross-check:** n/a for the placeholder generator (will apply to the Phase 2 generator).

## 5. Open issues / TODO

- [ ] Rivers, lakes, bonus resources, multiple landmass styles (FreeCol generator features).
- [ ] L3 camera input tests; L4 map golden.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Grid model, placeholder generator, flat-colour rendering | Phase 1 skeleton |
| 2026-06-13 | Climate-band generation from spec `<gen>` data; high-seas edges; fog rendering | Phase 2a |
