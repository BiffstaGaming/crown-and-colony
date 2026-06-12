# Module: CrownAndColony.GameLogic

| | |
|---|---|
| **Last verified** | 2026-06-13 @ Phase 0 scaffold commit |
| **Location** | `game/src/GameLogic/` |
| **Layer** | GameLogic (engine-free) |
| **Depends on** | nothing (BCL only) |
| **Used by** | `CrownAndColony` (Godot presentation project), `GameLogic.Tests` |

## Purpose

The entire rules engine of Crown & Colony: every game rule, calculation, and state transition. It is explicitly **not** responsible for anything visual, audible, or input-related — that's the Godot presentation project.

**The defining constraint (ADR-006):** this project must never reference Godot (no `Godot.NET.Sdk`, no `GodotSharp`). That keeps the whole rules engine testable with plain xUnit, headless, in milliseconds. The csproj carries a comment to that effect; treat adding a Godot dependency here as a build-breaking offence.

## Public API

| Type / member | What it does |
|---|---|
| `Randomness.IGameRandom` | The only permitted randomness source (ADR-009) |
| `Randomness.Pcg32Random` | Deterministic PCG32 implementation; `FromState()` resumes saves |
| `Randomness.RandomState` | Serializable generator snapshot |
| `Specification.Ruleset` | Parsed rule data; `LoadClassic()` reads the embedded classic spec |
| `Specification.TerrainType` / `ProductionEntry` / `GoodsOutput` | Immutable terrain rule data |
| `World.Position` | Grid coordinate; 8-way adjacency |
| `World.GameMap` | Immutable terrain grid |
| `World.MapGenerator` | Seeded placeholder map generation (Phase 2 replaces algorithm) |
| `Units.Unit` | Unit state; mutated only via `Game` |
| `GameSession.Game` | The running game: `New`, `CheckMove`, `MoveUnit`, `EndTurn`, `SpawnUnit` |
| `GameSession.MoveCheck` / `InvalidMoveException` | Move legality result / violation |
| `Persistence.SaveGame` / `SavedUnit` | Complete JSON-serializable game snapshot |

(Grows as systems land; keep this table current.)

## Key design notes

- `TreatWarningsAsErrors` and nullable reference types are on; keep them on.
- `GenerateDocumentationFile` is on — public members without XML doc comments fail the build, which mechanically enforces the documentation rule for APIs.
- Target: `net8.0` (matches Godot 4.6's .NET target). Tests roll forward to the installed runtime.

## Tests

`game/tests/GameLogic.Tests/` — xUnit, mirrors this project's folder structure. 13 tests, all passing as of 2026-06-13.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Project created with Randomness namespace | Phase 0 scaffold |
