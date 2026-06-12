# Module: CrownAndColony (Presentation)

| | |
|---|---|
| **Last verified** | 2026-06-13 @ Phase 1 walking skeleton |
| **Location** | `game/presentation/`, `game/scenes/` (project: `game/CrownAndColony.csproj`) |
| **Layer** | Presentation (Godot) |
| **Depends on** | `GameLogic`, Godot 4.6 |
| **Used by** | — (top of the stack) |

## Purpose

Everything the player sees and touches: scene tree, drawing, camera, input, UI. It forwards commands to `GameLogic` and reflects its state. It must contain **no game rules** (ADR-006) — if a conditional encodes "what's allowed in the game", it belongs in `GameLogic` and this layer calls `CheckMove`-style oracles instead.

## Key parts

| Part | What it does |
|---|---|
| `GameController` (root of `scenes/main.tscn`) | Owns the `Game`; input → commands; quicksave F5/F9; exported `Seed` for deterministic test runs |
| `MapView` | Flat-colour tile drawing; tile↔pixel conversions (`TileSize` = 32) |
| `UnitMarker` | Placeholder unit disc + selection ring |
| `CameraController` | Drag pan + wheel zoom |
| `presentation/tests/` | **L3 GdUnit4 tests — live inside this project** because the GdUnit4 adapter requires the test assembly's project to BE the Godot project (official gdUnit4Net layout; see ADR notes). Run: `dotnet test game/CrownAndColony.csproj` with `gdunit.runsettings` + `GODOT_BIN` set |

## Key design notes

- The csproj **excludes `src/**` and `tests/**`** from its compile glob — Godot's SDK otherwise globs every `.cs` under the project root (build errors guaranteed). New presentation code goes under `presentation/`.
- `RollForward=LatestMajor` so the VSTest host can run on a newer installed runtime.
- Test-framework packages (GdUnit4, Test SDK) are referenced by the game project per the official pattern; revisit before shipping builds (strip via condition if it bloats exports).
- Renderer is `gl_compatibility` (works headless/software in CI).

## Tests

L3: `presentation/tests/MainSceneTests.cs` — scene loads at turn 1, End Turn click advances the label, unit marker sits on a tile centre. 3/3 passing locally (real Godot runtime).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Walking-skeleton scene: map view, unit, camera, turn UI, quicksave; GdUnit4 L3 wiring | Phase 1 skeleton |
