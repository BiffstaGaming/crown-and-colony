# System: Units & movement

| | |
|---|---|
| **Status** | Implemented (ruleset unit types, naval units, off-map sailing/Europe, cargo + passengers, role movement bonuses) |
| **Last verified** | 2026-06-14 @ FP-5 (role movement bonuses) |
| **Code** | `game/src/GameLogic/Units/`, `GameSession/Game.cs` (`InitialMovement`) · rendering: `game/presentation/UnitMarker.cs` |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/GameTests.cs`, `RoleMovementTests.cs` |
| **FreeCol reference** | `freecol/src/net/sf/freecol/common/model/Unit.java` (`getMoveCost`, `MoveType`) |
| **Related systems** | [map-terrain](map-terrain.md), [turns](turns.md) |

## 1. How it works (plain English)

There's one explorer on the map. Click it to select (gold ring), click a neighbouring tile to walk there. Rough ground (forests, hills) uses up the turn faster than open plains; water is off-limits. When the unit can't move any further, end the turn and it's refreshed.

**Worked example:** the unit has 3 movement points. Stepping onto plains costs 3 — one move per turn. Stepping onto a forest costs 6, which the unit doesn't have… but a unit with any movement left may always make one move, so it steps in and is left with 0.

## 2. Detailed rules

| Condition | Result |
|---|---|
| Target off-map | rejected |
| Target not one of the 8 neighbours | rejected (one step at a time) |
| Land unit → water, or naval unit → land | rejected |
| Target holds an enemy unit (e.g. a native brave) | rejected — *attack* it instead (see [combat](combat.md)) |
| Target holds a native settlement | rejected — attack / trade / speak from beside it |
| Target holds a colony you don't own (a colonial unit with a differing `OwnerId`, or any native brave) | rejected — attack it from beside it (1c-1; capturing a rival colony is a later slice) |
| 0 movement points left | rejected |
| cost ≤ movement left | allowed; pay the terrain's move cost |
| cost > movement left | allowed **for all remaining points** only if near-full movement (`left+2 ≥ max`, where `max` = the unit's full turn movement *including its role bonus*) or small shortfall (`cost ≤ left+2`) or target is a settlement; otherwise rejected |

- Unit capabilities come from the ruleset (`UnitType`): free colonist 3 MP land, caravel 12 MP naval, etc.
- **Initial (per-turn) movement = unit-type base + role movement bonus** (FreeCol `Unit.getInitialMovesLeft` folding `model.modifier.movementBonus`): a **mounted** role adds **+9** (one "move" is 3 points, so +3 tiles) — dragoon, scout, cavalry, mounted/native-dragoon braves; the **missionary** role adds **+3**; foot roles (soldier/infantry/pioneer) and the unarmed default add nothing. The bonus is applied at each turn's movement reset (`Game.InitialMovement`); a freshly-equipped mount gets the bonus from its next turn (equipping doesn't refund moves mid-turn). **Ferdinand Magellan** adds **+3** to a naval unit's initial movement (`InitialMovement` folds the owner's Congress `model.modifier.movementBonus`, naval-gated = his scope; among fathers only Magellan carries it). *The per-nation-type naval +3 is a separate scoped modifier, still deferred (no classic power uses the naval nation type).*
- **Ownership & equipment** (Phase 5 slice 5b): a unit carries an `OwnerNationId` (null = the human player; a native nation id = a brave) and a military **role** (`RoleId`/`RoleCount` — unarmed by default; soldier/dragoon when equipped). `Game.PlayerUnits`/`NativeUnits` partition the unit list by owner. A unit cannot move onto a tile held by an enemy — combat is a separate action ([combat](combat.md)). Native units never lift the player's fog and can't be selected.

**Deviations from original / FreeCol:** ✅ **cross-check done (2026-06-13).** The partial-movement rule above is FreeCol's exactly (`Unit.getMoveCost`, Unit.java:2227). Not yet implemented from that method: tile-improvement cost changes (roads/rivers — arrive with improvements) and the settlement-target clause (no settlements yet). For 3-MP units the rule is equivalent to the old skeleton behaviour; it differs for faster units (pinned by test).

## 3. Technical design

- `Unit`: mutable state (Position, MovementLeft), internal setters — only `Game` mutates it.
- `Game.InitialMovement(unit)` = `unit.Type.Movement + (int)role.MovementBonus` (role resolved null-safely from `Ruleset.Roles`, so minimal rulesets without role data just get the base). Used by the per-turn reset in `EndTurn` and as the "full movement" reference in `CheckMove`'s partial-move rule. `Unit` no longer carries a `ResetMovement` (it can't see the ruleset to resolve the role).
- `Game.CheckMove(unit, target) → MoveCheck{Allowed, Cost, Reason}`: the single legality oracle; UI calls it before attempting. `Game.MoveUnit` throws `InvalidMoveException` if used without checking.
- Selection is presentation state (`GameController._selectedUnit`), not game state.
- `UnitMarker`: `_Draw` disc + selection ring; positioned via `MapView.TileCentre`.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `GameTests`: legal move spends points; rejects non-adjacent/off-map/water/exhausted; spawn validation. `RoleMovementTests`: dragoon/scout reset to base + 9, foot/default unchanged | ✅ |
| L2 Scenario | Always | 10-turn wander with per-move invariants; deterministic twin games | ✅ |
| L3 Interaction | Yes | `InputTests`: click-select + click-to-move (simulated mouse, camera-aware); marker placement | ✅ |
| L4 Visual | Yes | TODO with visual harness | ⬜ |

- **FreeCol cross-check:** ✅ partial-movement rule matches `Unit.getMoveCost`; rejection branch pinned with a synthetic 12-MP unit (`PartialMovement_BigShortfall_MidTurn_Rejected`); small-shortfall branch pinned with a caravel.

## 5. Open issues / TODO

- [x] Role movement bonuses (dragoon/scout/cavalry +9, pioneer +3) applied at the per-turn reset (FP-5, `86d3bbvv6`).
- [ ] Nation-type (naval +3) and Magellan (+3) movement bonuses — scoped `movementBonus` modifiers, deferred with scope evaluation / founding-father effects.
- [ ] Tile-improvement movement costs (roads/rivers) with the improvements system.
- [ ] Settlement-target movement clause when colonies exist.
- [ ] Multiple player units / unit cycling UI.
- [ ] L3 click-to-move test; L4 golden with unit + selection ring.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Skeleton unit, 8-way single-step movement, selection UI | Phase 1 skeleton |
| 2026-06-13 | Unit types from ruleset; naval movement; real FreeCol partial-movement rule (cross-check resolved) | Phase 2a |
| 2026-06-13 | FreeCol unit sprites by type short-name (`assets/freecol/units/`), red-disc fallback, iso ground-ellipse selection | Phase 2c |
| 2026-06-13 | Off-map units (sailing/Europe) can't be moved on the map; units gain a cargo hold — see [europe](europe.md) | Phase 4 |
| 2026-06-14 | Unit ownership (`OwnerNationId`, `PlayerUnits`/`NativeUnits`) + military roles (`RoleId`/`RoleCount`); can't move onto an enemy tile (attack instead); native units fog-excluded — see [combat](combat.md) | Phase 5 slice 5b |
| 2026-06-15 | `CheckMove` blocks moving onto a colony you don't own (`ColonyAt(target).OwnerId != unit.OwnerId`) — closes the foreign-colony walk-in; same-owner garrison/join moves unchanged (slice 1c-1) | Phase 5 slice 1c-1 |
| 2026-06-15 | Ferdinand Magellan: `InitialMovement` folds the owner's Congress `movementBonus` for naval units (+3); see [founding-fathers](founding-fathers.md) | Phase 5 (#3 fathers) |
| 2026-06-14 | Role movement bonuses applied: per-turn movement = unit-type base + role `movementBonus` (mounted +9, missionary +3) via `Game.InitialMovement`; partial-move "near full" now measured against the boosted max; `Unit.ResetMovement` removed | FP-5 (`86d3bbvv6`) |
