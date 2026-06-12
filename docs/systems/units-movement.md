# System: Units & movement

| | |
|---|---|
| **Status** | Implemented (skeleton: one generic land unit) |
| **Last verified** | 2026-06-13 @ Phase 1 walking skeleton |
| **Code** | `game/src/GameLogic/Units/`, `GameSession/Game.cs` · rendering: `game/presentation/UnitMarker.cs` |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/GameTests.cs` |
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
| Target is water | rejected (land unit) |
| 0 movement points left | rejected |
| ≥1 movement point left | allowed; cost = target terrain's move cost, clamped at 0 remaining |

- Skeleton unit: 3 movement points/turn (= free colonist's `movement="3"` in the spec).

**Deviations from original / FreeCol — PENDING CROSS-CHECK:** the "any remaining movement allows one move, overdraw clamps to 0" rule is our simplification. FreeCol's exact partial-movement rules (and the original's) must be cross-checked when unit types land in Phase 2; this is the system's top verification debt.

## 3. Technical design

- `Unit`: mutable state (Position, MovementLeft), internal setters — only `Game` mutates it.
- `Game.CheckMove(unit, target) → MoveCheck{Allowed, Cost, Reason}`: the single legality oracle; UI calls it before attempting. `Game.MoveUnit` throws `InvalidMoveException` if used without checking.
- Selection is presentation state (`GameController._selectedUnit`), not game state.
- `UnitMarker`: `_Draw` disc + selection ring; positioned via `MapView.TileCentre`.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `GameTests`: legal move spends points; rejects non-adjacent/off-map/water/exhausted; spawn validation | ✅ |
| L2 Scenario | Always | 10-turn wander with per-move invariants; deterministic twin games | ✅ |
| L3 Interaction | Yes | unit marker tile-centre test; click-to-move simulation TODO | ⚠️ partial |
| L4 Visual | Yes | TODO with visual harness | ⬜ |

- **FreeCol cross-check:** ❌ not yet — see deviation note above.

## 5. Open issues / TODO

- [ ] Cross-check partial-movement rule against FreeCol (`Unit.getMoveCost`) and adopt/document the real rule.
- [ ] Unit types from ruleset (movement, abilities); naval units; multiple units.
- [ ] L3 click-to-move test; L4 golden with unit + selection ring.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Skeleton unit, 8-way single-step movement, selection UI | Phase 1 skeleton |
