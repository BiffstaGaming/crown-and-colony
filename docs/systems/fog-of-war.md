# System: Fog of war (exploration)

| | |
|---|---|
| **Status** | Implemented (explored vs. currently-visible) |
| **Last verified** | 2026-06-13 @ Phase 5 (fog upgrade) |
| **Code** | `game/src/GameLogic/GameSession/Game.cs` (`Explored`/`Reveal`, `CurrentlyVisible`/`IsVisible`) · rendering: `game/presentation/MapView.cs` |
| **Tests** | `GameTests.FogOfWar_*`, `VisibilityTests`, `SaveGameTests.RoundTrip_PreservesExploredTilesExactly` |
| **FreeCol reference** | `Tile.exploredBy` / player `canSee` — cross-check when multiple players exist |
| **Related systems** | [units-movement](units-movement.md), [save-load](save-load.md), [randomness](randomness.md), [natives](natives.md) |

## 1. How it works (plain English)

The world starts hidden. Your units and colonies light up the map — each reveals the tiles around it (its "line of sight", 1 tile for a colonist or a colony). There are now **two** states beyond darkness: tiles you can **see right now** (in sight of one of your units or colonies) render fully, and tiles you've **seen before but no longer can** render **dimmed** — you remember the land, but anything moving there (a foreign unit) is hidden until you look again. Everywhere you've never been stays black. You start knowing only the 3×3 patch around your colonist.

## 2. Detailed rules

| Event | Effect |
|---|---|
| Unit spawned / moved | All tiles within its line-of-sight radius (square, per type) become explored |
| Colony founded | Its surroundings (radius `ColonySightRadius` = 1) become explored |
| Once explored | Stays explored forever (no re-hiding) |
| Currently visible | Tiles within sight of an on-map unit **or** a colony, right now — recomputed from positions each query (a subset of explored) |
| Explored but not visible | Rendered **dimmed**; foreign units there are hidden (until visible again) |
| Save/load | Explored set is preserved exactly; visible is derived (not saved) |

**Deviations from original / FreeCol:** none in model — we now distinguish *explored* (seen once) from *currently visible* (in someone's sight right now), matching the original. Colony sight is radius 1 (FreeCol settlements carry a configurable line of sight; 1 is a reasonable default, tunable later). Sight radius is a square (Chebyshev), matching FreeCol's `getSurroundingTiles` semantics for radius 1.

## 3. Technical design

- `Game._explored` (`HashSet<Position>`), exposed read-only (`Explored`, `IsExplored`); `Reveal(unit)` / `RevealAround(centre, radius)` called on spawn, every move, and colony founding — exploration is game state (persists, drives rules).
- `Game.CurrentlyVisible` (computed `IReadOnlySet<Position>`) and `IsVisible(p)` derive the in-sight set from current on-map units (their `LineOfSight`) and colonies (`ColonySightRadius`) on each query — never stored, so never stale; always ⊆ explored.
- Save format v2 stores explored tiles as row-major indexes (`y*W+x`); v1 saves (no list) reveal around units on load. Visible is **not** saved (derived).
- Rendering: `MapView.ShowState(map, explored, visible)` draws unexplored tiles as near-black, explored-but-not-visible tiles dimmed (a grey modulate on the sprites), and visible tiles at full brightness. Native settlements are the first hidden entities to consume this: `GameController.SyncNativeMarkers` draws a settlement only if `IsExplored(its tile)` (see [natives](natives.md)). With exploration-only fog, a settlement once seen stays drawn — the explored-vs-visible upgrade (P5.2) will dim it when out of current sight.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | new game reveals ≤3×3; moving grows explored; `VisibilityTests` (visible = sight set, ⊆ explored, shrinks when you move on, colony keeps its surroundings visible) | ✅ |
| L2 Scenario | Always | wander scenario asserts unit always on explored ground | ✅ |
| L3 Interaction | Rendering only | covered by scene load test | ✅ |
| L4 Visual | Yes | `remembered-fog-seed424242` golden (dimmed explored-but-unseen tiles) | ✅ |

## 5. Open issues / TODO

- [x] Explored-but-not-visible dimming (Phase 5 fog upgrade). Hidden foreign units follow naturally — renderers already skip non-visible entities (only explored native settlements draw).
- [ ] Larger sight radii (scout 2) once roles/types with higher line-of-sight are in play; per-type colony line of sight.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Exploration fog: reveal on spawn/move, persisted in saves (v2) | Phase 2a |
| 2026-06-13 | Explored vs. currently-visible: `CurrentlyVisible`/`IsVisible` (units + colonies), dimmed remembered tiles in `MapView`; `VisibilityTests` + `remembered-fog` golden | Phase 5 (fog upgrade) |
