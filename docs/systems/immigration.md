# System: Immigration & Europe recruitment

| | |
|---|---|
| **Status** | Implemented (accrual + auto-emigration + paid recruitment; on the Europe screen) |
| **Last verified** | 2026-06-13 @ Phase 4 slice 4 |
| **Code** | `game/src/GameLogic/GameSession/Game.cs` (immigration accrual, dock, recruit), `Specification/UnitType.cs` (recruit weight + person) |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/ImmigrationTests.cs`, `Scenarios/JourneyTests.cs` (Journey 5) |
| **FreeCol reference** | `Player.java` (immigration/`reduceImmigration`/`updateImmigrationRequired`), `Europe.java` (`getCurrentRecruitPrice`, `getImmigration`), `ServerEurope.java` (`generateRecruitablesList`, `increaseRecruitmentDifficulty`), `ServerPlayer.java` (`csEmigrate`), classic `specification.xml` difficulty options |
| **Related systems** | [europe.md](europe.md) (sailing/trade), [colonies.md](colonies.md) (crosses production), [founding-fathers.md](founding-fathers.md) (the parallel liberty system), [save-load.md](save-load.md) |

## 1. How it works (plain English)

New colonists don't only come from babies born in your colonies — they also **emigrate from Europe** to your docks. Two things bring them:

- **Crosses** (religious enthusiasm). Every colony has a free **chapel** that makes **1 cross per turn**; build a church and it makes more. Crosses are the "religious unrest" that makes people in the old world want to start a new life across the ocean.
- A small **flat bonus** (+2 per turn) just for being a colonial power.

These add up into an **immigration pool**. When the pool reaches the **target** (it starts at **15**), one colonist **emigrates** — it appears waiting on your **dock in Europe**. Each time that happens the target goes **up by 2**, so immigrants come a little slower as the game goes on.

There's a catch: colonists **standing idle in Europe slow immigration down** — each one waiting on the dock costs **−4 per turn** off the pool (so ship them to the New World to keep the flow going). The pool can stall but never goes below zero.

You don't have to wait. Europe always shows **three recruits** on offer; you can **pay gold to recruit one immediately**. The price starts at **200**, drops toward a **floor of 80** as your pool fills (you're nearly getting one for free anyway), and **jumps by 30** every time you pay — so each impatient purchase makes the next one dearer.

**Worked example:**
> Your one colony's chapel makes 1 cross a turn; with the +2 bonus your pool climbs 3 a turn: 3, 6, 9, 12, **15** — and on the fifth turn a colonist emigrates to your Europe dock. The target is now 17. That colonist now sits in Europe; until you sail it home it shaves 4 a turn off the pool, so the next immigrant takes much longer. Impatient, you pay **200** gold to recruit a second from the dock right away — the next recruit will cost **230**.

**What the player sees and does:** the **Europe screen** ([europe.md](europe.md)) shows the recruits, their price, and the immigration pool/target, with a Recruit button per slot. Recruited/emigrated units appear in Europe (`UnitsInEurope`) and can be shipped home ([transport.md](transport.md)).

## 2. Detailed rules

All values are the classic ruleset at the default (**medium**) difficulty, read from `specification.xml`.

| Input / condition | Result |
|---|---|
| Immigration per turn | `crosses produced by all colonies` + Europe contribution |
| Europe contribution | `(persons on the dock × −4) + 2`, but clamped so the **turn's total** immigration is never negative (a person *aboard a ship* in Europe does not count — see [transport.md](transport.md)) |
| Initial target | **15** (`model.option.initialImmigration`) |
| Emigrant produced | whenever pool ≥ target: one recruit leaves the dock for Europe; repeat while still ≥ target |
| On each emigration | pool reduced by the target (surplus kept — `saveProductionOverflow=true`); target **+2** (`crossesIncrement`) |
| Dock | **3** slots (`MigrantCount`), each a weighted-random recruitable unit type |
| Recruit weights | spec `recruit-probability`: free colonist / indentured servant / petty criminal **20**, experts **1**, everything else 0 |
| Recruit price | `max(base × max(target − pool, 0) / target, floor)`, base **200**, floor **80** |
| On each **paid** recruit | pay the current price; base **+30** (`recruitPriceIncrease`), floor **+0** (`lowerCapIncrease`); **then** also consume the pool + raise the target exactly as a free emigration does |
| Recruit placement | the unit lands in Europe (`InEurope`); the dock refills with a fresh weighted draw at the bottom slot |

**Deviations from original 1994 / FreeCol behavior:**
- **No recruit selection on free emigration.** William Brewster's recruit *ban* (no servants/criminals on the dock) is applied — see [founding-fathers.md](founding-fathers.md) — but his `selectRecruit` *choice* (pick which dock slot emigrates for free) is a UI hook not yet wired, so free emigration still takes a **random** slot. Paid recruitment already lets the player choose a slot.
- **No religious-unrest modifier.** FreeCol's `updateImmigrationRequired` folds in a `RELIGIOUS_UNREST_BONUS` modifier; with no modifier system yet, the target simply rises by the increment (the modifier resolves to ×1 anyway in the classic base game).
- **Recruits reach the New World by ship.** Boarding a recruit onto a ship and sailing it home is implemented — see [transport.md](transport.md). (Recruits still can't be carried directly into an existing colony's population yet.)
- **Single (human) colonial player**: the recruitable pool is filtered only by `recruit-probability > 0`, omitting FreeCol's per-nation `canRecruitUnit`/availability checks (irrelevant until foreign powers exist).

## 3. Technical design

**Domain model:** all immigration state lives on `Game` (the single-player game state), alongside the parallel liberty/Founding-Father state:
- `Immigration` / `ImmigrationRequired` — the pool and its target.
- `RecruitDock` — the three unit-type ids on offer.
- `RecruitPrice` — the computed current price; `BaseRecruitPrice` / `RecruitLowerCap` — the escalating internals (persisted).

**Data sources:** `UnitType` gains two parsed fields — `RecruitProbability` (the spec's direct `recruit-probability` attribute, **not** inherited via `extends`) and `IsPerson` (the `model.ability.person` ability, resolved up the `extends` chain). Crosses are `model.goods.crosses` (`is-farmed=false`, `storable=false`), produced unattended by `model.building.chapel`/`church`/`cathedral`.

**Algorithms & formulas** (`GameSession/Game.cs`):
- `AccumulateImmigrationAndEmigrate()` (called from `EndTurn`, after liberty): drains each colony's crosses store into the pool (mirroring bells→liberty), adds the clamped Europe contribution, then auto-emigrates while `pool ≥ target` (random dock slot).
- `RecruitPrice` getter = `max(base · max(required−immigration,0) / required, floor)` — FreeCol `Europe.getCurrentRecruitPrice` verbatim.
- `ReduceImmigration()` — subtract the target, keep surplus (`saveProductionOverflow=true`).
- `DrawRecruitType()` — seeded weighted pick over `Ruleset.UnitTypes` by `RecruitProbability` (same pattern as Founding-Father offers; ADR-009 RNG, no `System.Random`).
- `Recruit(slot)` / `CheckRecruit(slot)` — the paid path; mirrors `ServerPlayer.csEmigrate`'s RECRUIT case (pay → `increaseRecruitmentDifficulty` → fall through to the NORMAL pool consume + target raise).

**Integration points:** runs in `Game.EndTurn` between `AccumulateLibertyAndElectFathers` and `AdvanceSailing`. The Europe penalty counts only **person** units with `Location == InEurope` that are **not aboard a ship** (a docked trade ship, or a recruit already boarded for home, does not suppress immigration — see [transport.md](transport.md)). `CheckFoundColony` now also rejects off-map units (Europe emigrants can't found colonies).

**Persistence:** save **v12** adds `Immigration`, `ImmigrationRequired`, `BaseRecruitPrice`, `RecruitLowerCap`, and `RecruitDock`. Pre-v12 saves load with the classic defaults (target 15, base 200, floor 80) and a freshly drawn dock.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `ImmigrationTests`: recruit-price formula (5 pinned cases), spec weights + person ability, dock determinism, colonyless +2 accrual, crosses→pool drain, threshold emigration, Europe-penalty clamp, paid-recruit escalation, recruit rejection, save round-trip, pre-v12 load | ✅ |
| L2 Scenario | Always | `JourneyTests.Journey5` (accrue → emigrate → penalty stall → paid recruit → save acid test); `SoakTests` (25-seed × 200-turn invariants hold with immigrants accumulating) | ✅ |
| L3 Interaction | Yes (Europe screen) | `EuropePanelTests.RecruitButton_BuysAColonistIntoEurope` (recruit from the dock via the real screen; gold debited) — see [europe.md](europe.md) | ✅ |
| L4 Visual | UI hidden in goldens | — | — |

- **FreeCol cross-check:** every number pinned against source, not the task brief — `initialImmigration=15`, `crossesIncrement=2`, `europeanUnitImmigrationPenalty=−4`, `playerImmigrationBonus=2`, `recruitPriceIncrease=30`, `lowerCapIncrease=0` (medium difficulty in `specification.xml`); `RECRUIT_PRICE_INITIAL=200`, `LOWER_CAP_INITIAL=80`, `MIGRANT_COUNT=3` (`Europe.java`/`MigrationType`). The recruit pool follows the spec's `recruit-probability` (so indentured servants and petty criminals are in it at weight 20, which the original task brief omitted).

## 5. Open issues / TODO

- [x] **Europe screen UI** — done ([europe.md](europe.md)): the dock, prices, immigration clock and a recruit button are on the Europe screen.
- [x] **Carry recruits home** — done in [transport.md](transport.md): board a recruit onto a ship and sail it to the New World.
- [x] **William Brewster's recruit ban** — done ([founding-fathers.md](founding-fathers.md)): elected, he keeps servants/criminals off the dock. (His `selectRecruit` *slot choice* still needs a UI hook.)
- [ ] **Fountain of Youth** burst immigration and the survival auto-recruit are not modelled.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Immigration accrual (crosses + Europe formula), 3-slot dock, weighted recruit draw, escalating recruit price, auto-emigration; save v12 | Phase 4 slice 4 |
