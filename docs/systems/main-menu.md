# System: Main Menu (title screen)

| | |
|---|---|
| **Status** | In development (Slice A shipped — shell only) |
| **Last verified** | 2026-06-24 @ menus (`86d3f0w2x`) |
| **Code** | `game/presentation/MainMenu.cs`, `game/scenes/MainMenu.tscn` |
| **Tests** | `game/presentation/tests/MainMenuTests.cs` (L3) |
| **FreeCol reference** | `freecol/src/net/sf/freecol/client/gui/panel/MainPanel.java` (opening-menu layout); `freecol/data/base/resources/images/ui/` (art); `AboutPanel.java` (About) |
| **Related systems** | [save-load-ui.md](save-load-ui.md) (Load Game dialog), [settings.md](settings.md) (Settings overlay), [help.md](help.md) (Help overlay), [about.md](about.md) (About overlay), [pause-menu.md](pause-menu.md) (Quit to Main Menu target), [colonies.md](colonies.md) (shared theme) |

## 1. How it works (plain English)

*Audience: anyone — no jargon.*

When you launch Crown & Colony you now arrive at a **title screen** instead of dropping straight into a running game. It shows the game's name over an antique map of the New World, framed in the same carved-wood-and-parchment style as the colony window, with six choices.

**The rules, in plain words:**
- **New Game** starts a fresh game (the same game you used to get on launch).
- **Load Game** opens the save-slot dialog and boots the save you pick (see [save-load-ui.md](save-load-ui.md)).
- **Settings** opens the options screen (see [settings.md](settings.md)).
- **Help** opens the in-game help / guide screen (see [help.md](help.md)).
- **About** opens the version / license screen (see [about.md](about.md)).
- **Quit** closes the game immediately (there's no game in progress to lose, so it doesn't ask).

**Worked example:**
> You double-click the game. The map-backed menu appears with the title "Crown & Colony". You click **New Game**; the title screen is replaced by the map and your starting unit, turn 1 — exactly the game that used to appear immediately on launch. **Load Game** lists your saved games; **Settings** opens the options screen; **Help** opens the guide to the game and its controls; **About** shows the version and licence.

**What the player sees and does:** one screen, six working buttons — New Game, Load Game, Settings, Help, About, Quit.

## 2. Detailed rules

*Audience: designers/testers.*

| Input / condition | Result |
|---|---|
| App launches | `MainMenu.tscn` loads (it is the project's `run/main_scene`) |
| Click **New Game** | The scene changes to `scenes/main.tscn`, which builds a fresh game (the prior boot behaviour) |
| Click **Load Game** | Opens the save-slot dialog (see [save-load-ui.md](save-load-ui.md)); choosing a save sets `GameController.PendingLoadPath` and boots the game scene from it |
| Click **Settings** | Opens the `SettingsScreen` overlay (see [settings.md](settings.md)); its Back closes it |
| Click **Help** | Opens the `HelpPanel` overlay (see [help.md](help.md)); its Back closes it |
| Click **About** | Opens the `AboutPanel` overlay (see [about.md](about.md)); its Back closes it |
| Click **Quit** | `SceneTree.Quit()` — the application exits immediately (no confirmation: nothing in progress) |

**Deviations from original 1994 / FreeCol behavior:** the FreeCol opening menu also offers Multiplayer and Map Editor; we ship only the single-player essentials for now. We now offer **About** (FreeCol's `AboutPanel`). We deliberately do **not** reuse FreeCol's "FreeCol" wordmark image — the title is rendered as our own "Crown & Colony" text in the shared theme.

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Domain model:** none — this is a presentation-only shell (ADR-006). `MainMenu` is a `Control` that owns no game state and raises no game logic. New Game is a scene change; the game is still constructed by `GameController` when `main.tscn` loads.

**Scene composition** (`MainMenu.tscn`): a full-rect `Control` →
- `Background` (`TextureRect`, stretch *keep-aspect-covered*) — backdrop texture set in code.
- `Vignette` (`ColorRect`, `Color(0.03,0.05,0.09,0.42)`) — darkens the map so the panel reads.
- `Panel` (`PanelContainer`, centre-anchored, 440×550) → `VBox` of Title, Subtitle, separator, spacer, and the six `Button`s (New Game, Load Game, Settings, **Help**, **About**, Quit). The panel/border grew to −275/+275 (from −245/+245) to fit the sixth row.
- `Border` (`NinePatchRect`, same rect as `Panel`, `draw_center=false`, 23px margins) — the carved-wood frame overlaid on the panel edge. Same trick as the colony screen (a sibling at the identical rect; see `ColonyPanel`).

**Look reuse:** `MainMenu._Ready()` assigns `ColonyTheme.Get()` (cascades wood buttons + parchment popups + the `ColonyTitle` label variation), overrides the panel's `panel` stylebox with FreeCol's tiled brown parchment (`ColonyArt.PanelParchment()`, inset 26px — mirrors `ColonyPanel.BuildPanelBackground`), sets the `Border` texture from `ColonyArt.ColonyBorder()`, and loads the backdrop. Each art load is null-guarded so the screen degrades gracefully (and stays opaque in CI) if an asset is absent.

**Data sources:** none (no ruleset/XML). Art: `res://assets/freecol/ui/map.jpg` (backdrop), `ui/bg_paper_brown.png` (parchment), `ui/colony_border.png` (frame) — all FreeCol GPL v2 (see `game/assets/freecol/PROVENANCE.md`).

**Integration points:** `project.godot` `run/main_scene` → `res://scenes/MainMenu.tscn`. New Game → `GetTree().ChangeSceneToFile(MainMenu.GameScenePath)` where `GameScenePath = "res://scenes/main.tscn"`.

**Persistence:** none.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | n/a (no game logic) | — | — |
| L2 Scenario | n/a (no game logic) | — | — |
| L3 Interaction | Yes (has UI) | `MainMenuTests` — title + buttons present; enabled/disabled states; theme/parchment/border art applied; New Game wired to a valid game scene; **About opens the `AboutPanel` overlay**; **Help opens the `HelpPanel` overlay** | ✅ |
| L4 Visual | Yes (has a screen) | `main-menu` golden (`MenuGoldenTests`) — ⏳ needs CI-Linux regen (the Help button shifts the layout; see [help.md](help.md)) | ⏳ CI |
| L5 Soak | Covered by global suite | — | — |

- **FreeCol cross-check:** layout/style compared against `MainPanel.java` (parchment background, centred title, stacked buttons, wood styling). We omit Multiplayer/Map Editor by design; About is now offered (FreeCol `AboutPanel`).

## 5. Open issues / TODO

- [x] **L4 golden** for the menu (`MenuGoldenTests` → `main-menu`) — added once the UI font was bundled (Slice D).
- [x] **Load Game** wiring → save-slot dialog (Slice F — see [save-load-ui.md](save-load-ui.md)).
- [x] **Settings** wiring → settings screen (Slice B — see [settings.md](settings.md)).
- [ ] In-game **pause menu** (Esc → Resume / Settings / Save / Quit to menu) — separate slice.
- [ ] **New Game setup** screen (nation / difficulty / map) — needs the difficulty system (ClickUp `86d3c9y08`).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-17 | Slice A — main-menu shell: scene + script, FreeCol map backdrop + wood/parchment frame, New Game/Quit wired, Load/Settings disabled; L3 tests; boot scene switched to the menu | 17f00a6 |
| 2026-06-17 | Slice B — Settings button wired to `SettingsScreen`; parchment skin hoisted to `ColonyArt.ParchmentSkin()` | 11da6fa |
| 2026-06-17 | Slice C — Settings now opens `SettingsScreen` as an overlay (was a scene change), to match the pause menu's reuse | 895f958 |
| 2026-06-17 | Slice D — bundled UI font (Cardo) cascades here via `ColonyTheme`; added the `main-menu` L4 golden | 0106d9c |
| 2026-06-17 | Slice F — Load Game wired to the save-slot dialog (button enabled); `main-menu` golden regenerated | 4b71ede |
| 2026-06-24 | Added an **About** button (opens the `AboutPanel` overlay, see [about.md](about.md)); Quit stays immediate (no game in progress). +1 L3; `main-menu` golden regenerated | menus (`86d3f0w2x`) |
| 2026-06-24 | Added a **Help** button (opens the `HelpPanel` overlay, see [help.md](help.md)) between Settings and About; panel/border grew to −275/+275 for the sixth row. +1 L3 (`HelpButton_OpensTheHelpScreenAsAnOverlay`); `main-menu` golden needs CI-Linux regen (the Help button shifts the layout) | help (`86d3e98db`) |
| 2026-06-27 | **New-Game setup dials** (`86d3fq1df`/`86d3fq1fd`/`86d3fq13u`/`86d3fq18b`/`86d3fq1b8`/`86d3fq0za`): the `NewGameDialog` gained six setup dropdowns — **Rival powers**, **Starting year**, **Temperature**/**Humidity** (climate), **Rivers**/**Mountains**/**Forests**/**Bonus resources** (map-gen counts), **Lost-city rumours**, **National advantages** — each defaulting to the byte-identical classic value. The growing option list now lives in a bounded `ScrollContainer` so no row clips. The dials are GameLogic-backed (`Game.New` parameters + `MapGenerationOptions` + `Ruleset.WithStartingYear`; see [map-terrain](map-terrain.md)/[players](players.md)/[turns](turns.md)/[lost-city-rumours](lost-city-rumours.md)) and collected by the dialog onto `NewGameDialog.Pending*` statics (ADR-006). **Follow-up:** the new-game host (`GameController.NewGame`) must read+clear those statics and pass them to `Game.New` — see [presentation](../modules/presentation.md) Open issues. +2 L3 (`MainMenuTests`: the dials render with their defaults / forward onto the statics). | Wave (`86d3fq*`) |
