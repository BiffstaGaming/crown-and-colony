# System: Combat

| | |
|---|---|
| **Status** | In development (Phase 5 slice 5a: combat *data* + the pure resolution *model*. The attack action, brave units, settlements and naval are slices 5b/5c.) |
| **Last verified** | 2026-06-14 @ Phase 5 slice 5a |
| **Code** | `game/src/GameLogic/Combat/Combat.cs` (`CombatModel`); data on `Specification/UnitType.cs` (`Offence`/`Defence`) + `Specification/TerrainType.cs` (`DefenceBonus`), parsed in `Ruleset.cs` |
| **Tests** | `game/tests/GameLogic.Tests/Combat/CombatModelTests.cs` |
| **FreeCol reference** | `freecol/src/.../common/model/SimpleCombatModel.java`; combat data in `freecol/data/rules/classic/specification.xml` |
| **Related systems** | [ruleset-data](ruleset-data.md), [units-movement](units-movement.md), [natives](natives.md) (alarm on attack), [founding-fathers](founding-fathers.md) (Washington/Revere/Drake/Cortés) |

## 1. How it works (plain English)

When one unit attacks another, the game weighs the attacker's **offence** against the defender's **defence**, then rolls the dice. The chance of winning is simply the attacker's strength as a share of the total: a power-3 attacker against a power-1 defender wins three times out of four.

Strength isn't just the base unit. The attacker gets a bonus for being on the offensive, but is penalised if it has nearly run out of movement, is wading ashore from a ship, or is artillery caught in the open. The defender is helped by rough ground (a hill doubles defence, a forest adds half again), by digging in (fortifying), and by sheltering in a settlement. The roll is also *graded* — a clear win versus a narrow one — which (in the next slice) decides whether the loser is merely beaten, demoted, captured, or destroyed.

**Worked example:**
> A brave (offence 1) attacks your free colonist (defence 1) who is fortified on a hill. The colonist's defence becomes 1 × 2 (hill) × 1.5 (fortified) = 3. The brave's offence becomes 1 × 1.5 (attack bonus) = 1.5. The brave's win chance is 1.5 / (1.5 + 3) ≈ 33%.

**What the player sees and does (later slice):** moving onto an enemy will attack instead of move; this slice is the underlying maths, with no attack button yet.

## 2. Detailed rules

**Base power** comes from the unit type (`Offence`/`Defence`), already folding the type's own modifiers: free colonist 0/1, brave 1/1, veteran soldier 0/1.5, colonial regular 3/4, king's regular 4/5, artillery 7/5. (An unarmed colonist or veteran has 0 offence — its punch comes from a soldier/dragoon *role*, which we don't model yet.)

**Attacker percentage modifiers** (pinned to the spec): attack bonus **+50%** (normal attacks); movement penalty **−33%** (small) / **−66%** (big); amphibious **−75%**; artillery-in-the-open **−75%**.

**Defender percentage modifiers**: terrain defence bonus (plains 0, marsh/swamp +25%, most forest +50%, rainForest +75%, hills +100%); fortified **+50%**; settlement defence bonus (camp/village +50%, capital +100%, city +100%, city capital +200% — from the settlement type).

**Odds & resolution**: win probability = `attack / (attack + defence)`. A random draw `r ∈ [0,1)` grades the result: `r < 0.1·win` → great win; `r < win` → win; `r ≥ 0.1·win + 0.9` → great loss; otherwise loss. (Land units don't *evade*; evasion/naval comes later.)

**Deviations from original / FreeCol:** the model matches `SimpleCombatModel`'s land path. Modifier *values* are pinned constants in code (a transposability-migration item — see [game-modes](game-modes.md) — alongside the other native tuning constants); roles/equipment, ambush, cargo penalty, and the demote/promote/capture *outcomes* arrive with the attack slice (5b).

## 3. Technical design

- **Data:** `UnitType.Offence`/`Defence` (doubles) are parsed in `Ruleset.ResolveCombatValue` — the base `offence`/`defence` attribute resolved up the `extends` chain, then the type's own `<modifier id="model.modifier.offence|defence">` folded in by ascending index (reusing `ModifierMath`). `TerrainType.DefenceBonus` is the tile's `model.modifier.defence` percentage.
- **Model:** `Combat.CombatModel` (pure, static) — `AttackPower(baseOffence, AttackContext)`, `DefencePower(baseDefence, DefenceContext)`, `WinProbability(attack, defence)`, `Resolve(winProbability, IGameRandom) → CombatResult`. Percentages compound multiplicatively (equivalent to FreeCol's same-index fold). `AttackContext`/`DefenceContext` are zero-default structs whose default is a normal attack (attack bonus opt-out via `WithoutAttackBonus` to dodge C#'s record-struct default-ctor rule).
- **Determinism:** resolution draws from an injected `IGameRandom`; the attack slice will use the game's main saved RNG so combat is resume-deterministic (ADR-009) without a separate saved stream.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `CombatModelTests`: unit offence/defence + terrain defence pinned to spec; attack/defence power with each modifier; odds formula; the graded resolution partition (via a fixed RNG) | ✅ |
| L2 Scenario | When stateful | — (no in-game combat yet; arrives with the attack slice) | ⬜ |
| L3 Interaction | When there's UI | — | — |
| L4 Visual | No screen | — | — |

- **FreeCol cross-check:** modifier values, base unit powers and the odds formula are pinned to `SimpleCombatModel` + the classic spec in `CombatModelTests`.

## 5. Open issues / TODO

- [ ] **Combat 5b — attack action:** brave units + minimal unit ownership (player vs native), `CheckAttack`/`Attack` on an adjacent enemy, outcomes (demote/promote/capture/slaughter via `UpgradeUnitType`), native alarm on attack (`ChangeNativeAlarm`, FreeCol `TENSION_ADD_*`). Unblocks George Washington (auto-promote) and Paul Revere (auto-arm).
- [ ] **Combat 5c — settlements & naval:** attacking/plundering/destroying native settlements (uses the parsed settlement defence + `<plunder>`), naval combat + evade/sink, foreign-European unit combat. Unblocks Drake and Cortés.
- [ ] Roles/equipment (soldier/dragoon) so colonists can be armed; ambush bonus; cargo penalty.
- [ ] Move the pinned modifier constants to ruleset data (transposability).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-14 | Combat foundation: parse unit `offence`/`defence` (+ folded modifiers) and terrain defence bonus; pure `CombatModel` (power, odds `att/(att+def)`, graded resolution) | Phase 5 slice 5a |
