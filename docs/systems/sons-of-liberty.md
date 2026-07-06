# System: Sons of Liberty

| | |
|---|---|
| **Status** | Implemented (model + bar + production bonus + bell upkeep + Simón Bolívar) |
| **Last verified** | 2026-06-18 @ government limits routed through the difficulty system (`86d3c9y08` slice 2) |
| **Code** | `game/src/GameLogic/Colonies/Colony.cs` (liberty + SoL properties + preferred-size oracles), `game/src/GameLogic/GameSession/Game.cs` (`AccumulateLibertyAndElectFathers`), `game/presentation/ColonyPanel.cs` (`PreferredSizeHint`) |
| **Tests** | `game/tests/GameLogic.Tests/Colonies/SonsOfLibertyTests.cs`, `game/tests/GameLogic.Tests/Colonies/ColonyPreferredSizeTests.cs`, `game/presentation/tests/ColonyPanelTests.cs` (the bar) |
| **FreeCol reference** | `freecol/src/.../common/model/Colony.java` (`calculateSoLPercentage`/`calculateRebelCount`/`calculateToryCount`/`calculateProductionBonus`/`modifyLiberty`, L1251–1352), `freecol/data/rules/classic/specification.xml` (government limits, `model.difficulty.medium`) |
| **Related systems** | [colonies.md](colonies.md), [founding-fathers.md](founding-fathers.md), [turns.md](turns.md) |

## 1. How it works (plain English)

Every colony has a mood. As its town hall produces **liberty bells**, the colonists increasingly side with the revolution — they become **Sons of Liberty** (rebels). The rest stay loyal to the Crown (**royalists/tories**). A colony's **Sons-of-Liberty %** is how much of its population has come over to the rebel cause.

**The rules, in plain words:**
- Each colonist needs **200 liberty** to count as a rebel. So a colony's SoL% = its banked liberty ÷ (200 × population), as a percentage (capped at 100%).
- **Rebels** = that percentage of the population (rounded down); everyone else is a **royalist**.
- High membership makes the colony work harder; very low membership in a big colony makes it sulk. At **50%+** every worker produces **+1**, at **100%** **+2**; if a colony has **more than 6** royalists it's "bad government" (**−1** per worker), more than **10** royalists is "very bad" (**−2**). This applies to every colonist working a tile or a building, floored so no one ever produces below 0 — **per worker**, so in a building a productive colonist keeps its output even when a co-worker is dragged to 0. In a **building** the bonus is added to a worker's output *before* its expert bonus, so a master who **doubles** a good doubles the liberty bonus too; and the **lumber mill, cathedral (×2) and the factory tier (×1.5)** scale the bonus up — good government rewards those buildings more (their `rebel-factor`).
- Liberty also still flows to your **founding-father** progress, exactly as before — the same bells feed both your colony's mood and your nation's congress.

**Worked example:**
> A 5-colonist colony has banked 600 liberty. 600 ÷ (200 × 5) = 60%, so it's **60% Sons of Liberty** → 3 rebels, 2 royalists. Because membership is ≥ 50%, it earns a **+1** production bonus.

**What the player sees and does:** the colony screen shows a **Rebels · Population · Royalists** band with the SoL% and the production bonus, over a gold/dark membership meter. Under the population count a small **preferred-size hint** advises whether the colony has room to grow: "**+N room to grow**" (it can add N colonists before the bonus would drop), "**−N overcrowded**" (it should shed N colonists to recover its bonus), or "**at ideal size**". The player raises SoL by producing more bells (staffing the town hall) and keeping colonies from growing faster than their bell output.

**Preferred-size worked example:**
> A colony of 5 has banked 900 liberty → 90% Sons of Liberty (+1 bonus). Spreading that same 900 over more colonists dilutes the percentage; it stays at or above 50% up to a population of 9, then drops below at 10. So the hint reads "**+4 room to grow**" — four more colonists can join before the +1 bonus is at risk. A crowded colony of 12 with no liberty has 12 royalists (very-bad government, −2); shedding 2 brings it to 10 royalists and recovers a tier, so its hint reads "**−2 overcrowded**".

## 2. Detailed rules

| Input / condition | Result |
|---|---|
| liberty `L`, population `P` (`P>0`) | SoL% = `clamp(0, 100, floor(L·100 / (200·P)))` |
| empty colony (`P=0`) | SoL% = 0 (no divide-by-zero) |
| SoL% `s`, population `P` | rebels = `s·P / 100` (integer); royalists = `P − rebels` |
| SoL ≥ 100 | production bonus **+2** |
| SoL ≥ 50 (and < 100) | **+1** |
| royalists > 6 | **−1** (Col1's SINGLE bad-government tier; FreeCol adds a second −2 "very bad" tier above 10 tories that we **drop** for Col1 fidelity — `86d3kgbtj`) |
| otherwise | **0** (a small colony — ≤ 6 royalists — never gets a penalty regardless of low SoL) |
| Bolívar (`model.modifier.SoL`) in the owner's Congress | SoL% gets **+20** (added after the conversion, before the clamp) |
| net bells this turn `n = (FF-modified gross) − max(0, P−2)` | colony liberty `+= n` (floored at 0; clamped to `200·P` once SoL ≥ 100); player founding-father pool `+= n` (floored at 0) |
| government change at projected population `u` | project SoL% + tories at `u` (same banked liberty) and compare tiers vs the current SoL%/tories → **+1** (improves), **0** (unchanged), **−1** (deteriorates) — FreeCol `governmentChange` |
| units to add | largest `i∈1..10` with no deterioration when population grows by `i` (one short of the first `−1`); capped at 10 |
| units to remove | smallest `i∈1..P−1` whose removal first yields a `+1` improvement; 0 if none helps |
| preferred-size change | `ProductionBonus < 0` → `−(units to remove)` (shed); else `+(units to add)` (grow) |

**Deviations from original 1994 / FreeCol behavior:**
- **Bell upkeep is in.** Each colonist past the first two consumes 1 bell/turn (FreeCol `unitsThatUseNoBells` = 2), netted off before banking — so a colony that outgrows its bell output *loses* liberty and its SoL falls. A growing colony must **staff its town hall** to keep electing fathers; the net figure also feeds the player's founding-father pool (floored at 0), so it shifts election timing for large colonies. The "2 free colonists" value comes from the difficulty level (`Ruleset.Difficulty.UnitsThatUseNoBells`, 2 on every classic level; see [difficulty](difficulty.md)).
- **Production bonus display (tile badges).** The bonus is applied to actual colony output (Slice B), but the colony-screen *tile badges* still show each tile's base yield (the bonus is reflected in the SoL bar, not per-tile). Showing the effective per-tile yield is a presentation follow-up.
- **Government limits come from the difficulty level.** FreeCol's `badGovernmentLimit`/`veryBadGovernmentLimit` (and the good/very-good bonus limits) are *difficulty options* and differ per level (the tory-penalty limits tighten: veryEasy 8/12 … medium **6/10** … veryHard 4/8). They are now parsed into `Ruleset.Difficulty.Government` and carried on the colony (`Colony.Government`, defaulting to medium), so the production-bonus tiers shift with the chosen level — see [difficulty](difficulty.md).
- **Simón Bolívar (+20 SoL)** — implemented as a standing modifier: `Colony.SolModifierBonus` (the owner's Congress `model.modifier.SoL` sum) is added to the SoL percentage after the liberty→% conversion (FreeCol's order), refreshed from Congress on election/founding/load. Not a one-time liberty bake, so it stays correct as colonies grow/starve. See [founding-fathers](founding-fathers.md).
- **Liberty feeds both pools the same figure** — FreeCol's `modifyLiberty(amount)` adds the identical bell figure to the colony's liberty *and* the player's founding-father pool. We match that (the colony gets the same FF-modified bells the player banks).

## 3. Technical design

**Domain model:** `Colony` holds the stored `Liberty` field and four pure computed properties — `SonsOfLiberty`, `RebelCount`, `ToryCount`, `ProductionBonus` — each an integer function of `Liberty` + `Population` (single source of truth; the colony screen reads these, computes nothing). `AddLiberty(int)` floors at 0 and applies the 100%-SoL cap (`Liberty = 200·Population`).

**Preferred-size advisory (read-only oracles, ADR-006):** `Colony.GovernmentChange(int unitCount)` projects the SoL% and tory count the colony *would* have at a hypothetical population (keeping the current banked liberty and Bolívar bonus) via private `ProjectedSonsOfLiberty`/`ProjectedToryCount`, then runs FreeCol's `governmentChange` band comparison (Good/Very-Good SoL bands, Bad/Very-Bad tory bands) against the current tiers → +1/0/−1. `UnitsToAdd()` loops 1..`PreferredSizeChangeUpperBound` (=10) returning one short of the first deterioration; `UnitsToRemove()` loops 1..`Population−1` returning the first improvement; `PreferredSizeChange()` returns `−UnitsToRemove()` when the current bonus is negative, else `UnitsToAdd()`. All derived (never persisted), integer-only, RNG-free — the colony screen reads `PreferredSizeChange()` and formats the hint.

**Data sources:** the four government limits come from the selected difficulty level — `Ruleset.Difficulty.Government` (a `GovernmentLimits` value, default `model.difficulty.medium` = 100/50/6/10), set on `Colony.Government` at founding/load (the colony carries the value, not a `Ruleset` reference, keeping the colony logic pure). `LibertyPerRebel = 200` is FreeCol's `Colony.LIBERTY_PER_REBEL` code constant. See [difficulty](difficulty.md).

**Algorithms & formulas:** see §2. `RebelCount` uses integer division (`SoL·P/100`) — bit-identical to FreeCol's `(int)floor(0.01·sol·uc)` and float-free for ADR-009.

**Integration points:** `Game.AccumulateLibertyAndElectFathers` (once per colonial player, **after** all `RunColonyTurn` calls so population is settled) drains each colony's freshly produced bells, banks the FF-modified figure to `player.Liberty` (founding fathers — unchanged), and calls `colony.AddLiberty(sameFigure)`. The 100%-cap reads the already-settled population.

**Production-bonus application:** the bonus reaches output in `Game`'s colony turn. **Tiles** add it per worker after the tile yield, floored at 0 (`Math.Max(0, TileYield + ProductionBonus)`). **Buildings** (`RunBuildingProduction`, 86d3b6nrz slice 5) fold it into each worker's base *before* the unit's index-30 expert modifier (FreeCol `COLONY_PRODUCTION_INDEX = 20 < EXPERT_PRODUCTION_INDEX = 30`), scaled by the building's `rebel-factor` (`rebelBonus = floor(ProductionBonus × BuildingType.RebelFactor)`, default 1; lumber mill/cathedral 2, factory 1.5), then floors **each worker** at 0 before summing — so a multiplicative expert multiplies the bonus, the boosted buildings get more of it, and a negative bonus can't push another worker's output below 0. The bonus rides the same input-scarcity ratio as the rest of the output. The unattended town-hall bell and the colony-centre tile are excluded (FreeCol bonuses only worker production). See [colonies](colonies.md) §3.

**Persistence:** `SavedColony.Liberty` (`int?`, save **v22**, additive — omitted when 0, so a no-liberty colony is byte-identical to v21; ≤v21 saves load 0 = SoL 0%).

**Determinism (ADR-009):** RNG-free — reads `Liberty` + `Population` only, all integer arithmetic, advances no PCG stream. Stream-stable; cannot perturb byte-stable replay.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `SonsOfLibertyTests` — SoL% (half/full/over/truncate/empty), rebel+tory split (sum=pop), bonus tiers incl. the 6/10 penalty pins, `AddLiberty` floor + 100%-cap, per-turn accumulation tracking the player pool, v22 save round-trip (+ omitted-when-0, pre-v22 loads 0), the bonus reaching tile + building output (+2/worker), **bell upkeep (first two colonists free; a colony outgrowing its bells loses liberty)**. `PrintingPressTests` — printing press +50% / newspaper +100% bell bonus parsed + applied (101 → 151 liberty; 51 → 102). `ColonyPreferredSizeTests` — `GovernmentChange` direction (same-pop 0, grow-past-Good −1, shrink-out-of-Very-Bad +1), `UnitsToAdd` (4 for the pop-5/liberty-900 colony, bound-capped for a tiny high-liberty one), `UnitsToRemove` (2 for the crowded colony, 0 at full SoL), `PreferredSizeChange` sign (+ healthy / − overcrowded / FreeCol empty-colony math) | ✅ |
| L2 Scenario | When economy-touching | Existing economy suites unchanged (bonus is 0 below goodGovernment, so pop≤6/SoL-0 colonies are byte-identical — verified zero churn) | ✅ |
| L3 Interaction | The bar | `ColonyPanelTests.SonsOfLibertyBar_ShowsRebelsRoyalistsAndBonus_FromColonyLiberty` (pop 5 / liberty 600 → Rebels 3, 60%, Bonus +1, Royalists 2, 40%). The `PreferredSizeHint` label rides the same `SonsOfLibertyBar` render (a `PreferredSizeChange`-driven "room to grow / overcrowded / ideal" line under the population count) | ✅ |
| L4 Visual | Optional | — | ⬜ |
| L5 Soak | When economy-touching | The 200-turn soak stays green with the bonus applied — the floor-at-0 guards prevent negative production; no new starvation | ✅ |

## Changelog

| 2026-07-07 | **Overcrowding penalty capped at −1 (Col1 single tier)** (`86d3kgbtj`, Col1 online-fidelity pass — [col1-online-fidelity-review-2026-07](../reference/col1-online-fidelity-review-2026-07.md) Tier 1 #7): `Colony.ProductionBonus` **dropped the −2 "very bad government" tier** (FreeCol `> veryBadGovernmentLimit` → −2) — Col1 has a **single** bad-government tier of −1 (> 6 tories). `Government.VeryBad` is still parsed (the option is preserved) but no longer read for the bonus; the preferred-size hint (`GovernmentChange`) collapsed to the single tier to match. A large low-liberty colony now loses 1/worker, not 2. RNG-free; soak determinism + round-trip green (the 4 correctness soak tests pass; the perf micro-benchmark is the known machine-load flake, passes in isolation). Updated 5 tests to the single tier (`SonsOfLibertyTests`, `ColonyPreferredSizeTests` ×3, `BuildingWorkerTypeProductionTests`) + the `DifferentialFidelityTests` FreeCol-ref note. See §1 + feature-parity row 487. | Col1 fidelity (`86d3kgbtj`) |
| 2026-07-01 | **Colony preferred-size advisory** (`86d3fpy8p`): new read-only oracles on `Colony` — `GovernmentChange(unitCount)` (FreeCol `Colony.governmentChange`, projecting SoL%/tories at a hypothetical population against the current tiers → +1/0/−1), `UnitsToAdd()` / `UnitsToRemove()` (looping to `PreferredSizeChangeUpperBound` = 10 / `Population−1`), and `PreferredSizeChange()` (`−UnitsToRemove` when the bonus is negative, else `UnitsToAdd`). The colony screen surfaces a `PreferredSizeHint` label under the population count ("+N room to grow" / "−N overcrowded" / "at ideal size"). All derived, RNG-free, never persisted (reuses the existing `ProductionBonus` government tiers); zero economy churn. +10 L1 (`ColonyPreferredSizeTests`). | Phase (`86d3fpy8p`) |
| 2026-06-18 | **Government limits routed through the difficulty system** (`86d3c9y08` slice 2): the four production-bonus thresholds (`veryGood`/`good`/`bad`/`veryBad GovernmentLimit`) are now parsed into `Ruleset.Difficulty.Government` (a `GovernmentLimits` value) and carried on `Colony.Government` (set at founding/load; default medium 100/50/6/10), replacing the four hardcoded `Colony` consts — `ProductionBonus` reads them. The colony stays free of a `Ruleset` dependency (it holds the small value, not the ruleset). Behaviour-preserving at the default (medium); no save change (re-derived at load); soak byte-stable. The "must become data-driven" debt note is now resolved. +3 L1 (`DifficultyOptionsTests`). See [difficulty](difficulty.md). | Phase (`86d3c9y08` slice 2) |
| 2026-06-18 | **Building production bonus made FreeCol-faithful** (`86d3b6nrz` slice 5): the per-worker building SoL bonus is now folded into each worker's output **before** its index-30 expert modifier and scaled by the building's `rebel-factor` (new `BuildingType.RebelFactor`; lumber mill/cathedral 2, factory tier 1.5), and floored **per worker** instead of pooled. So a multiplicative expert multiplies the bonus (master distiller at +1 → 8 rum, was 7), rebel-factor buildings get more of it (lumber mill at +1 → +2/worker), and a −2 bonus no longer wipes a productive colonist's output when a co-worker floors. The tile path was already correct. Driven by a 10-agent adversarial review of the per-worker production fold. Zero churn where `ProductionBonus == 0` or `RebelFactor == 1` with no multiplicative expert (every prior test colony); +5 L1 in `BuildingWorkerTypeProductionTests`. See [colonies](colonies.md). | Phase 3 (`86d3b6nrz` slice 5) |

| Date | Change | Commit |
|---|---|---|
| 2026-06-16 | **Printing press + newspaper bell multiplier** (`86d3c9p33`): a colony's bell output is boosted before banking — printing press **+50%**, newspaper **+100%** (`BuildingType.BellBonus` from the building's `model.goods.bells` percentage, `Game.BellProductionBonus`). Applied in `AccumulateLibertyAndElectFathers` to the gross bells, before the founding-father fold and bell upkeep, feeding both the colony's SoL liberty and the player's father pool — so a press/newspaper speeds Sons-of-Liberty growth and father elections. Inert for existing colonies (none had a press → zero churn). +4 L1 (`PrintingPressTests`). | Phase 5 (`86d3c9p33`) |
| 2026-06-16 | **Bell upkeep + Bolívar.** Banking now nets bell **upkeep** (each colonist past the first two eats 1 bell — `AccumulateLibertyAndElectFathers`), so a colony that outgrows its bell output loses liberty (SoL falls) and the player's founding-father pool nets it too (floored at 0). **Simón Bolívar** grants a standing **+20 SoL%** to his player's colonies (`Colony.SolModifierBonus` from Congress, folded into `SonsOfLiberty`). +4 L1 (2 upkeep + the Bolívar suite in `FoundingFatherEffectsTests`). Journey 4 re-derived (a growing colony must staff its town hall to keep electing fathers — faithful). | Phase 5 (Sons of Liberty) |
| 2026-06-16 | **Slice B — production bonus applied.** `Colony.ProductionBonus` (+2/+1/0/−1/−2) now adds to each attended worker's output in `RunColonyTurn` (tile workers) and `RunBuildingProduction` (building workers), floored at 0; the unattended colony square + town-hall bell are excluded. Zero churn on the 579 existing tests (bonus is 0 below goodGovernment, which every existing test colony is) + soak green (floor-at-0 prevents negative production). +2 L1 application tests. **Bell upkeep still deferred** (broader FF-timing change). | Phase 5 (Sons of Liberty) |
| 2026-06-16 | **Slice A — model + bar.** Per-colony `Liberty` (save v22) + `SonsOfLiberty`/`RebelCount`/`ToryCount`/`ProductionBonus` computed properties; banked alongside the founding-father pool in `AccumulateLibertyAndElectFathers` (same figure to both). Colony-screen Rebels/Population/Royalists band + SoL% + production-bonus + a membership meter (presentation reads the properties only, ADR-006). Bonus **computed but not applied** → zero economy churn. +25 L1 `SonsOfLibertyTests` + 1 L3. No upkeep yet. | Phase 5 colony UI |
