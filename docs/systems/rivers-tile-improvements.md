# System: Rivers & Tile Improvements

| | |
|---|---|
| **Status** | Implemented — rivers are **parsed from the ruleset, placed on the map at game start, persisted (save v47), and wired into both tile yield and unit movement**. (Other improvement types — road/plow/clear/fish-bonus — and pioneer building are still future; large-river *style*/connectivity is a faithful-subset simplification — see §2/§5.) |
| **Last verified** | 2026-06-20 @ river placement + ruleset parse + v47 persistence + yield/movement wiring (`86d3b3qdx`) |
| **Code** | `game/src/GameLogic/World/Improvements/TileImprovementType.cs` (`TileImprovementType`, `ImprovementModifier`), `World/Improvements/ImprovementProduction.cs` (`YieldDelta`), `World/Improvements/ImprovementMovement.cs` (`RiverMoveCost`, `ReducedCost`); placement: `World/MapGenerator.cs` (`MakeRivers`); layer: `World/GameMap.cs` (`ImprovementAt`/`HasRiver`/`Improvements`/`SetImprovement`); parse: `Specification/Ruleset.cs` (`Improvement`/`RiverType`/`ImprovementTypes`); wiring: `GameSession/Game.cs` (`TileYieldPotential`, `CheckMove`); save: `Persistence/SaveGame.cs` (`SavedImprovement`) |
| **Tests** | `game/tests/GameLogic.Tests/World/TileImprovementTests.cs` (model rules), `World/WorldTests.cs` → `RiverTests` (parse, placement determinism/constraints, yield + movement folding, v47 round-trip) |
| **FreeCol reference** | `common/model/TileImprovementType.java` (`getMoveCost`, magnitude, `<modifier>` children), `model.improvement.river` in `data/rules/classic/specification.xml`; the "follow the river" move bonus (`Map`/`Tile` cost logic) |
| **Related systems** | [map-terrain](map-terrain.md), [units-movement](units-movement.md), [colonies](colonies.md), [market](market.md), [save-load](save-load.md) |

## 1. How it works (plain English)

A **tile improvement** is something laid on a map tile that changes what it's worth — most importantly a **river**. Rivers do two helpful things:

- **They speed up travel.** Move a unit *along* a river — stepping from one river tile to the next — and that step is cheap: it costs only a third of a normal move, so a unit can cover a lot of ground following a waterway. (The bonus only applies when you go from one river tile to another river tile; stepping off the river, or onto a river from dry land, costs the normal amount.)
- **They make the land richer.** A tile with a river on it produces *more* of several goods when a colonist works it — extra furs and lumber especially, plus a little more grain, sugar, tobacco, cotton, ore and silver. That's why colonies cluster along rivers.

**The rules, in plain words (river, classic ruleset):**
- A river gives **+2 furs, +2 lumber**, and **+1 each** to grain, sugar, tobacco, cotton, ore and silver, on top of whatever the tile already produces.
- Moving from a river tile to an adjacent river tile costs **1 movement point** (a third of a normal 3-point move). Any move that isn't river-to-river costs the normal amount.
- Rivers come in two sizes (small and large) for looks; both give the same bonuses.

**Worked example:**
> A free colonist working a grassland tile with a river produces its normal grain plus **1 extra grain**, and if it's a forest tile the river adds **2 lumber** and **2 furs**. A scout following a river south hops tile-to-tile for 1 point each instead of 3, so with a typical move allowance it can travel three river tiles in the space of one normal overland move.

**What the player sees and does:** rivers are placed by the map generator at game start (springs on high inland ground flowing down to the sea), the player doesn't build them (they're natural). They benefit from founding colonies on river tiles (richer yields) and from routing units along rivers (cheap movement). *(The river yield bonus and the river follow-cost are now live; the map still renders the underlying terrain without a river overlay — drawing the river itself is a presentation follow-up. Large-river "style"/directional connectivity is simplified — see §2/§5.)*

## 2. Detailed rules

Faithful to FreeCol's classic `model.improvement.river`.

**Production bonuses (additive, applied to the tile's worked yield):**

| Goods | River delta |
|---|---|
| `model.goods.grain` | +1 |
| `model.goods.sugar` | +1 |
| `model.goods.tobacco` | +1 |
| `model.goods.cotton` | +1 |
| `model.goods.furs` | +2 |
| `model.goods.lumber` | +2 |
| `model.goods.ore` | +1 |
| `model.goods.silver` | +1 |
| any other goods | 0 |

**Movement:**

| Condition | Cost to enter the destination tile |
|---|---|
| Both the tile left **and** the tile entered have a river | river cost (**1**), if it is strictly cheaper than the terrain's normal cost |
| Either tile lacks a river | the destination terrain's normal cost (no bonus) |
| River cost ≥ terrain cost | the terrain's normal cost (the bonus never makes a move *dearer*) |
| River cost ≤ 0 | the terrain's normal cost (a move is **never free** — FreeCol's explicit guard) |

Costs are in FreeCol movement units (3 = one normal move), the same scale as `TerrainType.MoveCost`.

**Magnitude / style:** the river type carries a `Magnitude` band — 1 = small (minor) river, 2 = large (major) river. This is FreeCol styling only; the flat production bonuses and the movement bonus are identical at either magnitude.

**Deviations from original 1994 / FreeCol behavior:**
- **Directional river connectivity / river style not modelled.** FreeCol stores each river tile's *style* (which of its edges the river runs along) and checks it so the move bonus only applies along a segment that actually connects in the direction of travel. We store river *presence* per tile, not style, so the move bonus applies whenever **both endpoints carry a river** — the faithful-in-spirit rule. Consequence: the river-mouth fish bonus (`fishBonusRiver`, +1 fish) is still skipped (it needs the mouth's style), and large rivers are not distinguished in movement/yield (they confer the same flat bonuses anyway).
- **Single-magnitude placement.** Rivers are stamped at the small magnitude (1). FreeCol grows river *section sizes* (small → large → fjord), joins tributaries into one river, and branches deltas at the mouth. Our faithful subset lays a single river presence on each tile a walk crosses (no section growth, joining, or deltas). The save format already carries per-tile magnitude, so large rivers are a forward-compatible data change.
- **River only.** Only the river improvement type is placed/wired. Other FreeCol improvement types (road, plowed, cleared, fish bonus) are parsed-through (`Ruleset.ImprovementTypes`) but not placed or wired, and pioneer building is future.

## 3. Technical design

**Domain model** (all in `game/src/GameLogic/World/Improvements/`, engine-free, ADR-006):
- `TileImprovementType` — immutable rule data for an improvement type (id, `Magnitude`, `MovementCost`, `AddWorkTurns`, `Modifiers`). Factory helpers: `FromModifiers(...)` (build from `(goodsId, delta)` pairs, additive by default) and `ClassicRiver(magnitude = 1)` (the canonical classic river instance, values verbatim from the spec). `RiverId` constant, `ShortName`, `GrantsMovementBonus`.
- `ImprovementModifier` — one production bonus (`GoodsId`, `Type`, `Value`), mirroring `ResourceModifier`/`FatherModifier`; reuses the shared `ModifierType` + `ModifierMath.Apply` (`Specification/FoundingFather.cs`). Scalar value-record, so modifier lists compare by sequence equality.
- `ImprovementProduction` — pure `YieldDelta(improvement, goodsId)` (and an `IEnumerable` overload for a tile carrying several improvements). Sums the matching modifiers applied to a zero base: additive modifiers contribute their flat value; a percentage modifier contributes nothing on its own (it scales an existing yield, folded in by the caller during the deferred wiring).
- `ImprovementMovement` — pure movement rules. `ReducedCost(improvementCost, baseCost)` is FreeCol's `TileImprovementType.getMoveCost(originalCost)` verbatim (replace base only when `0 < improvementCost < baseCost`; never returns zero). `RiverMoveCost(from, to, baseCost)` applies the "follow the river" bonus: reduced only when both the from-tile and to-tile carry a river that grants a bonus.

**Data sources:** FreeCol `data/rules/classic/specification.xml` → `<tile-improvement-type id="model.improvement.river" magnitude="1" movement-cost="1" add-work-turns="0">` with eight additive `<modifier>` children. **`Ruleset.cs` now parses `<tile-improvement-types>`** (the `ParseImprovementModifier` helper reads each `<modifier>`'s `id`/`type`/`value`), exposing `Ruleset.ImprovementTypes`, `Ruleset.Improvement(id)` and `Ruleset.RiverType`. The hard-coded `ClassicRiver()` factory remains only as a test convenience — generation and load both go through the parsed type.

**Placement** (`MapGenerator.MakeRivers`, a faithful subset of FreeCol `TerrainGenerator.createRivers` + `River.flowFromSource`/`flow`):
- A **river-allowed** tile is lowland land — not water, not hills/mountains, not arctic (the river type's negated scopes).
- A **spring** ("good river tile", FreeCol `Tile.isGoodRiverTile`) is a river-allowed tile whose entire 8-neighbourhood is land, so a source never starts on the coast.
- From the seed-shuffled springs, each not already a river starts a **walk**: pick a random direction, lay the river on the current tile, then step (50% straight, 25% left, 25% right — FreeCol's `DirectionChange`, nudged every other step) to the next tile; the walk ends at water (the river mouth), an existing river, the map edge, a non-allowed tile, or the per-river length cap (`MaxRiverLength = 16`).
- The pass stops once laid river tiles reach the **budget** = `allowedTileCount · RiverNumber / 100` (`RiverNumber = 15`, FreeCol's classic `model.option.riverNumber` default — hard-coded from the spec for now, like `MountainNumber`).
- Runs after terrain/mountains/high-seas and **before** bonus resources, drawing from the **map-gen RNG (stream 0)** — so it reorders the stream-0 draw sequence (a deliberate map-gen change; the L4 goldens were regenerated). Deterministic per seed (ADR-009).

**Algorithms & formulas:**
- Production delta: `YieldDelta = Σ over modifiers with matching goodsId of ModifierMath.Apply(type, 0, value)` (`ImprovementProduction.YieldDelta`).
- Movement: `RiverMoveCost = (from has river ∧ to has river-with-bonus) ? min-rule(to.MovementCost, baseCost) : baseCost`, where `min-rule` = `ReducedCost` (`ImprovementMovement`).

**Integration points (now wired):**
- **Yield** — `Game.TileYieldPotential` adds `ImprovementProduction.YieldDelta(Map.ImprovementAt(tile), goodsId)` **after** the index-10 resource modifiers (the river `<modifier>`s are additive at index 50), before the coastal-fish bonus. So a river's flat delta lands on top of any resource scaling, matching FreeCol's modifier order.
- **Movement** — `Game.CheckMove` computes `cost = ImprovementMovement.RiverMoveCost(Map.ImprovementAt(unit.Position), Map.ImprovementAt(target), terrain.MoveCost)` for **land units only** (ships never get the river bonus), before the partial-movement rule. River-to-river land steps pay the reduced cost (1).

**Persistence (save v47):** `GameMap` carries a sparse `Position → TileImprovementType` layer (`ImprovementAt`/`HasRiver`/`Improvements`/`SetImprovement`, ctor-restored like the resource/region layers). `SaveGame` stores it as `SavedImprovement(index, improvementId, magnitude)` under `Improvements` (v47, additive, **omitted when the map carries none** so a riverless fixture stays byte-identical to v46). On load the ruleset re-supplies the improvement type's rule data (modifiers, movement cost) from the id and the saved magnitude is re-stamped (`ruleset.Improvement(id) with { Magnitude = … }`). A pre-v47 save loads with no rivers.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `World/TileImprovementTests.cs` (model attributes vs. spec, all eight goods deltas, movement rule incl. both-endpoints / never-free / never-dearer edges); `World/WorldTests.cs` → `RiverTests` (ruleset parse of the river type + its modifiers/move-cost; default map places rivers; placement deterministic per seed; every river tile is lowland land within the soft budget; yield folding adds +1 grain on a river tile; the river follow-cost is 1 between two river tiles and absent with only one; v47 save round-trip, omit-when-empty, pre-v47 loads riverless) | ✅ |
| L2 Scenario | When wired | Soak suite exercises river placement + movement/yield every default game (determinism + byte-identical round-trip incl. the river layer) | ✅ |
| L3 Interaction | If UI | n/a (no river-specific UI; map render is a presentation follow-up) | — |
| L4 Visual | If a screen | All 6 map-view goldens (`map`/`colony`/`native-settlement`/`remembered-fog`/`rendered-units`/`rumour-marker` @ seed 424242) regenerated for the stream-0 reorder + eyeballed (terrain/units/colony sane; no river overlay yet) | ✅ |
| L5 Soak | Covered by global suite | `SoakTests` — green (rivers round-trip + game stays deterministic/stable) | ✅ |

- **FreeCol cross-check:** the model attributes (magnitude 1, movement-cost 1, add-work-turns 0) and all eight `<modifier>` goods deltas are asserted verbatim against `data/rules/classic/specification.xml` (now via the real `Ruleset` parse, not just the factory); `ReducedCost` mirrors `TileImprovementType.getMoveCost(int)` including the "never return zero" guard. Placement mirrors `createRivers` (spring selection = `isGoodRiverTile`, soft budget = allowed·riverNumber%, walk to the sea). The per-direction style connectivity check + section-size growth are the acknowledged simplifications (see §2 deviations).

## 5. Open issues / TODO

Landed: ruleset parse, map placement, persistence (v47), and both yield + movement wiring. Remaining follow-ups:

- [x] **Map placement** (`MapGenerator.MakeRivers`): rivers stamped during map-gen via the seeded RNG (ADR-009); per-tile river presence + magnitude; map-gen goldens regenerated.
- [x] **Persistence** (`SaveGame`): per-tile improvement field (`Improvements`/`SavedImprovement`), save v47, round-trip test.
- [x] **`Game` yield wiring**: `ImprovementProduction.YieldDelta` folded into `TileYieldPotential` in the correct FreeCol modifier order.
- [x] **`Game` movement wiring**: `ImprovementMovement.RiverMoveCost` applied per step in `CheckMove` (land units).
- [x] **Ruleset parsing**: `<tile-improvement-type>` parsed in `Ruleset.cs`; `RiverType`/`Improvement(id)`/`ImprovementTypes` accessors.
- [ ] **Per-tile river *style* / directional connectivity** + **section-size growth** (small→large→fjord), tributary joining, mouth deltas — and the river-mouth fish bonus (`fishBonusRiver`) that depends on the mouth style.
- [ ] **River map rendering** (presentation): draw the river overlay on the isometric map (the layer + data exist; MapView doesn't draw it yet).
- [ ] **`Pathfinder` movement**: apply the river follow-cost in multi-tile goto pathing too (today the per-step `CheckMove` honours it).
- [ ] **Other improvement types**: road, plowed, cleared, fish-bonus — and pioneer building of improvements (`AddWorkTurns` is already carried).
- [ ] **Read `riverNumber` from the spec map-gen options** instead of the hard-coded `RiverNumber = 15` (with `mountainNumber`/`bonusNumber`).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-20 | **River placement + ruleset parse + persistence (v47) + yield/movement wiring** (`86d3b3qdx`, FreeCol `TerrainGenerator.createRivers`/`River`): `Ruleset` now parses `<tile-improvement-types>` (`RiverType`/`Improvement`/`ImprovementTypes`); `MapGenerator.MakeRivers` stamps rivers at game start (springs on inland high ground walking to the sea, soft budget = allowed·15%); `GameMap` carries a sparse improvement layer; `Game.TileYieldPotential` folds the river yield delta (after resource modifiers) and `Game.CheckMove` applies the river follow-cost for land units; save v47 (`Improvements`, additive, omitted when none). **Draws map-gen RNG → deliberate stream-0 reorder:** all 6 map-view L4 goldens regenerated + eyeballed; 21 version-guard tests retargeted 46→47. +10 L1 (`RiverTests`); 1322 L1/L2 + 4 soak green. See [save-load](save-load.md), [map-terrain](map-terrain.md) | `86d3b3qdx` |
| 2026-06-20 | Initial documentation — river/tile-improvement **data-model foundation**: `TileImprovementType`/`ImprovementModifier` model, pure `ImprovementProduction.YieldDelta` and `ImprovementMovement.RiverMoveCost`/`ReducedCost` rules, L1 tests. No placement/persistence/wiring (deferred follow-ups in §5). | `86d3b3qdx` |
