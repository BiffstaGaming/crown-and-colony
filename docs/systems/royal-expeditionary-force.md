# System: Royal Expeditionary Force (the REF)

| | |
|---|---|
| **Status** | Implemented (GameLogic) — the REF grows over the game (ADD_TO_REF), realises into an army at the Declaration of Independence, lands and wages war, and a rebel that holds out long enough draws a friendly **foreign Intervention Force** (the REF's mirror-image, `86d3c9vap`). The intelligence-report + war UI are P7. |
| **Last verified** | 2026-06-21 @ specialised REF combat AI (`86d3drn5a` — colony-first targeting + navy hunting rebel ships; doctrine lives in [independence](independence.md)); targeted Independence/Ref/Combat 294 green |
| **Code** | `game/src/GameLogic/GameSession/Force.cs`, `Game.Monarch.cs` (`EnsureRefForce`/`BuildBaseRef`/`AddToRef`); `Game.Independence.cs` (intervention spawn); `Ruleset.InterventionForce` (`Specification/Ruleset.cs`) |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/MonarchTests.cs`, `IndependenceTests.cs` (intervention) |
| **FreeCol reference** | `Monarch.java` (`getExpeditionaryForce`, `addToREF`, `getInterventionForce`, `Force`), `specification.xml` (`model.option.refSize`/`interventionForce`) |
| **Related systems** | [monarchy](monarchy.md), [independence](independence.md) *(in progress)*, [combat](combat.md) |

## 1. How it works (plain English)

The King keeps a **Royal Expeditionary Force** — the army and navy he will send to crush you if you declare independence. It starts at a fixed size and **grows over the game**: whenever the King chooses to "add to the REF", he reinforces it. He builds up his **navy first** until it can actually ferry all his soldiers across the ocean (he keeps the fleet about 10% bigger than the troops it must carry), then adds **regulars, dragoons, and artillery**. The bigger your rebellion looms, the more turns he spends doing this — so the longer you wait to declare, the tougher the force waiting for you. The REF is invisible to you for now (no "intelligence report" UI yet); it's quietly accumulating in the background.

When the King's army actually assaults your towns, it fights as a **siege force**: a REF unit attacking a settlement gets a **+50% bombard bonus**, so the redcoats can take a colony an ordinary raider couldn't. Your defence against them is **popular support** — the more of a colony's people are committed rebels (its Sons-of-Liberty level), the harder it fights off the Crown; the King presses hardest where your support is thinnest. And out in the field a redcoat caught in concealing forest/hills can be **ambushed** (the REF's "ambush penalty"), just as a native war party ambushes a colonist. The combat maths for all three lives in [combat](combat.md); they only ever apply in the War of Independence, so an ordinary colonial war is untouched.

There's a friendly mirror to the King's army: the **foreign Intervention Force**. If you declare and then *hold out* — keep producing liberty during the war — a sympathetic foreign power eventually lands a small force (a couple of colonial regulars, dragoons, artillery, and men-o-war) at one of your ports to fight alongside you. Like the King's army, **it grows the longer the war runs** — each landing is bigger than the last (one more of each kind of soldier per ~52 turns of war, plus extra men-o-war to ferry them). The full rules for *when* it arrives and *how it grows* live in [independence](independence.md); the **what it's made of** is the same kind of "force" the King uses, and is set by ruleset data (`model.option.interventionForce`) so a variant can resize it.

## 2. Detailed rules

- The REF is a **force**: a list of land-unit blocks (king's regulars as infantry/cavalry, artillery) and naval-unit blocks (men-o-war). It is grown but **not yet turned into real units** — that happens at the Declaration of Independence (item 7).
- **Base size** (FreeCol classic medium `refSize`): **31** king's-regular infantry, **15** king's-regular cavalry, **14** artillery (60 land), **8** men-o-war. These counts are **difficulty-data-driven** (`Ruleset.Difficulty.Monarch.RefBase*`, parsed from the spec `refSize` `unitListOption`; default = classic medium — see [difficulty](difficulty.md)), so a variant can resize the King's army; only the unit/role **identities** (king's regular, man-o-war, infantry/cavalry roles) remain hardcoded.
- **ADD_TO_REF** (the King's highest-weighted active monarch action, weight `10 + dx` = 13) grows the force (FreeCol `Monarch.addToREF`):
  - if the navy can't yet carry the land force — **naval capacity < land space required × 1.1** — add **one man-o-war**;
  - otherwise add **1-3 land units**: either king's regulars (1-in-3 **mounted**, else infantry) or artillery.
- `SpaceRequired` = the cargo slots the land units occupy; `NavalCapacity` = the men-o-war's cargo capacity. The base navy (8 men-o-war) can't carry 60 land units, so early ADD_TO_REF reinforces the navy.
- **Foreign Intervention Force** (the friendly counterpart, `Ruleset.InterventionForce`): a base composition parsed from the spec `model.option.interventionForce` (classic medium **2 colonial-regular soldiers + 2 colonial-regular dragoons + 2 artillery + 2 men-o-war**). Unlike the REF it does **not** grow via a monarch action — instead it grows with the **war's length** (`Game.Independence.cs` `GrownInterventionForce(Turn)`, FreeCol `Monarch.updateInterventionForce`): each landing adds `Turn / interventionTurns` to every land block plus `prepareToBoard` transport men-o-war, so a long rebellion draws progressively larger waves. It's the force the foreign power lands when the rebel crosses the `interventionBells` threshold (the trigger + landing + the exact growth formula are in [independence](independence.md), `86d3e4bm9`).

- **REF siege combat** (FreeCol `SimpleCombatModel`, `86d3e4bkk`): when the realised REF assaults a rebel colony it carries three War-of-Independence-only modifiers — the **bombard bonus** (+50% offence vs a settlement, `model.modifier.bombardBonus` on the REF nation type, spec line 3009), the colony's **popular support** defence (the REF attacks against `100 − SoL%`), and the **ambush-penalty** mirror (a REF unit caught in open forest/hills can be ambushed). All three resolve in `Game`'s combat code and are documented in detail in [combat](combat.md) §2/§3; gated by `IsRefUnit`/`IsWarOfIndependenceColonyBattle` so they fire only against (or for) the REF in the rebellion. A non-rebellion game never triggers them.

**Deviations from original / FreeCol:** the composition + the `×1.1` navy-growth rule + the 1-3 land roll match FreeCol; the base **counts** are now `refSize` difficulty data (`86d3c9rg6`), while the unit **types** stay hardcoded (faithful-subset). RNG is the ephemeral monarch generator (off stream 0).

## 3. Technical design

- `Force` (`GameSession/Force.cs`): `LandUnits`/`NavalUnits` as `ForceEntry(UnitTypeId, RoleId, Count)` blocks; `AddLand`/`AddNaval` merge into a matching block; `SpaceRequired(ruleset)` / `NavalCapacity(ruleset)`; `LandUnitCount`/`NavalUnitCount`. The **intervention** force uses a separate, simpler `InterventionForceComposition`/`InterventionForceUnit` pair (declared alongside `Ruleset`) — it doesn't need the space/capacity ferry maths, so it's a flat list of `(UnitTypeId, RoleId, Count)` blocks with the land/naval split decided at spawn from each unit type's `IsNaval` flag.
- **Intervention force parse** (`Ruleset.ParseIntervention`): selects the chosen difficulty level subtree, reads `model.option.interventionBells`/`interventionTurns` (`<integerOption>`) and the `model.option.interventionForce` `<unitListOption>`, exposing `Ruleset.InterventionBells`/`InterventionTurns`/`InterventionForce`. Fallback = `InterventionForceComposition.ClassicMedium` (5000 / 52 / the 8-unit force). The spawn/accrual themselves live in [independence](independence.md) (`Game.Independence.cs`).
- `Game._refForce` (nullable): the King's force, **null until grown beyond the re-derivable base**. `EnsureRefForce()` materialises `BuildBaseRef()` on first need (an instance method since `86d3c9rg6` — it reads the base counts from `Ruleset.Difficulty.Monarch`); `AddToRef(rng)` applies the growth rule; `SetRefForce` installs a restored one.
- `MonarchActionIsValid(AddToRef)` is true (the ruleset always has REF land + naval types); the tick dispatches `AddToRef(monarchRng)`.
- **Save v40:** `SaveGame.RefForce` (a `SavedForce` of the two `ForceEntry` lists) is written only when `_refForce` is non-null (grown), so a pre-rebellion game is byte-identical to v39; a pre-v40 save (or an ungrown one) re-derives the base on demand. Restored via `SetRefForce` after `Game.Restore`.

## 4. Verification

| Layer | Required? | Tests | Status |
|---|---|---|---|
| L1 Unit | Always | `MonarchTests`: base REF composition (60 land / 8 naval) + unbalanced capacity; ADD_TO_REF grows the navy while it can't carry the land, then 1-3 land once it can; ADD_TO_REF offered by the chooser (weight 13); REF force round-trips save/load + omitted before growth. `IndependenceTests`: the **intervention force** composition is parsed (5000 / 52 / 2-2-2-2) and lands the right counts (6 land + 2 naval) | ✅ |
| L2 Scenario | ✅ | `IndependenceTests`: REF realisation + war (items 7-9) and the intervention force landing all run through `EndTurn` and round-trip save/load | ✅ |

- **FreeCol cross-check:** ✅ refSize medium + addToREF rule (`×1.1` navy, 1-3 land, 1-in-3 mounted) match `Monarch.addToREF`; the intervention force composition matches `Monarch.getInterventionForce` / `model.option.interventionForce`.

## 5. Open issues / TODO

- [x] REF Force model + ADD_TO_REF build-up (`86d3c9v4j`, save v40).
- [x] Realise the REF into a PlayerType.RoyalExpeditionaryForce army at the Declaration of Independence (item 7, `86d3c9v28`).
- [x] REF arrival/landing + War-of-Independence combat on the REF's own RNG stream (item 8, `86d3c9v8k`).
- [x] Route `refSize` (the base counts) through ruleset/difficulty data (`86d3c9rg6`) — `Ruleset.Difficulty.Monarch.RefBase*`, value-preserving at medium. See [difficulty](difficulty.md).
- [x] **Foreign Intervention Force** (`86d3c9vap`) — `Ruleset.InterventionForce` parsed from `model.option.interventionForce`; landed near a rebel port at the `interventionBells` threshold (the trigger/spawn are in [independence](independence.md)).
- [ ] Grow the standing intervention force every `interventionTurns` (FreeCol `Monarch.updateInterventionForce`) + load land units aboard the men-o-war before landfall — a fidelity follow-up.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-22 | **REF siege combat modifiers** (`86d3e4bkk`, FreeCol `SimpleCombatModel` War-of-Independence branches): the realised REF now fights its colony assaults with the three modifiers it was missing — **bombardBonus +50%** (offence vs a settlement, parsed from the REF nation type's `model.modifier.bombardBonus`), **popularSupport** (a rebel colony's Sons-of-Liberty scales its defence; the REF attacks against `100 − SoL%`), and the **ambushPenalty** mirror (a REF unit can be ambushed in open forest/hills). Resolution + parsing live in `Game`/`CombatModel`/`Ruleset`; full maths in [combat](combat.md). Gated to the REF↔rebel war, so the default game is byte-identical, **no save bump**. +8 L1 + 3 L2; full L1/L2 + soak green. | P6 (`86d3e4bkk`) |
| 2026-06-21 | **Specialised REF combat AI** (`86d3drn5a`, FreeCol `REFAIPlayer`): the REF's war turn (`RunRefTurn`) gained a bespoke doctrine — **colony-first targeting** (the rebel's connected ports favoured +500, weakest-defended first), **no chase of loose rebel field units until a colony is captured**, and **men-o-war hunting the rebel's ships / supporting the landings** — in place of the generic foreign-power war AI. The realisation/landing (this doc) is unchanged; the combat behaviour + the full faithful-subset notes live in [independence](independence.md). No save bump. | P6 (`86d3drn5a`) |
| 2026-06-21 | **Foreign Intervention Force** (`86d3c9vap`): the friendly counterpart to the REF — `Ruleset.InterventionForce` (parsed by `Ruleset.ParseIntervention` from the chosen difficulty level's `model.option.interventionForce`; classic medium 2 colonial-regular soldiers + 2 dragoons + 2 artillery + 2 men-o-war, fallback `InterventionForceComposition.ClassicMedium`), plus the `interventionBells`/`interventionTurns` thresholds. The trigger (rebel liberty accrual) + landing near a rebel port on a dedicated RNG stream live in [independence](independence.md); reuses `Player.InterventionBells` → no save bump. 1566 L1/L2 + 4 soak green | P6 (`86d3c9vap`) |
| 2026-06-20 | **refSize → difficulty data** (`86d3c9rg6`): the REF base counts (31/15/14/8) moved from hardcoded consts into `Ruleset.Difficulty.Monarch.RefBase*` (parsed from the spec `refSize` `unitListOption`); `BuildBaseRef` became an instance method. Value-preserving at medium (default game byte-identical); the unit/role identities stay hardcoded. See [difficulty](difficulty.md), [monarchy](monarchy.md). | Phase 5 (`86d3c9rg6`) |
| 2026-06-19 | **REF Force model + ADD_TO_REF build-up**: `Force` (land/naval blocks + space/capacity), `Game._refForce` grown by `AddToRef` (navy to +10% then 1-3 land), ADD_TO_REF dispatched + offered (weight 13). Save **v40** (`SaveGame.RefForce`, omit-until-grown). Composition hardcoded (faithful-subset) | P6 (`86d3c9v4j`) |
