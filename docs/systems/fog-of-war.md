# System: Fog of war (exploration)

| | |
|---|---|
| **Status** | Implemented (exploration only; per-turn visibility is later work) |
| **Last verified** | 2026-06-13 @ Phase 2a |
| **Code** | `game/src/GameLogic/GameSession/Game.cs` (`Explored`, `Reveal`) · rendering: `game/presentation/MapView.cs` |
| **Tests** | `GameTests.FogOfWar_*`, `SaveGameTests.RoundTrip_PreservesExploredTilesExactly` |
| **FreeCol reference** | `Tile.exploredBy` / player `canSee` — cross-check when multiple players exist |
| **Related systems** | [units-movement](units-movement.md), [save-load](save-load.md), [randomness](randomness.md) |

## 1. How it works (plain English)

The world starts hidden. Your units light up the map as they travel — each unit reveals the tiles around it (its "line of sight", 1 tile for a colonist). Anywhere you've seen stays visible on the map; everywhere else is dark. Exploring is now the early game: you start knowing only the 3×3 patch around your colonist.

## 2. Detailed rules

| Event | Effect |
|---|---|
| Unit spawned / moved | All tiles within its line-of-sight radius (square, per type) become explored |
| Once explored | Stays explored forever (no re-hiding) |
| Save/load | Explored set is preserved exactly |

**Deviations from original / FreeCol:** the original distinguishes *explored* (seen once) from *currently visible* (inside someone's sight right now — matters for hidden enemy units). We implement exploration only until foreign units exist. Sight radius is a square (Chebyshev), matching FreeCol's `getSurroundingTiles` semantics for radius 1.

## 3. Technical design

- `Game._explored` (`HashSet<Position>`), exposed read-only (`Explored`, `IsExplored`); `Reveal(unit)` called on spawn and every move — exploration is game state, not presentation state, because it must persist and later drive rules (e.g. can't found colonies on unseen land).
- Save format v2 stores explored tiles as row-major indexes (`y*W+x`); v1 saves (no list) reveal around units on load.
- Rendering: `MapView` draws unexplored tiles as near-black; explored tiles render normally.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | new game reveals ≤3×3; moving grows the set | ✅ |
| L2 Scenario | Always | wander scenario asserts unit always on explored ground | ✅ |
| L3 Interaction | Rendering only | covered by scene load test | ✅ |
| L4 Visual | Yes | fog golden TODO with visual harness | ⬜ |

## 5. Open issues / TODO

- [ ] Explored-but-not-visible dimming + hidden units (when foreign units land, Phase 5)
- [ ] Larger sight radii (scout 2) once roles/types with higher line-of-sight are in play

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Exploration fog: reveal on spawn/move, persisted in saves (v2) | Phase 2a |
