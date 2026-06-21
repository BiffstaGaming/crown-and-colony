# System: Scoring

| | |
|---|---|
| **Status** | Implemented (engine read + **victory / end-of-game screen** consuming it via `Game.ScoreBreakdown`; high-score table still pending — P7) |
| **Last verified** | 2026-06-22 @ `Game.ScoreBreakdown` oracle + the `VictoryPanel` that reads it (`86d3c9xc6`) |
| **Code** | `game/src/GameLogic/GameSession/Game.cs` (`PlayerScore`, **`ScoreBreakdown`**, `Score`, the unit-score table + independence-bonus helper — the "Player score" section) · `game/src/GameLogic/GameSession/ScoreComponents.cs` (the itemised-breakdown value object) · history-event score: `Game.History.cs` (`HistoryEvent.Score`, `Game.HistoryEventScore`) · victory screen: `game/presentation/VictoryPanel.cs` |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/ScoreTests.cs` (engine, incl. `ScoreBreakdown`) · `game/presentation/tests/VictoryPanelTests.cs` (L3 victory screen) |
| **FreeCol reference** | `freecol/src/net/sf/freecol/server/model/ServerPlayer.java` (`updateScore`, L858–888; the `SCORE_*` constants, L155–171), `freecol/src/.../common/model/Unit.java` (`getScoreValue`, L621), `freecol/src/.../common/model/UnitType.java` (`getScoreValue`, L179), `freecol/src/.../common/model/HistoryEvent.java` (`getScore`, L194), `freecol/src/.../common/model/HighScore.java`, `freecol/data/rules/classic/specification.xml` (unit `score-value` attributes, L1814–2213; `declareIndependence` event score, L68) |
| **Related systems** | [independence.md](independence.md), [founding-fathers.md](founding-fathers.md), [sons-of-liberty.md](sons-of-liberty.md), [colonies.md](colonies.md), [game-modes.md](game-modes.md) |

## 1. How it works (plain English)

Your **score** is a single number that rates how well your colony empire is doing. It is the figure the end-of-game victory screen and the high-score table show. You never set it directly — the game adds it up from what you own and what you have achieved, and it changes turn by turn as your nation grows.

**The rules, in plain words:**
- **Your units count.** Every colonist, soldier, ship and wagon you own is worth a few points — better units are worth more (a free colonist is worth 3, a man-o'-war 8, a petty criminal 1).
- **Your colonies' loyalty counts.** Each colony adds its banked **liberty** (the bells that drive Sons of Liberty) straight to your score, so a fervent, bell-rich colony is worth a lot.
- **Founding Fathers count.** Each Founding Father you elect to the Continental Congress adds **5 points**.
- **Discovering new lands counts.** The first time you reveal a region nobody has reached, you earn its exploration points — a land region up to ~1000 by size, a mountain range `2 ×` its size, the **Pacific Ocean 100** (see [map & terrain](map-terrain.md) → Region discovery). These accrue through your history log as you explore.
- **Your treasure counts — a little.** Gold converts at **1 point per 1,000 gold** (so 7,500 gold is worth 7 points). Money is the weakest contributor on purpose.
- **Winning independence is the big multiplier.** If you win the War of Independence you get a **percentage bonus on your whole score** — **+100%** for being the first nation to do it (i.e. your score doubles), +50% for the second, +25% for the third.

**Worked example:**
> You have two free colonists (3 + 3) and a caravel (3) on the map — that's **9**. You found a colony and it banks **600 liberty** — **+600**. You elect **two Founding Fathers** — **+10**. You are sitting on **7,500 gold** — **+7** (⌊0.001 × 7500⌋). Your score is **9 + 600 + 10 + 7 = 626**. Later you win independence first, so your **626 doubles to 1,252**.

**What the player sees and does:** nothing yet directly — this is the calculation engine. The **victory screen** and **high-score table** (separate Phase 7 tasks) will read this number and display it; the player raises their score by founding colonies, building bells, electing Fathers, accumulating units/gold, and ultimately winning independence.

## 2. Detailed rules

The score is recomputed from current state every time it is read (FreeCol `ServerPlayer.updateScore`). The summands, in order:

| Summand | Formula | FreeCol source |
|---|---|---|
| Units | Σ over the player's units of the unit type's `score-value` | `sum(getUnits(), Unit::getScoreValue)` (`ServerPlayer.java:860`); values from `specification.xml` `score-value` (L1814–2213) |
| Colonies | Σ over the player's colonies of `colony.Liberty` | `sum(getColonies(), Colony::getLiberty)` (`ServerPlayer.java:861`) |
| Founding Fathers | `5 × (number of elected fathers)` | `SCORE_FOUNDING_FATHER × count(getFoundingFathers())` (`ServerPlayer.java:862`, constant L165) |
| Gold | `⌊0.001 × gold⌋` (floored) | `(int)Math.floor(SCORE_GOLD × gold)` (`ServerPlayer.java:865`, constant L162) |
| History events | Σ of each history event's stored score (today: **region-discovery** scores; the human's log only) | `score += h.getScore()` (`ServerPlayer.java:881`) — `Game.HistoryEventScore`, added for the human (`86d3c9w2f`) |
| Independence bonus | After the subtotal: `subtotal + subtotal × bonus / 100`, where `bonus` is 100 / 50 / 25 for the 1st / 2nd / 3rd nation to win independence (else 0) | `this.score += (this.score × bonus) / 100` (`ServerPlayer.java:885`); ordinal from the INDEPENDENCE history event's place (L873–877, constants L169–171) |

**Unit `score-value` table (classic ruleset, `specification.xml` L1814–2213):**

| Unit type | Score | | Unit type | Score |
|---|---|---|---|---|
| freeColonist | 3 | | expertFarmer/Fisherman/FurTrapper/SilverMiner/LumberJack/OreMiner | 4 |
| masterSugarPlanter/CottonPlanter/TobaccoPlanter | 5 | | firebrandPreacher, elderStatesman | 6 |
| masterCarpenter | 4 | | masterDistiller/Weaver/Tobacconist/FurTrader/Blacksmith/Gunsmith | 5 |
| seasonedScout, hardyPioneer | 4 | | veteranSoldier, jesuitMissionary | 5 |
| indenturedServant | 2 | | pettyCriminal | 1 |
| indianConvert | 3 | | caravel | 3 |
| frigate | 6 | | galleon | 5 |
| manOWar | 8 | | merchantman, privateer | 4 |
| artillery | 2 | | damagedArtillery, treasureTrain, wagonTrain | 1 |

A unit type with no `score-value` in the ruleset scores **0** (FreeCol's default; e.g. `model.unit.brave`).

**Worked edge cases:**

| Input / condition | Result |
|---|---|
| Gold = 999 | `⌊0.999⌋ = 0` points |
| Gold = 1000 | `⌊1.0⌋ = 1` point |
| Gold = 2500 | `⌊2.5⌋ = 2` points |
| Colonist founds a colony | The colonist leaves the unit list (becomes colony population) — its unit score is removed; the colony's liberty now contributes |
| Player is `Colonial` or `Rebel` | No independence bonus (0%) |
| Player is `Independent` (first/only winner) | `+100%` — the whole score doubles |
| Native brave / human-unowned unit | Excluded — only the scored player's own non-native units contribute |

**Deviations from original 1994 / FreeCol behavior:**
- **Colony workers don't contribute unit scores.** FreeCol's `getUnits()` includes colonists working inside colonies; in our model those are colony *population*, not entries in the unit list, so only on-map/in-Europe units score. (Their economic contribution still shows up indirectly through the colony's liberty.) Same modelling boundary as the continental-army muster (see [independence.md](independence.md)). Closing it requires the colony-worker-as-unit refactor.
- **History-event scores: region discovery now counts; others still pending.** FreeCol folds region-discovery, lost-city-find and settlement/nation-destruction scores into the total via `HistoryEvent.getScore()`. As of `86d3c9w2f` our `HistoryEvent` carries a numeric `Score`, and **region-discovery** events populate it — `Game.HistoryEventScore` sums them and `PlayerScore` adds them **for the human** (the history log is the human's only; a foreign power scores its units/colonies but no history events). Lost-city-find scores and the settlement/nation-destruction **penalties** (−5/−50) are still 0 until those events record a score. The history log itself is still **not persisted**, so a reloaded game must re-earn its discovery score (the per-region *discovered* flag IS saved, so a re-revealed region is not re-discovered — only the score, riding the in-memory log, must be re-earned).
- **Independence ordinal is simplified.** FreeCol stores the 0/1/2 finishing place on the INDEPENDENCE history event. We have a single human player, so an `Independent` nation is necessarily the first to win and takes the 100% bonus; the 50%/25% constants exist for the multi-power future. The ordinal is derived from `DeclaredIndependenceTurn` ordering across independent players so it is already correct should more than one independent nation ever exist.
- **Destruction penalties not yet applied.** `SCORE_SETTLEMENT_DESTROYED` (−5) and `SCORE_NATION_DESTROYED` (−50) reach FreeCol's total through history-event scores; with history scores omitted, they are not yet counted. (`PlayerScore` is typed to allow a negative result for when they land.)

## 3. Technical design

**Domain model:** a self-contained "Player score" section on `Game` (`Game.cs`). `Game.ScoreBreakdown(Player)` returns the itemised `ScoreComponents` (a small `readonly record struct` in `ScoreComponents.cs`: the five summands + the independence bonus percent, with `Subtotal`/`IndependenceBonus`/`Total` derived); `Game.PlayerScore(Player)` is now defined as `ScoreBreakdown(player).Total`, so the breakdown is the **single source of truth** and the total can never drift from its parts. `Game.Score` is a human-convenience accessor (`PlayerScore(_human)`) mirroring the existing `Game.Gold`/`Game.Liberty` pattern. A private `IndependenceScoreBonusPercent(Player)` resolves the percentage bonus. The **victory / end-of-game screen** (`VictoryPanel`, `86d3c9xc6`) reads `ScoreBreakdown` to show the winner *why* their score is what it is, line by line — presentation-only (ADR-006), no rules in the panel. No state was added to `Player`: `Player` holds no back-reference to `Game`, so a player-side `Score` property cannot compute the cross-cutting sum (units/colonies live on `Game`) — the read is exposed on `Game` only, where the state lives (ADR-006: rules in engine-free `GameLogic`; presentation reads the oracle).

**Data sources:** the unit `score-value` numbers come from `freecol/data/rules/classic/specification.xml` (L1814–2213). Our `Specification.UnitType` record does **not** parse the `score-value` attribute, so the scoring section holds a static `UnitScoreValues` dictionary (id → value) transcribed from that spec, rather than editing the spec parser. A unit type absent from the table scores 0. (If unit `score-value`s are ever added to `UnitType`, this table should be replaced by reading `unit.Type.ScoreValue`.)

**Algorithms & formulas:** see §2. The C# mirrors `updateScore` line-for-line: integer arithmetic throughout, `Math.Floor` on the gold term, and the percentage bonus applied to the subtotal with integer division (`score += score * bonus / 100`) — bit-identical to FreeCol's `(this.score * bonus) / 100`.

**Integration points:** **none on the turn loop.** Nothing calls `PlayerScore`/`ScoreBreakdown` during `EndTurn`/AI turns — they are pull-only oracles. The **victory / end-of-game screen** (`VictoryPanel`) now calls `ScoreBreakdown` when it renders (opened by `GameController.UpdateVictoryUi` once `Game.Winner` is set); the high-score table (P7) is still to come. Because it is never invoked inside the turn, it cannot perturb the deterministic stream-0 sequence (ADR-009).

**Persistence:** the **score itself** is still *not* saved — it is recomputed from existing persisted state (units, colonies+liberty, congress, gold, player type, independence turn) on each read. The **history-event score summand** rides the in-memory history log, which is **not persisted** either, so a reloaded game's discovery score is re-earned as regions are re-revealed (the per-region *discovered* flag IS persisted at save v51 by the [region-discovery slice](map-terrain.md), so a re-revealed region is not re-discovered — only its score, riding the un-persisted log, accrues again). The score read added **no save-version bump** of its own; the v51 bump belongs to region discovery (the discovery state it persists), not to scoring.

**Purity / determinism (ADR-009):** `PlayerScore` draws **no randomness** (it constructs no generator and calls none) and **mutates no state** — it only reads the unit/colony lists, the player fields, and the (already-accrued) history log; verified by a test asserting `game.RandomState` and the unit/colony counts are unchanged across two reads. Region **discovery** (which *populates* the history score) is likewise RNG-free and happens at fog-reveal time, not at score-read time, so a score read remains a pure pull-only oracle.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `game/tests/GameLogic.Tests/GameSession/ScoreTests.cs` — each summand (father +5, gold ⌊0.001×g⌋, colony liberty, unit score-value), a known-fixture total, the default new-game roster total, the independence ×2 first-place bonus, determinism, RNG-free + no-mutation, unknown/native unit → 0; **`ScoreBreakdown` summands reassemble to `PlayerScore` (incl. the +100% independence-bonus path)** | ✅ |
| L2 Scenario | Always | Covered by the global Soak/autoplay suite (score is a read; the autoplay games are byte-identical, proving the default game is unchanged) | ✅ |
| L3 Interaction | If the system has UI | `game/presentation/tests/VictoryPanelTests.cs` — an independence win opens the `VictoryPanel` showing the score header (= `PlayerScore`), the breakdown lines (`ScoreLiberty`/`ScoreBonus`) and the end-game stats (`StatColonies`/`StatTurns`/`StatYear`) then Closes; a no-winner `Open` is a no-op. **GdUnit scene harness ran** (2 green). | ✅ |
| L4 Visual | If the system has a screen | No golden yet — `VictoryPanel` is hidden by default (opens only on a win), so it adds no churn to the existing scene goldens; a dedicated victory-screen golden is a follow-up | — (deferred) |
| L5 Soak | Covered by global suite | `--filter "Category=Soak"` green (4 tests) — default game byte-identical, no stream-0 drift | ✅ |

- **FreeCol cross-check:** the formula and every constant are transcribed directly from `ServerPlayer.updateScore` (L858–888) and the `SCORE_*` constants (L155–171); the unit values from `specification.xml` (L1814–2213). The **region-discovery** history-event score is now folded in (`86d3c9w2f`); the remaining gaps (lost-city-find scores, the settlement/nation-destruction penalties, colony-worker unit scores) stay omitted pending their supporting state — see the deviations in §2.

## 5. Open issues / TODO

- [x] **Region-discovery history-event score folded in** (`86d3c9w2f`): `HistoryEvent.Score` + `Game.HistoryEventScore`, added to the human's `PlayerScore`. **Follow-ups below cover the remaining history-event scores.**
- [ ] Add the remaining history-event scores once those events record one (lost-city finds; settlement/nation destruction penalties of −5/−50) **and** persist the history log so they survive load (region-discovery score is in-memory today).
- [ ] Count colony-worker units in the unit sum once colony workers live in the unit list (the colony-worker-as-unit refactor) — also closes the muster-fidelity deviation in [independence.md](independence.md).
- [ ] If `Specification.UnitType` gains a parsed `ScoreValue`, retire the `UnitScoreValues` table and read it from the type.
- [x] Consume the score from the **victory / end-of-game screen** (`86d3c9xc6`): `VictoryPanel` reads the new `Game.ScoreBreakdown` oracle. **Follow-up:** the **high-score table** (P7) still to consume it.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-22 | **`ScoreBreakdown` oracle + victory screen consume it** (`86d3c9xc6`): added pure `Game.ScoreBreakdown(Player)` returning the itemised `ScoreComponents` (five summands + independence bonus %, `Subtotal`/`Total` derived); refactored `PlayerScore` to `ScoreBreakdown(player).Total` so the total can't drift from its parts. New presentation `VictoryPanel` reads it (shown when `Game.Winner` is set) for the end-of-game stats screen. Still recomputed-on-read, RNG-free, **no save bump**. +2 L1 (`ScoreBreakdown` summands/bonus) + 2 L3 (`VictoryPanelTests`). | `86d3c9xc6` |
| 2026-06-21 | **Region-discovery score folded in** (P6): `HistoryEvent` gained an additive `Score`; `Game.HistoryEventScore` sums the human's history-event scores (today region discovery) and `PlayerScore` adds it for the human — closing the history-event-score deviation for discovery. Still recomputed-on-read, RNG-free; the v51 save bump belongs to the discovery state, not the score. `ScoreTests` updated to account for the discovery summand. | `86d3c9w2f` |
| 2026-06-21 | Initial scoring engine: pure `Game.PlayerScore` read (units + colony liberty + 5×fathers + ⌊0.001×gold⌋ + independence %bonus), faithful to FreeCol `ServerPlayer.updateScore`; not persisted, RNG-free, no save bump. | `86d3c9vjm` |
