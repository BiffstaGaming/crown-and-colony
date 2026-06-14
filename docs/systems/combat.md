# System: Combat

| | |
|---|---|
| **Status** | In development (Phase 5: slice 5b = the attack action, unit ownership, roles/equipment, brave defenders, the loser/winner outcomes, and native alarm; slice 5c = **native settlement assault** (attack / plunder / destroy). Naval combat, foreign-European unit combat and native-initiated raids are the foreign-powers slice.) |
| **Last verified** | 2026-06-14 @ Phase 5 slice 5c |
| **Code** | `game/src/GameLogic/Combat/Combat.cs` (`CombatModel`); the attack + settlement-assault actions, roles/equipment + outcomes in `GameSession/Game.cs`; data on `Specification/UnitType.cs`, `TerrainType.cs`, `RoleType.cs`, `UnitChange.cs`, `NativeNationType.cs` (`SettlementType`/`SettlementPlunder`), parsed in `Ruleset.cs`; unit owner/role on `Units/Unit.cs` |
| **Tests** | `Combat/CombatModelTests.cs` (pure model), `Specification/RoleTests.cs` (role/unit-change data), `GameSession/CombatTests.cs` (attack action, outcomes, fathers, equip, braves, **settlement assault/plunder**), `Persistence/SaveGameTests.cs` (v19) |
| **FreeCol reference** | `freecol/src/.../common/model/SimpleCombatModel.java` (power + `resolveAttack`, settlement branch), `Unit.java` (role helpers), `Tension.java` (alarm deltas), `RandomRange.java` (`getAmount` → plunder); data in `freecol/data/rules/classic/specification.xml` (`<roles>`, `<unit-change-types>`, `<plunder>`) |
| **Related systems** | [ruleset-data](ruleset-data.md), [units-movement](units-movement.md), [natives](natives.md) (settlements, alarm), [founding-fathers](founding-fathers.md) (Washington/Revere/Cortés), [save-load](save-load.md) |

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

**Sacking a settlement.** You can also attack a native settlement itself. It defends with its warriors *and* its walls — a camp adds half again to its defence, a capital doubles it — so you'll usually want artillery or massed soldiers. Win and you **sack it**: the settlement is destroyed and you carry off **plunder** (gold — a camp gives a few hundred, a rich village far more, and **Hernán Cortés** doubles the take and always finds treasure). The whole nation seethes: its *other* settlements' alarm jumps. Lose, and your attacker is beaten back — disarmed or, for artillery, smashed to damaged artillery — while that settlement turns on you. (You can't simply walk onto a native settlement; you attack it, or trade/talk from beside it.)

**What the player does (no combat UI yet):** this slice is the rules and the data — arming a colonist, attacking an adjacent brave or a settlement, the outcomes, plunder and alarm. The on-map combat buttons come later.

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

**Native alarm from combat** (FreeCol `defenderTension`, applied across the defender nation's *every* settlement via `csModifyTension`): tension is driven by the **outcome**, not the act of attacking. A European **win** raises it; a **repelled** attack *lowers* it (the natives prevailed, their tension toward you eases). Open-field (5b): killing a brave adds **+500** (`UNIT_DESTROYED` +400 + a minor insult `TENSION_ADD_MINOR` +100); a loss subtracts **−100** (−`MINOR`), or **−300** if your unit is slain (−`MINOR` − `NORMAL`).

**Settlement assault (5c).** A player land unit attacks a native settlement *tile* (`CheckAttackSettlement`/`AttackSettlement`). The settlement defends with an **implicit garrison**: a brave's defence (1) times its **settlement defence bonus** (camp/village +50%, capital/city +100%, city capital +200%); the open-tile terrain bonus, ambush, and the REF-only bombard bonus do **not** apply in a settlement. One graded combat draw resolves it:
- **Attacker wins** → the settlement is **sacked**: the attacker may promote (as above), takes **plunder** (next paragraph), the nation's alarm shifts (below), and the settlement is **destroyed** (removed from the map).
- **Attacker loses** → the attacker runs the loser-outcome precedence (disarmed / artillery → damaged artillery / slaughtered); the settlement survives and the nation's alarm *eases*.

**Plunder** (FreeCol `RandomRange.getAmount`, `continuous=false`): the sacked settlement's `<plunder>` range pays `(rnd[0, max−min] + min) × factor` gold, gated by a `probability`% roll (100 = always). Each settlement type carries a **base** range and a richer **extra** range; the attacker uses the extra range iff it has `model.ability.plunderNatives` — granted by **Hernán Cortés**. Pinned (gold): camp 200–300 @20% (Cortés 300–600 always); village 300–800 @50% (Cortés 400–1200); camp capital 400–800 (Cortés 600–1200); village capital 600–1600 (Cortés 900–2400). Credited straight to gold (treasure-train logistics + Cortés's transport-fee discount are deferred).

**Settlement-assault tension** (FreeCol `defenderTension`, nation-wide): sacking a **non-capital** settlement adds **+900** = `SETTLEMENT_ATTACKED` (+500, the slain in-settlement defender) + `MAJOR` (+300, the destruction) + `MINOR` (+100), applied to the nation's **other** settlements (the sacked one is removed; a lone-settlement tribe's alarm dies with it). Burning a **capital** instead makes the nation **surrender**: every surviving settlement's alarm is *set* to `SURRENDERED` = **350** (`(Content 600 + Happy 100)/2`). A **repelled** assault *lowers* the nation's alarm by **−100** (or **−300** if your unit is slain), across all its settlements.

**Deviations from original / FreeCol (deliberate, 5b/5c scope):**
- **Player-initiated combat against natives only.** Naval combat, foreign-European unit/colony combat, and native-initiated raids (native AI) are the foreign-powers slice.
- **Implicit garrison settlement defence.** A settlement defends with one transient brave-strength defender (the settlement bonus applied), not the strongest of several in/on-tile braves; the adjacent garrison braves need not be cleared first, and a winning assault destroys the settlement in one resolution. A faithful multi-brave on-tile defence waits for in-settlement unit lists.
- **Plunder is credited straight to gold.** FreeCol spawns a treasure train the attacker must escort home; treasure-train logistics + Cortés's `treasureTransportFee −100%` are deferred. `CAPTURE_CONVERT`/`BURN_MISSIONS` are omitted (missionary-gated, no missionary system); `DESTROY_NATION`/atrocity-score bookkeeping is not modelled.
- **Native tension is stored per-settlement only.** We apply FreeCol's `defenderTension` (with `getSlaughterTension` location routing — open-field +400 vs in-settlement +500/+600) uniformly across the nation's settlements, but keep no separate nation-level `Player.tension` store for *natives*, and don't model the player's own tension *toward* natives (so a repelled attack's attacker-side tension is dropped). Native stance changes off these values are the native-AI slice.
- **Colonial-vs-colonial:** from FP-6a, attacking a *rival colonial player's* unit records mutual War + a tension spike on the player-level diplomacy model (see [diplomacy](diplomacy.md)) — this is recorded only, it does not change combat resolution or gate the attack. Native combat is untouched by it.
- **Paul Revere applies the defence bonus without consuming/persisting the 50 muskets** (FreeCol's `AUTOEQUIP_UNIT` permanence is deferred); native auto-equipment is dormant until native settlements stock goods.
- Modifier *values*, tension deltas, the promotion probability, the great-win band and the plunder ranges are FreeCol-pinned constants in code (transposability-migration items — see [game-modes](game-modes.md)).

## 3. Technical design

- **Data:** `RoleType` (offence/defence summed from index-30 additive modifiers, required-goods, downgrade, granted/required abilities, `role-change` capture rules) and `UnitChange` (from→to + probability) parse in `Ruleset.ParseRoles`/`ParseUnitChanges`. `UnitType` gains combat-ability flags (`DisposeOnCombatLoss`, `CanBeCaptured`, `CaptureUnits`, `CaptureEquipment`, `DisposeOnAllEquipmentLost`, `DemoteOnAllEquipmentLost`, `Bombard`). `Ruleset.GetUnitChange` and `Ruleset.CaptureRole` are the lookups combat uses.
- **Model:** `Combat.CombatModel` is unchanged and still pure — roles fold into the **scalar base** passed to `AttackPower`/`DefencePower` (`UnitType.Offence + RoleType.Offence`), landing at FreeCol's index 30, before the index-50 situational percentages.
- **State:** `Unit.OwnerNationId` (null = player) + `RoleId`/`RoleCount`. `Game.CheckAttack`/`Attack` follow the `MoveCheck` + `CheckX`/`X` pattern; `Attack` builds the contexts, resolves through `CombatModel`, then `ResolveLoserOutcome` + `ApplyWinnerPromotion` apply the precedence above and `ChangeNativeAlarm` raises tension. `EquipRole`/`CheckEquipRole` arm a colonist from a colony's stock (charging the role-goods delta). `EffectiveCombatRole` implements FreeCol `getAutomaticRole` (Revere). Braves spawn in `Game.New` via `SpawnUnit(type, pos, ownerNationId)` on `FreeAdjacentLand`, consuming no RNG draw.
- **Settlement assault (5c):** `Game.CheckAttackSettlement`/`AttackSettlement` mirror the attack pair but target a `NativeSettlementAt(target)`. The defender is a **transient** `Unit` of `BraveUnitTypeId` (id 0, never added to `_units`) on the settlement tile, fed a `DefenceContext(SettlementDefenceBonus: SettlementType.DefenceModifier)`. A win removes the settlement from `_nativeSettlements`, runs `ApplyWinnerPromotion`, then `ComputePlunder`; a loss runs `ResolveLoserOutcome(transientDefender, attacker)`. `ComputePlunder` re-implements FreeCol `RandomRange.getAmount`; `SettlementType.PlunderRange(hasPlunderAbility)` selects base vs extra by the `plunderNatives` ability (via `AbilityForOwner`). `CheckMove` rejects a non-native move onto a native settlement tile.
- **Determinism (ADR-009):** the public `Attack`/`AttackSettlement` draw from the game's **main saved RNG** (`_random`); `internal …(IGameRandom)` overloads let tests force an outcome and script plunder draws. The settlement-assault RNG order is **combat draw → (win) promotion draw → plunder probability draw (only if `<100`) → plunder range draw**. `UpgradeUnitType` preserves owner/role/roleCount. Native units are excluded from `CurrentlyVisible`/`IsVisible`.
- **Save:** format **v18** adds `SavedUnit.Owner`/`Role`/`RoleCount` (default role omitted → v17 player saves byte-identical); braves round-trip via `Owner`. **v19** is a marker for settlement assault — a destroyed settlement is simply absent from the saved list and plunder folds into gold, so **no new field** and older saves load unchanged.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `CombatModelTests` (power/odds/resolution + role fold); `RoleTests` (roles, unit-changes, capture-roles, combat abilities pinned to spec); `CombatTests` (CheckAttack gating, slaughter/disarm/demote/promote outcomes, equip-capture chains, alarm deltas, Washington, Revere, EquipRole, brave garrison, fog exclusion; **settlement: plunder parse + formula, Cortés extra range, destroy, tension 500/600 + sibling propagation, loss-disarm, CheckAttackSettlement/CheckMove guards**) | ✅ |
| L2 Scenario | When stateful | `CombatTests` save round-trips (v18 owner/role/braves; **v19 destroyed-settlement state + production main-RNG resume-determinism**); `SaveGameTests` pre-v18 default load | ✅ |
| L3 Interaction | When there's UI | — (no combat UI yet) | — |
| L4 Visual | No screen | — | — |

- **FreeCol cross-check:** role/unit-change values, the loser-outcome precedence, the tension deltas and the power numbers are pinned to `SimpleCombatModel`, `Unit.java`, `Tension.java` and the classic spec in the tests above.

## 5. Open issues / TODO

- [x] **Combat 5c — native settlement assault** (attack / plunder / destroy + Cortés): shipped. See §2/§3.
- [ ] **Naval combat** (+ evade/sink/loot, privateers, **Francis Drake**) and **foreign-European unit/colony combat** — the foreign-powers slice (no targets exist natives-only).
- [ ] **Native-initiated attacks (native AI)** — exercises the capture-unit and Revere paths end-to-end.
- [ ] Faithful multi-brave on-tile settlement defence (in-settlement unit lists); clearing the adjacent garrison before the settlement falls.
- [ ] Treasure trains for plunder + Cortés's `treasureTransportFee`; `CAPTURE_CONVERT`/`BURN_MISSIONS` with the missionary system; `DESTROY_NATION`/atrocity bookkeeping.
- [ ] Nation-level tension store + `getSlaughterTension` location routing (5c keeps per-settlement + sibling propagation).
- [ ] Persist Revere's auto-equipped muskets (`AUTOEQUIP_UNIT`); native auto-equipment once settlements stock goods.
- [ ] Apply role movement bonuses (dragoon/scout +9) to unit movement.
- [ ] Move the pinned modifier/tension/promotion constants to ruleset data (transposability, [game-modes](game-modes.md)).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-14 | Combat 5c: native settlement assault (`CheckAttackSettlement`/`AttackSettlement`) — implicit-garrison defence with the settlement bonus, destroy-on-win, `<plunder>` gold (`SettlementPlunder`, `ComputePlunder` ≈ FreeCol `RandomRange`), Hernán Cortés (`plunderNatives` → extra range), loss-disarm; can't move onto a settlement; save v19. Reworked combat tension to FreeCol `defenderTension` (nation-wide; win raises, repelled attack lowers; non-capital sack +900, capital burn → surrender 350) — supersedes 5b's flat +200/+400 | Phase 5 slice 5c |
| 2026-06-14 | Combat 5b: unit ownership + roles/equipment (`RoleType`, `UnitChange`, `EquipRole`), brave defenders, the attack action (`CheckAttack`/`Attack`) with the FreeCol loser/winner outcome precedence (slaughter/disarm/equipment-capture/demote/promote), native alarm on attack, Washington & Revere; save v18 | Phase 5 slice 5b |
| 2026-06-14 | Combat foundation: parse unit `offence`/`defence` and terrain defence bonus; pure `CombatModel` (power, odds, graded resolution) | Phase 5 slice 5a |
