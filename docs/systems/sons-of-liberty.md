# System: Sons of Liberty

| | |
|---|---|
| **Status** | In development (Slice A: model + bar shipped; Slice B: production bonus pending) |
| **Last verified** | 2026-06-16 @ per-colony SoL model + colony-screen bar (577 L1+L2 + scene suite green) |
| **Code** | `game/src/GameLogic/Colonies/Colony.cs` (liberty + SoL properties), `game/src/GameLogic/GameSession/Game.cs` (`AccumulateLibertyAndElectFathers`) |
| **Tests** | `game/tests/GameLogic.Tests/Colonies/SonsOfLibertyTests.cs`, `game/presentation/tests/ColonyPanelTests.cs` (the bar) |
| **FreeCol reference** | `freecol/src/.../common/model/Colony.java` (`calculateSoLPercentage`/`calculateRebelCount`/`calculateToryCount`/`calculateProductionBonus`/`modifyLiberty`, L1251–1352), `freecol/data/rules/classic/specification.xml` (government limits, `model.difficulty.medium`) |
| **Related systems** | [colonies.md](colonies.md), [founding-fathers.md](founding-fathers.md), [turns.md](turns.md) |

## 1. How it works (plain English)

Every colony has a mood. As its town hall produces **liberty bells**, the colonists increasingly side with the revolution — they become **Sons of Liberty** (rebels). The rest stay loyal to the Crown (**royalists/tories**). A colony's **Sons-of-Liberty %** is how much of its population has come over to the rebel cause.

**The rules, in plain words:**
- Each colonist needs **200 liberty** to count as a rebel. So a colony's SoL% = its banked liberty ÷ (200 × population), as a percentage (capped at 100%).
- **Rebels** = that percentage of the population (rounded down); everyone else is a **royalist**.
- High membership makes the colony work harder; very low membership in a big colony makes it sulk. At **50%+** every worker produces **+1**, at **100%** **+2**; if a colony has **more than 6** royalists it's "bad government" (**−1** per worker), more than **10** royalists is "very bad" (**−2**). *(This production effect is the next slice — today the bonus is shown but not yet applied to output.)*
- Liberty also still flows to your **founding-father** progress, exactly as before — the same bells feed both your colony's mood and your nation's congress.

**Worked example:**
> A 5-colonist colony has banked 600 liberty. 600 ÷ (200 × 5) = 60%, so it's **60% Sons of Liberty** → 3 rebels, 2 royalists. Because membership is ≥ 50%, it earns a **+1** production bonus.

**What the player sees and does:** the colony screen shows a **Rebels · Population · Royalists** band with the SoL% and the production bonus, over a gold/dark membership meter. The player raises SoL by producing more bells (staffing the town hall) and keeping colonies from growing faster than their bell output.

## 2. Detailed rules

| Input / condition | Result |
|---|---|
| liberty `L`, population `P` (`P>0`) | SoL% = `clamp(0, 100, floor(L·100 / (200·P)))` |
| empty colony (`P=0`) | SoL% = 0 (no divide-by-zero) |
| SoL% `s`, population `P` | rebels = `s·P / 100` (integer); royalists = `P − rebels` |
| SoL ≥ 100 | production bonus **+2** |
| SoL ≥ 50 (and < 100) | **+1** |
| royalists > 10 | **−2** |
| royalists > 6 (and ≤ 10) | **−1** |
| otherwise | **0** (a small colony — ≤ 6 royalists — never gets a penalty regardless of low SoL) |
| bells produced this turn `b` (FF-modified) | colony liberty `+= b`; floored at 0; clamped to `200·P` once SoL reaches 100 |

**Deviations from original 1994 / FreeCol behavior:**
- **No bell upkeep yet (Slice A).** FreeCol nets bell **consumption** (each colonist past the first two eats 1 bell/turn) before banking, so a big low-bell colony can *lose* liberty. We currently bank the gross (FF-modified) figure — so SoL only rises. The net-of-upkeep accumulation lands with the production-bonus slice. *(This is why the `AddLiberty` floor-at-0 path exists but isn't yet exercised by the turn loop.)*
- **Production bonus computed, not applied (Slice A).** `ProductionBonus` is exposed + displayed, but not yet added to tile/building output — so the economy is byte-identical to pre-SoL. Applying it is Slice B (it perturbs economy goldens by ±N per worker, soak-verified).
- **Government limits are medium-difficulty.** FreeCol's `badGovernmentLimit`/`veryBadGovernmentLimit` (and the bonus limits) are *difficulty options* and differ per level (veryEasy 8/12 … medium **6/10** … veryHard 4/8). We hardcode **medium** (the classic default), consistent with the other tuning constants; they must become data-driven when a difficulty system lands.
- **Simón Bolívar (+SoL) not folded in** — that father is unimplemented; the owner-level SoL modifier is a follow-up.
- **Liberty feeds both pools the same figure** — FreeCol's `modifyLiberty(amount)` adds the identical bell figure to the colony's liberty *and* the player's founding-father pool. We match that (the colony gets the same FF-modified bells the player banks).

## 3. Technical design

**Domain model:** `Colony` holds the stored `Liberty` field and four pure computed properties — `SonsOfLiberty`, `RebelCount`, `ToryCount`, `ProductionBonus` — each an integer function of `Liberty` + `Population` (single source of truth; the colony screen reads these, computes nothing). `AddLiberty(int)` floors at 0 and applies the 100%-SoL cap (`Liberty = 200·Population`).

**Data sources:** the four government-limit constants mirror `model.difficulty.medium` in `specification.xml`; `LibertyPerRebel = 200` is FreeCol's `Colony.LIBERTY_PER_REBEL` code constant.

**Algorithms & formulas:** see §2. `RebelCount` uses integer division (`SoL·P/100`) — bit-identical to FreeCol's `(int)floor(0.01·sol·uc)` and float-free for ADR-009.

**Integration points:** `Game.AccumulateLibertyAndElectFathers` (once per colonial player, **after** all `RunColonyTurn` calls so population is settled) drains each colony's freshly produced bells, banks the FF-modified figure to `player.Liberty` (founding fathers — unchanged), and calls `colony.AddLiberty(sameFigure)`. The 100%-cap reads the already-settled population.

**Persistence:** `SavedColony.Liberty` (`int?`, save **v22**, additive — omitted when 0, so a no-liberty colony is byte-identical to v21; ≤v21 saves load 0 = SoL 0%).

**Determinism (ADR-009):** RNG-free — reads `Liberty` + `Population` only, all integer arithmetic, advances no PCG stream. Stream-stable; cannot perturb byte-stable replay.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `SonsOfLibertyTests` — SoL% (half/full/over/truncate/empty), rebel+tory split (sum=pop), bonus tiers incl. the 6/10 penalty pins, `AddLiberty` floor + 100%-cap, per-turn accumulation tracking the player pool, v22 save round-trip (+ omitted-when-0, pre-v22 loads 0) | ✅ |
| L2 Scenario | When economy-touching | (Slice B: production-bonus yield deltas + soak before/after) | ⬜ (Slice B) |
| L3 Interaction | The bar | `ColonyPanelTests.SonsOfLibertyBar_ShowsRebelsRoyalistsAndBonus_FromColonyLiberty` (pop 5 / liberty 600 → Rebels 3, 60%, Bonus +1, Royalists 2, 40%) | ✅ |
| L4 Visual | Optional | — | ⬜ |
| L5 Soak | When economy-touching | (Slice B: no new starvation from the bonus) | ⬜ (Slice B) |

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-16 | **Slice A — model + bar.** Per-colony `Liberty` (save v22) + `SonsOfLiberty`/`RebelCount`/`ToryCount`/`ProductionBonus` computed properties; banked alongside the founding-father pool in `AccumulateLibertyAndElectFathers` (same figure to both). Colony-screen Rebels/Population/Royalists band + SoL% + production-bonus + a membership meter (presentation reads the properties only, ADR-006). Bonus **computed but not applied** → zero economy churn. +25 L1 `SonsOfLibertyTests` + 1 L3. No upkeep yet. | Phase 5 colony UI |
