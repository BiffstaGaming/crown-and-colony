# System: Monarchy (the home-nation King)

| | |
|---|---|
| **Status** | In progress — the per-turn monarch tick + weighted action chooser (independence arc item 1). Action effects (tax/mercenaries/support/REF) land in later items. |
| **Last verified** | 2026-06-19 @ monarch tick + chooser (`86d3c9qvr`) |
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
  | ADD_TO_REF | `10 + dx` = 13 | the REF has unit types *(modelled in item 6)* |
  | DECLARE_PEACE | `6 − dx` = 3 | a rival is at war with you |
  | DECLARE_WAR | `5 + dx` = 8 | a rival is at peace with you |
  | MONARCH_MERCENARIES | `6 − dx` = 3 | at war, not displeased, ≥ 200 gold |
  | SUPPORT_LAND | `3 − dx` | **only when `dx < 3`** — *never offered at medium* |
  | SUPPORT_SEA | `6 − dx` = 3 | raided by privateers, not yet granted, not displeased |
  | HESSIAN_MERCENARIES | `6 − dx` = 3 | ≥ 5000 gold, has colonies |

- `FORCE_TAX` and `DISPLEASURE` are **never** chosen by the dice — they are consequences of a player *response* (later items).

**Deviations from original / FreeCol:** the chooser, weights, grace and validity gates are FreeCol's exactly. The action *effects* are wired in their own slices (this item is the decision only). `monarchMeddling`/`maximumTax` are temporary code constants pending the ruleset-constants pass (`86d3c9rg6`).

## 3. Technical design

- `MonarchAction` (enum): the 15 FreeCol actions.
- `Game.MonarchActionIsValid(action)`: a pure, RNG-free predicate over the human's state (FreeCol `actionIsValid`). `ADD_TO_REF` is gated off until the REF Force is modelled (item 6).
- `Game.GetMonarchActionChoices(turn)`: the weighted `(weight, action)` list — empty before the grace gate; otherwise NO_ACTION + each valid action at its weight.
- `Game.RunMonarchTick()`: called once per round in `EndTurn` after `UpdateColonialStances`. If the chooser is empty it returns having drawn nothing; otherwise it picks one action via `RandomChoice.WeightedRandom` and dispatches it. `DispatchMonarchAction` currently handles `NO_ACTION` only — unwired actions pass harmlessly until their slice lands.
- `RandomChoice.WeightedRandom(rng, choices)`: one weighted pick, one RNG draw (shared by the chooser, mercenary/support rolls, REF composition).
- **Determinism (ADR-009):** the Monarch is the human's King, but its roll must not shift the human's **stream 0** (that would change every existing seeded game past turn 30). So the tick seeds an **ephemeral** `Pcg32Random` from the human's *current* stream state (read non-destructively via `SaveState`) **+ the turn**, on a reserved stream id — it consumes nothing from stream 0, yet is fully reproducible across save/load (the human state and turn are persisted). A gated-out turn draws nothing. No save change in this item (the decision is derived; the first persisted monarch state arrives with the boycott/displeasure slices).

## 4. Verification

| Layer | Required? | Tests | Status |
|---|---|---|---|
| L1 Unit | Always | `MonarchTests`: `WeightedRandom` proportionality + determinism + empty-guard; chooser empty before grace / without settlements; FreeCol weights at turn 50/250; SUPPORT_LAND/ADD_TO_REF not offered at medium; validity oracle (tax bounds, Hessian gold gate, ForceTax/Displeasure never valid) | ✅ |
| L2 Scenario | Always | `MonarchTests.MonarchTick_IsByteIdenticalAcrossTwinGames_PastGrace` (twin founded games stay byte-identical on stream 0 across 40 turns — the monarch never perturbs the human stream); the full existing suite + L5 soak stay green unchanged (the ephemeral RNG proof) | ✅ |
| L3/L4 | UI (P7) | The monarch-action dialogs are deferred to P7 | ⬜ |

- **FreeCol cross-check:** ✅ weights/grace/validity match `Monarch.getActionChoices`/`actionIsValid` (constants quoted in §2).

## 5. Open issues / TODO (the independence arc)

- [x] Monarch turn-tick + weighted action chooser (`86d3c9qvr`).
- [ ] RAISE_TAX demand + tax mutation (`86d3c9r2m`) — accept/reject oracle.
- [ ] Boston Tea Party + boycott/arrears + pay-to-lift (`86d3c9r4w`).
- [ ] Monarch + Hessian mercenary offers + DISPLEASURE (`86d3c9rep`).
- [ ] Monarch SUPPORT_LAND / SUPPORT_SEA (`86d3c9rag`).
- [ ] REF build-up (ADD_TO_REF + Force) (`86d3c9v4j`).
- [ ] Declare Independence + continental muster (`86d3c9v28`).
- [ ] REF arrival + War of Independence combat (`86d3c9v8k`).
- [ ] Win (defeat REF) (`86d3c9vfn`) / Lose (last port) (`86d3c9vh1`).
- [ ] Route `monarchMeddling`/`maximumTax` through ruleset constants (`86d3c9rg6`); persist monarch state per stateful slice (`86d3c9rk6`).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-19 | **Monarch turn-tick + weighted action chooser**: `MonarchAction`, `MonarchActionIsValid`, `GetMonarchActionChoices` (FreeCol weights/grace/validity), `RunMonarchTick` in `EndTurn` (ephemeral RNG — stream 0 untouched, no save change), `RandomChoice.WeightedRandom`. Action effects deferred to later arc items | P6 (`86d3c9qvr`) |
