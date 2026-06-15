# System: Founding Fathers (liberty & Congress)

| | |
|---|---|
| **Status** | Implemented (liberty accrual + election + a modifier/ability system; the effects that touch existing systems are applied) |
| **Last verified** | 2026-06-13 @ Phase 4 slice 8 |
| **Code** | `game/src/GameLogic/GameSession/Game.cs` (liberty/Congress/offers, `HasAbility`, `ApplyGoodsModifiers`), `Specification/FoundingFather.cs` (`FatherModifier`, `FatherAbility`) |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/FoundingFatherTests.cs`, `FoundingFatherEffectsTests.cs`, `Scenarios/JourneyTests.cs` (Journey 8) |
| **FreeCol reference** | `Player.java` (`getTotalFoundingFatherCost` line 1544), `Modifier.java`/`FeatureContainer.applyModifiers`, `<founding-father>` spec elements |
| **Related systems** | [colonies](colonies.md) (bell production), [immigration](immigration.md) (crosses, Brewster's recruit ban), [turns](turns.md), [save-load](save-load.md) |

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

### Effects (what an elected father does)

Each father carries **modifiers** (bonuses) and **abilities** (capabilities) from the spec. They're applied to the systems that exist today; the rest are parsed and ready but inert until their system lands.

| Father | Effect | Status |
|---|---|---|
| Thomas Jefferson | +50% bell production (→ liberty) | ✅ applied |
| William Penn | +50% cross production (→ immigration) | ✅ applied |
| Thomas Paine | bell production +the tax rate (`addTaxToBells`) | ✅ applied |
| William Brewster | no servants/criminals on the recruit dock (`canRecruitUnit=false`); `selectRecruit` | ✅ pool ban applied (recruit-slot choice is a UI hook) |
| Henry Hudson | +100% furs (applied in `TileYield` — see [colonies](colonies.md)) | ✅ applied (slice 8) |
| George Washington / Paul Revere / Hernán Cortés | combat: auto-promote / auto-arm a colony's last defender / native plunder bonus — see [combat](combat.md) | ✅ applied (5b/5c) |
| **Pocahontas** | on election, **resets all native alarm to Happy** (`resetNativeAlarm`); permanently **damps native alarm gains by −50%** (`nativeAlarmModifier`) — see [natives](natives.md) | ✅ applied |
| Ferdinand Magellan | +3 ship movement, −1 sail turn | ⏳ infra ready; deferred to a naval-movement slice |
| Adam Smith (factories), Peter Stuyvesant (custom house), La Salle (free stockade) | building unlocks/grants | ⏳ deferred |
| Benjamin Franklin, Jan de Witt, Francis Drake, Simón Bolívar | European diplomacy (Franklin/de Witt), naval (Drake), Sons-of-Liberty (Bolívar) | ⏳ deferred to their systems |

**Pocahontas (native peacemaker)** — the political father who calms the natives. Two effects, both faithful to FreeCol on our per-settlement alarm model:
- **On election:** every native settlement's alarm toward you drops to **0 (Happy)** — the `model.event.resetNativeAlarm` event (`ServerPlayer.java`). All grievances forgotten; braves fall below the raid threshold and stop attacking. *(Deviations: FreeCol also sets a native-nation `Stance.PEACE` object and only resets settlements you've **contacted** — we have neither a native-nation stance type nor fine contact-tracking, so we reset **all** settlements; the alarm-to-0 yields the same observable "no raids".)*
- **Permanently:** native alarm **gains are halved** — the `model.modifier.nativeAlarmModifier` (−50%, read from the spec), applied at the `ApplyNativeCombatTension` chokepoint to combat-driven alarm increases; **gains only** — goodwill and decay (negative deltas) are unaffected, so recovery isn't slowed. *Placeholder divergence:* in FreeCol this modifier damps only the per-turn **ambient proximity** alarm (`ServerPlayer.csNewTurn`); combat tension is applied raw. We have no ambient-alarm system yet, so combat is our only positive alarm source — applying it there gives Pocahontas a tangible ongoing effect; it moves to the ambient path when that lands (kanban follow-up). Both are human-gated (alarm is toward the human; a foreign power electing Pocahontas is a native-alarm no-op) and ride on the persisted Congress (no save change, no RNG). Identified by father id rather than parsing `<event>` (single-event minimalism).

**Franklin is deferred (a European-diplomacy father, not a native one).** Despite the original task's "natives offer peace" framing, FreeCol's Benjamin Franklin is purely European: `ignoreEuropeanWars` (the monarch can't drag you into European wars), `alwaysOfferedPeace` (the European AI always accepts your peace offer), and `peaceTreaty +50%` (offered peace holds). Faithful delivery needs a monarch/REF-war system, inter-European stance, and an AI diplomatic-trade flow — none of which exist — so Franklin is deferred to the European-diplomacy bucket (with de Witt). A "natives never raid" Franklin rule would be a fabrication that duplicates Pocahontas.

**Deviations / simplifications:** modifier **scopes** (e.g. `person`/non-person) are not yet evaluated — a father's goods modifier applies to the colony's whole production of that goods, which matches the player-visible result for the applied fathers (each of these goods has a single production source). Production modifiers are applied at the **drain** point (bells→liberty, crosses→immigration), which equals the per-colony total (one truncation, as FreeCol). **Age** (which weights apply) uses simple turn bands (1–99 / 100–199 / 200+) until the calendar exists; FreeCol keys age off the in-game year. The `factor` is fixed at 24 until the difficulty system lands.

## 3. Technical design

- `FoundingFather` (record): `Id`, `Type` (`FatherType` enum), age weights `Weight1/2/3`, `WeightForAge(age)`. Parsed from `<founding-father>` in the ruleset.
- `Game`: `Liberty`, `Congress`, `CurrentFather`, `OfferedFathers`, `CurrentAge`; `ChooseFather(id)`; static `FoundingFatherCost(electedCount, factor)` and instance `TotalFoundingFatherCost()`.
- **Modifier/ability system:** `FatherModifier(TargetId, Type, Value, Index)` with `ApplyTo` (additive / multiplicative / percentage, FreeCol `Modifier`); `FatherAbility(Id, Value, ScopeTypes)`. From **FP-5** the fold is **per player**: `Game.ApplyGoodsModifiers(player, goodsId, base)` folds *that player's* elected fathers' modifiers for a goods (ascending index, truncated to int — FreeCol `FeatureContainer.applyModifiers`) and adds that player's Paine tax-rate bell bonus; `HasAbilityFor(player, id)` queries its abilities. The public no-player overloads (`ApplyGoodsModifiers(goodsId, base)`, `HasAbility(id)`) delegate to the human for presentation/tests (behaviourally identical to before). The bell and cross drains run each player's totals through its own fold; the recruit pool filters out that player's father-banned types and its dock refreshes on its own election. So a foreign power's economy uses **its own** Congress, never the human's.
- Turn step `AccumulateLibertyAndElectFathers(player)` runs in `RunPlayerTurn` for every colonial player after its colonies: drains each of its colonies' bells into its liberty (modified by its own fathers), elects its chosen father if affordable, then refreshes its offers when nothing is pending. `GenerateOffers(player)` does the seeded weighted draw from `RandomFor(player)` (the human's stream 0, a foreign power's own stream) and is also called in `Game.New` for the human so choices exist from turn 1; a foreign power generates its offers lazily on its first turn and picks one via its own AI ([players.md](players.md)).
- **Persistence:** save v10 stores liberty, Congress, the current target, and the offered set (so a reload restores the same choice); pre-v10 loads empty. Father effects need no extra save state — they ride on the persisted Congress (modifiers/abilities are ruleset-derived).

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `FoundingFatherTests`: 25 fathers / 5 per type parsed; **cost formula vs FreeCol at factor 24 and 40**; offers one-per-category + deterministic; bells→liberty + warehouse drained; ChooseFather validation | ✅ |
| L2 Scenario | Always | election at the turn liberty reaches 24 (chosen father joins Congress, liberty resets, not re-offered); save round-trip preserves liberty/Congress/offers; pre-v10 compat. **Effects** (`FoundingFatherEffectsTests`): Jefferson +50% bells, Penn +50% crosses, Paine +tax% bells, Brewster's dock ban (50-seed) + `selectRecruit`, election refresh. **Pocahontas** (`NativeFatherEffectsTests`): on-election alarm reset to Happy across all settlements (+ a non-Pocahontas election doesn't reset, reset survives save round-trip, replay-stable); (`CombatTests`) the −50% halves a combat-win alarm gain (250 vs 500) and leaves a repelled-attack alarm drop at full magnitude. **Journey 8**: choose → accrue → elect Jefferson → subsequent bells boosted → survives reload | ✅ |
| L3 Interaction | No UI yet | — (Congress screen is a later slice) | — |
| L4 Visual | No screen yet | — | — |

- **FreeCol cross-check:** cost formula ported from `Player.java:1544` and pinned at two factors (the research workflow's factor-24 sequence was wrong — caught and corrected against source). Modifier application order/arithmetic from `Modifier.java`/`FeatureContainer.applyModifiers`; each applied father's value pinned to its `<modifier>`/`<ability>` in the spec.

## 5. Open issues / TODO

- [x] **Native father — Pocahontas** (alarm reset + −50% alarm-gain damping). Combat fathers (Washington/Revere/Cortés) done in 5b/5c.
- [ ] Apply the remaining **deferred** father effects as their systems land: movement (Magellan) + naval (Drake) → naval slice; buildings (Smith/Stuyvesant/La Salle); **European diplomacy (Benjamin Franklin, Jan de Witt)** → needs monarch/REF wars + inter-European stance + AI diplomatic-trade; Sons-of-Liberty (Bolívar). *(Hudson's +100% furs is applied — slice 8.)*
- [ ] Evaluate modifier **scopes** (person/non-person, unit-type) when per-source production modifiers are needed (bonus-resource yields).
- [ ] `selectRecruit` UI (choose which dock recruit emigrates); real age boundaries with the calendar; difficulty-driven `factor`; Congress / father-choice UI.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Liberty accrual from bells, cost formula, weighted offers, election; save v10 | Phase 4 slice 2 |
| 2026-06-13 | Modifier + ability system; applied father effects (Jefferson/Penn/Paine production, Brewster recruit ban); rest deferred | Phase 4 slice 7 |
| 2026-06-14 | Military fathers wired into combat: **George Washington** (`automaticPromotion` — every win promotes) and **Paul Revere** (`automaticEquipment` — auto-arm an unarmed colony defender); see [combat](combat.md) | Phase 5 slice 5b |
| 2026-06-14 | **Hernán Cortés** wired into combat: `plunderNatives` → a sacked native settlement yields its richer "extra" plunder range (treasure-train fee deferred); see [combat](combat.md) | Phase 5 slice 5c |
| 2026-06-15 | **Pocahontas** wired into native interaction: on election, `resetNativeAlarm` zeroes all native alarm toward the human (→ Happy); `nativeAlarmModifier` (−50%, spec-read) permanently damps combat alarm gains (`ScaleNativeAlarmGain` at `ApplyNativeCombatTension`, gains-only). Human-gated, rides on the persisted Congress (no save/RNG change). Franklin deferred (European-diplomacy father). See [natives](natives.md) | Phase 5 (#3 native fathers) |
