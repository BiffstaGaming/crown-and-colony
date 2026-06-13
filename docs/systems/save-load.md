# System: Save & load

| | |
|---|---|
| **Status** | Implemented (skeleton scope) |
| **Last verified** | 2026-06-13 @ Phase 1 walking skeleton |
| **Code** | `game/src/GameLogic/Persistence/SaveGame.cs` · UI: `GameController` F5/F9 |
| **Tests** | `game/tests/GameLogic.Tests/Persistence/SaveGameTests.cs`, `Scenarios/` |
| **FreeCol reference** | n/a — our own format (FreeCol's .fsg is not a compatibility goal) |
| **Related systems** | [randomness](randomness.md) (RNG state is part of the save) |

## 1. How it works (plain English)

Press **F5** to save, **F9** to load. A save captures everything — the map, the units, whose turn it is, and even the game's hidden "dice position" — so a loaded game continues *exactly* as if you'd never stopped: the same future battles, the same future maps. Saves are readable JSON files (handy for debugging and bug reports).

## 2. Detailed rules

- A save restores: turn, map (terrain per tile), every unit (id, **type**, position, movement left), **explored tiles (fog of war)**, RNG state.
- Loading an interrupted game then continuing produces **identical outcomes** to never having saved (tested).
- Saves carry a format `Version` (currently **13**); older saves still load with sensible defaults for fields that didn't exist yet (see the changelog), and v1 saves default units to free colonists and reveal fog around units.
- Saves reference terrain by ruleset id — loading needs the matching ruleset; unknown ids fail loudly.

**Deviations:** our own JSON format by design; no FreeCol save compatibility planned.

## 3. Technical design

- `SaveGame` (record): pure DTO snapshot; `From(game)` / `Restore(ruleset)` / `ToJson()` / `FromJson()`. System.Text.Json, indented output.
- RNG round-trip via `RandomState` (ADR-009) — the linchpin of resume-identical behaviour.
- Quicksave path: `user://quicksave.json` (Godot user dir); file I/O lives in presentation (`GameController`), serialization in GameLogic — keeps GameLogic free of file-system concerns.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | JSON round-trip preserves all fields; RNG state preserved; unknown terrain id throws | ✅ |
| L2 Scenario | Always | save-mid-game acid test: interrupted vs uninterrupted runs end byte-identical | ✅ |
| L3 Interaction | Yes (F5/F9) | `InputTests.QuickSaveF5_ThenF9_RestoresTheTurn` (simulated keys) | ✅ |
| L4 Visual | No screen | — | — |

## 5. Open issues / TODO

- [ ] Save slots / save dialog UI (later phase); L3 hotkey test.
- [ ] Versioned migration once format changes post-1.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | JSON save format v1, F5/F9 quicksave, resume-identical guarantee | Phase 1 skeleton |
| 2026-06-13 | Format v2: unit type ids + explored tiles; v1 loads with defaults (tested) | Phase 2a |
| 2026-06-13 | Format v3: colonies; pre-v3 loads with none (tested) | Phase 2b |
| 2026-06-13 | Format v4: colony goods stores; pre-v4 colonies load with empty stores (tested) | Phase 3 |
| 2026-06-13 | Format v5: colony tile workers; pre-v5 loads with none (tested) | Phase 3 |
| 2026-06-13 | Format v6: colony buildings + staffing; pre-v6 re-derives free base buildings (tested). Legacy raw-grain stores normalize to food on load | Phase 3 |
| 2026-06-13 | Format v7: colony construction target (CurrentBuild) | Phase 3 |
| 2026-06-13 | Format v8: map bonus resources (sparse index list) | Phase 2/3 |
| 2026-06-13 | Format v9: treasury (gold, tax) + moved-market inventories (sparse); pre-v9 loads 0 gold/tax, market reseeded (tested) | Phase 4 |
| 2026-06-13 | Format v10: liberty, Congress, current/offered fathers; pre-v10 loads empty (tested) | Phase 4 |
| 2026-06-13 | Format v11: unit location + sail turns + cargo hold; pre-v11 units load on-map empty (tested) | Phase 4 |
| 2026-06-13 | Format v12: immigration (pool, target) + Europe recruitment dock (slots, escalating base price/floor); pre-v12 loads classic defaults + a fresh dock (tested) | Phase 4 slice 4 |
| 2026-06-13 | Format v13: unit carrier ids (colonists aboard ships); pre-v13 units load not-aboard (tested) | Phase 4 slice 5 |
