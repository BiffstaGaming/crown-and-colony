# System: Difficulty levels

| | |
|---|---|
| **Status** | In development (slice 1: the parse + overlay infrastructure and the founding-father factor + units-that-use-no-bells are routed through it; the rest of the ~17 tuning constants follow in later slices) |
| **Last verified** | 2026-06-18 @ difficulty parse + FF-factor routing (`86d3c9y08` slice 1) |
| **Code** | `game/src/GameLogic/Specification/DifficultyOptions.cs`; `Specification/Ruleset.cs` (`ParseDifficulty`, `Difficulty` property); consumers read `Ruleset.Difficulty.*` |
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
- **Routed so far (slice 1):** `model.option.foundingFatherFactor` (24/32/40/48/56 across the levels — see [founding-fathers](founding-fathers.md)) and `model.option.unitsThatUseNoBells` (2 on every classic level — bell upkeep, see [sons-of-liberty](sons-of-liberty.md)).
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
- **Only two constants are routed so far.** The remaining ~15 (government limits, native land-price/demands/rumour, immigration increments, treasure-transport fee, the base-`gameOptions` immigration trio) are still hard-coded at their medium values and move across in later slices — each behaviour-preserving at the default, since every one already holds its medium value.

## 3. Technical design

*Audience: developers / future sessions.*

- **`DifficultyOptions` (record):** an immutable bundle of the routed tuning values (grown slice by slice), plus `DifficultyOptions.ClassicMedium` — the fallback and default source of truth. Pure/immutable (ADR-009): parsed once, no state, no RNG.
- **`Ruleset.ParseDifficulty(XElement root, string levelId = "model.difficulty.medium")`:** mirrors the `ParseCalendar`/`ParseFatherAgeYears` idiom but with one **critical divergence** — difficulty options are restated under every level group, so it **first selects the level subtree** (`root.Descendants("optionGroup").FirstOrDefault(id == levelId)`) **then** searches *within* that subtree (`level.Descendants("integerOption")…`). Searching the whole document would match the first level (`veryEasy`) — a deliberate test (`ParseDifficulty_DefaultsToMedium_NotTheFirstLevel`) guards against exactly that. A local `IntOption(id, fallback)` helper rooted at the level reads each value, each falling back to `ClassicMedium`. A missing level returns `ClassicMedium` wholesale.
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

- [ ] **Slice 2** — route the four **government limits** into `Colony.ProductionBonus` (pass a `GovernmentLimits` value in; remove the "must become data-driven" debt note). See [sons-of-liberty](sons-of-liberty.md).
- [ ] **Slice 3** — route the **natives** group (`landPriceFactor`, `nativeDemands` raw, `rumourDifficulty` raw). See [natives](natives.md), [lost-city-rumours](lost-city-rumours.md).
- [ ] **Slice 4** — a `percentageOption` parser variant + route `badRumour`/`goodRumour` and the remaining immigration integers (`crossesIncrement`, `lowerCapIncrease`, `priceIncrease.artillery`, `recruitPriceIncrease`).
- [ ] **Slice 5** — `treasureTransportFee`; split the base-`gameOptions` immigration trio (`initialImmigration`/`europeanUnitImmigrationPenalty`/`playerImmigrationBonus`) into a `GameOptions` bundle (they are not difficulty-scoped).
- [ ] **Slice 6** — player-selectable + **persisted** level (`Game.New` levelId param + new-game picker; **additive save-version bump** to store the chosen level so a reload reconstructs the same options; old saves default to medium).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-18 | **Difficulty-level system, slice 1** (`86d3c9y08`): `DifficultyOptions` record + `Ruleset.ParseDifficulty` (selects a named level's subtree, default medium, with per-option fallback to `ClassicMedium`) + `Ruleset.Difficulty`. Routed the **founding-father factor** and **units-that-use-no-bells** off their Game.cs consts. Behaviour-preserving at the default level (medium = the old 40/2); no save change; soak byte-stable. Corrected the inaccurate "24 = other difficulty" comments (24 is veryEasy). +8 L1 (`DifficultyOptionsTests`). | Phase (`86d3c9y08` slice 1) |
