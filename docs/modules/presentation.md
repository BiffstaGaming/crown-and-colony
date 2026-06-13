# Module: CrownAndColony (Presentation)

| | |
|---|---|
| **Last verified** | 2026-06-13 @ Phase 5 slice 1 |
| **Location** | `game/presentation/`, `game/scenes/` (project: `game/CrownAndColony.csproj`) |
| **Layer** | Presentation (Godot) |
| **Depends on** | `GameLogic`, Godot 4.6 |
| **Used by** | — (top of the stack) |

## Purpose

Everything the player sees and touches: scene tree, drawing, camera, input, UI. It forwards commands to `GameLogic` and reflects its state. It must contain **no game rules** (ADR-006) — if a conditional encodes "what's allowed in the game", it belongs in `GameLogic` and this layer calls `CheckMove`-style oracles instead.

## Key parts

| Part | What it does |
|---|---|
| `GameController` (root of `scenes/main.tscn`) | Owns the `Game` + the selected `GameVariant` (new game loads its ruleset; saves record it; loads restore under the save's variant — ADR-018); input → commands (click select/move, B found, E Europe, F5/F9 save/load); opens the colony + Europe panels; exported `Seed` for deterministic test runs |
| `MapView` | Isometric tile drawing with FreeCol terrain art (ADR-014, 128×64 diamonds); tile↔pixel conversions |
| `UnitMarker` | FreeCol unit sprite + selection ring (on-map units only) |
| `ColonyMarker` | FreeCol settlement sprite + name plate, one per colony |
| `NativeSettlementMarker` | FreeCol indian-settlement art (camp/village/Inca/Aztec) + nation plate (capitals starred); one per discovered settlement (`GameController.SyncNativeMarkers`, fog-gated) |
| `CameraController` | Drag pan + wheel zoom |
| `ColonyPanel` | Interactive colony screen: staffing, field release, auto-assign, construction choice — built programmatically per open/refresh, all actions via Game oracles |
| `EuropePanel` | The Europe screen: recruitment dock (recruit), ships in port (sail home, sell cargo, buy goods), colonists on the dock (board), buy/train units, immigration clock — all via Game oracles |
| `presentation/tests/` | **L3 GdUnit4 + L4 visual tests — live inside this project** because the GdUnit4 adapter requires the test assembly's project to BE the Godot project (official gdUnit4Net layout; ADR-011/015). Run: `dotnet test game/CrownAndColony.csproj` with `gdunit.runsettings` + `GODOT_BIN` set, after a clean `godot --build-solutions` |

## Key design notes

- The csproj **excludes `src/**` and `tests/**`** from its compile glob — Godot's SDK otherwise globs every `.cs` under the project root (build errors guaranteed). New presentation code goes under `presentation/`.
- `RollForward=LatestMajor` so the VSTest host can run on a newer installed runtime.
- Test-framework packages (GdUnit4, Test SDK) are referenced by the game project per the official pattern; revisit before shipping builds (strip via condition if it bloats exports).
- Renderer is `gl_compatibility` (works headless/software in CI).

## Tests

**19 scene tests** (16 L3 interaction + 3 L4 visual goldens), green on the real Godot runtime: `MainSceneTests`, `InputTests` (click/move, hotkeys, F5/F9), `ColonyPanelTests`, `EuropePanelTests` (recruit/board/sail + sell/buy goods + buy units), `JourneyE2ETests` (the one scene-level E2E), `VisualGoldenTests` (golden-screenshot diff: map, colony, native settlement). Driven in CI by ADR-015 (CI owns the Godot install; 3-attempt retry under xvfb).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Walking-skeleton scene: map view, unit, camera, turn UI, quicksave; GdUnit4 L3 wiring | Phase 1 skeleton |
| 2026-06-13 | Isometric rendering with FreeCol terrain/unit/settlement art (ADR-014); L4 visual-golden harness | Phase 2 |
| 2026-06-13 | Interactive colony screen (`ColonyPanel`); L3 input + panel tests | Phase 3 |
| 2026-06-13 | Europe screen (`EuropePanel`): dock/recruit, ships, board/sail; map renders only on-map units; L3-tested | Phase 4 slice 6 |
| 2026-06-13 | Europe screen: goods Sell/Buy (slice 10) and Buy/train units (slice 11); L3-tested | Phase 4 slices 10–11 |
| 2026-06-13 | Native settlements on the map (`NativeSettlementMarker`, fog-gated; FreeCol indian art, ADR-014); `native-settlement` L4 golden | Phase 5 slice 1 |
| 2026-06-13 | `GameController` selects a game variant — new game loads its ruleset, saves record/restore it (ADR-018) | Phase 5 (variant layer) |
