# System: Rivers & Tile Improvements

| | |
|---|---|
| **Status** | In development — **foundation slice only**: the `TileImprovementType` data model (river, classic `model.improvement.river`) plus the pure production-delta and river-movement rule functions, fully L1-tested. **No map placement, no save persistence, and no `Game` yield/movement wiring yet** — those are explicit follow-up slices (see §5). |
| **Last verified** | 2026-06-20 @ rivers/tile-improvements data-model foundation (`86d3b3qdx`) |
| **Code** | `game/src/GameLogic/World/Improvements/TileImprovementType.cs` (`TileImprovementType`, `ImprovementModifier`), `World/Improvements/ImprovementProduction.cs` (`YieldDelta`), `World/Improvements/ImprovementMovement.cs` (`RiverMoveCost`, `ReducedCost`) |
| **Tests** | `game/tests/GameLogic.Tests/World/TileImprovementTests.cs` |
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

**What the player sees and does:** rivers are drawn on the map; the player doesn't build them (they're natural). They simply benefit from founding colonies on river tiles and from routing units along rivers. *(Placement on the map and the in-game movement/yield effects are not wired up yet — this slice only builds the underlying rules; see §5.)*

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
- **Directional river connectivity not yet modelled.** FreeCol additionally checks the river's per-tile *style* so the move bonus only applies along a river segment that actually connects in the direction of travel. This foundation slice models the simpler, faithful-in-spirit rule "both endpoints carry a river". The per-direction style check is deferred to the placement slice (it needs the per-tile river style, which only exists once rivers are generated). No other deviations.
- **River only.** Only the river improvement type is modelled here. Other FreeCol improvement types (road, plowed, cleared, fish bonus) are out of scope for this foundation.

## 3. Technical design

**Domain model** (all in `game/src/GameLogic/World/Improvements/`, engine-free, ADR-006):
- `TileImprovementType` — immutable rule data for an improvement type (id, `Magnitude`, `MovementCost`, `AddWorkTurns`, `Modifiers`). Factory helpers: `FromModifiers(...)` (build from `(goodsId, delta)` pairs, additive by default) and `ClassicRiver(magnitude = 1)` (the canonical classic river instance, values verbatim from the spec). `RiverId` constant, `ShortName`, `GrantsMovementBonus`.
- `ImprovementModifier` — one production bonus (`GoodsId`, `Type`, `Value`), mirroring `ResourceModifier`/`FatherModifier`; reuses the shared `ModifierType` + `ModifierMath.Apply` (`Specification/FoundingFather.cs`). Scalar value-record, so modifier lists compare by sequence equality.
- `ImprovementProduction` — pure `YieldDelta(improvement, goodsId)` (and an `IEnumerable` overload for a tile carrying several improvements). Sums the matching modifiers applied to a zero base: additive modifiers contribute their flat value; a percentage modifier contributes nothing on its own (it scales an existing yield, folded in by the caller during the deferred wiring).
- `ImprovementMovement` — pure movement rules. `ReducedCost(improvementCost, baseCost)` is FreeCol's `TileImprovementType.getMoveCost(originalCost)` verbatim (replace base only when `0 < improvementCost < baseCost`; never returns zero). `RiverMoveCost(from, to, baseCost)` applies the "follow the river" bonus: reduced only when both the from-tile and to-tile carry a river that grants a bonus.

**Data sources:** FreeCol `data/rules/classic/specification.xml` → `<tile-improvement-type id="model.improvement.river" magnitude="1" movement-cost="1" add-work-turns="0">` with eight additive `<modifier>` children. Values are hard-coded into `ClassicRiver()` for this slice; the `Ruleset.cs` parser is **not** wired up here (deferred — see §5).

**Algorithms & formulas:**
- Production delta: `YieldDelta = Σ over modifiers with matching goodsId of ModifierMath.Apply(type, 0, value)` (`ImprovementProduction.YieldDelta`).
- Movement: `RiverMoveCost = (from has river ∧ to has river-with-bonus) ? min-rule(to.MovementCost, baseCost) : baseCost`, where `min-rule` = `ReducedCost` (`ImprovementMovement`).

**Integration points:** **none yet, by design.** These are standalone pure rules. The future `Game.TileYield` call will add `ImprovementProduction.YieldDelta` in the correct FreeCol modifier order (after terrain base + resource bonus, alongside unit/father modifiers); the future `Game`/`Pathfinder` movement will call `ImprovementMovement.RiverMoveCost` per step. See §5.

**Persistence:** **none yet.** No save field, no save-version bump. Per-tile river state and its persistence are a deferred follow-up (see §5).

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `game/tests/GameLogic.Tests/World/TileImprovementTests.cs` (model attributes vs. spec, all eight goods deltas, movement rule incl. both-endpoints / never-free / never-dearer edges) | ✅ |
| L2 Scenario | When wired | Deferred with the `Game` yield/movement wiring slice | ⬜ |
| L3 Interaction | If UI | n/a (no UI in this slice) | — |
| L4 Visual | If a screen | Deferred with the river map-render/placement slice | ⬜ |
| L5 Soak | Covered by global suite | — | — |

- **FreeCol cross-check:** the model attributes (magnitude 1, movement-cost 1, add-work-turns 0) and all eight `<modifier>` goods deltas are asserted verbatim against `data/rules/classic/specification.xml`; `ReducedCost` mirrors `TileImprovementType.getMoveCost(int)` including the "never return zero" guard. The river move bonus "applies only river-to-river" matches FreeCol's documented behaviour. The per-direction style connectivity check is the one acknowledged simplification (see §2 deviations), deferred to placement.

## 5. Open issues / TODO — deferred follow-up slices (next wave)

This slice is the **model foundation**. The following are explicit, separately-owned follow-ups (each will own the shared file it must edit, to keep waves collision-free):

- [ ] **Map placement** (owns `MapGenerator.cs`): generate rivers during map-gen via the seeded RNG (ADR-009), assign per-tile river presence + style + magnitude, and regenerate the affected map-gen golden. Adds the per-tile river *style* needed for the directional connectivity check.
- [ ] **Persistence** (owns `SaveGame.cs`): add a per-tile river/improvement field to the save format and bump the save version; round-trip test.
- [ ] **`Game` yield wiring** (owns `Game.cs`): fold `ImprovementProduction.YieldDelta` into `Game.TileYield` in the correct FreeCol modifier order; L2 scenario cross-check.
- [ ] **`Game`/`Pathfinder` movement wiring** (owns `Game.cs` / `Pathfinder.cs`): apply `ImprovementMovement.RiverMoveCost` per movement step, including the per-direction river-style connection check; L2 scenario cross-check.
- [ ] **Ruleset parsing** (owns `Ruleset.cs`): parse `<tile-improvement-type>` from `specification.xml` instead of the hard-coded `ClassicRiver()` factory.
- [ ] **Other improvement types**: road, plowed, cleared, fish-bonus — and pioneer building of improvements (`AddWorkTurns` is already carried).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-20 | Initial documentation — river/tile-improvement **data-model foundation**: `TileImprovementType`/`ImprovementModifier` model, pure `ImprovementProduction.YieldDelta` and `ImprovementMovement.RiverMoveCost`/`ReducedCost` rules, L1 tests. No placement/persistence/wiring (deferred follow-ups in §5). | `86d3b3qdx` |
