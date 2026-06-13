# System: Founding Fathers (liberty & Congress)

| | |
|---|---|
| **Status** | Implemented (liberty accrual + election; father *effects* are recorded but not yet applied) |
| **Last verified** | 2026-06-13 @ Phase 4 slice 2 |
| **Code** | `game/src/GameLogic/GameSession/Game.cs` (liberty/Congress/offers), `Specification/FoundingFather.cs` |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/FoundingFatherTests.cs` |
| **FreeCol reference** | `Player.java` (`getTotalFoundingFatherCost` line 1544), `<founding-father>` spec elements |
| **Related systems** | [colonies](colonies.md) (bell production), [turns](turns.md), [save-load](save-load.md) |

## 1. How it works (plain English)

Your colonies' town halls ring **liberty bells**; every bell becomes a **liberty point** for your nation. Spend enough liberty and a **Founding Father** joins your Continental Congress — a famous figure who (eventually) grants a lasting bonus. Each round you're **offered one candidate per category** (trade, exploration, military, political, religious); you pick which to recruit, and they're elected the turn your banked liberty reaches their cost. The more fathers you already have, the more liberty the next one costs.

**Worked example:** a new colony's town hall makes 1 bell a turn, so you bank 1 liberty a turn. The first father costs 24 — so after 24 turns, the candidate you chose joins Congress, your liberty resets, and a fresh slate of candidates is offered.

## 2. Detailed rules

- **Liberty** = total bells produced (bells are consumed into liberty each turn, not stockpiled as goods).
- **Cost of the next father** (FreeCol `getTotalFoundingFatherCost`, verified vs source): with `factor` = 24 (classic),
  - first father (none elected yet): `factor` → **24**
  - thereafter: `2 × (elected + 1) × factor + 1` → **97, 145, 193, 241, …**
- **Offers:** one father per category that still has an un-elected candidate with a non-zero weight for the current age; the candidate is drawn by **seeded weight** (each father has age-1/2/3 weights). Already-elected fathers are never re-offered.
- **Election:** when your chosen father's cost is met, they join Congress, that cost is subtracted from liberty, and a new offer set is generated.

**Deviations / simplifications:** father **effects** (abilities, modifiers) are parsed and the membership recorded, but they grant no gameplay bonus yet — a later slice. **Age** (which weights apply) uses simple turn bands (1–99 / 100–199 / 200+) until the calendar exists; FreeCol keys age off the in-game year. The `factor` is fixed at 24 until the difficulty system lands.

## 3. Technical design

- `FoundingFather` (record): `Id`, `Type` (`FatherType` enum), age weights `Weight1/2/3`, `WeightForAge(age)`. Parsed from `<founding-father>` in the ruleset.
- `Game`: `Liberty`, `Congress`, `CurrentFather`, `OfferedFathers`, `CurrentAge`; `ChooseFather(id)`; static `FoundingFatherCost(electedCount, factor)` and instance `TotalFoundingFatherCost()`.
- Turn step `AccumulateLibertyAndElectFathers` runs in `EndTurn` after colonies: drains each colony's bells into liberty, elects the chosen father if affordable, then refreshes offers when nothing is pending. `GenerateOffers` does the seeded weighted draw and is also called in `Game.New` so choices exist from turn 1.
- **Persistence:** save v10 stores liberty, Congress, the current target, and the offered set (so a reload restores the same choice); pre-v10 loads empty.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `FoundingFatherTests`: 25 fathers / 5 per type parsed; **cost formula vs FreeCol at factor 24 and 40**; offers one-per-category + deterministic; bells→liberty + warehouse drained; ChooseFather validation | ✅ |
| L2 Scenario | Always | election at the turn liberty reaches 24 (chosen father joins Congress, liberty resets, not re-offered); save round-trip preserves liberty/Congress/offers; pre-v10 compat | ✅ |
| L3 Interaction | No UI yet | — (Congress screen is a later slice) | — |
| L4 Visual | No screen yet | — | — |

- **FreeCol cross-check:** cost formula ported from `Player.java:1544` and pinned at two factors (the research workflow's factor-24 sequence was wrong — caught and corrected against source).

## 5. Open issues / TODO

- [ ] Apply father effects (abilities/modifiers) when the modifier system exists.
- [ ] Real age boundaries with the calendar; difficulty-driven `factor`.
- [ ] Congress / father-choice UI.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Liberty accrual from bells, cost formula, weighted offers, election; save v10 | Phase 4 slice 2 |
