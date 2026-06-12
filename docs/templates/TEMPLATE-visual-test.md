# Visual test: <screen>-<state>

| | |
|---|---|
| **Golden file** | `game/tests/visual/goldens/<screen>-<state>.png` |
| **Scene** | `game/scenes/<path>.tscn` |
| **Resolution** | 1920×1080 (project standard — do not vary per test) |
| **Seed / fixture** | `<fixture builder + seed>` (must be fully deterministic) |
| **Tolerance** | per-pixel Δ ≤ <n>, max differing pixels ≤ <n> |
| **Last regenerated** | <YYYY-MM-DD> @ <commit> — reason: <why> |

## What this golden verifies

Plain English: what should be on screen and why it matters (e.g. "Colony screen with a fully-staffed lumber mill: all 3 worker portraits visible, production arrows showing 6 lumber → 3 hammers").

## When it fails, a human should check

- [ ] <specific thing 1 — e.g. worker portraits present and in correct slots>
- [ ] <specific thing 2 — e.g. production numbers match the fixture math>
- [ ] Layout intact at standard resolution (no clipped/overlapping controls)

## Known acceptable variation

Anything expected to differ slightly without being a regression (e.g. animated water tiles are frozen at frame 0 by the fixture — if not, fix the fixture, not the tolerance).
