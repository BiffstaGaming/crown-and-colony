# System: Units & movement

| | |
|---|---|
| **Status** | Implemented (ruleset unit types, naval units, off-map sailing/Europe, cargo + passengers, role movement bonuses, standing orders: fortify/sentry/clear-orders/disband) |
| **Last verified** | 2026-06-16 @ unit orders (fortify/sentry/clear/disband, save v23, `86d3c9pfh`) |
| **Code** | `game/src/GameLogic/Units/` (`Unit.Orders`/`UnitOrders`), `GameSession/Game.cs` (`InitialMovement`, `Fortify`/`Sentry`/`ClearOrders`/`Disband` + their `Check*`) · rendering: `game/presentation/UnitMarker.cs` |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/GameTests.cs`, `RoleMovementTests.cs`, `MagellanTests.cs`, `UnitOrdersTests.cs` |
| **FreeCol reference** | `freecol/src/net/sf/freecol/common/model/Unit.java` (`getMoveCost`, `MoveType`) |
| **Related systems** | [map-terrain](map-terrain.md), [turns](turns.md) |

## 1. How it works (plain English)

There's one explorer on the map. Click it to select (gold ring), click a neighbouring tile to walk there. Rough ground (forests, hills) uses up the turn faster than open plains; water is off-limits. When the unit can't move any further, end the turn and it's refreshed.

You can also give a unit a **standing order** instead of moving it. **Fortify** a unit and it spends a turn digging in; from then on it defends **half again as hard** (+50%) — invaluable for a soldier guarding a colony or a chokepoint. **Sentry** rests a unit until something happens (so it stops asking for orders). **Clear orders** wakes a fortified or resting unit, and **disband** removes a unit you no longer want for good. Moving a fortified unit wakes it — you trade the defensive bonus for the march.

**Worked example:** the unit has 3 movement points. Stepping onto plains costs 3 — one move per turn. Stepping onto a forest costs 6, which the unit doesn't have… but a unit with any movement left may always make one move, so it steps in and is left with 0.

## 2. Detailed rules

| Condition | Result |
|---|---|
| Target off-map | rejected |
| Target not one of the 8 neighbours | rejected (one step at a time) |
| Land unit → water, or naval unit → land | rejected |
| Target holds an enemy unit (e.g. a native brave) | rejected — *attack* it instead (see [combat](combat.md)) |
| Target holds a native settlement | rejected — attack / trade / speak from beside it |
| Target holds a colony you don't own (a colonial unit with a differing `OwnerId`, or any native brave) | rejected as a *move* — instead assault it from beside it: a colonial land unit can **capture** an ungarrisoned rival colony (`AttackColony`), a brave can **pillage** an undefended one (`PillageColony`) — see [combat](combat.md) |
| 0 movement points left | rejected |
| cost ≤ movement left | allowed; pay the terrain's move cost |
| cost > movement left | allowed **for all remaining points** only if near-full movement (`left+2 ≥ max`, where `max` = the unit's full turn movement *including its role bonus*) or small shortfall (`cost ≤ left+2`) or target is a settlement; otherwise rejected |

- Unit capabilities come from the ruleset (`UnitType`): free colonist 3 MP land, caravel 12 MP naval, etc.
- **Initial (per-turn) movement = unit-type base + role movement bonus** (FreeCol `Unit.getInitialMovesLeft` folding `model.modifier.movementBonus`): a **mounted** role adds **+9** (one "move" is 3 points, so +3 tiles) — dragoon, scout, cavalry, mounted/native-dragoon braves; the **missionary** role adds **+3**; foot roles (soldier/infantry/pioneer) and the unarmed default add nothing. The bonus is applied at each turn's movement reset (`Game.InitialMovement`); a freshly-equipped mount gets the bonus from its next turn (equipping doesn't refund moves mid-turn). **Ferdinand Magellan** adds **+3** to a naval unit's initial movement (`InitialMovement` folds the owner's Congress `model.modifier.movementBonus`, naval-gated = his scope; among fathers only Magellan carries it). *The per-nation-type naval +3 is a separate scoped modifier, still deferred (no classic power uses the naval nation type).*
- **Ownership & equipment** (Phase 5 slice 5b): a unit carries an `OwnerNationId` (null = the human player; a native nation id = a brave) and a military **role** (`RoleId`/`RoleCount` — unarmed by default; soldier/dragoon when equipped). `Game.PlayerUnits`/`NativeUnits` partition the unit list by owner. A unit cannot move onto a tile held by an enemy — combat is a separate action ([combat](combat.md)). Native units never lift the player's fog and can't be selected.
- **Standing orders** (`86d3c9pfh`, FreeCol `Unit.UnitState`): a unit carries an `Orders` state — **Active** (default), **Fortifying**, **Fortified**, or **Sentry**.

  | Order | Check | Effect |
  |---|---|---|
  | **Fortify** (`CheckFortify`/`Fortify`) | on-map land unit, not already (forti)fied | sets *Fortifying* and spends the turn (0 moves); at the next turn reset it ages to *Fortified*, a **+50% defence** bonus in field combat (FreeCol `model.modifier.fortified` — see [combat](combat.md)). Ships can't fortify (they sentry). |
  | **Sentry** (`CheckSentry`/`Sentry`) | any on-map unit | rests the unit (0 moves); a sentry unit is skipped when cycling for orders. |
  | **Clear orders** (`ClearOrders`) | any unit | back to *Active* (no movement refund). |
  | **Disband** (`CheckDisband`/`Disband`) | any unit, except a carrier still holding passengers | removes the unit from the game for good (its hold, if any, is lost). |

  **Moving wakes a unit** — `MoveUnit` resets a fortified/sentry unit to *Active*, so you trade the dig-in bonus for the move (FreeCol clears the state on a move). The order persists across save/load (v23, omitted for an *Active* unit so a no-orders game stays byte-identical).

**Deviations from original / FreeCol:** ✅ **cross-check done (2026-06-13).** The partial-movement rule above is FreeCol's exactly (`Unit.getMoveCost`, Unit.java:2227). Not yet implemented from that method: tile-improvement cost changes (roads/rivers — arrive with improvements) and the settlement-target clause (no settlements yet). For 3-MP units the rule is equivalent to the old skeleton behaviour; it differs for faster units (pinned by test).

## 3. Technical design

- `Unit`: mutable state (Position, MovementLeft), internal setters — only `Game` mutates it.
- `Game.InitialMovement(unit)` = `unit.Type.Movement + (int)role.MovementBonus` (role resolved null-safely from `Ruleset.Roles`, so minimal rulesets without role data just get the base). Used by the per-turn reset in `EndTurn` and as the "full movement" reference in `CheckMove`'s partial-move rule. `Unit` no longer carries a `ResetMovement` (it can't see the ruleset to resolve the role).
- `Game.CheckMove(unit, target) → MoveCheck{Allowed, Cost, Reason}`: the single legality oracle; UI calls it before attempting. `Game.MoveUnit` throws `InvalidMoveException` if used without checking.
- Selection is presentation state (`GameController._selectedUnit`), not game state.
- `UnitMarker`: `_Draw` disc + selection ring; positioned via `MapView.TileCentre`.
- **Orders** (`Unit.Orders`, a `UnitOrders` enum): `Game.Fortify`/`Sentry`/`ClearOrders`/`Disband` follow the `CheckX`/`X` + `MoveCheck`/`InvalidMoveException` pattern. The per-turn reset in `EndTurn` ages `Fortifying → Fortified` before resetting movement; `MoveUnit` resets `Orders` to `Active`. Combat reads `Unit.IsFortified` into `DefenceContext.Fortified` in the field-`Attack` path (see [combat](combat.md) §3). Save: `SavedUnit.Orders` (v23) is nullable and omitted for an *Active* unit, so an all-active game serializes byte-identically to v22 and a pre-v23 save loads every unit *Active*.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `GameTests`: legal move spends points; rejects non-adjacent/off-map/water/exhausted; spawn validation. `RoleMovementTests`: dragoon/scout reset to base + 9, foot/default unchanged. `UnitOrdersTests`: fortify → fortifying (0 moves) → fortified on the next turn; moving wakes it; ships can't fortify; **a fortified defender repels an attack an active one loses** (combat wiring, computed flip draw); sentry rests + clear-orders/disband; disband refused for a carrier with passengers; order state round-trips (v23) + byte-identical when all-active + pre-v23 loads Active | ✅ |
| L2 Scenario | Always | 10-turn wander with per-move invariants; deterministic twin games | ✅ |
| L3 Interaction | Yes | `InputTests`: click-select + click-to-move (simulated mouse, camera-aware); marker placement | ✅ |
| L4 Visual | Yes | TODO with visual harness | ⬜ |

- **FreeCol cross-check:** ✅ partial-movement rule matches `Unit.getMoveCost`; rejection branch pinned with a synthetic 12-MP unit (`PartialMovement_BigShortfall_MidTurn_Rejected`); small-shortfall branch pinned with a caravel.

## 5. Open issues / TODO

- [x] Role movement bonuses (dragoon/scout/cavalry +9, pioneer +3) applied at the per-turn reset (FP-5, `86d3bbvv6`).
- [x] Standing orders — fortify (+50% defence), sentry, clear-orders, disband (`86d3c9pfh`, save v23). Presentation hotkeys/buttons (F = fortify, etc.) + skipping sentry units when cycling are the **follow-up presentation slice** (the GameLogic + combat wiring + save are done).
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
| 2026-06-16 | Standing orders (`Unit.Orders`/`UnitOrders`): fortify (Fortifying → Fortified at the next reset = +50% field defence, wired into `DefenceContext.Fortified`), sentry, clear-orders, disband (carrier-with-passengers guarded); moving wakes a unit; save **v23** (omitted when Active). +12 L1/L2 (`UnitOrdersTests`) | Phase 5 (`86d3c9pfh`) |
