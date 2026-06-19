# System: Royal Expeditionary Force (the REF)

| | |
|---|---|
| **Status** | In progress — the REF as a growing force the King amasses (ADD_TO_REF, item 6). Realising it into an army at the Declaration of Independence + the war itself land in items 7-8. |
| **Last verified** | 2026-06-19 @ REF build-up + Force model (`86d3c9v4j`, save v40) |
| **Code** | `game/src/GameLogic/GameSession/Force.cs`, `Game.Monarch.cs` (`EnsureRefForce`/`BuildBaseRef`/`AddToRef`) |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/MonarchTests.cs` |
| **FreeCol reference** | `Monarch.java` (`getExpeditionaryForce`, `addToREF`, `Force`), `specification.xml` (`model.option.refSize`) |
| **Related systems** | [monarchy](monarchy.md), [independence](independence.md) *(in progress)*, [combat](combat.md) |

## 1. How it works (plain English)

The King keeps a **Royal Expeditionary Force** — the army and navy he will send to crush you if you declare independence. It starts at a fixed size and **grows over the game**: whenever the King chooses to "add to the REF", he reinforces it. He builds up his **navy first** until it can actually ferry all his soldiers across the ocean (he keeps the fleet about 10% bigger than the troops it must carry), then adds **regulars, dragoons, and artillery**. The bigger your rebellion looms, the more turns he spends doing this — so the longer you wait to declare, the tougher the force waiting for you. The REF is invisible to you for now (no "intelligence report" UI yet); it's quietly accumulating in the background.

## 2. Detailed rules

- The REF is a **force**: a list of land-unit blocks (king's regulars as infantry/cavalry, artillery) and naval-unit blocks (men-o-war). It is grown but **not yet turned into real units** — that happens at the Declaration of Independence (item 7).
- **Base size** (FreeCol classic medium `refSize`): **31** king's-regular infantry, **15** king's-regular cavalry, **14** artillery (60 land), **8** men-o-war.
- **ADD_TO_REF** (the King's highest-weighted active monarch action, weight `10 + dx` = 13) grows the force (FreeCol `Monarch.addToREF`):
  - if the navy can't yet carry the land force — **naval capacity < land space required × 1.1** — add **one man-o-war**;
  - otherwise add **1-3 land units**: either king's regulars (1-in-3 **mounted**, else infantry) or artillery.
- `SpaceRequired` = the cargo slots the land units occupy; `NavalCapacity` = the men-o-war's cargo capacity. The base navy (8 men-o-war) can't carry 60 land units, so early ADD_TO_REF reinforces the navy.

**Deviations from original / FreeCol:** the composition + the `×1.1` navy-growth rule + the 1-3 land roll match FreeCol; the unit types are hardcoded (faithful-subset — TODO `86d3c9rg6` to route `refSize` through ruleset/difficulty data). RNG is the ephemeral monarch generator (off stream 0).

## 3. Technical design

- `Force` (`GameSession/Force.cs`): `LandUnits`/`NavalUnits` as `ForceEntry(UnitTypeId, RoleId, Count)` blocks; `AddLand`/`AddNaval` merge into a matching block; `SpaceRequired(ruleset)` / `NavalCapacity(ruleset)`; `LandUnitCount`/`NavalUnitCount`. Reused by the foreign-intervention force later.
- `Game._refForce` (nullable): the King's force, **null until grown beyond the re-derivable base**. `EnsureRefForce()` materialises `BuildBaseRef()` on first need; `AddToRef(rng)` applies the growth rule; `SetRefForce` installs a restored one.
- `MonarchActionIsValid(AddToRef)` is true (the ruleset always has REF land + naval types); the tick dispatches `AddToRef(monarchRng)`.
- **Save v40:** `SaveGame.RefForce` (a `SavedForce` of the two `ForceEntry` lists) is written only when `_refForce` is non-null (grown), so a pre-rebellion game is byte-identical to v39; a pre-v40 save (or an ungrown one) re-derives the base on demand. Restored via `SetRefForce` after `Game.Restore`.

## 4. Verification

| Layer | Required? | Tests | Status |
|---|---|---|---|
| L1 Unit | Always | `MonarchTests`: base REF composition (60 land / 8 naval) + unbalanced capacity; ADD_TO_REF grows the navy while it can't carry the land, then 1-3 land once it can; ADD_TO_REF offered by the chooser (weight 13); REF force round-trips save/load + omitted before growth | ✅ |
| L2 Scenario | Later | REF realisation + war land with items 7-8 | ⬜ |

- **FreeCol cross-check:** ✅ refSize medium + addToREF rule (`×1.1` navy, 1-3 land, 1-in-3 mounted) match `Monarch.addToREF`.

## 5. Open issues / TODO

- [x] REF Force model + ADD_TO_REF build-up (`86d3c9v4j`, save v40).
- [ ] Realise the REF into a PlayerType.RoyalExpeditionaryForce army at the Declaration of Independence (item 7, `86d3c9v28`).
- [ ] REF arrival/landing + War-of-Independence combat on the REF's own RNG stream (item 8, `86d3c9v8k`).
- [ ] Route `refSize` through ruleset/difficulty data (`86d3c9rg6`).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-19 | **REF Force model + ADD_TO_REF build-up**: `Force` (land/naval blocks + space/capacity), `Game._refForce` grown by `AddToRef` (navy to +10% then 1-3 land), ADD_TO_REF dispatched + offered (weight 13). Save **v40** (`SaveGame.RefForce`, omit-until-grown). Composition hardcoded (faithful-subset) | P6 (`86d3c9v4j`) |
