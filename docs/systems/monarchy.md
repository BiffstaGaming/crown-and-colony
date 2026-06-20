# System: Monarchy (the home-nation King)

| | |
|---|---|
| **Status** | In progress — tick+chooser (1), tax (2), tea party (3), mercenaries (4), support (5), ADD_TO_REF build-up (6); monarch tuning is now difficulty-data-driven (`86d3c9rg6`). Declare-Independence + war land in items 7-10. |
| **Last verified** | 2026-06-20 @ monarch declares war/peace (`86d3c9r7j`) |
| **Code** | `game/src/GameLogic/GameSession/Game.Monarch.cs`, `MonarchAction.cs`; `Randomness/RandomChoice.cs` |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/MonarchTests.cs` |
| **FreeCol reference** | `freecol/src/net/sf/freecol/common/model/Monarch.java` (`getActionChoices`, `actionIsValid`), the server monarch tick |
| **Related systems** | [europe](europe.md) (tax), [sons-of-liberty](sons-of-liberty.md), [independence](independence.md) *(arc in progress)* |

## 1. How it works (plain English)

Back home, your **King** keeps an eye on your colonies. Each turn — once the colony is a few decades old — he weighs his options and may do something: most turns he does **nothing**, but as the game goes on he grows bolder. He can **raise your taxes** (or, rarely, lower them), build up the army he'd send to crush a rebellion, declare war or peace on a rival on your behalf, or **offer you mercenaries and military support**. Early on, and while you have no colonies, he leaves you alone entirely.

This first piece is the King *deciding* — the weighted dice-roll each turn that picks what (if anything) he does. What each decision actually *does* to your game (the tax demand, the mercenary offer, the war fleet) is being added piece by piece.

**What the player sees and does:** nothing yet — the dialogs that surface the King's actions are a later (P7) UI task. For now the decision happens under the hood.

## 2. Detailed rules

- The King acts at most **once per turn**, decided in a single weighted pick. He does nothing at all when **any** of these hold (the *grace gate*, no dice rolled):
  - the turn number is below the **grace period** = `(6 − dx) × 10` = **30** at medium (`dx = 1 + monarchMeddling`, medium meddling = 2 → `dx = 3`);
  - the player has **no colonies**;
  - the player has already **declared independence** (is no longer a plain colonial power).
- Past the gate, the chooser offers **NO_ACTION** plus every *valid* action, each at its FreeCol weight, and picks one in proportion:

  | Action | Weight (medium) | Offered when valid… |
  |---|---|---|
  | NO_ACTION | `max(200 − turn, 100)` | always (dominates early, floors at 100) |
  | RAISE_TAX (act / war) | `5 + dx` = 8 each | tax < 65 |
  | LOWER_TAX (war / other) | `5 − dx` = 2 each | tax > 30 |
  | ADD_TO_REF | `10 + dx` = 13 | always (grows the [Royal Expeditionary Force](royal-expeditionary-force.md)) |
  | DECLARE_PEACE | `6 − dx` = 3 | a rival is at war with you |
  | DECLARE_WAR | `5 + dx` = 8 | a rival is at peace with you |
  | MONARCH_MERCENARIES | `6 − dx` = 3 | at war, not displeased, ≥ 200 gold |
  | SUPPORT_LAND | `3 − dx` | **only when `dx < 3`** — *never offered at medium* |
  | SUPPORT_SEA | `6 − dx` = 3 | raided by privateers, not yet granted, not displeased |
  | HESSIAN_MERCENARIES | `6 − dx` = 3 | ≥ 5000 gold, has colonies |

- `FORCE_TAX` and `DISPLEASURE` are **never** chosen by the dice — they are consequences of a player *response* (later items).

### Tax actions (item 2)

- **RAISE_TAX** opens a *demand* the player answers (accept / reject). The proposed new rate is `min(tax + 1 + rnd[0, 3 + turn/40), 65)`. The King names the player's **most valuable tradeable stockpile** (highest sale-value across colonies, one cargo's worth); a raise demand is only made when there is such a good.
  - **Accept** → the tax rises to the proposed rate.
  - **Reject, goods already gone** (sold/moved before answering) → the King raises it anyway, **+3** (FORCE_TAX).
  - **Reject, goods still in the colony** → a **Boston Tea Party**: the colony dumps the demanded goods overboard, the good is **boycotted** (back-tax = its sale price × 300 — it cannot be sold until paid off), rebel sentiment **surges** (+50% bell output for 25 turns, decaying −2%/turn), and the **tax is not raised**. Pay the back-tax (`PayArrears`, costs the full arrears) to lift the boycott.
- **LOWER_TAX** (war / other goodwill) applies immediately, no player choice: `max(tax − 1 − rnd[0, 8), 20)`.
- **WAIVE_TAX** is a message only — no change.

### Mercenary offers + the King's displeasure (item 4)

- **MONARCH_MERCENARIES** (offered at war, not displeased, ≥ 200 gold) and **HESSIAN_MERCENARIES** (≥ 5000 gold) offer a force of **veteran soldiers** (armed or mounted — 2-3 groups of 1-2), priced at the European purchase price **× 65%**, **trimmed to what your treasury can afford** (no affordable units → no offer).
- **Accept** → pay the gold, the veterans appear on your **Europe dock**. **Decline an offer you could afford** → the King is **displeased** (`DISPLEASURE`): he offers **no more mercenaries or military support** for the rest of the game.
- *Faithful-subset:* the mercenary force is veteran soldiers (the classic land mercenary); the naval man-o-war mercenary and full ability-driven type selection are simplified. The mercenary **price percentage** is difficulty-data-driven (`mercenaryPrice`, medium 65 — see [difficulty](difficulty.md)); the force **composition** is still a hardcoded faithful-subset.

### Free military support (item 5)

- **SUPPORT_SEA** (offered after a **privateer raid**, if not already granted and the King isn't displeased) grants **one free naval ship** on your Europe dock — a **one-shot** (`SupportSeaGranted`). **SUPPORT_LAND** grants free land troops (medium support level 2 = **2 mounted veterans**). Both are free and immediate (no demand).
- *Reachability:* `SUPPORT_LAND` is **never offered at medium** (the chooser only lists it when `dx < 3`); `SUPPORT_SEA` needs a privateer raid, which arrives with privateer combat later. Both handlers + validity gates are implemented and tested (forced via the handler) for fidelity now.

### Declare war / declare peace (item `86d3c9r7j`)

- **DECLARE_WAR** (offered when at least one European rival is at **peace** with you) — the King drags you into his war: he picks one peace-standing rival and sets your stance to **War**. No player choice (it's the King's decree, immediate).
- **DECLARE_PEACE** (offered when at least one rival is at **war or cease-fire** with you) — the King makes peace for you: he picks one such rival and sets your stance back to **Peace**.
- The King's target is chosen on **his own RNG** (never your stream 0), so a default game stays byte-identical. Stance change only — no tension delta is applied (the war simply *exists*), and no save change (stances already persist). A no-op if, despite the gate, no eligible rival remains.

**Deviations from original / FreeCol:** the chooser, weights, grace and validity gates are FreeCol's exactly. The action *effects* are wired in their own slices (this item is the decision only). The monarch's **tuning numbers** — meddling, tax cap/spread, mercenary price, support size, boycott factor and the REF base composition — are now **difficulty-data-driven** (`Ruleset.Difficulty.Monarch`, default = classic medium; see [difficulty](difficulty.md), [royal-expeditionary-force](royal-expeditionary-force.md)), so they are transposable per game-mode / variant. The only deliberate value-preserving deviation: the **boycott back-tax factor** stays the classic-game **300** (the spec's medium `arrearsFactor` is 500), kept on `MonarchOptions.ArrearsFactor` rather than read from the spec id. The remaining monarch numbers (`MINIMUM_TAX_RATE` 20, `MONARCH_MINIMUM_PRICE` 200, `HESSIAN_MINIMUM_PRICE` 5000, the FORCE_TAX +3 surcharge, the 25-turn tea-party bell duration) are FreeCol **code** constants (not difficulty options) and stay as C# consts.

## 3. Technical design

- `MonarchAction` (enum): the 15 FreeCol actions.
- `Game.MonarchActionIsValid(action)`: a pure, RNG-free predicate over the human's state (FreeCol `actionIsValid`). `ADD_TO_REF` is gated off until the REF Force is modelled (item 6).
- `Game.GetMonarchActionChoices(turn)`: the weighted `(weight, action)` list — empty before the grace gate; otherwise NO_ACTION + each valid action at its weight.
- `Game.RunMonarchTick()`: called once per round in `EndTurn` after `UpdateColonialStances`. If the chooser is empty it returns having drawn nothing; otherwise it picks one action via `RandomChoice.WeightedRandom` and dispatches it via `DispatchMonarchAction(action, monarchRng)`.
- **Tax (item 2):** `DispatchMonarchAction` handles `NO_ACTION`, `RAISE_TAX_*` (build a `PendingMonarchDemand` from `RaiseTaxAmount(rng)` + `GetMostValuableGoods`), `LOWER_TAX_*` (`SetTax(LowerTaxAmount(rng))` immediately), and `WAIVE_TAX` (no-op). `PendingMonarchDemand` (transient, not saved — the ADR-006 oracle the P7 UI reads) is answered by `RespondToMonarch(bool accept)`. `RaiseTaxAmount`/`LowerTaxAmount` are the FreeCol formulas; tax reuses the existing `Player.TaxRate` save field.
- **Boston Tea Party / boycott (item 3):** `RespondToMonarch(false)` → if the goods are gone, FORCE_TAX (`SetTax(taxRaise+3)`); else `HoldTeaParty` — `colony.AddGoods(good, −amount)`, `Market.SetArrears(good, salePrice × 300)`, `colony.TeaPartyBellTurns = 25`. `Market` carries per-good `Arrears` (>0 = boycotted; `CanTrade` gates both `SellColonyGoods`/`SellShipCargo`). `Colony.TeaPartyBellTurns` adds `+2%`/turn to bell output (`AccumulateLibertyAndElectFathers`) and ticks down each turn. `CheckPayArrears`/`PayArrears` (ADR-006) lift the boycott for the full arrears. Save **v37**: `SavedPlayer.Arrears` (omit-when-empty) + `SavedColony.TeaPartyBellTurns` (omit-when-0). Remaining actions stay no-op until their slice.
- **Declare war / peace (`86d3c9r7j`):** `DispatchMonarchAction` wires `DECLARE_WAR`/`DECLARE_PEACE` to `ImposeMonarchStance(stance, candidates, rng)` — it orders the candidate rivals (`MonarchPotentialEnemies` = peace-standing, for war; `MonarchPotentialFriends` = war/cease-fire, for peace) by player id, draws the King's pick on the **monarch RNG** (`rng.Next`, never stream 0), and calls `SetStance`. Empty candidate set → safe no-op. No tension delta, no save change (stances persist via the existing player-stance field). See [diplomacy](diplomacy.md).
- `RandomChoice.WeightedRandom(rng, choices)`: one weighted pick, one RNG draw (shared by the chooser, mercenary/support rolls, REF composition).
- **Determinism (ADR-009):** the Monarch is the human's King, but its roll must not shift the human's **stream 0** (that would change every existing seeded game past turn 30). So the tick seeds an **ephemeral** `Pcg32Random` from the human's *current* stream state (read non-destructively via `SaveState`) **+ the turn**, on a reserved stream id — it consumes nothing from stream 0, yet is fully reproducible across save/load (the human state and turn are persisted). A gated-out turn draws nothing. No save change in this item (the decision is derived; the first persisted monarch state arrives with the boycott/displeasure slices).

## 4. Verification

| Layer | Required? | Tests | Status |
|---|---|---|---|
| L1 Unit | Always | `MonarchTests`: `WeightedRandom` proportionality + determinism + empty-guard; chooser empty before grace / without settlements; FreeCol weights at turn 50/250; SUPPORT_LAND/ADD_TO_REF not offered at medium; validity oracle (tax bounds, Hessian gold gate, ForceTax/Displeasure never valid); **item 2**: raise/lower tax amounts + bounds (cap 65 / floor 20), `GetMostValuableGoods` pick + cargo cap + null-when-none, RAISE_TAX demand → accept raises / reject-goods-gone forces +3, LOWER applies immediately, respond-without-demand throws; **declare war/peace (`86d3c9r7j`)**: DECLARE_WAR sets the King's peace-standing pick to War, DECLARE_PEACE sets a war/cease-fire pick to Peace, every-rival-at-war → DECLARE_WAR not offered + dispatch is a safe no-op | ✅ |
| L2 Scenario | Always | `MonarchTests.MonarchTick_IsByteIdenticalAcrossTwinGames_PastGrace` (twin founded games stay byte-identical on stream 0 across 40 turns — the monarch never perturbs the human stream); the full existing suite + L5 soak stay green unchanged (the ephemeral RNG proof) | ✅ |
| L3/L4 | UI (P7) | The monarch-action dialogs are deferred to P7 | ⬜ |

- **FreeCol cross-check:** ✅ weights/grace/validity match `Monarch.getActionChoices`/`actionIsValid` (constants quoted in §2).

## 5. Open issues / TODO (the independence arc)

- [x] Monarch turn-tick + weighted action chooser (`86d3c9qvr`).
- [x] RAISE_TAX demand + tax mutation (`86d3c9r2m`) — accept/reject oracle.
- [x] Boston Tea Party + boycott/arrears + pay-to-lift (`86d3c9r4w`, save v37).
- [x] Monarch + Hessian mercenary offers + DISPLEASURE (`86d3c9rep`, save v38).
- [x] Monarch SUPPORT_LAND / SUPPORT_SEA (`86d3c9rag`, save v39).
- [x] REF build-up (ADD_TO_REF + Force) (`86d3c9v4j`, save v40) — see [royal-expeditionary-force](royal-expeditionary-force.md).
- [x] Monarch **declare war / declare peace** (`86d3c9r7j`) — `DECLARE_WAR`/`DECLARE_PEACE` now impose a stance on the human toward a King-chosen rival (`ImposeMonarchStance`, monarch RNG, no save change). See §2/§3.
- [ ] Declare Independence + continental muster (`86d3c9v28`).
- [ ] REF arrival + War of Independence combat (`86d3c9v8k`).
- [ ] Win (defeat REF) (`86d3c9vfn`) / Lose (last port) (`86d3c9vh1`).
- [x] Route the monarch tuning numbers (`monarchMeddling`/`maximumTax`/`taxAdjustment`/`mercenaryPrice`/`monarchSupport`/`refSize` + the boycott factor) through ruleset difficulty data (`86d3c9rg6`) — `Ruleset.Difficulty.Monarch`, value-preserving at medium. See [difficulty](difficulty.md).
- [ ] Persist monarch state per stateful slice (`86d3c9rk6`).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-20 | **Monarch declares war / peace** (`86d3c9r7j`, FreeCol `MONARCH_DECLARE_WAR`/`MONARCH_DECLARE_PEACE`): wired the two formerly-no-op dispatch cases — `DECLARE_WAR` sets the human at War with a King-chosen peace-standing rival, `DECLARE_PEACE` restores Peace with a war/cease-fire rival, via the new `ImposeMonarchStance(stance, candidates, rng)` over `MonarchPotentialEnemies`/`MonarchPotentialFriends`. The target is drawn on the **monarch RNG** (never stream 0); stance-only (no tension delta), **no save change** (stances already persist), safe no-op when no rival is eligible. The chooser already gated/offered both. +3 L1 (`MonarchTests`: war-pick, peace-pick, all-at-war → not offered + no-op); 1236 L1/L2 + soak byte-stable. See §2/§3, [diplomacy](diplomacy.md) | Phase 5 (`86d3c9r7j`) |
| 2026-06-20 | **Monarch tuning → ruleset data** (`86d3c9rg6`): the monarch's hardcoded C# tuning consts (`monarchMeddling`/`maximumTax`/`taxAdjustment`/`mercenaryPrice`/`monarchSupport` + the REF base composition `refSize`) moved into `Specification/MonarchOptions.cs` on `Ruleset.Difficulty.Monarch`, parsed from the `model.difficulty.monarch` group (and the nested `refSize` `unitListOption`). The monarch methods read `Ruleset.Difficulty.Monarch.*`; `BuildBaseRef` became instance. **Value-preserving at medium** (defaults equal the old consts → default game byte-identical, soak byte-stable; no save bump — difficulty is ruleset-derived). The boycott back-tax factor was also moved onto `MonarchOptions.ArrearsFactor` but **deliberately kept at the classic-game 300**, not read from the spec's medium `arrearsFactor` (500), to preserve behaviour. Remaining monarch numbers (min-tax/min-merc-price/Hessian-floor/FORCE_TAX surcharge/tea-party duration) are FreeCol code constants and stay consts. Removed the `TODO(86d3c9rg6)`. +7 L1 (parse-by-id + refSize + ArrearsFactor guard + grace/tax-cap/REF/merc-price behaviour proofs). See [difficulty](difficulty.md), [royal-expeditionary-force](royal-expeditionary-force.md). | Phase 5 (`86d3c9rg6`) |
| 2026-06-19 | **Monarch turn-tick + weighted action chooser**: `MonarchAction`, `MonarchActionIsValid`, `GetMonarchActionChoices` (FreeCol weights/grace/validity), `RunMonarchTick` in `EndTurn` (ephemeral RNG — stream 0 untouched, no save change), `RandomChoice.WeightedRandom`. Action effects deferred to later arc items | P6 (`86d3c9qvr`) |
| 2026-06-19 | **Tax mutation**: RAISE_TAX opens a `PendingMonarchDemand` (`RaiseTaxAmount`, `GetMostValuableGoods`) answered by `RespondToMonarch` (accept raises; reject-goods-gone forces +3; goods-present reject = tea party, item 3); LOWER_TAX/WAIVE_TAX dispatched. Tax reuses `Player.TaxRate` (no save bump) | P6 (`86d3c9r2m`) |
| 2026-06-19 | **Boston Tea Party + boycott**: goods-present reject dumps the goods, boycotts the good (`Market.Arrears` = salePrice×300, gates selling), surges bells (+50% for 25 turns decaying), tax unchanged; `CheckPayArrears`/`PayArrears` lift it. Save **v37** (`SavedPlayer.Arrears` + `SavedColony.TeaPartyBellTurns`, omit-when-default) | P6 (`86d3c9r4w`) |
| 2026-06-19 | **Mercenary offers + DISPLEASURE**: MONARCH/HESSIAN_MERCENARIES offer veteran soldiers (`LoadMercenaries`, price ×65% trimmed to affordable) into a `PendingMonarchDemand`; accept spends gold + spawns them in Europe, decline-when-affordable sets `Player.MonarchDispleasure` (gates future mercenaries/support). `ForceEntry`. Save **v38** (`SavedPlayer.MonarchDispleasure`, omit-when-false) | P6 (`86d3c9rep`) |
| 2026-06-19 | **Free support**: SUPPORT_SEA grants a free naval ship (one-shot, `Player.SupportSeaGranted`), SUPPORT_LAND grants 2 mounted veterans (`GetSupport`/`GrantSupport`); both free + immediate. SUPPORT_LAND never offered at medium, SUPPORT_SEA needs a privateer raid (`AttackedByPrivateers`). Save **v39** (`SavedPlayer.SupportSeaGranted`, omit-when-false) | P6 (`86d3c9rag`) |
| 2026-06-19 | **ADD_TO_REF build-up**: the King grows his Royal Expeditionary Force (`Force` model, `AddToRef` — navy to +10% then 1-3 land). Save **v40** (`SaveGame.RefForce`, omit-until-grown). See [royal-expeditionary-force](royal-expeditionary-force.md) | P6 (`86d3c9v4j`) |
