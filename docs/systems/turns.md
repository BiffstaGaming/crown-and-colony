# System: Turns

| | |
|---|---|
| **Status** | Implemented (skeleton) |
| **Last verified** | 2026-06-13 @ Phase 1 walking skeleton |
| **Code** | `game/src/GameLogic/GameSession/Game.cs` (`EndTurn`) |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/GameTests.cs`, `Scenarios/` |
| **FreeCol reference** | `freecol/src/net/sf/freecol/server/control/` turn handling (cross-check when economy lands) |
| **Related systems** | [units-movement](units-movement.md) |

## 1. How it works (plain English)

The game advances in turns, starting at turn 1. Press **End Turn** and every unit gets its movement back, and the turn counter ticks up. (Later phases hang everything else off this moment: colony production, growth, European prices, AI moves.)

## 2. Detailed rules

| On EndTurn | Effect (in order) |
|---|---|
| 1. Each colony | colony square produces → worked tiles produce → buildings produce (unattended + per-worker conversions, breeding-gated) → construction completes if materials cover it → colonists eat (2 each) → growth at 200 food (newborn auto-assigned) — see [colonies](colonies.md) |
| 2. All units | movement restored to full |
| 3. Turn counter | +1 |

**Deviations from original / FreeCol:** none yet — the skeleton turn does the minimum. The original's turn/season/year mapping (1492 start, seasons after 1600) arrives with the calendar in Phase 2+.

## 3. Technical design

- `Game.EndTurn()` is the single end-of-turn entry point; future phase steps (production → growth → market → AI) will execute inside it in a defined order, which this doc must then specify.
- UI: End Turn button → `GameController.OnEndTurnPressed` → `Game.EndTurn()` → view refresh. No turn logic in the UI (ADR-006).

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `EndTurn_AdvancesTurn_AndRestoresMovement` | ✅ |
| L2 Scenario | Always | 10-turn run asserts counter each turn | ✅ |
| L3 Interaction | Yes (button) | `EndTurnButton_AdvancesTurn` (GdUnit4, real click signal) | ✅ |
| L4 Visual | Via status label | covered by future main-screen golden | ⬜ |

## 5. Open issues / TODO

- [ ] Calendar (year/season display), turn-order pipeline as systems land.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Turn counter + movement reset + End Turn UI | Phase 1 skeleton |
