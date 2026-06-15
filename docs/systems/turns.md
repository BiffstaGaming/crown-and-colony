# System: Turns

| | |
|---|---|
| **Status** | Implemented (per-player ring: every colonial player runs the economy; foreign powers also run the AI; native nations run their raid/wander AI (slice 1b); colony economy, liberty/fathers, immigration, high-seas sailing) |
| **Last verified** | 2026-06-15 @ slice 1b (native AI) |
| **Code** | `game/src/GameLogic/GameSession/Game.cs` (`EndTurn`, `RunPlayerTurn`, `RunForeignPowerEconomy`, `RunForeignPowerTurn`, `RunNativeTurn`) |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/GameTests.cs`, `MultiPlayerTests.cs`, `Scenarios/` |
| **FreeCol reference** | `freecol/src/net/sf/freecol/server/control/` turn handling (cross-check when economy lands) |
| **Related systems** | [players](players.md), [units-movement](units-movement.md), [colonies](colonies.md), [founding-fathers](founding-fathers.md), [immigration](immigration.md), [europe](europe.md) |

## 1. How it works (plain English)

The game advances in turns, starting at turn 1. Press **End Turn** and play goes around the ring of players: first **you** take your end-of-turn (colonies produce, eat and grow; bells become liberty and may elect a Founding Father; crosses bring immigrants), then each **foreign power** takes its computer-played turn — it runs the *same* economy (its colonies produce, it banks liberty/immigration), then sells surplus and recruits in its own Europe, founds a colony and sends colonists exploring (see [players](players.md)) — then each **native nation** takes its turn: its braves raid your units if the tribe is angry enough, otherwise they roam (see [natives](natives.md)). After the full round the shared world ticks once — ships at sea advance and arrive, native alarm cools, every unit gets its movement back, the turn counter ticks up — and it's your turn again. Any raids you suffered during the round are reported in the status bar.

## 2. Detailed rules

Each player takes its turn in ring order (`RunPlayerTurn`); then the **world steps** run once:

| Per player (`RunPlayerTurn`) | Effect |
|---|---|
| Colonial economy (human **and** foreign powers) | its colonies produce/eat/grow → its bells become liberty (electing a father if affordable) → its crosses + Europe contribution become immigration (emigrants arrive on the dock). Owner-filtered (only that player's colonies/units count) and folding that player's own fathers — see [colonies](colonies.md), [founding-fathers](founding-fathers.md), [immigration](immigration.md) |
| Foreign power (AI), after its economy | `RunForeignPowerEconomy` — pursue a father, sell each colony's tradeable surplus (never food) to its own market, recruit while affordable up to a Europe cap — then `RunForeignPowerTurn`, a flat per-unit switch: **at war with the human**, an armed land unit beside an **undefended human colony captures it** (1c-3f, priority), otherwise an armed unit hunts the human's nearest unit (attack adjacent, else step toward — 1c-2), or with no field unit to chase a land unit **marches on the nearest human colony** (besiege fallback, `86d3bx03d`); otherwise (at peace) found a colony while under `MaxAiColonies`, else step toward the nearest unexplored tile; ships idle. All draws (incl. combat/capture) from the player's own RNG stream — see [players](players.md), [combat](combat.md), [market](market.md) |
| Native (AI) | `RunNativeTurn` — each brave takes one action in stable by-id order: if its home settlement is alarmed enough (Displeased+, FreeCol's seek-and-destroy gate) it **pillages** an adjacent undefended human colony (carries off goods, not captured), else attacks the nearest human unit when adjacent else steps toward it; otherwise it wanders one tile. All draws from the nation's own RNG stream; raids/pillages on the human are recorded as transient notices — see [players](players.md), [natives](natives.md) |

| Then, world steps once (in order) | Effect |
|---|---|
| Sailing | ships in transit advance; arrivals dock in Europe or re-enter the map (passengers travel with them) — see [europe](europe.md)/[transport](transport.md) |
| Colonial contact | first sight of a rival colonial power's unit/colony records mutual Peace (`DetectColonialContacts`) — see [diplomacy](diplomacy.md) |
| Colonial tension decay | each colonial-pair tension cools toward 0 (`value/100 + 4`, same as native alarm; `DecayColonialTension`) — see [diplomacy](diplomacy.md) |
| Colonial stance update | each met pair's stance re-derives from its cooled tension (war→cease-fire→peace; `UpdateColonialStances`) — see [diplomacy](diplomacy.md) |
| Native settlements | each settlement's alarm toward the player cools toward 0 (`value/100 + 4`) — see [natives](natives.md) |
| All units | movement restored to full |
| Turn counter | +1 |

**Deviations from original / FreeCol:** the original's turn/season/year mapping (1492 start, seasons after 1600) arrives with the calendar (still pending); ages currently use simple turn bands. The foreign-power AI is a minimal flat switch + a minimal economy (ADR-019), not FreeCol missions/`ColonyPlan`; the native AI is likewise a flat raid/wander switch, not FreeCol's mission planner (gifts, tribute demands, colony pillage, growth deferred — see [natives](natives.md)). Foreign-power combat/diplomacy *action* on stance is still ahead.

## 3. Technical design

- `Game.EndTurn()` first clears the transient `_combatNotices`, `_colonyLossNotices` **and `_colonyRaidNotices`** (the round's AI raids, AI colony-captures, and native colony-pillages against the human, surfaced to the UI; never saved), then walks the player ring from `_currentPlayerIndex` (via `NextPlayerIndex`), calling `RunPlayerTurn` on each, then the **world steps once** (`AdvanceSailing` → `DetectColonialContacts` → `DecayColonialTension` → `UpdateColonialStances` → `DecayNativeAlarm` per settlement → reset movement → `Turn++`) and the pointer returns to the human. `RunPlayerTurn` dispatches by `PlayerType`: a **native** runs `RunNativeTurn(player)` (pillage/raid/wander, slice 1b + colony pillage) and returns; every colonial player runs the economy — `RunColonyTurn(player, …)` per owned colony → `AccumulateLibertyAndElectFathers(player)` → `AccumulateImmigrationAndEmigrate(player)` — and a foreign power then runs `RunForeignPowerEconomy(player)` + `RunForeignPowerTurn(player)`. Everything is owner-filtered, folds that player's own fathers, and draws from that player's RNG stream (`RandomFor`; the human is stream 0) and trades on that player's market, so adding/animating players never disturbs the human's seeded game. **Ordering note:** braves read settlement alarm *during* the ring (before the end-of-round `DecayNativeAlarm`), so a raid uses the alarm the human has earned, which then cools.
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
| 2026-06-14 | FP-6a: two new world-step phases — `DetectColonialContacts` (first sight → Peace) and `DecayColonialTension` — added to `EndTurn` beside native-alarm decay (recorded diplomacy; no RNG) — see [diplomacy](diplomacy.md) | FP-6a |
| 2026-06-15 | FP-6b: `UpdateColonialStances` world-step added after tension decay — each met colonial pair's stance follows its tension (war→cease-fire→peace; deterministic, no RNG) — see [diplomacy](diplomacy.md) | FP-6b |
| 2026-06-15 | Native AI (slice 1b): `RunPlayerTurn` dispatches `PlayerType.Native` to `RunNativeTurn` (raid/wander on the nation's own stream); `EndTurn` clears the transient `_combatNotices` (raids surfaced to the UI, not saved) at the top of the round — see [natives](natives.md), [players](players.md) | Phase 5 slice 1b |
| 2026-06-15 | Slice 1c-2: `RunForeignPowerTurn` gains a war branch — a power at `War` with the human sends its armed units to attack the human's nearest unit (on the power's own RNG stream); dormant at peace — see [players](players.md), [combat](combat.md) | Phase 5 slice 1c-2 |
| 2026-06-15 | Slice 1c-3a′: the war branch now also drives armed **warships** (the naval skip narrowed); ship-vs-ship combat sinks the loser — see [combat](combat.md) | Phase 5 slice 1c-3a |
| 2026-06-15 | Slice 1c-3f: the war branch also **captures the human's undefended colonies** (priority over the unit-hunt, on the power's own stream); `EndTurn` now also clears the transient `_colonyLossNotices` feed — see [players](players.md), [combat](combat.md) | Phase 5 slice 1c-3f |
| 2026-06-15 | Native colony pillage: `RunNativeTurn` pillages an adjacent undefended human colony (before the unit-hunt, nation's own stream); `EndTurn` now also clears the transient `_colonyRaidNotices` feed — see [natives](natives.md), [combat](combat.md) | Phase 5 native pillage |
| 2026-06-16 | Besiege fallback (`86d3bx03d`): `RunForeignPowerTurn`'s war branch — a land unit with no field prey marches on the nearest human colony (`NearestHumanColony`) instead of exploring; power's own stream, additive. See [players](players.md), [combat](combat.md) | Phase 5 (besiege) |
| 2026-06-16 | Human defeat (`86d3bx04e`): `Game.IsHumanDefeated` (computed) + a HUD defeat banner. `EndTurn` deliberately does **not** short-circuit on defeat — that would break ADR-009 stream-0 byte-stability (a wiped-out game would diverge from a surviving one); stopping the game is a deferred presentation follow-up. See [players](players.md) | Phase 5 (human defeat) |
| 2026-06-16 | Game-over flow (`86d3c0x3f`): the deferred presentation follow-up above is shipped — a game-over overlay + a disabled/relabelled End Turn button when `Game.IsHumanDefeated`. `EndTurn` is **still** untouched (the turn loop runs unchanged; the stop is purely presentation). See [players](players.md), [presentation](../modules/presentation.md) | Phase 5 (game-over flow) |
