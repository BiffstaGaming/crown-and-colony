# Module: CrownAndColony.GameLogic

| | |
|---|---|
| **Last verified** | 2026-06-13 @ Phase 5 slice 1 |
| **Location** | `game/src/GameLogic/` |
| **Layer** | GameLogic (engine-free) |
| **Depends on** | nothing (BCL only) |
| **Used by** | `CrownAndColony` (Godot presentation project), `GameLogic.Tests` |

## Purpose

The entire rules engine of Crown & Colony: every game rule, calculation, and state transition. It is explicitly **not** responsible for anything visual, audible, or input-related — that's the Godot presentation project.

**The defining constraint (ADR-006):** this project must never reference Godot (no `Godot.NET.Sdk`, no `GodotSharp`). That keeps the whole rules engine testable with plain xUnit, headless, in milliseconds. The csproj carries a comment to that effect; treat adding a Godot dependency here as a build-breaking offence.

## Public API

| Type / member | What it does |
|---|---|
| `Randomness.IGameRandom` | The only permitted randomness source (ADR-009) |
| `Randomness.Pcg32Random` | Deterministic PCG32 implementation; `FromState()` resumes saves |
| `Randomness.RandomState` | Serializable generator snapshot |
| `Specification.Ruleset` | Parsed rule data; `LoadClassic()` / `LoadEmbedded(resource)` / `Load(Stream)`; `Terrain(id)`, `Unit(id)` |
| `Specification.GameVariant` / `GameVariants` | Selectable game variant (id, name, ruleset loader) + registry (`ClassicAmerica`, `All`, `Default`, `ById`, `Resolve`) — transposability backbone (ADR-018) |
| `Specification.TerrainType` / `ProductionEntry` / `GoodsOutput` / `GenRanges` | Immutable terrain rule data incl. climate envelopes |
| `Specification.UnitType` | Unit rule data (movement, sight, naval, foundColony, `RecruitProbability`, `IsPerson`, `Space`/`SpaceTaken`/`IsCarrier`/`CarrySlots`, `Price`/`IsPurchasable`) with `extends` inheritance resolved |
| `Specification.GoodsType` | Goods rule data: `is-food`, `stored-as`, `made-from`, breeding number, market seed |
| `Specification.BuildingType` | Building rule data: conversions (with inputs), workplaces, upgrade chain, build cost |
| `Specification.ResourceType` / `ResourceModifier` | Bonus-resource yield modifiers (goods, type, index, unit-type scopes) |
| `Specification.NativeNationType` / `SettlementType` / `NativeSkill` / `SettlementNumber` / `NativeAggression` | Native nation + settlement rule data (templates, counts, aggression, taught skills); `Ruleset.NativeNation(id)`, `Settlement(id)` |
| `Natives.NativeSettlement` / `Natives.AlarmLevel` | A placed native settlement: nation type, settlement type, capital flag, position, size, taught skill, + interaction state (alarm/`AlarmLevel`, visited, skill-consumed) |
| `World.NativeSettlementGenerator` | Seeded native-settlement placement (capital-first, min-distance, suitability) |
| `World.Position` | Grid coordinate; 8-way adjacency |
| `World.GameMap` | Immutable terrain grid + bonus resources (`ResourceAt`) |
| `World.MapGenerator` | Seeded climate-band map generation + resource placement |
| `Units.Unit` / `UnitLocation` | Unit state: map/sailing/Europe `Location`, `SailTurnsRemaining`, `Cargo`, `CarrierId`/`IsAboard`, `IsOnMap`; mutated only via `Game` |
| `Colonies.Colony` | Colony state: population, stores, tile/building workers, buildings, build target |
| `Trade.Market` | European market: per-good bid/ask, supply-driven `Sell` with tax (FreeCol price model) |
| `Specification.GoodsMarket` | Per-good market seed (initial amount/price/spread) |
| `Specification.FoundingFather` / `FatherType` / `FatherModifier` / `FatherAbility` / `ModifierType` / `ModifierMath` | Founding-father rule data: category, age weights, the modifiers + abilities an election grants |
| `GameSession.Game` | The running game. Map/units: `New`, `CheckMove`/`MoveUnit`, `EndTurn`, `SpawnUnit`, `CheckFoundColony`/`FoundColony`, `TileYield`. Colony work: `AssignWork`/`UnassignWork`, `AssignBuildingWork`/`UnassignBuildingWork`, `SetBuild`/`Buildables`, `JoinColony`/`LeaveColony`. Trade: `Gold`, `TaxRate`, `Market`, `SellColonyGoods`/`SellShipCargo`/`BuyEuropeGoods`, `BuyUnit`/`CheckBuyUnit`. Europe/sailing: `SailToEurope`/`SailToNewWorld`, `UnitsInEurope`. Transport: `Board`/`Disembark`/`DisembarkToDock`, `Passengers`, `CargoCapacity`/`CargoSlotsUsed`/`CargoSlotsFree`. Fathers: `Liberty`, `Congress`, `ChooseFather`, `OfferedFathers`, `HasAbility`, `ApplyGoodsModifiers`. Immigration: `Immigration`/`ImmigrationRequired`, `RecruitDock`, `RecruitPrice`, `Recruit`/`CheckRecruit`. Natives: `NativeSettlements`, `NativeSettlementAt`, `ChangeNativeAlarm`, `Visit`/`CheckVisit`, `LearnSkill`/`CheckLearnSkill`. Fog: `Explored`/`IsExplored`, `CurrentlyVisible`/`IsVisible`. (All checks have a `Check…` oracle, ADR-006.) |
| `GameSession.MoveCheck` / `InvalidMoveException` | Move legality result / violation |
| `Persistence.SaveGame` / `SavedUnit` / `SavedColony` / `SavedResource` / `SavedWorker` / `SavedNativeSettlement` | Complete JSON-serializable game snapshot (format v16; records its game variant + native interaction state) |

(Grows as systems land; keep this table current.)

## Key design notes

- `TreatWarningsAsErrors` and nullable reference types are on; keep them on.
- `GenerateDocumentationFile` is on — public members without XML doc comments fail the build, which mechanically enforces the documentation rule for APIs.
- Target: `net8.0` (matches Godot 4.6's .NET target). Tests roll forward to the installed runtime.

## Tests

`game/tests/GameLogic.Tests/` — xUnit, mirrors this project's folder structure. **266 tests** (264 L1+L2 incl. 10 E2E journeys + 2 nightly soak), all green as of 2026-06-14. (Scene/visual L3+L4 live in the Godot project — see [presentation.md](presentation.md).)

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Project created with Randomness namespace | Phase 0 scaffold |
| 2026-06-13 | Phases 1–3: ruleset parsing, map/units/turns/save, fog, colonies + full economy (stores, tile/building work, construction, growth) | Phases 1–3 |
| 2026-06-13 | Phase 4: market+treasury, founding fathers + effects (modifier/ability system), Europe + high-seas sailing, immigration & recruitment, unit transport, bonus-resource yields, colonist join/leave; save v13 | Phase 4 |
| 2026-06-13 | Phase 5 slice 1: native nation + settlement rule data, `NativeSettlement` domain + seeded placement (`NativeSettlementGenerator`), save v14 | Phase 5 slice 1 |
| 2026-06-13 | Variant/game-mode selection layer (`GameVariant`/`GameVariants`, `Ruleset.LoadEmbedded`); variant-aware saves (v15) — transposability backbone (ADR-018) | Phase 5 (variant layer) |
| 2026-06-13 | Explored-vs-visible fog: `CurrentlyVisible`/`IsVisible` (units + colonies), colony reveal on founding | Phase 5 (fog upgrade) |
| 2026-06-14 | Native interaction: alarm model (`AlarmLevel`, `ChangeNativeAlarm`, turn decay), `Visit` (tales + gift), `LearnSkill` (unit upgrade); save v16 | Phase 5 slice 3 |
