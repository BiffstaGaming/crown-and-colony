# Visual tests: main map goldens

| | |
|---|---|
| **Golden files** | `game/tests/visual/goldens/map-seed424242.png`, `colony-seed424242.png` |
| **Test** | `game/presentation/tests/VisualGoldenTests.cs` (GdUnit4, runs with the L3 suite) |
| **Scene** | `res://scenes/main.tscn`, UI layer hidden (map-only capture for cross-platform stability) |
| **Resolution** | 1024×600 window capture |
| **Seed / fixture** | `StartNewGame(424242)`; colony golden additionally founds a colony at the start tile + one End Turn |
| **Tolerance** | per-channel Δ ≤ 8; ≤ 0.5% of pixels may exceed it |
| **Last regenerated** | 2026-06-13 — initial flat-colour baseline, regenerated same day for ADR-014 isometric art |

## What these goldens verify

The seeded world renders identically: terrain layout/colours, fog-of-war coverage, unit marker placement — and for the colony golden, the colony marker + consumed unit.

## When they fail, a human should check

- [ ] Map layout identical to golden (terrain generation or palette change?)
- [ ] Fog boundary identical (line-of-sight change?)
- [ ] Markers present at the same tiles
- [ ] If the change is intentional: regenerate (`GOLDEN_UPDATE=1`), eyeball the new PNGs, commit them with the change

## Known acceptable variation

GPU rasterization differences (local NVIDIA vs CI llvmpipe) absorbed by the tolerance; UI text excluded from capture by design.
