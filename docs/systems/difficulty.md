# System: Difficulty levels

| | |
|---|---|
| **Status** | In development (slices 1–5: the parse + overlay infrastructure and **every difficulty-level-scoped tuning constant with consuming code** are routed through it. Two pieces remain: the base-`gameOptions` immigration trio (a separate follow-up — those options are not difficulty-scoped and snag a `Player` field-initializer), and player-selectable + persisted level (slice 6, needs a new-game UI + a save-version bump)) |
| **Last verified** | 2026-06-18 @ treasure-transport fee routed; difficulty-scoped routing complete (`86d3c9y08` slice 5) |
| **Code** | `game/src/GameLogic/Specification/DifficultyOptions.cs`, `Specification/GovernmentLimits.cs`; `Specification/Ruleset.cs` (`ParseDifficulty`, `Difficulty` property); consumers read `Ruleset.Difficulty.*` (`Colony.Government` carries the limits) |
| **Tests** | `game/tests/GameLogic.Tests/Specification/DifficultyOptionsTests.cs` |
| **FreeCol reference** | `Specification.applyDifficultyLevel`/`getInteger` (`Specification.java` ~1185), spec `optionGroup id="difficultyLevels"` (`specification.xml` ~3398) with `model.difficulty.veryEasy…veryHard`; default `model.difficulty.medium` (`FreeCol.java` `DIFFICULTY_DEFAULT`) |
| **Related systems** | [founding-fathers](founding-fathers.md), [sons-of-liberty](sons-of-liberty.md), [natives](natives.md), [immigration](immigration.md), [ruleset-data](ruleset-data.md), [game-modes](game-modes.md) |

## 1. How it works (plain English)

A **difficulty level** is a named bundle of tuning numbers that make the game harder or easier — how dearly founding fathers cost, how greedy the natives are, how fast recruits get pricey, where the "good/bad government" lines sit, and so on. Classic Colonization ships five levels — **very easy, easy, medium, hard, very hard** — and the game is balanced around **medium**, which is the default.

Until now these numbers were scattered through the C# as hard-coded constants. This system reads them from the rules data instead, exactly like the original: pick a level, and every one of those numbers comes from that level. Today the game always plays at **medium** (the same numbers as before — nothing changes for you yet); a later step adds a new-game difficulty picker and remembers your choice in the save.

**The rules, in plain words:**
- The rules data lists all five levels; each level states its own full set of numbers (the levels don't inherit from one another).
- The game selects one level (medium by default) and reads its numbers wherever a tuning value is needed.
- If the data is missing a level or a number, the game falls back to the classic medium value, so it always has something sensible to use.

**Worked example:**
> The founding-father "factor" (how much liberty each father costs) is **24** on very easy, **40** on medium, **56** on very hard. At medium a colony needs 40 liberty for its first father; on very hard it needs 56 for the same father — fathers are a bigger commitment on harder levels.

**What the player sees and does:** nothing yet — the level is fixed at medium. A new-game difficulty picker (and saving the chosen level) is a later slice.

## 2. Detailed rules

- **The levels** live under the spec `optionGroup id="difficultyLevels"` (which is `recursive="false"`, so its options are *not* flattened into the global option map — a level must be selected and overlaid). Each of `model.difficulty.{veryEasy,easy,medium,hard,veryHard}` restates the full option set across six fixed subgroups: `immigration`, `natives`, `monarch`, `government`, `other`, `cheat`. **There is no inheritance between levels** — each value is concrete per level.
- **Selection / overlay:** a chosen level's `model.option.*` values are read straight into `DifficultyOptions`. The default is `model.difficulty.medium`. (FreeCol overlays the level onto the base options by whole-option, last-write-wins by id; because there is no cross-level inheritance, reading the selected level's subtree directly is equivalent.)
- **Routed so far (slices 1–5):** `model.option.foundingFatherFactor` (24/32/40/48/56 — see [founding-fathers](founding-fathers.md)); `unitsThatUseNoBells` (2 — bell upkeep); the **four government limits** (medium 100/50/6/10) → `Ruleset.Difficulty.Government`, on `Colony.Government`, read by `Colony.ProductionBonus` (see [sons-of-liberty](sons-of-liberty.md)); the **natives group** — `landPriceFactor` (60), raw `nativeDemands` (2) and raw `rumourDifficulty` (2) (see [natives](natives.md), [lost-city-rumours](lost-city-rumours.md)); the **rumour percentages** `badRumour`/`goodRumour` (23/48, `percentageOption`s); the **immigration/recruit/artillery increments** `crossesIncrement`/`recruitPriceIncrease`/`lowerCapIncrease`/`priceIncrease.artillery` (2/30/0/100 — see [immigration](immigration.md), [europe](europe.md)); and the **treasure-transport fee** `treasureTransportFee` (60 — see [treasure-train](treasure-train.md)). That is **every difficulty-level-scoped option with consuming code.** **Derived transforms keep the raw spec value in the option and the formula in code** (rumour-dx `10−x`, demand-dx `+1`, accept-relief `(5−x)·50`).
- **Fallbacks:** a selected level absent from the spec → all values from `DifficultyOptions.ClassicMedium`; an individual option absent from an otherwise-present level → that one value from `ClassicMedium`.

| Input / condition | Result |
|---|---|
| Default load (classic spec) | `Ruleset.Difficulty` = medium values (`FoundingFatherFactor`=40, `UnitsThatUseNoBells`=2) |
| `ParseDifficulty(root, "model.difficulty.veryEasy")` | `FoundingFatherFactor`=24 |
| `ParseDifficulty(root, "model.difficulty.veryHard")` | `FoundingFatherFactor`=56 |
| Selected level not in spec | `DifficultyOptions.ClassicMedium` |
| Option missing from a present level | that option = `ClassicMedium`'s value; others parsed |

**Deviations from original 1994 / FreeCol behavior:**
- **Public surface is typed, not string-keyed.** FreeCol reads options by id (`getInteger("model.option.foundingFatherFactor")`); we expose strongly-typed properties on `DifficultyOptions` (`Ruleset.Difficulty.FoundingFatherFactor`) for compile-time call sites and per-option XML docs. The by-id lookup is an implementation detail of the parser.
- **Selection is not yet persisted.** The level is fixed at medium; a player-selectable, save-persisted level (with an additive save-version bump) is a later slice. Until then a save carries no level — it always reloads at medium (which is correct while medium is the only level used).
- **The base-`gameOptions` immigration trio is a separate follow-up.** `initialImmigration`/`europeanUnitImmigrationPenalty`/`playerImmigrationBonus` are **not** difficulty-scoped (they live in the base `gameOptions` group, identical across levels), and `initialImmigration` is a `Player` field-initializer default (`Player.ImmigrationRequired`), which can't read an instance ruleset value at construction — so routing them needs a small `GameOptions` bundle + threading the value into player creation. Split into its own task to keep this difficulty epic focused on the difficulty levels.

## 3. Technical design

*Audience: developers / future sessions.*

- **`DifficultyOptions` (record):** an immutable bundle of the routed tuning values (grown slice by slice), plus `DifficultyOptions.ClassicMedium` — the fallback and default source of truth. Pure/immutable (ADR-009): parsed once, no state, no RNG.
- **`Ruleset.ParseDifficulty(XElement root, string levelId = "model.difficulty.medium")`:** mirrors the `ParseCalendar`/`ParseFatherAgeYears` idiom but with one **critical divergence** — difficulty options are restated under every level group, so it **first selects the level subtree** (`root.Descendants("optionGroup").FirstOrDefault(id == levelId)`) **then** searches *within* that subtree (`level.Descendants("integerOption")…`). Searching the whole document would match the first level (`veryEasy`) — a deliberate test (`ParseDifficulty_DefaultsToMedium_NotTheFirstLevel`) guards against exactly that. A local `IntOption(id, fallback)` helper rooted at the level reads each value, each falling back to `ClassicMedium`; a parallel `PctOption` variant reads `percentageOption` elements (the rumour percentages) — same `value` attribute, different element name. Dotted ids (`model.option.priceIncrease.artillery`) match as opaque strings. A missing level returns `ClassicMedium` wholesale.
- **Wiring:** `Ruleset` gains a trailing ctor param and a `public DifficultyOptions Difficulty { get; }` property, assigned alongside `Calendar`/`FatherAgeYears`. `Load` calls `ParseDifficulty(root)` next to the calendar parses.
- **Consumers:** `Game` already holds `Ruleset`, so each migration is mechanical — e.g. `TotalFoundingFatherCost` reads `Ruleset.Difficulty.FoundingFatherFactor`, and the bell-upkeep step reads `Ruleset.Difficulty.UnitsThatUseNoBells`. **`Colony` is deliberately kept free of a `Ruleset` dependency** — when the government limits are routed (slice 2) they will be *passed into* `Colony.ProductionBonus`, not pulled from a ruleset reference, to keep pure colony logic decoupled.
- **Determinism / save:** no RNG; nothing new persisted. Difficulty is re-derived from the (versioned) ruleset + selected level at load, exactly like the calendar — so the soak round-trips byte-identically and **no save-version bump** is needed until a player-selectable level is persisted.
- **Derived-vs-raw rule (later slices):** where a constant is a *transform* of a spec value (e.g. the lost-city `rumourDifficultyDx` = `10 − rumourDifficulty`), store the **raw** spec value in `DifficultyOptions` and keep the transform in code, so the option matches the spec.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `DifficultyOptionsTests`: default = medium (not the first/`veryEasy` level); named-level selection (veryEasy 24 / medium 40 / veryHard 56 + units-that-use-no-bells); absent-level + per-option fallback to `ClassicMedium`; **real classic spec parses medium 40/2**; `TotalFoundingFatherCost` unchanged (behaviour preserved) | ✅ |
| L2 Scenario | Via consumers | the founding-father / immigration / soak suites stay green (regression proof of no behaviour change at medium) | ✅ |
| L3 Interaction | No UI yet | — (difficulty picker is a later slice) | — |
| L4 Visual | No screen yet | — | — |
| L5 Soak | Always | byte-identical round-trip (medium values equal the old consts; nothing new persisted) | ✅ |

- **FreeCol cross-check:** the per-level `foundingFatherFactor` ladder (24/32/40/48/56) and `unitsThatUseNoBells` (2) match the classic spec's `difficultyLevels` groups; the default level (medium) matches `FreeCol.DIFFICULTY_DEFAULT`.

## 5. Open issues / TODO

- [x] **Slice 2** — the four **government limits** routed into `Colony.ProductionBonus` via `Colony.Government` (a `GovernmentLimits` value set from `Ruleset.Difficulty`; no `Ruleset` dep on `Colony`); the "must become data-driven" debt note resolved. See [sons-of-liberty](sons-of-liberty.md).
- [x] **Slice 3** — the **natives** group routed (`landPriceFactor`, raw `nativeDemands`, raw `rumourDifficulty`); the derived `RumourDifficultyDx`/`NativeDemandsDx`/`NativeDemandAcceptAlarmRelief` became instance properties (raw value in the option, transform in code), and `CapDemand` is now an instance method. See [natives](natives.md), [lost-city-rumours](lost-city-rumours.md).
- [x] **Slice 4** — added the `PctOption` parser variant (over `percentageOption`) and routed `badRumour`/`goodRumour` + the immigration integers (`crossesIncrement`, `recruitPriceIncrease`, `lowerCapIncrease`, `priceIncrease.artillery`). See [lost-city-rumours](lost-city-rumours.md), [immigration](immigration.md), [europe](europe.md).
- [x] **Slice 5** — `treasureTransportFee` routed (`Ruleset.Difficulty.TreasureTransportFee`, medium 60). This completes the difficulty-level-scoped routing.
- [ ] **Base-`gameOptions` immigration trio** (split out of slice 5 into its own task) — a `GameOptions` bundle for `initialImmigration`/`europeanUnitImmigrationPenalty`/`playerImmigrationBonus` (not difficulty-scoped; needs the `Player.ImmigrationRequired` field-init reworked to read it at player creation).
- [ ] **Slice 6** — player-selectable + **persisted** level (`Game.New` levelId param + new-game picker; **additive save-version bump** to store the chosen level so a reload reconstructs the same options; old saves default to medium). **Needs Chris's steer on the new-game UI flow.**

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-18 | **Slice 5 — treasure-transport fee** (`86d3c9y08`): `treasureTransportFee` (medium 60, the monarch-group option) routed to `Ruleset.Difficulty.TreasureTransportFee`, completing the routing of **every difficulty-level-scoped option with consuming code**. The base-`gameOptions` immigration trio was split into its own follow-up (not difficulty-scoped + a `Player` field-init snag). Behaviour-preserving at medium; no save change; soak byte-stable. +2 L1. See [treasure-train](treasure-train.md). | Phase (`86d3c9y08` slice 5) |
| 2026-06-18 | **Slice 4 — percentage + immigration options** (`86d3c9y08`): added a `PctOption` parser variant (over `percentageOption`) and routed the rumour percentages `badRumour`/`goodRumour` (23/48) + the immigration/recruit/artillery integers `crossesIncrement`/`recruitPriceIncrease`/`lowerCapIncrease`/`priceIncrease.artillery` (2/30/0/100) off their Game.cs consts (the dotted `priceIncrease.artillery` id matches as an opaque string). Behaviour-preserving at medium; no save change; soak byte-stable. +2 L1 (percentage path + dotted-id + real-spec medium). See [lost-city-rumours](lost-city-rumours.md), [immigration](immigration.md), [europe](europe.md). | Phase (`86d3c9y08` slice 4) |
| 2026-06-18 | **Slice 3 — natives group** (`86d3c9y08`): `landPriceFactor`, raw `nativeDemands`, raw `rumourDifficulty` added to `DifficultyOptions` and routed off their Game.cs consts. The derived constants became instance computed properties keeping the spec value raw and the transform in code (`RumourDifficultyDx = 10 − rumourDifficulty`, `NativeDemandsDx = nativeDemands + 1`, `NativeDemandAcceptAlarmRelief = (5 − nativeDemands)·50`); `CapDemand` static → instance. Behaviour-preserving at medium (60/2/2 → the old 60/3/150/8); no save change; soak byte-stable. +2 L1 (parse-by-id non-default + real-spec medium). See [natives](natives.md), [lost-city-rumours](lost-city-rumours.md). | Phase (`86d3c9y08` slice 3) |
| 2026-06-18 | **Slice 2 — government limits** (`86d3c9y08`): the four `*GovernmentLimit` options routed into a `GovernmentLimits` value on `DifficultyOptions.Government`, carried by `Colony.Government` (set from `Ruleset.Difficulty` at founding/load) and read by `Colony.ProductionBonus` — the colony stays free of a `Ruleset` dependency. Removed the four hardcoded `Colony` consts + the "must become data-driven" debt note. Behaviour-preserving at medium (100/50/6/10); no save change; soak byte-stable. +3 L1 (parse-by-id with non-default values, real-spec medium, end-to-end bonus shift). | Phase (`86d3c9y08` slice 2) |
| 2026-06-18 | **Difficulty-level system, slice 1** (`86d3c9y08`): `DifficultyOptions` record + `Ruleset.ParseDifficulty` (selects a named level's subtree, default medium, with per-option fallback to `ClassicMedium`) + `Ruleset.Difficulty`. Routed the **founding-father factor** and **units-that-use-no-bells** off their Game.cs consts. Behaviour-preserving at the default level (medium = the old 40/2); no save change; soak byte-stable. Corrected the inaccurate "24 = other difficulty" comments (24 is veryEasy). +8 L1 (`DifficultyOptionsTests`). | Phase (`86d3c9y08` slice 1) |
