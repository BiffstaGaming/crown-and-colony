# System: Combat

| | |
|---|---|
| **Status** | In development (Phase 5 slice 5b: the attack action, unit ownership, roles/equipment, brave defenders, the loser/winner outcomes, and native alarm on attack. Settlement assault, naval combat and foreign-European combat are slice 5c.) |
| **Last verified** | 2026-06-14 @ Phase 5 slice 5b |
| **Code** | `game/src/GameLogic/Combat/Combat.cs` (`CombatModel`); the attack action + roles/equipment + outcomes in `GameSession/Game.cs`; data on `Specification/UnitType.cs`, `Specification/TerrainType.cs`, `Specification/RoleType.cs`, `Specification/UnitChange.cs`, parsed in `Ruleset.cs`; unit owner/role on `Units/Unit.cs` |
| **Tests** | `Combat/CombatModelTests.cs` (pure model), `Specification/RoleTests.cs` (role/unit-change data), `GameSession/CombatTests.cs` (attack action, outcomes, fathers, equip, braves), `Persistence/SaveGameTests.cs` (v18) |
| **FreeCol reference** | `freecol/src/.../common/model/SimpleCombatModel.java` (power + `resolveAttack`), `Unit.java` (role helpers), `Tension.java` (alarm deltas); data in `freecol/data/rules/classic/specification.xml` (`<roles>`, `<unit-change-types>`) |
| **Related systems** | [ruleset-data](ruleset-data.md), [units-movement](units-movement.md), [natives](natives.md) (alarm on attack), [founding-fathers](founding-fathers.md) (Washington/Revere), [save-load](save-load.md) |

## 1. How it works (plain English)

When one unit attacks another, the game weighs the attacker's **offence** against the defender's **defence**, then rolls the dice. The chance of winning is simply the attacker's strength as a share of the total: a power-3 attacker against a power-1 defender wins three times out of four.

Strength is the unit plus its **equipment**. A bare colonist can't fight — it has no offence. Arm it with 50 muskets and it becomes a **soldier** (+2 offence, +1 defence); add 50 horses and it's a **dragoon** (+3/+2). Equipment comes from a colony's warehouse: standing in the colony, a colonist trades muskets and horses for a military role. Native **braves** are the same idea from the other side — a settlement's warriors, who can pick up captured muskets to become armed braves.

The defender is also helped by rough ground (a hill doubles defence, a forest adds half again), by digging in, and by sheltering in a settlement. The roll is **graded** — a clear win versus a narrow one — which decides how badly the loser comes off.

**What happens to the loser**, in order:
- A unit that is **doomed on any loss** (a brave, a scout) is **killed**.
- An **armed** loser is **disarmed** first: it loses one step of equipment (a dragoon drops to a soldier, a soldier to an unarmed colonist) and the *winner can capture that equipment* — a brave that beats your soldier walks away armed with his muskets. A disarmed unit usually survives.
- An **unarmed but capturable** colonist is **taken prisoner** (it switches sides).
- Some units **demote** instead — artillery that loses becomes damaged artillery, and damaged artillery that loses is destroyed.
- Otherwise the loser is **killed**.

**The winner** may be **promoted**: a free colonist soldier who wins decisively can become a veteran soldier, and a veteran a colonial regular. Normally only a *decisive* win promotes, and only by chance — but with **George Washington** in your Congress, *every* win promotes. **Paul Revere** is the defensive counterpart: if natives ever attack a colony whose last colonist is unarmed, that colonist grabs muskets from the warehouse to defend.

**Attacking natives makes them angry.** Striking at a brave raises its settlement's alarm; killing the brave raises it a lot more. Enough hostility and the settlement turns from wary to hateful.

**What the player does (no combat UI yet):** this slice is the rules and the data — arming a colonist, attacking an adjacent brave, the outcomes and the alarm. The on-map combat buttons come later.

**Worked example:**
> Your free colonist, armed as a soldier (offence 0 + 2 = 2), attacks a native brave (defence 1) in the open. The soldier's offence with the +50% attack bonus is 2 × 1.5 = 3; the brave's defence is 1. The win chance is 3 / (3 + 1) = 75%. If you win, the brave (doomed on any loss) is killed and the settlement's alarm jumps. If you lose, your soldier drops his muskets and walks home a plain colonist — and the brave picks the muskets up.

## 2. Detailed rules

**Base power** comes from the unit type (`UnitType.Offence`/`Defence`) plus the unit's **role** (`RoleType.Offence`/`Defence`): free colonist 0/1, brave 1/1, artillery 7/5, veteran soldier 0/1.5 (its punch is the role); roles — soldier +2/+1 (50 muskets), dragoon +3/+2 (50 muskets + 50 horses), scout +1/+1 (50 horses), armed brave +2/+1 (25 muskets, native-only), mounted brave +1/+1 (25 horses), native dragoon +3/+2.

**Attacker percentage modifiers** (FreeCol `GENERAL_COMBAT_INDEX`): attack bonus **+50%**; movement penalty **−33%** (2 movement points left) / **−66%** (1 left); amphibious **−75%**; artillery-in-the-open **−75%** (a `bombard` unit fighting outside a settlement).

**Defender percentage modifiers**: terrain defence bonus (plains 0, marsh +25%, most forest +50%, rainForest +75%, hills +100%); fortified **+50%**; settlement defence bonus (5c).

**Odds & resolution**: win probability = `attack / (attack + defence)`; a draw `r ∈ [0,1)` grades it — `r < 0.1·win` great win, `r < win` win, `r ≥ 0.1·win + 0.9` great loss, else loss.

**Loser outcome precedence** (FreeCol `SimpleCombatModel.resolveAttack`, land open-field path):
1. **dispose-on-combat-loss** (type or role: brave, scout) → **slaughter**.
2. **offensive role** → disarm: the winner captures the equipment (`Unit.canCaptureEquipment` — the winner has `captureEquipment` and a military `role-change` matching `from = winner role`, `capture = loser role`, available to its side) and arms itself into that role; the loser steps to `Role.Downgrade ?? default`. Then, if the loser is *dispose-on-all-equipment-lost* with no downgrade it is slaughtered, or if *demote-on-all-equipment-lost* it is type-demoted.
3. **canBeCaptured + winner captureUnits** (not amphibious) → **capture the unit**: it changes owner, taking the `capture` unit-change (veteran soldier → free colonist) if one exists, and is disarmed.
4. a **demotion** unit-change exists → **demote the type** (artillery → damaged artillery).
5. otherwise → **slaughter**.

**Winner promotion** (FreeCol): if the winner's type has a `promotion` unit-change and either it has `automaticPromotion` (Washington) **or** the result was a *great* win and `100·r ≤ probability`, it promotes (free colonist → veteran soldier → colonial regular). The promotion roll is a **second draw** from the same RNG, taken only when needed (`great && !automatic`).

**Native alarm on attack** (per attacked settlement; FreeCol `Tension.TENSION_ADD_*`): attacking a brave adds **+200** (`TENSION_ADD_NORMAL`) to its nearest settlement's alarm; if the brave dies, **+400** more (`TENSION_ADD_UNIT_DESTROYED`). Nation-level tension and propagation are slice 5c.

**Deviations from original / FreeCol (deliberate, 5b scope):**
- **Player-initiated open-field unit combat against native braves only.** Braves are stationary defenders placed on land **adjacent to** their settlement (one per settlement); assaulting the settlement tile itself, settlement defence/destruction/plunder, naval, foreign-European and native-initiated combat are slice 5c.
- **Tension is per-settlement** with flat deltas; the nation-level store + `getSlaughterTension` location routing are 5c.
- **Paul Revere applies the defence bonus without consuming/persisting the 50 muskets** (FreeCol's `AUTOEQUIP_UNIT` permanence is deferred); native auto-equipment is dormant until native settlements stock goods.
- The **veteran-with-role percentage fold** (a unit type's own +50% baked into `UnitType.Defence` before a role additive) has **no live 5b path** (only Revere on a free colonist defends with a role); it is flagged for 5c.
- Modifier *values*, tension deltas, the promotion probability and the great-win band are FreeCol-pinned constants in code (transposability-migration items — see [game-modes](game-modes.md)).

## 3. Technical design

- **Data:** `RoleType` (offence/defence summed from index-30 additive modifiers, required-goods, downgrade, granted/required abilities, `role-change` capture rules) and `UnitChange` (from→to + probability) parse in `Ruleset.ParseRoles`/`ParseUnitChanges`. `UnitType` gains combat-ability flags (`DisposeOnCombatLoss`, `CanBeCaptured`, `CaptureUnits`, `CaptureEquipment`, `DisposeOnAllEquipmentLost`, `DemoteOnAllEquipmentLost`, `Bombard`). `Ruleset.GetUnitChange` and `Ruleset.CaptureRole` are the lookups combat uses.
- **Model:** `Combat.CombatModel` is unchanged and still pure — roles fold into the **scalar base** passed to `AttackPower`/`DefencePower` (`UnitType.Offence + RoleType.Offence`), landing at FreeCol's index 30, before the index-50 situational percentages.
- **State:** `Unit.OwnerNationId` (null = player) + `RoleId`/`RoleCount`. `Game.CheckAttack`/`Attack` follow the `MoveCheck` + `CheckX`/`X` pattern; `Attack` builds the contexts, resolves through `CombatModel`, then `ResolveLoserOutcome` + `ApplyWinnerPromotion` apply the precedence above and `ChangeNativeAlarm` raises tension. `EquipRole`/`CheckEquipRole` arm a colonist from a colony's stock (charging the role-goods delta). `EffectiveCombatRole` implements FreeCol `getAutomaticRole` (Revere). Braves spawn in `Game.New` via `SpawnUnit(type, pos, ownerNationId)` on `FreeAdjacentLand`, consuming no RNG draw.
- **Determinism (ADR-009):** the public `Attack` draws from the game's **main saved RNG** (`_random`); an `internal Attack(..., IGameRandom)` overload lets tests force an outcome band. `UpgradeUnitType` preserves owner/role/roleCount so a promotion/demotion/capture survives save/resume. Native units are excluded from `CurrentlyVisible`/`IsVisible` (braves don't lift the player's fog).
- **Save:** format **v18** adds `SavedUnit.Owner`/`Role`/`RoleCount` (default role written as null → v17 player saves are byte-identical); braves round-trip through the unit list via `Owner`.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `CombatModelTests` (power/odds/resolution + role fold); `RoleTests` (roles, unit-changes, capture-roles, combat abilities pinned to spec); `CombatTests` (CheckAttack gating, slaughter/disarm/demote/promote outcomes, equip-capture chains, alarm deltas, Washington, Revere, EquipRole, brave garrison, fog exclusion) | ✅ |
| L2 Scenario | When stateful | `CombatTests` save round-trip (v18 owner/role/braves); `SaveGameTests` pre-v18 default load; determinism via the injected-RNG attack path | ✅ |
| L3 Interaction | When there's UI | — (no combat UI yet) | — |
| L4 Visual | No screen | — | — |

- **FreeCol cross-check:** role/unit-change values, the loser-outcome precedence, the tension deltas and the power numbers are pinned to `SimpleCombatModel`, `Unit.java`, `Tension.java` and the classic spec in the tests above.

## 5. Open issues / TODO

- [ ] **Combat 5c — settlements & naval:** assaulting/plundering/destroying native settlements (settlement defence + `<plunder>`), naval combat + evade/sink, foreign-European unit combat, native-initiated attacks (native AI). Unblocks Drake and Cortés, and exercises the capture-unit and Revere paths end-to-end.
- [ ] Nation-level tension store + cross-settlement propagation + `getSlaughterTension` location routing.
- [ ] Persist Revere's auto-equipped muskets (`AUTOEQUIP_UNIT`); native auto-equipment once settlements stock goods.
- [ ] Split a unit type's own combat percentage out of the baked `UnitType.Offence/Defence` scalar before any veteran-with-role path (5c).
- [ ] Apply role movement bonuses (dragoon/scout +9) to unit movement.
- [ ] Move the pinned modifier/tension/promotion constants to ruleset data (transposability, [game-modes](game-modes.md)).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-14 | Combat 5b: unit ownership + roles/equipment (`RoleType`, `UnitChange`, `EquipRole`), brave defenders, the attack action (`CheckAttack`/`Attack`) with the FreeCol loser/winner outcome precedence (slaughter/disarm/equipment-capture/demote/promote), native alarm on attack, Washington & Revere; save v18 | Phase 5 slice 5b |
| 2026-06-14 | Combat foundation: parse unit `offence`/`defence` and terrain defence bonus; pure `CombatModel` (power, odds, graded resolution) | Phase 5 slice 5a |
