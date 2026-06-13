# System: Turns

| | |
|---|---|
| **Status** | Implemented (colony economy, liberty/fathers, immigration, high-seas sailing) |
| **Last verified** | 2026-06-13 @ Phase 4 slice 9 |
| **Code** | `game/src/GameLogic/GameSession/Game.cs` (`EndTurn`) |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/GameTests.cs`, `Scenarios/` |
| **FreeCol reference** | `freecol/src/net/sf/freecol/server/control/` turn handling (cross-check when economy lands) |
| **Related systems** | [units-movement](units-movement.md), [colonies](colonies.md), [founding-fathers](founding-fathers.md), [immigration](immigration.md), [europe](europe.md) |

## 1. How it works (plain English)

The game advances in turns, starting at turn 1. Press **End Turn** and the world ticks once: colonies produce, eat and grow; bells become liberty (and may elect a Founding Father); religious crosses bring immigrants to the Europe dock; ships at sea advance and arrive; then every unit gets its movement back and the turn counter ticks up. (Foreign powers and AI will hang off this same moment in later phases.)

## 2. Detailed rules

| On EndTurn | Effect (in order) |
|---|---|
| 1. Each colony | colony square produces → worked tiles produce (incl. bonus-resource yields) → buildings produce (unattended + per-worker conversions, breeding-gated) → construction completes if materials cover it → colonists eat (2 each) → growth at 200 food (newborn auto-assigned) — see [colonies](colonies.md) |
| 2. Liberty & fathers | each colony's bells → player liberty (with father modifiers); elect the chosen Founding Father if affordable; refresh offers — see [founding-fathers](founding-fathers.md) |
| 3. Immigration | colony crosses + the Europe contribution → the immigration pool; emigrants arrive on the Europe dock when the target is met — see [immigration](immigration.md) |
| 4. Sailing | ships in transit advance; arrivals dock in Europe or re-enter the map (passengers travel with them) — see [europe](europe.md)/[transport](transport.md) |
| 5. All units | movement restored to full |
| 6. Turn counter | +1 |

**Deviations from original / FreeCol:** the original's turn/season/year mapping (1492 start, seasons after 1600) arrives with the calendar (still pending); ages currently use simple turn bands. No AI/foreign-power step yet.

## 3. Technical design

- `Game.EndTurn()` is the single end-of-turn entry point; its steps run in the fixed order above (`RunColonyTurn` → `AccumulateLibertyAndElectFathers` → `AccumulateImmigrationAndEmigrate` → `AdvanceSailing` → reset movement → `Turn++`). New phase steps slot into this order and are documented here.
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
| 2026-06-13 | Turn pipeline grew: colony economy (Phase 3), then liberty/fathers, immigration, and high-seas sailing steps (Phase 4) | Phases 3–4 |
