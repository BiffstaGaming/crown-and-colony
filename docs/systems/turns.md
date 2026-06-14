# System: Turns

| | |
|---|---|
| **Status** | Implemented (per-player ring: human economy + foreign-power AI; colony economy, liberty/fathers, immigration, high-seas sailing) |
| **Last verified** | 2026-06-14 @ FP-4 |
| **Code** | `game/src/GameLogic/GameSession/Game.cs` (`EndTurn`, `RunPlayerTurn`, `RunForeignPowerTurn`) |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/GameTests.cs`, `MultiPlayerTests.cs`, `Scenarios/` |
| **FreeCol reference** | `freecol/src/net/sf/freecol/server/control/` turn handling (cross-check when economy lands) |
| **Related systems** | [players](players.md), [units-movement](units-movement.md), [colonies](colonies.md), [founding-fathers](founding-fathers.md), [immigration](immigration.md), [europe](europe.md) |

## 1. How it works (plain English)

The game advances in turns, starting at turn 1. Press **End Turn** and play goes around the ring of players: first **you** take your end-of-turn (colonies produce, eat and grow; bells become liberty and may elect a Founding Father; crosses bring immigrants), then each **foreign power** takes its computer-played turn (it founds a colony and sends colonists exploring — see [players](players.md)), then the natives' (still empty) turns. After the full round the shared world ticks once — ships at sea advance and arrive, native alarm cools, every unit gets its movement back, the turn counter ticks up — and it's your turn again.

## 2. Detailed rules

Each player takes its turn in ring order (`RunPlayerTurn`); then the **world steps** run once:

| Per player (`RunPlayerTurn`) | Effect |
|---|---|
| Human | its colonies produce/eat/grow → its bells become liberty (electing a father if affordable) → its crosses + Europe contribution become immigration (emigrants arrive on the dock). Only the human's own colonies/units count (owner-filtered) — see [colonies](colonies.md), [founding-fathers](founding-fathers.md), [immigration](immigration.md) |
| Foreign power (AI) | a flat per-unit switch: found a colony where it stands while under `MaxAiColonies`, else step one tile toward the nearest tile it has not explored; ships idle. Draws from the player's own RNG stream; no economy yet (FP-5) — see [players](players.md) |
| Native | inert (no AI yet) |

| Then, world steps once (in order) | Effect |
|---|---|
| Sailing | ships in transit advance; arrivals dock in Europe or re-enter the map (passengers travel with them) — see [europe](europe.md)/[transport](transport.md) |
| Native settlements | each settlement's alarm toward the player cools toward 0 (`value/100 + 4`) — see [natives](natives.md) |
| All units | movement restored to full |
| Turn counter | +1 |

**Deviations from original / FreeCol:** the original's turn/season/year mapping (1492 start, seasons after 1600) arrives with the calendar (still pending); ages currently use simple turn bands. The foreign-power AI is a minimal flat switch (ADR-019), not FreeCol missions; native nations still only decay alarm (their AI — raids, gifts, growth — and the foreign powers' economy/combat come with FP-5/FP-6).

## 3. Technical design

- `Game.EndTurn()` walks the player ring from `_currentPlayerIndex` (via `NextPlayerIndex`), calling `RunPlayerTurn` on each (the human's economy: `RunColonyTurn` → `AccumulateLibertyAndElectFathers` → `AccumulateImmigrationAndEmigrate`; a foreign power: `RunForeignPowerTurn`; natives: inert), then the **world steps once** (`AdvanceSailing` → `DecayNativeAlarm` per settlement → reset movement → `Turn++`) and the pointer returns to the human. Per-player economy/AI is owner-filtered and draws from that player's RNG stream (the human is stream 0), so adding players never disturbs the human's seeded game.
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
| 2026-06-14 | Native settlement alarm decay step added (Phase 5 slice 3) | Phase 5 slice 3 |
| 2026-06-14 | Per-player ring (FP-3b): `EndTurn` iterates `_players` (`RunPlayerTurn`) then runs the world steps once | FP-3b |
| 2026-06-14 | Foreign-power AI step (FP-4): `RunForeignPowerTurn` (found/explore/idle) on each power's own RNG stream | FP-4 |
