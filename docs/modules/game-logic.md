# Module: CrownAndColony.GameLogic

| | |
|---|---|
| **Last verified** | 2026-06-14 @ FP-5 (foreign-power AI economy) |
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
| `Specification.TerrainType` / `ProductionEntry` / `GoodsOutput` / `GenRanges` | Immutable terrain rule data incl. climate envelopes + combat `DefenceBonus` |
| `Specification.UnitType` | Unit rule data (movement, sight, naval, foundColony, `RecruitProbability`, `IsPerson`, `Space`/`SpaceTaken`/`IsCarrier`/`CarrySlots`, `Price`/`IsPurchasable`, `Offence`/`Defence`, combat-ability flags `DisposeOnCombatLoss`/`CanBeCaptured`/`CaptureUnits`/`CaptureEquipment`/`DisposeOnAllEquipmentLost`/`DemoteOnAllEquipmentLost`/`Bombard`) with `extends` inheritance resolved |
| `Specification.RoleType` / `RoleRequiredGoods` / `RoleChange` | Military role/equipment rule data: offence/defence, required-goods, downgrade, granted/required abilities, equipment-capture (`role-change`) rules; `Ruleset.Role(id)`/`Roles`/`CaptureRole(winner, loser, native)` |
| `Specification.UnitChange` / `UnitChangeTypeIds` | Unit-type transitions (promotion/demotion/capture: from→to + probability); `Ruleset.GetUnitChange(changeType, fromId)` |
| `Combat.CombatModel` / `AttackContext` / `DefenceContext` / `CombatResult` / `MovementPenalty` | Pure combat model: attack/defence power, win odds (`att/(att+def)`), graded resolution (FreeCol `SimpleCombatModel`). Roles fold into the scalar base power passed in. |
| `Combat.CombatNotice` | A transient record of a native raid on the human during `EndTurn` (attacker nation id, defender unit-type id, `CombatResult`, position) — collected on `Game` (`CombatNotices`), never saved; the presentation formats it (ADR-006) |
| `Specification.GoodsType` | Goods rule data: `is-food`, `stored-as`, `made-from`, breeding number, market seed |
| `Specification.BuildingType` | Building rule data: conversions (with inputs), workplaces, upgrade chain, build cost |
| `Specification.ResourceType` / `ResourceModifier` | Bonus-resource yield modifiers (goods, type, index, unit-type scopes) |
| `Specification.NativeNationType` / `SettlementType` / `SettlementPlunder` / `NativeSkill` / `SettlementNumber` / `NativeAggression` | Native nation + settlement rule data (templates, counts, aggression, taught skills, defence + `<plunder>` ranges); `Ruleset.NativeNation(id)`, `Settlement(id)`, `SettlementType.PlunderRange` |
| `Natives.NativeSettlement` / `Natives.AlarmLevel` | A placed native settlement: nation type, settlement type, capital flag, position, size, taught skill, + interaction state (alarm/`AlarmLevel`, visited, skill-consumed) |
| `World.NativeSettlementGenerator` | Seeded native-settlement placement (capital-first, min-distance, suitability) |
| `World.Position` | Grid coordinate; 8-way adjacency |
| `World.GameMap` | Immutable terrain grid + bonus resources (`ResourceAt`) |
| `World.MapGenerator` | Seeded climate-band map generation + resource placement |
| `GameSession.Player` / `PlayerType` / `RestoredPlayer` | A player and its player-scoped state (ADR-019): identity (`PlayerId`/`NationId`/`IsHuman`/`PlayerType` {Colonial, Native}), `Gold`/`TaxRate`, its **own** `Market`, liberty/`Congress`/`CurrentFather`/`OfferedFathers`, immigration/`RecruitDock`/`RecruitPrice`, `Explored` fog, and its own RNG stream (`RngStreamId`/`Rng`). `RestoredPlayer` is the load-time DTO. Mutated via `Game` |
| `Units.Unit` / `UnitLocation` | Unit state: map/sailing/Europe `Location`, `SailTurnsRemaining`, `Cargo`, `CarrierId`/`IsAboard`, `IsOnMap`, `OwnerNationId`/`IsNative` (native owner), `OwnerId` (colonial owner; the human is 0), `RoleId`/`RoleCount`/`HasDefaultRole` (equipment); mutated only via `Game` |
| `Colonies.Colony` | Colony state: `OwnerId` (colonial owner; the human is 0), population, stores, tile/building workers, buildings, build target |
| `Trade.Market` | A **per-player** European market: per-good bid/ask, supply-driven `Sell` with tax (FreeCol price model), `SaveDeltas`/`LoadDeltas` |
| `Specification.GoodsMarket` | Per-good market seed (initial amount/price/spread) |
| `Specification.FoundingFather` / `FatherType` / `FatherModifier` / `FatherAbility` / `ModifierType` / `ModifierMath` | Founding-father rule data: category, age weights, the modifiers + abilities an election grants |
| `GameSession.Game` | The running game. Players (ADR-019): `Players`, `HumanPlayer`, `CurrentPlayer` (the ring pointer); player-scoped state is reached through `HumanPlayer` (the public no-arg `Gold`/`Market`/`Congress`/… pass through to the human, each mutating seam has an internal `Player`-taking overload). The human + 3 foreign colonial powers + native nations are `Player` rows; foreign powers run their own AI economy (sell/recruit/father) + unit AI in `EndTurn`, and native nations run a raid/wander AI (`RunNativeTurn`, slice 1b — braves attack the human's units when alarmed, resolved via the internal `Attack` overload), each on its own RNG stream/market so the human's stream 0 stays byte-stable. Map/units: `New`, `CheckMove`/`MoveUnit`, `EndTurn`, `SpawnUnit`, `CheckFoundColony`/`FoundColony`, `TileYield`. Colony work: `AssignWork`/`UnassignWork`, `AssignBuildingWork`/`UnassignBuildingWork`, `SetBuild`/`Buildables`, `JoinColony`/`LeaveColony`. Trade: `Gold`, `TaxRate`, `Market`, `SellColonyGoods`/`SellShipCargo`/`BuyEuropeGoods`, `BuyUnit`/`CheckBuyUnit`. Europe/sailing: `SailToEurope`/`SailToNewWorld`, `UnitsInEurope`. Transport: `Board`/`Disembark`/`DisembarkToDock`, `Passengers`, `CargoCapacity`/`CargoSlotsUsed`/`CargoSlotsFree`. Fathers: `Liberty`, `Congress`, `ChooseFather`, `OfferedFathers`, `HasAbility`, `ApplyGoodsModifiers`. Immigration: `Immigration`/`ImmigrationRequired`, `RecruitDock`, `RecruitPrice`, `Recruit`/`CheckRecruit`. Natives: `NativeSettlements`, `NativeSettlementAt`, `ChangeNativeAlarm`, `Visit`/`CheckVisit`, `LearnSkill`/`CheckLearnSkill`, `SellToNatives`/`CheckSellToNatives`/`NativeSalePrice`. Combat: `PlayerUnits`/`NativeUnits`, `CheckAttack`/`Attack`, `CheckAttackSettlement`/`AttackSettlement`, `CheckEquipRole`/`EquipRole`, `EffectiveCombatRole`, `CombatNotices` (transient native-raid notices). Fog: `Explored`/`IsExplored`, `CurrentlyVisible`/`IsVisible`. (All checks have a `Check…` oracle, ADR-006.) |
| `GameSession.MoveCheck` / `InvalidMoveException` | Move legality result / violation |
| `Persistence.SaveGame` / `SavedUnit` / `SavedColony` / `SavedResource` / `SavedWorker` / `SavedNativeSettlement` / `SavedPlayer` | Complete JSON-serializable game snapshot (format **v20**; per-player state is the **sole** source in a `Players[]` array — gold/tax/per-player market/liberty/Congress/immigration/dock/RNG/explored/diplomacy stance+tension; per-unit + per-colony owner ids; records its game variant, native interaction/trade state, per-unit role; a sacked settlement is simply absent. The legacy flat top-level player fields are no longer written as of FP-7 — kept read-only so ≤v19 saves still fold into one human player) |

(Grows as systems land; keep this table current.)

## Key design notes

- `TreatWarningsAsErrors` and nullable reference types are on; keep them on.
- `GenerateDocumentationFile` is on — public members without XML doc comments fail the build, which mechanically enforces the documentation rule for APIs.
- Target: `net8.0` (matches Godot 4.6's .NET target). Tests roll forward to the installed runtime.

## Tests

`game/tests/GameLogic.Tests/` — xUnit, mirrors this project's folder structure. **418 tests** (415 L1+L2 incl. 10 E2E journeys + 3 nightly soak), all green as of 2026-06-15 @ slice 1c-1. (Scene/visual L3+L4 live in the Godot project — see [presentation.md](presentation.md); 444 across all five layers.)

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
| 2026-06-14 | Native trade: sell cargo to a coastal settlement (`SellToNatives`/`NativeSalePrice`), wanted goods per settlement; save v17 | Phase 5 slice 4 |
| 2026-06-14 | Combat foundation: parse unit offence/defence + terrain defence bonus; pure `CombatModel` (power, odds, graded resolution) | Phase 5 slice 5a |
| 2026-06-14 | Combat 5b: unit ownership (`OwnerNationId`, `PlayerUnits`/`NativeUnits`) + roles/equipment (`RoleType`, `UnitChange`, `EquipRole`), brave defenders, attack action (`CheckAttack`/`Attack`) with FreeCol loser/winner outcome precedence + native alarm, Washington/Revere; save v18 | Phase 5 slice 5b |
| 2026-06-14 | Combat 5c: native settlement assault (`CheckAttackSettlement`/`AttackSettlement`, `ComputePlunder`, `SettlementPlunder`) — implicit-garrison defence, destroy + plunder gold, +500/+600 tension with sibling propagation, Cortés; can't move onto a settlement; save v19 | Phase 5 slice 5c |
| 2026-06-14 | Foreign-powers wave FP-1→FP-5 (ADR-019): extracted `Player` (player-scoped state incl. its own `Market`); owner-id seam (`Unit.OwnerId`/`Colony.OwnerId`; enemy/fog/abilities resolve by owner + a stance hook); parsed European nations as ruleset data; multi-player `Game` (human + 3 foreign powers + natives as `Player` rows; ring-buffer `EndTurn`); foreign-power AI — land/found/explore (FP-4) then a per-player economy — trade/immigration/recruit/father (FP-5), each on its own RNG stream; save **v20** (`Players[]`, per-player markets/RNG, owner ids; ≤v19 folds to one human) | FP-1…FP-5 |
| 2026-06-15 | FP-6a/6b: diplomacy — per-colonial-pair `Stance`/tension on `Player` (`StanceBetween`/`SetStance`/`ChangeTension`); contact→Peace, attack→War, decay, and the tension→stance machine (`StanceFromTension`/`UpdateColonialStances`: war→cease-fire→peace). Recorded only (no legality/AI-action change). See [diplomacy.md](../systems/diplomacy.md) | FP-6a/6b |
| 2026-06-15 | Native AI (slice 1b): `RunNativeTurn` (braves raid the human's units when their home settlement is Displeased+, else wander; one action/turn; nation's own RNG stream — human stream 0 byte-stable), `StepToward` pathing helper, `Combat.CombatNotice` + `Game.CombatNotices` transient raid feed, `CaptureUnit` capture-owner hardening. No save-format change. See [natives.md](../systems/natives.md) | Phase 5 slice 1b |
| 2026-06-15 | Slice 1c-1: `CheckMove` blocks moving onto a colony you don't own (`ColonyAt(target).OwnerId != unit.OwnerId`). Logic-only (the rival/own-unit rendering is presentation — see [presentation.md](presentation.md)); no save/RNG change. See [combat.md](../systems/combat.md), [units-movement.md](../systems/units-movement.md) | Phase 5 slice 1c-1 |
| 2026-06-15 | FP-7: save-format v20 consolidation — the legacy flat top-level player fields are no longer written (`Players[]` is the sole source); format version unchanged; flat properties kept read-only for ≤v19 fold + pre-FP-7 v20 back-compat | FP-7 |
