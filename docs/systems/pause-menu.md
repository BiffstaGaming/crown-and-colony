# System: Pause Menu (in-game)

| | |
|---|---|
| **Status** | In development (Slice C shipped) |
| **Last verified** | 2026-06-24 @ menus (`86d3f0vf5`/`86d3f0w2x`) |
| **Code** | `game/presentation/PauseMenu.cs`, `game/scenes/main.tscn` (`UI/PauseMenu`) |
| **Tests** | `game/presentation/tests/PauseMenuTests.cs` (L3) |
| **FreeCol reference** | conceptual (in-game menu); quit-confirm ≈ FreeCol's quit dialog |
| **Related systems** | [main-menu.md](main-menu.md) (Quit to Main Menu target), [settings.md](settings.md) (Settings overlay), [help.md](help.md) (Help overlay), [about.md](about.md) (About overlay), [save-load-ui.md](save-load-ui.md) (Save/Load), [info-popup.md](info-popup.md) (confirmations) |

## 1. How it works (plain English)

*Audience: anyone — no jargon.*

While you're playing, pressing **Esc** brings up a pause menu over the (now frozen) game. From it you can resume, save or load your game, open settings, read the in-game help, see the About screen, go back to the main menu, or quit to the desktop.

**The rules, in plain words:**
- **Esc** opens the menu and pauses the game; **Esc** again (or **Resume**) closes it and unpauses.
- **Save Game** / **Load Game** open the save-slot dialog (the game stays paused); a popup confirms a save.
- **Settings** opens the options screen on top — the game stays paused underneath; closing it returns you to the pause menu.
- **Help** opens the in-game help / guide screen on top; closing it returns you to the pause menu.
- **About** opens the version / license screen on top; closing it returns you to the pause menu.
- **Quit to Main Menu** and **Quit to Desktop** each ask **"Quit without saving?"** first — because leaving an unsaved game would lose your progress. Choosing **Quit** goes ahead; **Cancel** keeps you in the game (nothing happens).

**Worked example:**
> Mid-game you press **Esc**: the map dims and a "Paused" panel appears; the game is frozen. You open **Settings**, lower the music, press **Back** — you're back at the pause menu, game still paused. You press **Resume**; play continues exactly where you left off. Later you press **Esc → Quit to Main Menu**; a "Quit without saving?" prompt appears — you click **Cancel** and you're still in the game, then change your mind, press **Esc → Quit to Main Menu → Quit**, and you're back at the title screen.

**What the player sees and does:** an Esc-summoned panel (Resume, Save Game, Load Game, Settings, Help, About, Quit to Main Menu, Quit to Desktop) over a dimmed, paused game; the two quit choices confirm before leaving.

## 2. Detailed rules

*Audience: designers/testers.*

| Input / condition | Result |
|---|---|
| **Esc** while playing | Pause menu shown; `GetTree().Paused = true` |
| **Esc** while the pause menu is open | Resume (hide + unpause) |
| **Esc** while a sub-overlay (settings / save-load) is open | Ignored — use that overlay's **Back** button |
| **Resume** | Hide + unpause |
| **Save Game** | Opens the save-slot dialog (Save mode); choosing a slot saves the game there and confirms with an info popup (see [save-load-ui.md](save-load-ui.md)) |
| **Load Game** | Opens the save-slot dialog (Load mode); choosing a save loads it, unpauses, and confirms with an info popup |
| **Settings** | Opens the `SettingsScreen` overlay (game stays paused); its **Back** returns to the pause menu |
| **Help** | Opens the `HelpPanel` overlay (game stays paused); its **Back** returns to the pause menu (see [help.md](help.md)) |
| **About** | Opens the `AboutPanel` overlay (game stays paused); its **Back** returns to the pause menu (see [about.md](about.md)) |
| **Quit to Main Menu** | Raises a **"Quit without saving?"** confirmation; **Quit** → unpause then `ChangeSceneToFile` the main menu; **Cancel** → no-op (still paused, menu still up) |
| **Quit to Desktop** | Raises a **"Quit without saving?"** confirmation; **Quit** → `SceneTree.Quit()`; **Cancel** → no-op |
| **Esc** while a quit confirmation is up | Ignored — use the dialog's Quit/Cancel buttons |

- While paused, the game (map clicks, hotkeys, AI) is frozen; only the pause menu and its overlays (settings / save-load) respond.
- The pause panel + its dim backdrop block mouse input to the game beneath.

**Deviations from original 1994 / FreeCol behavior:** a modern convenience; the 1994 game had no single Esc pause menu. The quit-confirmation is a modern safety prompt (FreeCol confirms a quit similarly). No gameplay effect.

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Domain model:** none — presentation-only (ADR-006). `PauseMenu` is a `Control` in the game scene's `UI` layer, hidden by default.

**Pause mechanism:** `Open()` sets `GetTree().Paused = true` and shows the panel; `Resume()` clears it and hides. The `PauseMenu` node is authored with `process_mode = Always` (3) in `main.tscn`, so it keeps receiving input while the rest of the tree (which is `Pausable`/`Inherit`, including `GameController`) is frozen — that is what lets Esc both *open* (un-paused) and *close* (paused) the menu, and the buttons work while paused.

**Input:** `_UnhandledInput` toggles on `ui_cancel` (Esc) and calls `GetViewport().SetInputAsHandled()`. It is suppressed while any sub-overlay (settings or save/load) is up — tracked by a single `_overlay` field — so Esc can't dismiss the pause menu out from under it.

**Sub-overlays:** Settings, Help, About, Save and Load all open via `TrackOverlay`, which adds the overlay (a `Control`) to the **UI layer** with `ProcessMode.Always` (so it works while the tree is paused), records it as `_overlay`, and frees it on its `Closed` signal. **Save/Load** delegate the file work to the host `GameController` (`SaveTo`/`LoadFrom`) and confirm with an [InfoPopup](info-popup.md); a load also unpauses. **Help** and **About** are read-only (see [help.md](help.md), [about.md](about.md)). See [settings.md](settings.md) and [save-load-ui.md](save-load-ui.md).

**Quit confirmation:** both quit buttons route through `ConfirmQuit(prompt, onConfirm)`, which builds a Godot `ConfirmationDialog` (title "Quit without saving?", OK text "Quit", themed with `ColonyTheme`, `ProcessMode.Always`), adds it to the UI layer and `PopupCentered`s it. `Confirmed` runs `onConfirm` (`QuitToMenu` / `GetTree().Quit()`); `Canceled` just frees it. Because `ConfirmationDialog` is a `Window` (not a `Control`), it is tracked in a separate `_quitConfirm` field, and `_UnhandledInput` parks Esc while either `_overlay` **or** `_quitConfirm` is set. The title-screen `MainMenu.Quit` deliberately does **not** confirm — no game is in progress there (see [main-menu.md](main-menu.md)).

**Look:** shares `ColonyTheme` + `ColonyArt.ParchmentSkin()` + the carved-wood border with the other menus.

**Integration points:** the pause-menu node is additive to `main.tscn` under `UI` (the visual goldens hide the `UI` layer and the panel starts hidden, so nothing regressed). The **Help** button (`UI/PauseMenu/Panel/VBox/HelpButton`, between Settings and About) grew the pause panel/border to −258/+258 (from −230/+230) for the eighth row. Save/Load call `GameController.SaveTo`/`LoadFrom`; **Quit to Main Menu** uses `MainMenu.MenuScenePath`. **Persistence:** none of its own (save files live under the save/load feature).

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | n/a (no game logic) | — | — |
| L2 Scenario | n/a | — | — |
| L3 Interaction | Yes (has UI) | `PauseMenuTests` — starts hidden/unpaused; Open pauses+shows, Resume unpauses; Resume button closes; Settings opens the overlay and Back closes it; **Help opens the overlay and Back closes it**; **About opens the overlay and Back closes it**; **Quit to Desktop / Quit to Main Menu each raise a confirmation and Cancel keeps the paused game**; Quit-to-Menu wired to a valid menu scene | ✅ |
| L4 Visual | Yes (has a screen) | `pause-menu` golden (`MenuGoldenTests`) — ⏳ needs CI-Linux regen (the Help button shifts the layout; see [help.md](help.md)) | ⏳ CI |
| L5 Soak | Covered by global suite | — | — |

## 5. Open issues / TODO

- [x] **L4 golden** for the pause menu (`MenuGoldenTests` → `pause-menu`) — added with the bundled font (Slice D).
- [ ] Optionally let Esc close the settings overlay too (currently only its Back button does).
- [x] **Save/Load** entries (Slice F — see [save-load-ui.md](save-load-ui.md)).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-17 | Slice C — in-game pause menu (Esc → Resume / Settings / Quit to Main Menu / Quit to Desktop); pauses the tree; reuses `SettingsScreen` as an overlay | 895f958 |
| 2026-06-17 | Slice D — added the `pause-menu` L4 golden (UI font bundled) | 0106d9c |
| 2026-06-17 | Slice F — added Save Game / Load Game (save-slot dialog + info popup); overlays unified into one `_overlay` in the UI layer; `pause-menu` golden regenerated | 4b71ede |
| 2026-06-24 | Added an **About** button (opens the `AboutPanel` overlay, see [about.md](about.md)) and a **"Quit without saving?"** confirmation before both Quit to Main Menu and Quit to Desktop (tracked in `_quitConfirm`; Cancel is a no-op). +3 L3; `pause-menu` golden regenerated | menus (`86d3f0vf5`/`86d3f0w2x`) |
| 2026-06-24 | Added a **Help** button (opens the `HelpPanel` overlay via the same `TrackOverlay` path, see [help.md](help.md)) between Settings and About; pause panel/border grew to −258/+258 for the eighth row. +1 L3 (`HelpButton_OpensTheHelpOverlay_BackClosesIt`); `pause-menu` golden needs CI-Linux regen (the Help button shifts the layout) | help (`86d3e98db`) |
