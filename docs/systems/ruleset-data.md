# System: Ruleset data

| | |
|---|---|
| **Status** | Implemented (terrain incl. `<gen>` envelopes + resources + defence bonus, unit types incl. offence/defence, goods types incl. market/`stored-as`/`made-from`, building types, founding fathers incl. modifiers/abilities, resource types, native nation + settlement types, European nations + nation-types) |
| **Last verified** | 2026-06-17 @ unit build fields + building/terrain ambush + build abilities (`86d3c9tp0`) |
| **Code** | `game/src/GameLogic/Specification/` |
| **Tests** | `game/tests/GameLogic.Tests/Specification/RulesetTests.cs`, `NativeNationTypeTests.cs`, `EuropeanNationTypeTests.cs` |
| **FreeCol reference** | `freecol/data/rules/classic/specification.xml` (copied to `game/data/rules/classic/`) |
| **Related systems** | [map-terrain](map-terrain.md), all future rule-driven systems |

## 1. How it works (plain English)

All the game's rule numbers — what each terrain produces, how hard it is to cross, what can be built where — come from a data file, not from code. We use FreeCol's "classic" rules file unchanged: it's the community's careful reconstruction of the original 1994 game's numbers. Because rules are data, the planned Australia variant is mostly "write a different data file."

**Worked example:** the rules file says plains cost 3 movement to enter, take 3 turns to plough, and yield 5 grain when farmed. Our code reads those numbers; it never hard-codes them.

## 2. Detailed rules

- The classic ruleset defines **23 terrain types**: 8 base land, 8 forest variants, hills, mountains, arctic, and 4 water types (ocean, lake, high seas, great river).
- Flags: `is-forest`, `is-water`, `is-elevation`, `can-settle` (default true; false for mountains and all water), `is-connected` (high-seas access: true for ocean/high seas; **false for great rivers and lakes**).
- Every type has `basic-move-cost` (3/6/9 scale; 3 = one normal move) and `basic-work-turns`.
- Production entries: one `unattended` (colony-centre yield), plus attended options per goods type.

**Deviations from FreeCol:** none — the file is used verbatim.

## 3. Technical design

- `Ruleset.LoadEmbedded(resource)` reads a `specification.xml` **embedded in GameLogic.dll** (identical bytes for game, tests, CI); `Ruleset.LoadClassic()` is the convenience for the classic variant. Which spec loads is chosen by the selected **game variant** (`GameVariant.LoadRuleset`, ADR-018 — see [game-modes](game-modes.md)). `Ruleset.Load(Stream)` parses any spec — the engine is variant-agnostic.
- Parse: `System.Xml.Linq`; strict — missing ids/costs or duplicate ids throw `RulesetFormatException`.
- Model: `TerrainType` (immutable record; `ShortName` strips the `model.tile.` prefix), `ProductionEntry`, `GoodsOutput`. Lookup via `Ruleset.Terrain(id)` (throws `KeyNotFoundException` on unknown id).
- The copied spec file is upstream data — never edit (see `game/data/README.md`); deviations happen in code or future overlay rulesets.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `RulesetTests`: 23-type count, plains/mountains/water values pinned against the file, defaults, malformed-XML rejection | ✅ |
| L2 Scenario | Always | Exercised by every scenario test (all gameplay reads this data) | ✅ |
| L3 Interaction | No UI | — | — |
| L4 Visual | No screen | — | — |

- **FreeCol cross-check:** the data *is* FreeCol's, so values match by construction.

## 5. Open issues / TODO

- [x] European **`<nation>`** + **`<european-nation-type>`** parsed (FP-3a): the four colonial powers + their REFs, advantages (abilities/modifiers), starting units (extends-resolved), and per-nation classic colony names — see [players](players.md). *(Native nation types + settlement templates: see [natives](natives.md).)*
- [x] Unit **roles** parsed (`<roles>` → `RoleType`: required-goods, downgrade, granted/required abilities, `role-change` capture) and **unit-change types** (`UnitChange`: promotion/demotion/capture; `lostCity` consumed by [lost-city-rumours](lost-city-rumours.md) — the parser already reads every `<unit-change-type>` generically, `UnitChangeTypeIds.LostCity` just names it) — slice 5b; settlement `<plunder>` ranges — slice 5c.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-18 | Building `rebel-factor` attribute (`BuildingType.RebelFactor`; `double`, default 1, nearest definition wins up the `extends` chain via `ResolveDoubleAttribute` — lumber mill/cathedral 2, factory tier 1.5) — scales the Sons-of-Liberty production bonus folded into each building worker (see [sons-of-liberty](sons-of-liberty.md), [colonies](colonies.md)) | Phase 3 (`86d3b6nrz` slice 5) |
| 2026-06-18 | Unit `expert-production` + index-30 goods `<modifier>`s (`UnitType.ExpertProduction` + `ProductionModifiers` = `UnitProductionModifier(GoodsId/Type/Value/Index)`) — the expert bonus / indentured-petty penalty; parse-only foundation for the colony-worker-identity refactor (see [colonies](colonies.md)) | Phase 5 (`86d3b6nrz` slice 1) |
| 2026-06-17 | Settlement-type `<gifts>` RandomRange (`SettlementType.Gifts` = `SettlementGifts(probability/min/max/factor)`) — a scout's chief "beads" (see [natives](natives.md)) | Phase 5 (`86d3c9tf0`) |
| 2026-06-17 | Unit `skill` attribute (`UnitType.Skill`; 0 colonist/ship/artillery, ≥1 expert) → `IsTrainedInEurope`/`IsPurchasedInEurope` Europe partition (see [europe](europe.md)) | Phase 5 (`86d3c9qgy`) |
| 2026-06-17 | Building breeding modifiers `breedingDivisor`/`breedingFactor` (`BuildingType.BreedingDivisor`/`BreedingFactor`; resolved additive-then-multiplicative up the `extends` chain via `ResolveScalarModifierUpChain` — pasture 50/2, stables ×0.5 → 25/2) — drives horse breeding (see [colonies](colonies.md)) | Phase 5 (`86d3c9nwr`) |
| 2026-06-17 | Building ability flag `export` (`BuildingType.GrantsExport`; the custom house) — see [custom-house](custom-house.md) | Phase 5 (`86d3c9ru3`) |
| 2026-06-17 | Unit ability flag `carryTreasure` (`UnitType.CarryTreasure`; the treasure train) — see [treasure-train](treasure-train.md) | Phase 5 (`86d3c9ryj`) |
| 2026-06-17 | Building ability flag `bombardShips` (`BuildingType.BombardsShips`; fort grants, fortress inherits) — see [combat](combat.md) | Phase 5 (`86d3c9tkk`) |
| 2026-06-13 | Terrain-type parsing, embedded classic ruleset | Phase 1 skeleton |
| 2026-06-13 | Unit types (with `extends` inheritance + ability resolution); terrain `<gen>` climate envelopes | Phase 2a |
| 2026-06-13 | Goods types: `is-food`, `stored-as` (grain/fish/meat → food), `made-from` (chains data), `is-farmed`; `Ruleset.StorageIdOf` | Phase 3 |
| 2026-06-13 | Building types: per-worker input→output conversions (ProductionEntry gains Inputs), workplaces, upgrade chains, build costs (required-goods) | Phase 3 |
| 2026-06-13 | Goods market data (`<market>`: initial-amount/price/difference) + new-world flag on GoodsType | Phase 4 |
| 2026-06-13 | Founding Fathers (`<founding-father>`: type + age weights) | Phase 4 |
| 2026-06-13 | Founding-father `<modifier>`/`<ability>` (FatherModifier/FatherAbility); unit `recruit-probability`, `model.ability.person`, `space`/`spaceTaken` | Phase 4 slices 4–7 |
| 2026-06-13 | Resource types (`<resource-type>` yield modifiers, ResourceType/ResourceModifier) | Phase 4 slice 8 |
| 2026-06-13 | Unit `price` (Europe purchase/training cost) → `UnitType.Price`/`IsPurchasable` | Phase 4 slice 11 |
| 2026-06-13 | Native nation types (`<indian-nation-type>`: settlement templates, number-of-settlements, aggression, skills, regions) + settlement types (`<settlement>`: sizes, radii, trade-bonus, defence modifier) with `extends` resolution; `NativeNationType`/`SettlementType`/`NativeSkill` | Phase 5 slice 1 |
| 2026-06-13 | `Ruleset.LoadEmbedded` + variant selection (`GameVariant`/`GameVariants`) — spec chosen by game variant (ADR-018) | Phase 5 (variant layer) |
| 2026-06-14 | Unit `offence`/`defence` (base + folded offence/defence modifiers → `UnitType.Offence`/`Defence`); terrain `model.modifier.defence` → `TerrainType.DefenceBonus` | Phase 5 slice 5a |
| 2026-06-14 | Roles (`<roles>` → `RoleType`: offence/defence, required-goods, downgrade, abilities, `role-change` capture rules) + unit-change types (`<unit-change-types>` → `UnitChange`: promotion/demotion/capture; `Ruleset.GetUnitChange`/`CaptureRole`); unit combat-ability flags (`disposeOnCombatLoss`/`canBeCaptured`/`captureUnits`/`captureEquipment`/`disposeOnAllEquipLost`/`demoteOnAllEquipLost`/`bombard`) | Phase 5 slice 5b |
| 2026-06-14 | Settlement `<plunder>` ranges (`SettlementPlunder` base + extra → `SettlementType.Plunder`/`PlunderRange`); the plunder gold formula matches FreeCol `RandomRange` | Phase 5 slice 5c |
| 2026-06-14 | European nations + nation-types (`<nation>` / `<european-nation-type>` → `EuropeanNation`/`EuropeanNationType`/`EuropeanStartingUnit`): the four classic powers + REFs, advantages (abilities/modifiers), starting units (extends-resolved, expert variants kept), `ref` flag. Per-nation classic colony names embedded from FreeCol's message strings (`european-nation-names.properties`) and parsed. Data only — `FoundColony` adopts per-nation names in FP-3b | FP-3a |
| 2026-06-16 | Goods classification flags: `is-military` / `trade-goods` → `GoodsType.IsMilitary`/`IsTradeGoods`; derived `Ruleset.BuildingMaterials` (every goods id any building's build cost requires). For native tribute-demand goods selection (FreeCol `GoodsType.getMilitary`/`isTradeGoods`/`isBuildingMaterial`) — `BuildingMaterials` is the building subset only (it omits food, which FreeCol derives from the colonist's food growth-cost; documented in [natives.md](natives.md)) | Phase 5 native tribute |
| 2026-06-16 | Building `model.modifier.warehouseStorage` → `BuildingType.WarehouseStorage` (summed up the `extends` chain via `SumModifierUpChain`: depot 100 / warehouse 200 / expansion 300) — drives `Game.WarehouseCapacity` + overflow spoilage (see [colonies.md](colonies.md)) | Phase 5 (`86d3c9nnp`) |
| 2026-06-16 | Building `model.goods.bells` percentage → `BuildingType.BellBonus` (own valued/non-delete modifier, like `DefenceBonus`: printing press 50, newspaper 100) — drives `Game.BellProductionBonus` (the printing-press/newspaper bell boost, see [sons-of-liberty.md](sons-of-liberty.md)) | Phase 5 (`86d3c9p33`) |
| 2026-06-16 | Building `<required-ability>` → `BuildingType.RequiredAbilities` (id → required value; collected down the `extends` chain via `CollectRequiredAbilitiesUpChain` so drydock/shipyard inherit docks' `hasPort`) — drives the build-ability gate (factory tier `buildFactory`, custom house `buildCustomHouse`, docks/drydock/shipyard `hasPort`; see [colonies.md](colonies.md)) | Phase 5 (`86d3c9p0q`) |
| 2026-06-16 | Building `model.ability.repairUnits` → `BuildingType.RepairsNavalUnits` (resolved down the `extends` chain via `ResolveAbility`: drydock grants it, shipyard inherits) — drives drydock-colony ship repair (see [combat.md](combat.md)) | Phase 5 (`86d3c9p0q`) |
| 2026-06-17 | Terrain `model.ability.ambushTerrain` → `TerrainType.AmbushTerrain` (forests + hills) — drives the native terrain-ambush combat bonus (see [combat.md](combat.md)) | Phase 5 (`86d3c9tp0`) |
| 2026-06-17 | Role `model.modifier.lineOfSightBonus` → `RoleType.LineOfSightBonus` (scout +1) — drives the scout sight bonus (`Game.LineOfSightOf`; see [fog-of-war.md](fog-of-war.md)) | Phase 5 (`86d3c9upk`) |
| 2026-06-17 | Unit `<required-goods>`/`required-population`/`<required-ability>`/`<limit>` → `UnitType.BuildCost`/`RequiredPopulation`/`RequiredAbilities`/`BuildLimit`; building `model.ability.build` `<scope>` → `BuildingType.BuildableUnitTypeIds`/`BuildsNavalUnits` (collected down the `extends` chain via `CollectBuildUnitTypeScopes`/`GrantsNavalBuildScope`) — drives colony unit construction (artillery→armory, wagon train→carpenter's house, the wagon-train cap; see [colonies.md](colonies.md)) | Phase 5 (`86d3ce1bu`) |
