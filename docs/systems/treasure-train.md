# System: Treasure trains

| | |
|---|---|
| **Status** | In development — **the unit + spawn-on-sack + capturability + save (v27)** done; **cashing it in** (King's transport cut + tax) is the next slice (`86d3c9rzu`) |
| **Last verified** | 2026-06-17 @ treasure train unit + spawn-on-sack (`86d3c9ryj`) |
| **Code** | `game/src/GameLogic/Specification/UnitType.cs` (`CarryTreasure`) + `Ruleset.cs` (parse); `Units/Unit.cs` (`TreasureAmount`/`SetTreasureAmount`); `GameSession/Game.cs` (`TreasureTrainUnitTypeId`; the `AttackSettlement` spawn-on-sack; capture via the shared `ResolveLoserOutcome`/`CaptureUnit` path); `Persistence/SaveGame.cs` (`SavedUnit.TreasureAmount`, v27) |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/TreasureTrainTests.cs`; spawn-on-sack in `CombatTests.cs` |
| **FreeCol reference** | `ServerPlayer.csDestroySettlement` (spawn), `Unit.getTreasureAmount`/`canCashInTreasureTrain`/`getTransportFee`, spec `model.unit.treasureTrain` |
| **Related systems** | [combat](combat.md), [natives](natives.md), [save-load](save-load.md), [lost-city-rumours](lost-city-rumours.md), [transport](transport.md) |

## 1. How it works (plain English)

When you **sack a native settlement** and there's treasure to be had, you don't pocket the gold on the spot any more. Instead a **treasure train** — a slow, defenceless wagon piled with gold — appears where the settlement stood. It's yours, but the gold isn't *banked* yet: you have to **escort the train back to one of your colonies** (or all the way to Europe) and **cash it in** there. Until then it just sits on the map, and because it can't fight (and can be **captured**), an enemy who beats it on its tile takes the whole haul. So a rich sack is now a prize you have to *get home*, not an instant windfall.

The cities of gold (Cibola) and rich ruins you'll find in Lost City Rumours will hand you treasure trains the same way — that wiring comes with the treasure-rumour slice.

*(This slice gives you the treasure train and the way it's born from a sack; **cashing it in** — and the King's fat fee for shipping it across the Atlantic — is the next slice.)*

## 2. Detailed rules

- The **treasure train** (`model.unit.treasureTrain`) is a land unit with **0 offence / 0 defence**, movement 3, that **carries gold** (`model.ability.carryTreasure`) and **can be captured** (`model.ability.canBeCaptured`). It fills a whole galleon hold (`spaceTaken` 6).
- **Spawn on sack:** when an attacker destroys a native settlement and the sack yields **plunder > 0**, a treasure train carrying that amount musters **on the razed settlement tile**, owned by the attacker (FreeCol `csDestroySettlement`). This **replaces** the old instant-gold plunder — the gold is no longer credited directly. A sack that rolls **no** plunder spawns nothing.
- The plunder amount is unchanged from before (the same settlement-`<plunder>` range + Cortés's richer range); it just becomes the train's cargo instead of gold.
- **Capture:** a treasure train has no defence, so any unit that attacks its tile and wins **captures it** if the winner can capture units (FreeCol generic `CanBeCaptured`; the captor must be armed — `captureUnits` is carried by artillery and the regulars, not a bare colonist). The carried gold travels with the captured train to its new owner. *(A winner that cannot capture destroys the train and its gold — the same fate as any uncaptured capturable unit.)*

**Deviations from original / FreeCol:** none yet for the spawn/capture mechanic. **Deferred:** **cashing in** (`86d3c9rzu`); spawning treasure trains from **Lost City Rumour** ruins/Cibola (`86d3c9t1e`, now unblocked — it needs this v27 amount); loading a treasure train onto a galleon to **sail it home** (needs treasure-as-cargo wiring); the AI cashing in / valuing in-transit treasure (with FP-6 settlement-sacking AI).

## 3. Technical design

- **Parse:** `UnitType.CarryTreasure` (`model.ability.carryTreasure`, parsed in `Ruleset` next to `CaptureGoods`/`Piracy`). `CanBeCaptured` and `SpaceTaken` (6) already parsed for the treasure train down its `extends wagon` chain.
- **State:** `Unit.TreasureAmount` (read-only) + internal `SetTreasureAmount` (floors negatives, like `AddCargo`). 0 for every non-treasure unit. `UpgradeUnitType` carries the amount across a type swap (defensive — treasure trains have no type change today).
- **Spawn-on-sack:** in `Game.AttackSettlement`'s win branch the instant `_human.Gold += ComputePlunder(...)` is replaced by `int plunder = ComputePlunder(type, hasPlunderAbility, random); if (plunder > 0) SpawnUnit(TreasureTrainUnitTypeId, target, attacker.OwnerId).SetTreasureAmount(plunder);`. **Determinism (ADR-009):** `ComputePlunder` is still called exactly once, in the same position (after the promotion draw), so the human's stream-0 RNG sequence is byte-identical to the old path; `SpawnUnit` draws no RNG. The L5 soak stays byte-stable (and the AI never sacks settlements, so no train spawns there).
- **Capture:** reuses the existing `ResolveLoserOutcome` → `CaptureUnit` path. A defence-0 train loses any fight; with no `model.unitChange.capture` row for the treasure train, `CaptureUnit` flips the **same** `Unit` object's owner, so `TreasureAmount` rides along automatically — no new code.
- **Save (v27, additive):** `SavedUnit.TreasureAmount` is **omitted when 0**, so every non-treasure unit (and a treasure-free game) serializes byte-identically to v26; pre-v27 saves load with 0. `Restore` applies it via `SetTreasureAmount` after the object initializer (it's an internal method, like `AddCargo`).

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `TreasureTrainTests`: the unit parses (carryTreasure / canBeCaptured / 0 offence+defence / spaceTaken 6); `TreasureAmount` defaults 0 + floors negatives; an undefended native-owned train is **captured with its amount** by an armed (artillery) attacker; the amount **round-trips through save v27**; a treasure-free game **omits the token**. `CombatTests`: a great-win sack **spawns a treasure train** carrying the plunder (not instant gold); a failed plunder probability spawns **no** train; Cortés's richer range sets the train's amount | ✅ |
| L2 Scenario | Always | the L5 soak is byte-stable (the plunder RNG draw is unchanged; the AI never sacks settlements, so no train spawns) | ✅ |
| L3 Interaction | No UI yet | a treasure-train marker + the cash-in prompt are later presentation slices | — |
| L4 Visual | No screen yet | — | — |

## 5. Open issues / TODO

- [x] **Treasure train unit + spawn-on-sack + capturability + save v27** (`86d3c9ryj`).
- [ ] **Cash in a treasure train** (`86d3c9rzu`): at an owned colony the King ships it across for a **60%** transport cut (Hernán Cortés makes it free), then the monarch's **tax** applies to the remainder; carry it to Europe yourself (a galleon) to dodge the King's fee. The train is consumed on cash-in.
- [ ] **Treasure from Lost City Rumours** (`86d3c9t1e`): wire `RUINS`/`CIBOLA` into the LCR table to spawn a treasure train (now unblocked — the v27 amount exists).
- [ ] **Load a treasure train onto a galleon** (treasure-as-cargo) so it can be sailed to Europe for the fee-free cash-in.
- [ ] AI cashing-in / valuing in-transit treasure (with FP-6 settlement-sacking AI).
- [ ] Map **treasure-train marker** + cash-in prompt (presentation).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-17 | **Treasure train unit + spawn-on-sack + capture + save v27** (`86d3c9ryj`): `model.unit.treasureTrain` parses (`CarryTreasure`); `Unit.TreasureAmount`; sacking a native settlement with plunder > 0 spawns a treasure train carrying it (replaces the instant-gold plunder in `AttackSettlement` — same RNG draw, soak byte-stable); an undefended train is captured (with its amount) via the existing `CanBeCaptured` path; save **v27** adds `SavedUnit.TreasureAmount` (omitted when 0). +5 L1 (+3 `CombatTests` migrated); 715 + soak green. Cash-in is the next slice. | Phase 5 (`86d3c9ryj`) |
