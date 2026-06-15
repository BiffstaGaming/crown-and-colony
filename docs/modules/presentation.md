# Module: CrownAndColony (Presentation)

| | |
|---|---|
| **Last verified** | 2026-06-15 @ native interaction UI (scene suite green, 28 L3+L4; clicking a discovered native settlement opens an interaction panel — speak / learn / attack) |
| **Location** | `game/presentation/`, `game/scenes/` (project: `game/CrownAndColony.csproj`) |
| **Layer** | Presentation (Godot) |
| **Depends on** | `GameLogic`, Godot 4.6 |
| **Used by** | — (top of the stack) |

## Purpose

Everything the player sees and touches: scene tree, drawing, camera, input, UI. It forwards commands to `GameLogic` and reflects its state. It must contain **no game rules** (ADR-006) — if a conditional encodes "what's allowed in the game", it belongs in `GameLogic` and this layer calls `CheckMove`-style oracles instead.

## Key parts

| Part | What it does |
|---|---|
| `GameController` (root of `scenes/main.tscn`) | Owns the `Game` + the selected `GameVariant` (new game loads its ruleset; saves record it; loads restore under the save's variant — ADR-018); input → commands (click select/move, **click an adjacent enemy/settlement to attack** via `AttackUnitAt`/`AttackSettlementAt`, B found, E Europe, F5/F9 save/load); opens the colony + Europe panels; after End Turn, surfaces what the human suffered during the AI phase by formatting `Game.CombatNotices` (raids, via `FormatCombatNotice`), **`Game.ColonyRaidNotices`** (colonies pillaged by a brave, via `FormatColonyRaidNotice` → "⚔ The X raided Y and carried off N goods!") **and `Game.ColonyLossNotices`** (colonies seized by a power at war, via `FormatColonyLossNotice` → "⚑ The X captured your colony Y!") into the status bar — presentation-only string-building from raw ids (`NationLabel` shared; slices 1b / native pillage / 1c-3f); exported `Seed` for deterministic test runs. **Owner-gated for multi-player (FP-4):** `SyncColonyMarkers` fog-gates colony markers (a foreign colony stays hidden until a human unit discovers its tile), and tile-click, the HUD subject line, and the camera focus resolve the human **by owner** — so the human can't manage or centre on a foreign power's colony |
| `MapView` | Isometric tile drawing with FreeCol terrain art (ADR-014, 128×64 diamonds); `ShowState(map, explored, visible)` — unexplored black, explored-but-unseen dimmed, visible full bright; tile↔pixel conversions. Child layers: `NativeLayer`, `ColonyLayer`, `UnitLayer` |
| `UnitMarker` | FreeCol unit sprite (red-disc fallback) + selection ring + an **owner ground-ring** (`OwnerColor`) marking a non-yours unit; one per drawn unit, reconciled into `UnitLayer` by `GameController.SyncUnitMarkers` — the human's own on-map units always, every non-human unit (a foreign power's or a native brave) only while its tile is in live sight (`Game.IsVisible`). The human's own units pass `default` (no ring); foreign units use their `EuropeanNation.Color`, natives a constant (1c-1) |
| `ColonyMarker` | FreeCol settlement sprite + name plate, one per colony |
| `NativeSettlementMarker` | FreeCol indian-settlement art (camp/village/Inca/Aztec) + nation plate (capitals starred); one per discovered settlement (`GameController.SyncNativeMarkers`, fog-gated) |
| `CameraController` | Drag pan + wheel zoom |
| `ColonyPanel` | Interactive colony screen: staffing, field release, auto-assign, construction choice — built programmatically per open/refresh, all actions via Game oracles |
| `EuropePanel` | The Europe screen: recruitment dock (recruit), ships in port (sail home, sell cargo, buy goods — a ship **under repair** instead shows its repair countdown and offers no sail/buy controls), colonists on the dock (board), buy/train units, immigration clock — all via Game oracles |
| `NativeSettlementPanel` | The on-map native-settlement interaction panel: opened by clicking a discovered settlement, offers **speak with chief / learn skill / attack**, each shown only when its `Check…` allows the acting unit; re-resolves the acting unit by id each rebuild (a learned colonist is swapped), hides itself if the settlement is sacked from it. Reads state + forwards to Game oracles only (ADR-006) |
| `presentation/tests/` | **L3 GdUnit4 + L4 visual tests — live inside this project** because the GdUnit4 adapter requires the test assembly's project to BE the Godot project (official gdUnit4Net layout; ADR-011/015). Run: `dotnet test game/CrownAndColony.csproj` with `gdunit.runsettings` + `GODOT_BIN` set, after a clean `godot --build-solutions` |

## Key design notes

- The csproj **excludes `src/**` and `tests/**`** from its compile glob — Godot's SDK otherwise globs every `.cs` under the project root (build errors guaranteed). New presentation code goes under `presentation/`.
- `RollForward=LatestMajor` so the VSTest host can run on a newer installed runtime.
- Test-framework packages (GdUnit4, Test SDK) are referenced by the game project per the official pattern; revisit before shipping builds (strip via condition if it bloats exports).
- Renderer is `gl_compatibility` (works headless/software in CI).

## Tests

**26 scene tests** (21 L3 interaction + 5 L4 visual goldens), green on the real Godot runtime: `MainSceneTests`, `InputTests` (click/move, click-to-attack, native-raid status-bar notice, **multiple own units render, a non-human unit renders only when in sight, a foreign unit renders in its nation colour**, hotkeys, F5/F9), `ColonyPanelTests`, `EuropePanelTests` (recruit/board/sail + sell/buy goods + buy units), `JourneyE2ETests` (the one scene-level E2E), `VisualGoldenTests` (golden-screenshot diff: map, colony, native settlement, remembered-fog, **rendered-units** — two own units + an in-sight brave with its owner ring). Driven in CI by ADR-015 (CI owns the Godot install; 3-attempt retry under xvfb). **Local note:** close any running Godot **editor** before running scene tests — a live editor on the project collides with the headless GdUnit runner (the `-1073741819` cold-start crash).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-14 | FP-4: `GameController` owner-gates the rivals — `SyncColonyMarkers` fog-gates foreign colonies (hidden under the human's fog), and tile-click / HUD subject / camera focus resolve the human by owner (can't manage or centre on a foreign colony). FP-5 (AI economy) added no presentation change — rival economies are off-screen | FP-4 |
| 2026-06-15 | On-map combat UI: `HandleTileClick` routes a click on an adjacent enemy unit / native settlement to `Attack`/`AttackSettlement` (else move), HUD outcome notice, selection cleared after; L3 `ClickingAnEnemy_WithSelectedUnit_Attacks` | combat UI |
| 2026-06-15 | Native-raid feedback (slice 1b): `OnEndTurnPressed` reads `Game.CombatNotices` and `FormatCombatNotice` renders them into the status bar (from the human defender's view); L3 `NativeRaid_DuringEndTurn_ShowsANoticeInTheStatusBar`. Presentation-only (ADR-006) | Phase 5 slice 1b |
| 2026-06-15 | Rival/own-unit rendering (slice 1c-1): replaced the single `UnitMarker` node with a reconciled `MapView/UnitLayer` (`SyncUnitMarkers`) drawing every on-map unit the human can see — own units always, foreign powers + braves when in live sight (`IsVisible`); non-human units get an owner ring (`OwnerColorOf`: foreign `EuropeanNation.Color`, native constant), the human's none. +2 L3 + 1 L4 golden (`rendered-units-seed424242`); the 4 `MapView/UnitMarker` L3 tests migrated to `UnitLayer`. Presentation-only | Phase 5 slice 1c-1 |
| 2026-06-15 | Ship repair UI (slice 1c-3b): `EuropePanel` shows a damaged ship as "under repair (N turns)" and omits its Sail/Buy controls until whole (the logic guards in `SailToNewWorld`/`CheckBuyEuropeGoods`/`CheckBoard` are authoritative — the panel only reads `IsUnderRepair`). +1 L3 `ShipUnderRepair_ShowsNoSailButton`. Presentation reads only (ADR-006) | Phase 5 slice 1c-3b |
| 2026-06-15 | Native interaction UI (slice 1c-UI): new `NativeSettlementPanel` + `HandleTileClick` routes a discovered-settlement click (fog-gated) to it (was: immediate attack); offers speak/learn/attack gated by `CheckVisit`/`CheckLearnSkill`/`CheckAttackSettlement`; removed the now-unused `AttackSettlementAt`. +1 L3 `ClickingANativeSettlement_OpensInteractionPanel_AndSpeakWithChiefVisits`. Presentation reads only (ADR-006) | Phase 5 slice 1c-UI |
| 2026-06-15 | Piracy notice (slice 1c-3d-i): `FormatCombatNotice` renders the `Game.UnknownEnemyNationId` sentinel as an anonymous "privateer" (no nation named) when a foreign privateer raids the human. Presentation formatting only (ADR-006) | Phase 5 slice 1c-3d-i |
| 2026-06-15 | Colony-capture routing (slice 1c-3e): `HandleTileClick` routes a click on an ungarrisoned rival colony to `AttackColonyAt` (was: a CheckMove dead-end) → `CheckAttackColony`/`AttackColony`; reports "You captured X!" / "repelled". Presentation routing only (ADR-006) | Phase 5 slice 1c-3e |
| 2026-06-15 | Colony-loss notice (slice 1c-3f): `OnEndTurnPressed` now also renders `Game.ColonyLossNotices` via `FormatColonyLossNotice` ("⚑ The X captured your colony Y!"); `NationLabel` extracted and shared with `FormatCombatNotice` (no behaviour change). +1 L3 `ForeignPowerCapturesUndefendedColony_DuringEndTurn_ShowsALossNotice`. Presentation formatting only (ADR-006) | Phase 5 slice 1c-3f |
| 2026-06-15 | Colony-raid notice (native pillage): `OnEndTurnPressed` also renders `Game.ColonyRaidNotices` via `FormatColonyRaidNotice` ("⚔ The X raided Y and carried off N goods!"). Mirrors the L3-tested colony-loss notice path (same `OnEndTurnPressed` concat). Presentation formatting only (ADR-006) | Phase 5 native pillage |
| 2026-06-16 | Defeat banner (human defeat, `86d3bx04e`): `OnEndTurnPressed` appends a "💀 You have been defeated…" status message when `Game.IsHumanDefeated` (no colonies + no units). Message-only first cut: the End Turn button isn't disabled and `Game.EndTurn` does **not** short-circuit (that would break ADR-009 stream-0 byte-stability), so the game keeps advancing and the banner re-displays on repeated presses — a full game-over flow (disable the button + a game-over screen) is a deferred presentation follow-up. +1 L3 `WhenTheHumanIsWipedOut_EndTurn_ShowsDefeat`. Reads-only (ADR-006) | Phase 5 (human defeat) |
| 2026-06-14 | `GameController` only selects/renders the **player's** units (native braves are skipped as the HUD unit and on click); camera centres on a player unit → colony → map centre (braves now share the unit list) | Phase 5 slice 5b |
| 2026-06-13 | Walking-skeleton scene: map view, unit, camera, turn UI, quicksave; GdUnit4 L3 wiring | Phase 1 skeleton |
| 2026-06-13 | Isometric rendering with FreeCol terrain/unit/settlement art (ADR-014); L4 visual-golden harness | Phase 2 |
| 2026-06-13 | Interactive colony screen (`ColonyPanel`); L3 input + panel tests | Phase 3 |
| 2026-06-13 | Europe screen (`EuropePanel`): dock/recruit, ships, board/sail; map renders only on-map units; L3-tested | Phase 4 slice 6 |
| 2026-06-13 | Europe screen: goods Sell/Buy (slice 10) and Buy/train units (slice 11); L3-tested | Phase 4 slices 10–11 |
| 2026-06-13 | Native settlements on the map (`NativeSettlementMarker`, fog-gated; FreeCol indian art, ADR-014); `native-settlement` L4 golden | Phase 5 slice 1 |
| 2026-06-13 | `GameController` selects a game variant — new game loads its ruleset, saves record/restore it (ADR-018) | Phase 5 (variant layer) |
| 2026-06-13 | `MapView` dims explored-but-unseen tiles (explored-vs-visible fog); `remembered-fog` L4 golden | Phase 5 (fog upgrade) |
