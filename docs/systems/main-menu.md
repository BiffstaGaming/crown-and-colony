# System: Main Menu (title screen)

| | |
|---|---|
| **Status** | In development (Slice A shipped — shell only) |
| **Last verified** | 2026-06-17 @ 11da6fa |
| **Code** | `game/presentation/MainMenu.cs`, `game/scenes/MainMenu.tscn` |
| **Tests** | `game/presentation/tests/MainMenuTests.cs` (L3) |
| **FreeCol reference** | `freecol/src/net/sf/freecol/client/gui/panel/MainPanel.java` (opening-menu layout); `freecol/data/base/resources/images/ui/` (art) |
| **Related systems** | [settings.md](settings.md) (Settings button), [colonies.md](colonies.md) (shared parchment/wood theme), [save-load.md](save-load.md) (Load Game, later) |

## 1. How it works (plain English)

*Audience: anyone — no jargon.*

When you launch Crown & Colony you now arrive at a **title screen** instead of dropping straight into a running game. It shows the game's name over an antique map of the New World, framed in the same carved-wood-and-parchment style as the colony window, with four choices.

**The rules, in plain words:**
- **New Game** starts a fresh game (the same game you used to get on launch).
- **Settings** opens the options screen (see [settings.md](settings.md)).
- **Load Game** is shown but greyed out — it switches on in a later slice.
- **Quit** closes the game.

**Worked example:**
> You double-click the game. The map-backed menu appears with the title "Crown & Colony". You click **New Game**; the title screen is replaced by the map and your starting unit, turn 1 — exactly the game that used to appear immediately on launch. **Settings** opens the options screen; **Load Game** will list your saves in a future build.

**What the player sees and does:** one screen, four buttons (New Game, Load Game, Settings, Quit); New Game, Settings and Quit are active, Load Game is disabled until its feature lands.

## 2. Detailed rules

*Audience: designers/testers.*

| Input / condition | Result |
|---|---|
| App launches | `MainMenu.tscn` loads (it is the project's `run/main_scene`) |
| Click **New Game** | The scene changes to `scenes/main.tscn`, which builds a fresh game (the prior boot behaviour) |
| Click **Settings** | The scene changes to `scenes/SettingsScreen.tscn` (see [settings.md](settings.md)) |
| Click **Quit** | `SceneTree.Quit()` — the application exits |
| **Load Game** button | Disabled (greyed) until the save-load dialog UI ships |

**Deviations from original 1994 / FreeCol behavior:** the FreeCol opening menu also offers Multiplayer, Map Editor and About; we ship only the single-player essentials for now. We deliberately do **not** reuse FreeCol's "FreeCol" wordmark image — the title is rendered as our own "Crown & Colony" text in the shared theme.

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Domain model:** none — this is a presentation-only shell (ADR-006). `MainMenu` is a `Control` that owns no game state and raises no game logic. New Game is a scene change; the game is still constructed by `GameController` when `main.tscn` loads.

**Scene composition** (`MainMenu.tscn`): a full-rect `Control` →
- `Background` (`TextureRect`, stretch *keep-aspect-covered*) — backdrop texture set in code.
- `Vignette` (`ColorRect`, `Color(0.03,0.05,0.09,0.42)`) — darkens the map so the panel reads.
- `Panel` (`PanelContainer`, centre-anchored, 440×490) → `VBox` of Title, Subtitle, separator, spacer, and the four `Button`s.
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
| L3 Interaction | Yes (has UI) | `MainMenuTests` — title + four buttons present; enabled/disabled states; theme/parchment/border art applied; New Game wired to a valid game scene | ✅ |
| L4 Visual | Deferred | golden blocked on the UI-font task (ClickUp `86d3c9y32`) — see Open issues | ⬜ |
| L5 Soak | Covered by global suite | — | — |

- **FreeCol cross-check:** layout/style compared against `MainPanel.java` (parchment background, centred title, stacked buttons, wood styling). We omit Multiplayer/Map Editor/About by design.

## 5. Open issues / TODO

- [ ] **L4 golden** for the menu — blocked on bundling a licence-clear UI font (ClickUp `86d3c9y32`); add once goldens for UI are unblocked.
- [ ] **Load Game** wiring → save-load dialog UI (ClickUp `86d3c9y5y`).
- [x] **Settings** wiring → settings screen (Slice B — see [settings.md](settings.md)).
- [ ] In-game **pause menu** (Esc → Resume / Settings / Save / Quit to menu) — separate slice.
- [ ] **New Game setup** screen (nation / difficulty / map) — needs the difficulty system (ClickUp `86d3c9y08`).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-17 | Slice A — main-menu shell: scene + script, FreeCol map backdrop + wood/parchment frame, New Game/Quit wired, Load/Settings disabled; L3 tests; boot scene switched to the menu | 17f00a6 |
| 2026-06-17 | Slice B — Settings button wired to `SettingsScreen`; parchment skin hoisted to `ColonyArt.ParchmentSkin()` | 11da6fa |
