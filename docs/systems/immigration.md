# System: Immigration & Europe recruitment

| | |
|---|---|
| **Status** | Implemented (accrual + auto-emigration + paid recruitment; on the Europe screen; **per-player** — the foreign-power AI runs it too, FP-5) |
| **Last verified** | 2026-06-19 @ religious-unrest nation modifier (`86d3c7yca`) |
| **Code** | `game/src/GameLogic/GameSession/Player.cs` (per-player immigration/dock state), `GameSession/Game.cs` (accrual, dock, recruit — `Player`-parameterised), `Specification/UnitType.cs` (recruit weight + person) |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/ImmigrationTests.cs`, `ForeignPowerEconomyTests.cs` (the AI recruits onto its own dock with correctly-owned units), `Scenarios/JourneyTests.cs` (Journey 6) |
| **FreeCol reference** | `Player.java` (immigration/`reduceImmigration`/`updateImmigrationRequired`), `Europe.java` (`getCurrentRecruitPrice`, `getImmigration`), `ServerEurope.java` (`generateRecruitablesList`, `increaseRecruitmentDifficulty`), `ServerPlayer.java` (`csEmigrate`), classic `specification.xml` difficulty options |
| **Related systems** | [europe.md](europe.md) (sailing/trade), [colonies.md](colonies.md) (crosses production), [founding-fathers.md](founding-fathers.md) (the parallel liberty system), [save-load.md](save-load.md) |

## 1. How it works (plain English)

New colonists don't only come from babies born in your colonies — they also **emigrate from Europe** to your docks. Two things bring them:

- **Crosses** (religious enthusiasm). Every colony has a free **chapel** that makes **1 cross per turn**; build a church and it makes more. Crosses are the "religious unrest" that makes people in the old world want to start a new life across the ocean.
- A small **flat bonus** (+2 per turn) just for being a colonial power.

Some nations emigrate faster than others: the **English** are a nation of religious dissenters, so their people are **a third keener to leave** — their immigration target is **−33%**, and immigrants reach their docks sooner all game long. (Other nations have their own advantages elsewhere.)

These add up into an **immigration pool**. When the pool reaches the **target** (it starts at **15**, or **10** for the English), one colonist **emigrates** — it appears waiting on your **dock in Europe**. Each time that happens the target goes **up by 2**, so immigrants come a little slower as the game goes on.

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
| Initial target | **15** (`model.option.initialImmigration`), **× the nation's religious-unrest factor** (English `immigration` type ×0.67 → effective 10) |
| Emigrant produced | whenever pool ≥ target: one recruit leaves the dock for Europe; repeat while still ≥ target |
| On each emigration | pool reduced by the target (surplus kept — `saveProductionOverflow=true`); target **+2** (`crossesIncrement`) |
| Dock | **3** slots (`MigrantCount`), each a weighted-random recruitable unit type |
| Recruit weights | spec `recruit-probability`: free colonist / indentured servant / petty criminal **20**, experts **1**, everything else 0 |
| Recruit price | `max(base × max(target − pool, 0) / target, floor)`, base **200**, floor **80** |
| On each **paid** recruit | pay the current price; base **+30** (`recruitPriceIncrease`), floor **+0** (`lowerCapIncrease`); **then** also consume the pool + raise the target exactly as a free emigration does |
| Recruit placement | the unit lands in Europe (`InEurope`); the dock refills with a fresh weighted draw at the bottom slot |

**Deviations from original 1994 / FreeCol behavior:**
- **Recruit selection on free emigration (`86d3c9xft`).** William Brewster's recruit *ban* (no servants/criminals on the dock) is applied — see [founding-fathers.md](founding-fathers.md) — and his `selectRecruit` *choice* is now wired: a **human** who has elected Brewster (`model.ability.selectRecruit`) **pauses** when an emigrant is due (`Game.PendingEmigration`) and picks which dock slot emigrates via `Game.ChooseEmigrant(slot)`, instead of a random one auto-emigrating. A player **without** Brewster (and **every** AI) keeps the historical random auto-emigrate — so the RNG stream + goldens are byte-identical for the common case. The pending choice is **in-memory only** (not persisted this wave); a reloaded game re-pauses on the next turn the player is still due an emigrant.
- **Religious-unrest modifier** (`86d3c7yca`, FreeCol `RELIGIOUS_UNREST_BONUS`): the immigration target is reduced by the player's **nation-type** `religiousUnrestBonus` — the **English** (`immigration` nation type) carry **−33%**, so they need a third fewer points per emigrant, from the start (15 → effective 10). We store the **raw** target (so the save and the flat `crossesIncrement` growth are unchanged) and apply the factor on use (`EffectiveImmigrationRequired`), which is equivalent to FreeCol's reduced-stored value; the human defaults to **no nation** (×1), so a default game is byte-identical. The **first nation-type advantage modifier** wired (the pattern Dutch trade etc. will follow).
- **Recruits reach the New World by ship.** Boarding a recruit onto a ship and sailing it home is implemented — see [transport.md](transport.md). (Recruits still can't be carried directly into an existing colony's population yet.)
- **Per-player from FP-5**: every colonial player accrues immigration and recruits on its **own** dock, drawing from its **own** RNG stream (`RandomFor(player)`) — the foreign-power AI runs the same accrual + auto-emigration and recruits when affordable (capped Europe pile-up; see [players.md](players.md)). Recruited/emigrated units carry the owner's id (the human is 0), so a foreign power's colonists never land on the human's dock. The **recruit-dock availability filter** is `Game.IsRecruitable` — a unit type is offered only when its `recruit-probability > 0` and no elected father bans it (Brewster's `canRecruitUnit=false`); this is the filter both the initial dock and every refill draw through. (FreeCol's *per-nation* `canRecruitUnit`/availability beyond Brewster is still omitted.)

## 3. Technical design

**Domain model:** immigration state is **per player** (`Player`, ADR-019), alongside the parallel liberty/Founding-Father state; `Game`'s no-argument properties pass through to the human:
- `Immigration` / `ImmigrationRequired` — the pool and its target.
- `RecruitDock` — the three unit-type ids on offer.
- `RecruitPrice` — the computed current price; `BaseRecruitPrice` / `RecruitLowerCap` — the escalating internals (persisted).

**Data sources:** `UnitType` gains two parsed fields — `RecruitProbability` (the spec's direct `recruit-probability` attribute, **not** inherited via `extends`) and `IsPerson` (the `model.ability.person` ability, resolved up the `extends` chain). Crosses are `model.goods.crosses` (`is-farmed=false`, `storable=false`), produced unattended by `model.building.chapel`/`church`/`cathedral`.

**Algorithms & formulas** (`GameSession/Game.cs`):
- `AccumulateImmigrationAndEmigrate(player)` (called from `RunPlayerTurn` for every colonial player, after liberty): drains each of the player's colonies' crosses store into its pool (mirroring bells→liberty; the player's own fathers fold via `ApplyGoodsModifiers(player, …)`), adds the clamped Europe contribution (its own dock persons), then emigrates while `pool ≥ target`. A **human with William Brewster** (`model.ability.selectRecruit`) instead **pauses** on the first due emigrant — sets `_pendingEmigration` (a `PendingEmigrationChoice`) and returns, no RNG drawn for the choice; everyone else auto-emigrates a random dock slot drawn from `RandomFor(player)` exactly as before. `Game.Emigration.cs` (partial) holds `PendingEmigration`/`ChooseEmigrant(slot)` — the latter runs `Emigrate`+`ReduceImmigration`+target-raise (the same per-emigrant bookkeeping), then re-arms for a backlog or clears. **In-memory only** (not persisted).
- `RecruitPrice` getter = `max(base · max(required−immigration,0) / required, floor)` — FreeCol `Europe.getCurrentRecruitPrice` verbatim.
- `ReligiousUnrestFactor(player)` — the player's nation-type `religiousUnrestBonus` resolved to a multiplier (1.0 with no nation / no modifier; English ×0.67); `EffectiveImmigrationRequired(player) = round(ImmigrationRequired × factor)`. The auto-emigrate loop and the price/recruit checks compare the pool against this **effective** target (the raw `ImmigrationRequired` field still grows by the flat `crossesIncrement` and is what the save stores).
- `ReduceImmigration()` — subtract the **effective** target, keep surplus (`saveProductionOverflow=true`).
- `DrawRecruitType(player)` — seeded weighted pick over `Ruleset.UnitTypes` by `RecruitProbability`, drawing from `RandomFor(player)` (the human's stream 0, a foreign power's own stream); same pattern as Founding-Father offers (ADR-009 RNG, no `System.Random`).
- `Recruit(slot)` / `CheckRecruit(slot)` — the paid path; mirrors `ServerPlayer.csEmigrate`'s RECRUIT case (pay → `increaseRecruitmentDifficulty` → fall through to the NORMAL pool consume + target raise).

**Integration points:** runs in `Game.EndTurn` between `AccumulateLibertyAndElectFathers` and `AdvanceSailing`. The Europe penalty counts only **person** units with `Location == InEurope` that are **not aboard a ship** (a docked trade ship, or a recruit already boarded for home, does not suppress immigration — see [transport.md](transport.md)). `CheckFoundColony` now also rejects off-map units (Europe emigrants can't found colonies).

**Persistence:** save **v12** adds `Immigration`, `ImmigrationRequired`, `BaseRecruitPrice`, `RecruitLowerCap`, and `RecruitDock`. Pre-v12 saves load with the classic defaults (target 15, base 200, floor 80) and a freshly drawn dock.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `ImmigrationTests`: recruit-price formula (5 pinned cases), spec weights + person ability, dock determinism, colonyless +2 accrual, crosses→pool drain, threshold emigration, Europe-penalty clamp, paid-recruit escalation, recruit rejection, save round-trip, pre-v12 load | ✅ |
| L2 Scenario | Always | `JourneyTests.Journey6_AccrueImmigrationEmigrateThenRecruit` (accrue → emigrate → penalty stall → paid recruit → save acid test); `SoakTests` (25-seed × 200-turn invariants hold with immigrants accumulating) | ✅ |
| L3 Interaction | Yes (Europe screen) | `EuropePanelTests.RecruitButton_BuysAColonistIntoEurope` (recruit from the dock via the real screen; gold debited) — see [europe.md](europe.md) | ✅ |
| L4 Visual | UI hidden in goldens | — | — |

- **FreeCol cross-check:** every number pinned against source, not the task brief — `initialImmigration=15`, `crossesIncrement=2`, `europeanUnitImmigrationPenalty=−4`, `playerImmigrationBonus=2`, `recruitPriceIncrease=30`, `lowerCapIncrease=0` (medium difficulty in `specification.xml`); `RECRUIT_PRICE_INITIAL=200`, `LOWER_CAP_INITIAL=80`, `MIGRANT_COUNT=3` (`Europe.java`/`MigrationType`). The difficulty-driven ones — `crossesIncrement`, `recruitPriceIncrease`, `lowerCapIncrease` — now read from `Ruleset.Difficulty` (default medium 2/30/0; see [difficulty](difficulty.md)); `initialImmigration`/`europeanUnitImmigrationPenalty`/`playerImmigrationBonus` live in the base `gameOptions` group and move into the difficulty system's `GameOptions` bundle in slice 5. The recruit pool follows the spec's `recruit-probability` (so indentured servants and petty criminals are in it at weight 20, which the original task brief omitted).

## 5. Open issues / TODO

- [x] **Europe screen UI** — done ([europe.md](europe.md)): the dock, prices, immigration clock and a recruit button are on the Europe screen.
- [x] **Carry recruits home** — done in [transport.md](transport.md): board a recruit onto a ship and sail it to the New World.
- [x] **William Brewster's recruit ban** — done ([founding-fathers.md](founding-fathers.md)): elected, he keeps servants/criminals off the dock.
- [x] **William Brewster's `selectRecruit` slot choice** — done (`86d3c9xft`): a human with Brewster picks which dock recruit emigrates (`Game.PendingEmigration`/`ChooseEmigrant`, the `EmigrationChoicePanel` UI). In-memory only (a persisted pending choice is a follow-up).
- [x] **Fountain of Youth** burst immigration — done (`86d3c9ujx`): `Game.GenerateFountainRecruits` lands `dx` (medium 8) fresh recruits on the owner's Europe dock — see [lost-city-rumours](lost-city-rumours.md). Remaining: the survival auto-recruit.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-20 | **William Brewster's recruit selection** (`86d3c9xft`, FreeCol `selectRecruit`): a **human** who has elected Brewster (`model.ability.selectRecruit`) now **pauses** when an emigrant is due — `Game.PendingEmigration` (a `PendingEmigrationChoice`) offers the three dock recruits; `Game.ChooseEmigrant(slot)` lands the chosen one (refill + immigration consume + target raise, re-arming for a backlog). New `Game.Emigration.cs` partial + an `EmigrationChoicePanel` modal (opened from `GameController.RefreshView` when pending). A player **without** Brewster (and every AI) keeps the random auto-emigrate — RNG stream + goldens byte-identical. **In-memory only** (no save bump this wave). +5 L1 (`EmigrationChoiceTests`) + 1 L3 (`EmigrationChoicePanelTests`); 1320 L1 + golden suites green, zero goldens moved. See [presentation.md](../modules/presentation.md). | P7 wave-2 D (`86d3c9xft`) |
| 2026-06-19 | **Religious-unrest nation modifier** (`86d3c7yca`, FreeCol `RELIGIOUS_UNREST_BONUS`): the first nation-type advantage wired — `ReligiousUnrestFactor(player)` resolves the player's `EuropeanNation.NationType` `religiousUnrestBonus` (English `immigration` type −33%) and `EffectiveImmigrationRequired` reduces the immigration target on use (raw target still stored + grown flat → no save change; the auto-emigrate loop + `ReduceImmigration` use the effective value). Human defaults to no nation (×1) so a default game is byte-identical. The task's *recruit-dock availability filter* is the existing `IsRecruitable` (recruit-probability + Brewster). +2 L1 (`ImmigrationTests`: English target 15→10, save round-trip); 1123 + 4 soak green. | Phase 5 (`86d3c7yca`) |
|---|---|---|
| 2026-06-18 | **Crosses/recruit increments routed through the difficulty system** (`86d3c9y08` slice 4): `crossesIncrement`, `recruitPriceIncrease`, `lowerCapIncrease` now read `Ruleset.Difficulty.*` (default medium 2/30/0) instead of hardcoded consts. Behaviour-preserving at medium; no save change; soak byte-stable. (The base-`gameOptions` trio `initialImmigration`/`europeanUnitImmigrationPenalty`/`playerImmigrationBonus` is slice 5.) See [difficulty](difficulty.md). | Phase (`86d3c9y08` slice 4) |
| 2026-06-13 | Immigration accrual (crosses + Europe formula), 3-slot dock, weighted recruit draw, escalating recruit price, auto-emigration; save v12 | Phase 4 slice 4 |
| 2026-06-14 | FP-5: immigration/recruitment run **per player** — accrual + dock draws + emigrate use `RandomFor(player)`; the foreign-power AI recruits onto its own dock (capped), recruited/bought units carry the owner id; foreign docks seeded at New + topped up on load | FP-5 |
