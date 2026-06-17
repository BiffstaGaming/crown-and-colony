# System: Pause Menu (in-game)

| | |
|---|---|
| **Status** | In development (Slice C shipped) |
| **Last verified** | 2026-06-17 @ (pending) |
| **Code** | `game/presentation/PauseMenu.cs`, `game/scenes/main.tscn` (`UI/PauseMenu`) |
| **Tests** | `game/presentation/tests/PauseMenuTests.cs` (L3) |
| **FreeCol reference** | conceptual (in-game menu) |
| **Related systems** | [main-menu.md](main-menu.md) (Quit to Main Menu target), [settings.md](settings.md) (Settings overlay) |

## 1. How it works (plain English)

*Audience: anyone — no jargon.*

While you're playing, pressing **Esc** brings up a pause menu over the (now frozen) game. From it you can resume, open settings, go back to the main menu, or quit to the desktop.

**The rules, in plain words:**
- **Esc** opens the menu and pauses the game; **Esc** again (or **Resume**) closes it and unpauses.
- **Settings** opens the options screen on top — the game stays paused underneath; closing it returns you to the pause menu.
- **Quit to Main Menu** leaves the game and returns to the title screen.
- **Quit to Desktop** closes the game.

**Worked example:**
> Mid-game you press **Esc**: the map dims and a "Paused" panel appears; the game is frozen. You open **Settings**, lower the music, press **Back** — you're back at the pause menu, game still paused. You press **Resume**; play continues exactly where you left off. Later you press **Esc → Quit to Main Menu** and you're back at the title screen.

**What the player sees and does:** an Esc-summoned panel (Resume, Settings, Quit to Main Menu, Quit to Desktop) over a dimmed, paused game.

## 2. Detailed rules

*Audience: designers/testers.*

| Input / condition | Result |
|---|---|
| **Esc** while playing | Pause menu shown; `GetTree().Paused = true` |
| **Esc** while the pause menu is open | Resume (hide + unpause) |
| **Esc** while the settings overlay is open | Ignored — use the settings **Back** button |
| **Resume** | Hide + unpause |
| **Settings** | Opens the `SettingsScreen` overlay (game stays paused); its **Back** returns to the pause menu |
| **Quit to Main Menu** | Unpause, then `ChangeSceneToFile` the main menu |
| **Quit to Desktop** | `SceneTree.Quit()` |

- While paused, the game (map clicks, hotkeys, AI) is frozen; only the pause menu and the settings overlay respond.
- The pause panel + its dim backdrop block mouse input to the game beneath.

**Deviations from original 1994 / FreeCol behavior:** a modern convenience; the 1994 game had no single Esc pause menu. No gameplay effect.

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Domain model:** none — presentation-only (ADR-006). `PauseMenu` is a `Control` in the game scene's `UI` layer, hidden by default.

**Pause mechanism:** `Open()` sets `GetTree().Paused = true` and shows the panel; `Resume()` clears it and hides. The `PauseMenu` node is authored with `process_mode = Always` (3) in `main.tscn`, so it keeps receiving input while the rest of the tree (which is `Pausable`/`Inherit`, including `GameController`) is frozen — that is what lets Esc both *open* (un-paused) and *close* (paused) the menu, and the buttons work while paused.

**Input:** `_UnhandledInput` toggles on `ui_cancel` (Esc) and calls `GetViewport().SetInputAsHandled()`. It is suppressed while the settings overlay is up (tracked by a field) so Esc doesn't dismiss the pause menu out from under it.

**Settings reuse:** `OpenSettings` instantiates `SettingsScreen` (set to `ProcessMode.Always`), adds it as a child (drawn on top), and connects its `Closed` signal to free it. The game remains paused throughout. See [settings.md](settings.md).

**Look:** shares `ColonyTheme` + `ColonyArt.ParchmentSkin()` + the carved-wood border with the other menus.

**Integration points:** added to `main.tscn` under `UI` (additive — `GameController` is untouched; the visual goldens hide the `UI` layer and the panel starts hidden, so nothing regressed). **Quit to Main Menu** uses `MainMenu.MenuScenePath`. **Persistence:** none.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | n/a (no game logic) | — | — |
| L2 Scenario | n/a | — | — |
| L3 Interaction | Yes (has UI) | `PauseMenuTests` — starts hidden/unpaused; Open pauses+shows, Resume unpauses; Resume button closes; Settings opens the overlay and Back closes it; Quit-to-Menu wired to a valid menu scene | ✅ |
| L4 Visual | Deferred | golden blocked on the UI-font task (ClickUp `86d3c9y32`) | ⬜ |
| L5 Soak | Covered by global suite | — | — |

## 5. Open issues / TODO

- [ ] **L4 golden** for the pause menu — blocked on a licence-clear UI font (ClickUp `86d3c9y32`).
- [ ] Optionally let Esc close the settings overlay too (currently only its Back button does).
- [ ] **Save/Load** entries here once the save-load dialog UI lands (ClickUp `86d3c9y5y`).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-17 | Slice C — in-game pause menu (Esc → Resume / Settings / Quit to Main Menu / Quit to Desktop); pauses the tree; reuses `SettingsScreen` as an overlay | (pending) |
