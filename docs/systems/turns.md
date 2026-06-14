# System: Turns

| | |
|---|---|
| **Status** | Implemented (per-player ring: every colonial player runs the economy; foreign powers also run the AI; colony economy, liberty/fathers, immigration, high-seas sailing) |
| **Last verified** | 2026-06-14 @ FP-5 |
| **Code** | `game/src/GameLogic/GameSession/Game.cs` (`EndTurn`, `RunPlayerTurn`, `RunForeignPowerEconomy`, `RunForeignPowerTurn`) |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/GameTests.cs`, `MultiPlayerTests.cs`, `Scenarios/` |
| **FreeCol reference** | `freecol/src/net/sf/freecol/server/control/` turn handling (cross-check when economy lands) |
| **Related systems** | [players](players.md), [units-movement](units-movement.md), [colonies](colonies.md), [founding-fathers](founding-fathers.md), [immigration](immigration.md), [europe](europe.md) |

## 1. How it works (plain English)

The game advances in turns, starting at turn 1. Press **End Turn** and play goes around the ring of players: first **you** take your end-of-turn (colonies produce, eat and grow; bells become liberty and may elect a Founding Father; crosses bring immigrants), then each **foreign power** takes its computer-played turn — it runs the *same* economy (its colonies produce, it banks liberty/immigration), then sells surplus and recruits in its own Europe, founds a colony and sends colonists exploring (see [players](players.md)) — then the natives' (still empty) turns. After the full round the shared world ticks once — ships at sea advance and arrive, native alarm cools, every unit gets its movement back, the turn counter ticks up — and it's your turn again.

## 2. Detailed rules

Each player takes its turn in ring order (`RunPlayerTurn`); then the **world steps** run once:

| Per player (`RunPlayerTurn`) | Effect |
|---|---|
| Colonial economy (human **and** foreign powers) | its colonies produce/eat/grow → its bells become liberty (electing a father if affordable) → its crosses + Europe contribution become immigration (emigrants arrive on the dock). Owner-filtered (only that player's colonies/units count) and folding that player's own fathers — see [colonies](colonies.md), [founding-fathers](founding-fathers.md), [immigration](immigration.md) |
| Foreign power (AI), after its economy | `RunForeignPowerEconomy` — pursue a father, sell each colony's tradeable surplus (never food) to its own market, recruit while affordable up to a Europe cap — then `RunForeignPowerTurn`, a flat per-unit switch: found a colony while under `MaxAiColonies`, else step toward the nearest unexplored tile; ships idle. All draws from the player's own RNG stream — see [players](players.md), [market](market.md) |
| Native | inert (no AI yet) |

| Then, world steps once (in order) | Effect |
|---|---|
| Sailing | ships in transit advance; arrivals dock in Europe or re-enter the map (passengers travel with them) — see [europe](europe.md)/[transport](transport.md) |
| Native settlements | each settlement's alarm toward the player cools toward 0 (`value/100 + 4`) — see [natives](natives.md) |
| All units | movement restored to full |
| Turn counter | +1 |

**Deviations from original / FreeCol:** the original's turn/season/year mapping (1492 start, seasons after 1600) arrives with the calendar (still pending); ages currently use simple turn bands. The foreign-power AI is a minimal flat switch + a minimal economy (ADR-019), not FreeCol missions/`ColonyPlan`; native nations still only decay alarm (their AI — raids, gifts, growth — and the foreign powers' combat/diplomacy come with FP-6).

## 3. Technical design

- `Game.EndTurn()` walks the player ring from `_currentPlayerIndex` (via `NextPlayerIndex`), calling `RunPlayerTurn` on each, then the **world steps once** (`AdvanceSailing` → `DecayNativeAlarm` per settlement → reset movement → `Turn++`) and the pointer returns to the human. `RunPlayerTurn` is unified (FP-5): a native returns immediately (inert); every colonial player runs the economy — `RunColonyTurn(player, …)` per owned colony → `AccumulateLibertyAndElectFathers(player)` → `AccumulateImmigrationAndEmigrate(player)` — and a foreign power then runs `RunForeignPowerEconomy(player)` + `RunForeignPowerTurn(player)`. Everything is owner-filtered, folds that player's own fathers, and draws from that player's RNG stream (`RandomFor`; the human is stream 0) and trades on that player's market, so adding/animating players never disturbs the human's seeded game.
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
| 2026-06-14 | FP-5: `RunPlayerTurn` unified — every colonial player runs the economy (colony turns + liberty + immigration, folding its own fathers); foreign powers add `RunForeignPowerEconomy` (sell/recruit/father) before the unit AI; all on per-player streams/markets | FP-5 |
