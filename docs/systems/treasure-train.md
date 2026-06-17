# System: Treasure trains

| | |
|---|---|
| **Status** | In development — **the unit + spawn-on-sack + capturability + save (v27)** and **cashing it in** (King's transport cut + tax, Cortés-free, Europe-free) done; spawning from LCR ruins/Cibola and loading onto a galleon to sail home are the next slices |
| **Last verified** | 2026-06-17 @ treasure train cash-in (`86d3c9rzu`) |
| **Code** | `game/src/GameLogic/Specification/UnitType.cs` (`CarryTreasure`) + `Ruleset.cs` (parse); `Units/Unit.cs` (`TreasureAmount`/`SetTreasureAmount`); `GameSession/Game.cs` (`TreasureTrainUnitTypeId`; the `AttackSettlement` spawn-on-sack; capture via the shared `ResolveLoserOutcome`/`CaptureUnit` path; `LandPrice`-style `TransportFee`/`CashInValue`/`CheckCashInTreasureTrain`/`CashInTreasureTrain`); `Persistence/SaveGame.cs` (`SavedUnit.TreasureAmount`, v27) |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/TreasureTrainTests.cs`; spawn-on-sack in `CombatTests.cs` |
| **FreeCol reference** | `ServerPlayer.csDestroySettlement` (spawn), `Unit.getTreasureAmount`/`canCashInTreasureTrain`/`getTransportFee`, spec `model.unit.treasureTrain` |
| **Related systems** | [combat](combat.md), [natives](natives.md), [save-load](save-load.md), [lost-city-rumours](lost-city-rumours.md), [transport](transport.md) |

## 1. How it works (plain English)

When you **sack a native settlement** and there's treasure to be had, you don't pocket the gold on the spot any more. Instead a **treasure train** — a slow, defenceless wagon piled with gold — appears where the settlement stood. It's yours, but the gold isn't *banked* yet: you have to **escort the train back to one of your colonies** and **cash it in** there. Until then it just sits on the map, and because it can't fight (and can be **captured**), an enemy who beats it on its tile takes the whole haul. So a rich sack is now a prize you have to *get home*, not an instant windfall.

**Cashing in.** Bring the train to one of your colonies and the **King offers to ship the treasure to Europe for you — for a fat cut (60%)**. Whatever's left is then **taxed** at your usual rate, and the rest lands in your treasury; the train is used up. If you'd rather not hand the King 60%, you can **carry the treasure across the Atlantic yourself** on a galleon and cash it in **fee-free** in Europe (only the tax applies). And if **Hernán Cortés** sits in your Congress, the King ships it for **nothing** — full value, fee-free, anywhere.

The cities of gold (Cibola) and rich ruins you'll find in Lost City Rumours hand you treasure trains the same way (see [lost-city-rumours](lost-city-rumours.md)).

*(The slow part you still do by hand: there's no "load the train onto a galleon" button yet, so the fee-free Europe route arrives with that cargo wiring; cashing in **at a colony** — paying the King's cut — works now.)*

## 2. Detailed rules

- The **treasure train** (`model.unit.treasureTrain`) is a land unit with **0 offence / 0 defence**, movement 3, that **carries gold** (`model.ability.carryTreasure`) and **can be captured** (`model.ability.canBeCaptured`). It fills a whole galleon hold (`spaceTaken` 6).
- **Spawn on sack:** when an attacker destroys a native settlement and the sack yields **plunder > 0**, a treasure train carrying that amount musters **on the razed settlement tile**, owned by the attacker (FreeCol `csDestroySettlement`). This **replaces** the old instant-gold plunder — the gold is no longer credited directly. A sack that rolls **no** plunder spawns nothing.
- The plunder amount is unchanged from before (the same settlement-`<plunder>` range + Cortés's richer range); it just becomes the train's cargo instead of gold.
- **Capture:** a treasure train has no defence, so any unit that attacks its tile and wins **captures it** if the winner can capture units (FreeCol generic `CanBeCaptured`; the captor must be armed — `captureUnits` is carried by artillery and the regulars, not a bare colonist). The carried gold travels with the captured train to its new owner. *(A winner that cannot capture destroys the train and its gold — the same fate as any uncaptured capturable unit.)*
- **Cash in** (FreeCol `Unit.canCashInTreasureTrain`/`getTransportFee` + the cash-in handler): a treasure train standing at **a colony its owner holds**, or docked **in Europe**, can be cashed in. The owner banks `(amount − fee) × (100 − taxRate) / 100`, where the **King's transport fee** = `60% × amount` (classic-medium `treasureTransportFee`), reduced by **Hernán Cortés**'s `treasureTransportFee −100%` to nothing. A train **in Europe** pays **no fee** (you shipped it yourself); a train **at a colony** pays the fee (the King ships it). The monarch's **tax** then applies to the remainder. The train is consumed on cash-in.

**Deviations from original / FreeCol:** the cash-in **location** is simplified — FreeCol requires a port *connected to Europe* and only lets the King ship it when you have no suitable carrier; we have no Europe-connectivity graph or treasure-as-cargo, so **any owned colony** qualifies (always paying the King's fee), and the fee-free path is modelled as cashing **in Europe**. **No native treasury** is involved (the King/tax economics only). **Deferred:** spawning treasure trains from **Lost City Rumour** ruins/Cibola (`86d3c9t1e`, now unblocked — it needs this v27 amount); **loading** a treasure train onto a galleon to actually sail it home (treasure-as-cargo wiring — until then the in-Europe fee-free branch is reached only programmatically/in tests); the **independent/rebel** cash-in (no Europe → full value, no fee) once independence exists; the AI cashing in / valuing in-transit treasure (with FP-6 settlement-sacking AI).

## 3. Technical design

- **Parse:** `UnitType.CarryTreasure` (`model.ability.carryTreasure`, parsed in `Ruleset` next to `CaptureGoods`/`Piracy`). `CanBeCaptured` and `SpaceTaken` (6) already parsed for the treasure train down its `extends wagon` chain.
- **State:** `Unit.TreasureAmount` (read-only) + internal `SetTreasureAmount` (floors negatives, like `AddCargo`). 0 for every non-treasure unit. `UpgradeUnitType` carries the amount across a type swap (defensive — treasure trains have no type change today).
- **Spawn-on-sack:** in `Game.AttackSettlement`'s win branch the instant `_human.Gold += ComputePlunder(...)` is replaced by `int plunder = ComputePlunder(type, hasPlunderAbility, random); if (plunder > 0) SpawnUnit(TreasureTrainUnitTypeId, target, attacker.OwnerId).SetTreasureAmount(plunder);`. **Determinism (ADR-009):** `ComputePlunder` is still called exactly once, in the same position (after the promotion draw), so the human's stream-0 RNG sequence is byte-identical to the old path; `SpawnUnit` draws no RNG. The L5 soak stays byte-stable (and the AI never sacks settlements, so no train spawns there).
- **Capture:** reuses the existing `ResolveLoserOutcome` → `CaptureUnit` path. A defence-0 train loses any fight; with no `model.unitChange.capture` row for the treasure train, `CaptureUnit` flips the **same** `Unit` object's owner, so `TreasureAmount` rides along automatically — no new code.
- **Cash in:** `TransportFee(owner, train)` = `ApplyGoodsModifiers(owner, "model.modifier.treasureTransportFee", TreasureTransportFeePercent(60) × amount / 100)` — reusing the same Congress-modifier fold as `LandPrice`, so Cortés's −100% zeroes it. `CashInValue(owner, train)` = `(amount − fee) × (100 − owner.TaxRate) / 100`, with `fee = 0` when the train is `InEurope`. `CheckCashInTreasureTrain` gates on a `CarryTreasure` unit with `TreasureAmount > 0` standing at an owner-held colony **or** in Europe (its `MoveCheck.Yes` cost carries the net for the UI). `CashInTreasureTrain` banks the net to `PlayerById(train.OwnerId)` and removes the train. No RNG, no save field — pure arithmetic over already-saved state.
- **Save (v27, additive):** `SavedUnit.TreasureAmount` is **omitted when 0**, so every non-treasure unit (and a treasure-free game) serializes byte-identically to v26; pre-v27 saves load with 0. `Restore` applies it via `SetTreasureAmount` after the object initializer (it's an internal method, like `AddCargo`).

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `TreasureTrainTests`: the unit parses (carryTreasure / canBeCaptured / 0 offence+defence / spaceTaken 6); `TreasureAmount` defaults 0 + floors negatives; an undefended native-owned train is **captured with its amount** by an armed (artillery) attacker; the amount **round-trips through save v27**; a treasure-free game **omits the token**. **Cash-in**: at a colony banks `amount − 60% fee` (then nets the King's cut), the tax applies to the remainder, Hernán Cortés → no fee (full value), in Europe → no fee, away from a colony / at a **rival** colony is refused (train kept), a spent train **can't be cashed twice** (no double credit), and `CheckCashInTreasureTrain` previews the net. `CombatTests`: a great-win sack **spawns a treasure train** carrying the plunder (not instant gold, gold unchanged); a failed plunder probability spawns **no** train; Cortés's richer range sets the train's amount | ✅ |
| L2 Scenario | Always | the L5 soak is byte-stable (the plunder RNG draw is unchanged; the AI never sacks settlements, so no train spawns) | ✅ |
| L3 Interaction | No UI yet | a treasure-train marker + the cash-in prompt are later presentation slices | — |
| L4 Visual | No screen yet | — | — |

## 5. Open issues / TODO

- [x] **Treasure train unit + spawn-on-sack + capturability + save v27** (`86d3c9ryj`).
- [x] **Cash in a treasure train** (`86d3c9rzu`): at an owned colony the King ships it for a **60%** cut (Cortés-free), then the monarch's **tax** applies to the remainder; in Europe there's no fee. The train is consumed.
- [x] **Treasure from Lost City Rumours** (`86d3c9t1e`): `RUINS` (gold < 500, else a treasure train) and `CIBOLA` (a big treasure train) spawn trains via the v27 amount (shared `SpawnTreasureTrain`). See [lost-city-rumours](lost-city-rumours.md).
- [ ] **Load a treasure train onto a galleon** (treasure-as-cargo) so it can be sailed to Europe for the fee-free cash-in (the in-Europe cash-in branch already works; only the load/sail route is missing).
- [ ] AI cashing-in / valuing in-transit treasure (with FP-6 settlement-sacking AI).
- [ ] Map **treasure-train marker** + cash-in prompt (presentation).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-17 | **Treasure from Lost City Rumours** (`86d3c9t1e`): the LCR `RUINS`/`CIBOLA` outcomes now spawn treasure trains (shared `Game.SpawnTreasureTrain`); RUINS < 500 pays gold, else a train; CIBOLA a big train. No save change. See [lost-city-rumours](lost-city-rumours.md). | Phase 5 (`86d3c9t1e`) |
| 2026-06-17 | **Cash in a treasure train** (`86d3c9rzu`): `CheckCashInTreasureTrain`/`CashInTreasureTrain` at an owner-held colony or in Europe; nets `(amount − fee) × (100 − tax)/100`, fee = `TreasureTransportFeePercent(60)% × amount` reduced by Hernán Cortés's `treasureTransportFee −100%` (free), 0 in Europe; the train is consumed. No RNG, no save change. +6 L1; 721 + soak green. LCR ruins/Cibola + the load-onto-galleon route remain. | Phase 5 (`86d3c9rzu`) |
| 2026-06-17 | **Treasure train unit + spawn-on-sack + capture + save v27** (`86d3c9ryj`): `model.unit.treasureTrain` parses (`CarryTreasure`); `Unit.TreasureAmount`; sacking a native settlement with plunder > 0 spawns a treasure train carrying it (replaces the instant-gold plunder in `AttackSettlement` — same RNG draw, soak byte-stable); an undefended train is captured (with its amount) via the existing `CanBeCaptured` path; save **v27** adds `SavedUnit.TreasureAmount` (omitted when 0). +5 L1 (+3 `CombatTests` migrated); 715 + soak green. Cash-in is the next slice. | Phase 5 (`86d3c9ryj`) |
