# System: Ruleset data

| | |
|---|---|
| **Status** | Implemented (terrain incl. `<gen>` envelopes, unit types, goods types incl. `stored-as`/`made-from`, building types incl. conversions/upgrade chains/build costs) |
| **Last verified** | 2026-06-13 @ Phase 1 walking skeleton |
| **Code** | `game/src/GameLogic/Specification/` |
| **Tests** | `game/tests/GameLogic.Tests/Specification/RulesetTests.cs` |
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

- `Ruleset.LoadClassic()` reads `specification.xml` **embedded in GameLogic.dll** (identical bytes for game, tests, CI; no loose-file export handling). `Ruleset.Load(Stream)` exists for future rulesets/mods.
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

- [ ] Parse goods-types, buildings, nations, founding fathers as those systems land (Phases 3–5).
- [ ] Unit roles (scout = colonist + horses etc.) — FreeCol models these separately from unit types.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Terrain-type parsing, embedded classic ruleset | Phase 1 skeleton |
| 2026-06-13 | Unit types (with `extends` inheritance + ability resolution); terrain `<gen>` climate envelopes | Phase 2a |
| 2026-06-13 | Goods types: `is-food`, `stored-as` (grain/fish/meat → food), `made-from` (chains data), `is-farmed`; `Ruleset.StorageIdOf` | Phase 3 |
| 2026-06-13 | Building types: per-worker input→output conversions (ProductionEntry gains Inputs), workplaces, upgrade chains, build costs (required-goods) | Phase 3 |
| 2026-06-13 | Goods market data (`<market>`: initial-amount/price/difference) + new-world flag on GoodsType | Phase 4 |
| 2026-06-13 | Founding Fathers (`<founding-father>`: type + age weights) | Phase 4 |
