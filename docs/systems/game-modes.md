# System: Game modes / variants (transposability)

| | |
|---|---|
| **Status** | In development (selection layer + Classic variant; further variants — e.g. Australia — are future data) |
| **Last verified** | 2026-06-13 @ Phase 5 (variant layer) |
| **Code** | `game/src/GameLogic/Specification/GameVariant.cs`, `Ruleset.LoadEmbedded`; selection in `game/presentation/GameController.cs` |
| **Tests** | `game/tests/GameLogic.Tests/Specification/GameVariantTests.cs` |
| **FreeCol reference** | FreeCol ships multiple rulesets (`data/rules/classic`, `data/rules/freecol`) + mods (`data/mods/`) — same "data selects the world" idea |
| **Related systems** | [ruleset-data](ruleset-data.md), [save-load](save-load.md), [founding-fathers](founding-fathers.md), [natives](natives.md) |

## 1. How it works (plain English)

The game can tell more than one story. "Colonial America" is the one we ship today; an "Australia" setting (and others later) will follow. Each of these is a **variant** — a self-contained world with its *own* nations, Founding Fathers (each with their own perks), starting countries, units and goods.

The important promise: **picking a variant is the only thing that changes the data.** All the game's rules — how colonies produce, how combat resolves, how turns advance — are written once and read whatever the chosen variant defines. So adding a new setting is a *content* job (write its data, add it to the list), not a rewrite. American Founding Fathers and Australian Founding Fathers can both exist; only the selected variant's set is used.

**Worked example:**
> In the Classic variant you might recruit Thomas Jefferson (+50% bells). A future Australia variant would define its own historical figures with their own perks in its own data file — the election screen, the bonus maths, and everything else work unchanged, because they just apply whatever the selected variant's people grant.

**What the player sees and does:** today the game starts in the Classic variant. A variant-select screen is future UI; the plumbing to choose one and have its data flow through is what this slice builds.

## 2. Detailed rules

- A **variant** has a stable `Id` (e.g. `classic`), a display name, a description, and the embedded specification it loads.
- The shipped variants live in one registry (`GameVariants.All`); `GameVariants.Default` is used for a new game or a legacy save.
- A **save records its variant id** so it always reloads under the matching ruleset. A pre-v15 save has none → it resolves to the default (Classic).
- Loading a variant whose id isn't installed fails loudly (you can't play a world whose data you don't have).

**The transposability contract (what a variant must honour):**
- The engine references a small set of **well-known ids** that every variant is expected to define — core goods (`model.goods.food`, `bells`, `crosses`, `grain`), the high-seas/ocean tiles, and the core abilities (`navalUnit`, `foundColony`, `person`). These are the *contract*, the same way FreeCol's own rulesets keep them. A variant changes the *content* (which nations, which fathers, which perks), not these structural anchors.
- Founding-Father perks expressed as `<modifier>`/`<ability>` (ADR-017) need **no code** — any variant's fathers can grant bonuses to goods, etc. A genuinely *novel* mechanic perk (one not expressible as a modifier/ability) needs a small handler keyed to its ability id.

**Deviations from original / FreeCol:** none in spirit — FreeCol is itself ruleset-driven with mods; we adopt the same "data selects the world" model. We do not (yet) support mod *overlays* that patch a base ruleset — each variant is a whole spec.

## 3. Technical design

**Domain model:** `Specification.GameVariant` (id, display name, description, embedded spec resource; `LoadRuleset()`), `Specification.GameVariants` (the registry: `ClassicAmerica`, `All`, `Default`, `ById`, `Resolve(id?)`).

**Data sources:** each variant points at an embedded `specification.xml`. `Ruleset.LoadEmbedded(resource)` reads it; `Ruleset.LoadClassic()` is now a convenience for the classic variant. The generic `Ruleset.Load(Stream)` parses *any* spec — the engine is variant-agnostic by construction.

**Integration points:** `GameController` holds the selected `_variant`; a new game loads `_variant.LoadRuleset()`. On save it records `_variant.Id`; on load it reads the save's variant, resolves it (`GameVariants.Resolve`), and restores under that ruleset.

**Persistence:** save format **v15** adds the variant id (`SaveGame.Variant`); `SaveGame.From(game, variantId)` records it. Pre-v15 saves load as Default.

**Still hard-coded (tracked migrations toward full per-variant data):**
- Colony name list (`Game.ColonyNames`) — should come from nation data (needs `<nation>` parsing; lands with the foreign-powers slice).
- Starting unit type (`Game.StartingUnitTypeId`) and a few father-effect handlers keyed to specific ability ids.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `GameVariantTests`: registry/`Default`/`ById`/`Resolve`; classic loads the American world | ✅ |
| L2 Scenario | Always | `GameVariantTests.DifferentRuleset_YieldsDifferentFathersAndNations` — the transposability proof (a custom spec → a custom father with its own perk, no shared content) | ✅ |
| L3 Interaction | Via save/load | `InputTests` F5/F9 round-trip drives the variant-aware load path | ✅ |
| L4 Visual | No screen yet | — (variant-select UI is future) | — |

- **FreeCol cross-check:** conceptual — FreeCol is ruleset+mod driven; we match the "data selects the world" model (without mod overlays yet).

## 5. Open issues / TODO

- [ ] Variant-select screen (UI) when there is more than one variant.
- [ ] Migrate the remaining hard-coded America-specific data into nation/ruleset data (colony names via `<nation>` parsing; review the well-known-id contract).
- [ ] Decide on mod-overlay support (patch a base ruleset) vs. whole-spec variants, before the Australia variant (Phase 8).
- [ ] The Australia variant itself (Phase 8): author its spec + register a `GameVariant`.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Variant/game-mode selection layer (`GameVariant`/`GameVariants`, `Ruleset.LoadEmbedded`), variant-aware saves (v15), transposability proof test (ADR-018) | Phase 5 (variant layer) |
