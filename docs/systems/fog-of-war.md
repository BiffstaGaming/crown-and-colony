# System: Fog of war (exploration)

| | |
|---|---|
| **Status** | Implemented (explored vs. currently-visible) |
| **Last verified** | 2026-06-22 @ Coronado `exposedTilesRadius` +3 → full 11×11 see-all-colonies reveal (Wave 12 follow-up) |
| **Code** | `game/src/GameLogic/GameSession/Game.cs` (`Explored`/`Reveal`, `CurrentlyVisible`/`IsVisible`) · rendering: `game/presentation/MapView.cs` |
| **Tests** | `GameTests.FogOfWar_*`, `VisibilityTests`, `SaveGameTests.RoundTrip_PreservesExploredTilesExactly` |
| **FreeCol reference** | `Tile.exploredBy` / player `canSee` — cross-check when multiple players exist |
| **Related systems** | [units-movement](units-movement.md), [save-load](save-load.md), [randomness](randomness.md), [natives](natives.md) |

## 1. How it works (plain English)

The world starts hidden. Your units and colonies light up the map — each reveals the tiles around it (its "line of sight"). A lone colonist sees **1** tile in every direction (a 3×3 patch); a **colony** sees further — **2** tiles in every direction (a 5×5 patch), because a settled town watches a wider stretch of country. A **scout** sees further than a colonist too: mounting a colonist as a scout adds **+1** to its sight, so it lights up two tiles in every direction — that's what makes scouts the explorers. The founding father **Hernando de Soto** widens *every* one of your **land** units' sight by **+1** the same way (your ships don't benefit), so a de Soto empire scouts the continent faster. There are now **two** states beyond darkness: tiles you can **see right now** (in sight of one of your units or colonies) render fully, and tiles you've **seen before but no longer can** render **dimmed** — you remember the land, but anything moving there (a foreign unit) is hidden until you look again. Everywhere you've never been stays black. You start knowing only the 3×3 patch around your colonist.

## 2. Detailed rules

| Event | Effect |
|---|---|
| Unit spawned / moved | All tiles within its line-of-sight radius (square; its type's sight **plus its role's `lineOfSightBonus`** — scout +1 — **plus the owner's founding-father `lineOfSightBonus`** — Hernando de Soto +1, **land units only**) become explored |
| Colony founded | Its surroundings (radius `ColonySightRadius` = **2** — a 5×5 ring; classic colony `visible-radius`) become explored |
| Once explored | Stays explored forever (no re-hiding) |
| Currently visible | Tiles within sight of an on-map unit **or** a colony, right now — recomputed from positions each query (a subset of explored) |
| Explored but not visible | Rendered **dimmed**; foreign units there are hidden (until visible again) |
| Save/load | Explored set is preserved exactly; visible is derived (not saved) |

**Deviations from original / FreeCol:** none in model — we now distinguish *explored* (seen once) from *currently visible* (in someone's sight right now), matching the original. Colony sight is radius **2**, read data-driven from the classic colony settlement's `visible-radius="2"` (FreeCol `Settlement.getLineOfSight` = the settlement type's `visibleRadius`; `SettlementType.visibleRadius` itself defaults to 2). Sight radius is a square (Chebyshev), matching FreeCol's `getSurroundingTiles(0, lineOfSight)` semantics. Francisco de Coronado's `model.event.seeAllColonies` now reveals each colony at its line-of-sight **plus** the father's `exposedTilesRadius` modifier — classic additive **+3**, so `2 + 3 = 5`, a full **11×11** block around every colony on the map (faithful to Col1 and FreeCol's `father.apply(colony.getLineOfSight(), …, EXPOSED_TILES_RADIUS)`).

## 3. Technical design

- `Game._explored` (`HashSet<Position>`), exposed read-only (`Explored`, `IsExplored`); `Reveal(unit)` / `RevealAround(centre, radius)` called on spawn, every move, and colony founding — exploration is game state (persists, drives rules).
- `Game.CurrentlyVisible` (computed `IReadOnlySet<Position>`) and `IsVisible(p)` derive the in-sight set from current on-map units (their **effective** sight, `Game.LineOfSightOf` = `UnitType.LineOfSight` + the role's `RoleType.LineOfSightBonus` + the owner's founding-father `model.modifier.lineOfSightBonus`) and colonies (`ColonySightRadius`) on each query — never stored, so never stale; always ⊆ explored. `Reveal` uses the same effective radius, so a scout explores +1 tile too.
- **`Game.ColonySightRadius`** is no longer a hardcoded constant: it is the instance property `=> Ruleset.ColonyConstants.ColonySightRadius`, parsed from the classic colony settlement's `visible-radius="2"` (the `model.settlement.colony` `<settlement>` under `european-nation-types`) in `Ruleset.ParseColonyConstants` — FreeCol `Settlement.getLineOfSight` returns the settlement type's `visibleRadius` (default 2). A spec omitting the attribute falls back to `ColonyConstants.ClassicDefaults.ColonySightRadius` = 2. This radius drives every colony fog site: founding reveal (`RevealAround` in `FoundColony`/save-load), the live `CurrentlyVisible`/`IsVisible` computation, the scout's "learn the colony's surroundings" reveal, and the **base** of Coronado's see-all-colonies reveal. Deterministic (a data constant), so saves round-trip byte-identically — no save-format change (the explored set is derived from it, not separately versioned).
- **Coronado's see-all-colonies reveal radius** (`Game.CoronadoRevealRadius`, used in the founding-father election handler when `father.RevealsAllColonies`) folds Coronado's own `model.modifier.exposedTilesRadius` modifier onto `ColonySightRadius`, mirroring FreeCol `ServerPlayer` `model.event.seeAllColonies` → `father.apply(colony.getLineOfSight(), turn, Modifier.EXPOSED_TILES_RADIUS)`. Classic Coronado declares `<modifier id="model.modifier.exposedTilesRadius" type="additive" value="3"/>`, so the reveal is `ColonySightRadius (2) + 3 = 5` — an **11×11** block around every colony. The generic `ParseModifier` already captures the modifier into `FoundingFather.Modifiers` (no new parsing needed); `CoronadoRevealRadius` reads it via `Modifiers.Where(TargetId == "model.modifier.exposedTilesRadius").OrderBy(Index)` and applies each (additive). A father/ruleset without the modifier reveals at the bare `ColonySightRadius`. **Only a Coronado holder is affected** — the default game (no Coronado) reveals nothing here, so it is byte-identical; RNG-free (ADR-009), no save bump (fog is derived).
- **`LineOfSightOf(unit)`** folds, for a **colonial** owner's **non-naval** unit, the owner's founding-father `lineOfSightBonus` via `ApplyGoodsModifiers(owner, LineOfSightBonusId, sight)` — **Hernando de Soto** grants additive +1 (FreeCol scope `navalUnit=false`, honoured by the `UnitType.IsNaval` gate — we parse the value, not the method-scope, as with the Spanish `offenceAgainst`). A native unit or an owner without de Soto folds nothing → the sight (and therefore the fog reveal / visibility) is byte-identical, so a default game is unchanged. RNG-free (ADR-009).
- Save format v2 stores explored tiles as row-major indexes (`y*W+x`); v1 saves (no list) reveal around units on load. Visible is **not** saved (derived).
- Rendering: `MapView.ShowState(map, explored, visible)` draws unexplored tiles as near-black, explored-but-not-visible tiles dimmed (a grey modulate on the sprites), and visible tiles at full brightness. Native settlements are the first hidden entities to consume this: `GameController.SyncNativeMarkers` draws a settlement only if `IsExplored(its tile)` (see [natives](natives.md)). With exploration-only fog, a settlement once seen stays drawn — the explored-vs-visible upgrade (P5.2) will dim it when out of current sight.
- Minimap (`MiniMap`, `86d3c9x64`): the corner overview consumes the same fog seams — unexplored tiles stay dark, remembered (explored-but-not-visible) cells render dimmed, and a colony/native dot shows once its tile is `IsExplored` while a unit dot shows only while `IsVisible`. Pure presentation (ADR-006); see [presentation](../modules/presentation.md).

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | new game reveals ≤3×3; moving grows explored; `VisibilityTests` (visible = sight set, ⊆ explored, shrinks when you move on, colony keeps its surroundings visible); **`ScoutSightTests`** (the scout role parses `lineOfSightBonus` +1, the default role 0; a scout sees a tile two away that a plain colonist can't; **de Soto** parses an additive +1 `lineOfSightBonus`, gives a plain land colonist that same extra tile, and does **not** extend a caravel's sight — naval excluded) | ✅ |
| L2 Scenario | Always | wander scenario asserts unit always on explored ground | ✅ |
| L3 Interaction | Rendering only | covered by scene load test | ✅ |
| L4 Visual | Yes | `remembered-fog-seed424242` golden (dimmed explored-but-unseen tiles) | ✅ |

## 5. Open issues / TODO

- [x] Explored-but-not-visible dimming (Phase 5 fog upgrade). Hidden foreign units follow naturally — renderers already skip non-visible entities (only explored native settlements draw).
- [x] **Larger sight radii** (`86d3c9upk`): the scout role's `lineOfSightBonus` (+1) is folded into the fog reveal (`Game.LineOfSightOf`), so a scout sees two tiles out. *(Follow-up: the seasoned-scout's extra **exploration traits** — better Lost-City-Rumour outcomes — wait on the Lost City Rumours system.)*
- [x] **Colony sight radius 1→2, data-driven** (Wave 10): the colony's line of sight is read from the classic colony settlement's `visible-radius="2"` (`Ruleset.ColonyConstants.ColonySightRadius`), not hardcoded — matching FreeCol `Settlement.getLineOfSight`. A colony now reveals/keeps a 5×5 ring.
- [x] **Coronado `exposedTilesRadius`** (Wave 12 follow-up): `model.event.seeAllColonies` now reveals each colony at its line-of-sight **+** the father's `model.modifier.exposedTilesRadius` (classic additive +3 → a full 11×11 block), via `Game.CoronadoRevealRadius`. Only a Coronado holder is affected; default game unchanged.
- [x] **Hernando de Soto's `lineOfSightBonus`** (`86d3dj7nv`): +1 sight for all the player's land units, folded into `LineOfSightOf` (naval excluded). *(de Soto's rumour weighting / `rumoursAlwaysPositive` shipped separately under `86d3c9uhj`.)*

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Exploration fog: reveal on spawn/move, persisted in saves (v2) | Phase 2a |
| 2026-06-13 | Explored vs. currently-visible: `CurrentlyVisible`/`IsVisible` (units + colonies), dimmed remembered tiles in `MapView`; `VisibilityTests` + `remembered-fog` golden | Phase 5 (fog upgrade) |
| 2026-06-14 | Native units (braves) are excluded from `CurrentlyVisible`/`IsVisible` and don't reveal/explore — only the player's own units and colonies lift fog | Phase 5 slice 5b |
| 2026-06-17 | **Scout line-of-sight bonus** (`86d3c9upk`): a unit's sight is now its type `LineOfSight` + its role's `lineOfSightBonus` (`Game.LineOfSightOf`); the scout role grants +1 (parsed into `RoleType.LineOfSightBonus`), so a scout sees/reveals two tiles out. Deterministic, no save change. +2 L1 (`ScoutSightTests`) | Phase 5 (`86d3c9upk`) |
| 2026-06-20 | **Hernando de Soto — +1 land-unit sight** (`86d3dj7nv`, FreeCol `model.modifier.lineOfSightBonus` scope `navalUnit=false`): `LineOfSightOf` now folds the owner's founding-father `lineOfSightBonus` (via `ApplyGoodsModifiers`) for a colonial owner's non-naval unit, so a de Soto player's land units each see +1 tile further; ships are excluded (the `IsNaval` gate). No father → byte-identical fog reveal; RNG-free; no save change. +3 L1 (`ScoutSightTests`: spec-parse, land +1, naval exclusion); 1154 + soak green | Phase 5 (`86d3dj7nv`) |
| 2026-06-20 | **Minimap consumes the fog** (`86d3c9x64`): the new corner `MiniMap` (presentation) reads the same fog seams — unexplored dark, remembered dimmed, colony/native dots gated on `IsExplored`, unit dots on `IsVisible`. Pure presentation (ADR-006); no logic/save change. See [presentation](../modules/presentation.md) | P5/P7 (`86d3c9x64`) |
| 2026-06-22 | **Colony sight radius 1→2, data-driven** (Wave 10): `Game.ColonySightRadius` is now the instance property `=> Ruleset.ColonyConstants.ColonySightRadius`, parsed from the classic colony settlement's `visible-radius="2"` in `ParseColonyConstants` (FreeCol `Settlement.getLineOfSight`), replacing the hardcoded `const = 1` — a fidelity fix; a colony now reveals/keeps a 5×5 ring. Coronado's see-all-colonies reveal now uses `ColonySightRadius` too (was hardcoded 1; full LoS+exposedTilesRadius left as a follow-up). Deterministic, so saves round-trip byte-identically — no save bump. `VisibilityTests` updated to assert radius 2; targeted (1960) + soak (4) green | Wave 10 |
| 2026-06-22 | **Coronado `exposedTilesRadius` +3 — full 11×11 reveal** (Wave 12 follow-up): `model.event.seeAllColonies` now reveals each colony at `ColonySightRadius` **+** Coronado's `model.modifier.exposedTilesRadius` (classic additive +3) via the new `Game.CoronadoRevealRadius` — `2 + 3 = 5`, an 11×11 block (faithful to FreeCol `father.apply(colony.getLineOfSight(), …, EXPOSED_TILES_RADIUS)` / Col1). The modifier is read off the elected father's already-parsed `Modifiers`; **only a Coronado holder is affected**, so the default (no-Coronado) game is byte-identical. Deterministic, no save bump (fog is derived). +2 L1 (`FoundingFatherTests`: parses the +3 additive modifier; reveal fills the full 11×11 and stops at radius 5); targeted (58) + soak (4) green | Wave 12 |
