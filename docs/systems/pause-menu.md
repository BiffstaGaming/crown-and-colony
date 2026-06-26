# System: Pause Menu (in-game)

| | |
|---|---|
| **Status** | In development (Slice C shipped) |
| **Last verified** | 2026-06-27 @ **unsaved-changes-aware quit prompt** (`86d3fq1v8`): `ConfirmQuitUnsaved` (Save / Quit anyway / Cancel when dirty, plain confirm when clean) reading the new `GameController` dirty flag; +3 L3. · 2026-06-24 @ menus (`86d3f0vf5`/`86d3f0w2x`) |
| **Code** | `game/presentation/PauseMenu.cs`, `game/presentation/GameController.cs` (the dirty flag + `SaveThenQuit` save dialog), `game/scenes/main.tscn` (`UI/PauseMenu`) |
| **Tests** | `game/presentation/tests/PauseMenuTests.cs` (L3) |
| **FreeCol reference** | conceptual (in-game menu); quit-confirm + "save before quitting?" ≈ FreeCol's quit dialog |
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
- **Quit to Main Menu** and **Quit to Desktop** each ask before leaving, because quitting would lose unsaved progress. The prompt now depends on whether you've actually changed anything since your last save (`86d3fq1v8`):
  - If you have **unsaved changes**, you get a three-way **"Unsaved changes"** prompt — **Save** (saves first, then quits), **Quit anyway** (quits without saving), or **Cancel** (stays in the game). Pressing **Save** opens the save-slot dialog; once you've saved, the quit goes ahead.
  - If you've **already saved** (nothing has changed since), you get the plain **"Quit without saving?"** confirm — **Quit** or **Cancel**.
  - **Cancel** is always a no-op; nothing happens and you stay in the paused game.

**Worked example:**
> Mid-game you move a unit and found a colony, then press **Esc → Quit to Main Menu**. Because you've changed things since your last save, an **"Unsaved changes"** prompt appears with **Save / Quit anyway / Cancel**. You press **Save**, pick a slot — the game saves and then drops you back to the title screen. Had you instead saved first and changed nothing, the same button would have shown the plain **"Quit without saving?"** confirm, and **Cancel** would simply keep you in the game.

**What the player sees and does:** an Esc-summoned panel (Resume, Save Game, Load Game, Settings, Help, About, Quit to Main Menu, Quit to Desktop) over a dimmed, paused game; the two quit choices confirm before leaving — a three-way *Save / Quit anyway / Cancel* prompt when there are unsaved changes, a plain *Quit / Cancel* confirm otherwise.

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
| **Quit to Main Menu** | Unsaved-aware confirm (`86d3fq1v8`): **dirty** → a three-way **"Unsaved changes"** prompt (**Save** → save dialog → on save, unpause + `ChangeSceneToFile` the main menu; **Quit anyway** → that quit straight away; **Cancel** → no-op); **clean** → the plain **"Quit without saving?"** confirm (**Quit** → that quit; **Cancel** → no-op) |
| **Quit to Desktop** | Same unsaved-aware confirm; the quit action is `SceneTree.Quit()` instead of a scene change |
| **Esc** while a quit confirmation is up | Ignored — use the dialog's buttons |
| **Dirty flag** | `GameController` tracks "unsaved changes since the last save/load/new game": set by any state-mutating command (unit move/order/found/disband/attack, colony commands) and by End Turn; cleared on save (manual / quick / autosave) and in `StartGame` (new + load). `PauseMenu` reads `GameController.HasUnsavedChanges` to pick the prompt |

- While paused, the game (map clicks, hotkeys, AI) is frozen; only the pause menu and its overlays (settings / save-load) respond.
- The pause panel + its dim backdrop block mouse input to the game beneath.

**Deviations from original 1994 / FreeCol behavior:** a modern convenience; the 1994 game had no single Esc pause menu. The quit-confirmation is a modern safety prompt (FreeCol confirms a quit similarly). No gameplay effect.

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Domain model:** none — presentation-only (ADR-006). `PauseMenu` is a `Control` in the game scene's `UI` layer, hidden by default.

**Pause mechanism:** `Open()` sets `GetTree().Paused = true` and shows the panel; `Resume()` clears it and hides. The `PauseMenu` node is authored with `process_mode = Always` (3) in `main.tscn`, so it keeps receiving input while the rest of the tree (which is `Pausable`/`Inherit`, including `GameController`) is frozen — that is what lets Esc both *open* (un-paused) and *close* (paused) the menu, and the buttons work while paused.

**Input:** `_UnhandledInput` toggles on `ui_cancel` (Esc) and calls `GetViewport().SetInputAsHandled()`. It is suppressed while any sub-overlay (settings or save/load) is up — tracked by a single `_overlay` field — so Esc can't dismiss the pause menu out from under it.

**Sub-overlays:** Settings, Help, About, Save and Load all open via `TrackOverlay`, which adds the overlay (a `Control`) to the **UI layer** with `ProcessMode.Always` (so it works while the tree is paused), records it as `_overlay`, and frees it on its `Closed` signal. **Save/Load** delegate the file work to the host `GameController` (`SaveTo`/`LoadFrom`) and confirm with an [InfoPopup](info-popup.md); a load also unpauses. **Help** and **About** are read-only (see [help.md](help.md), [about.md](about.md)). See [settings.md](settings.md) and [save-load-ui.md](save-load-ui.md).

**Quit confirmation (unsaved-aware, `86d3fq1v8`):** both quit buttons route through `ConfirmQuitUnsaved(action, onQuit)`. It reads the host `GameController.HasUnsavedChanges`:
- **Clean** → it falls back to the original `ConfirmQuit($"{action} without saving?", onQuit)` — a two-button `ConfirmationDialog` (title "Quit without saving?", OK "Quit", Cancel).
- **Dirty** → it builds a `ConfirmationDialog` titled **"Unsaved changes"** with OK text **"Quit anyway"** (`Confirmed → onQuit`), Cancel (no-op), and a **third "Save" button** added via `AcceptDialog.AddButton` (named `QuitSaveButton` so the L3 test can find it among the dialog's internal buttons). Pressing **Save** closes the prompt and runs `SaveThenQuit(onQuit)`, which opens the `SaveLoadDialog` in Save mode and chains `onQuit` onto a successful slot save (`Game.SaveTo(path)` then `onQuit()`); backing out of the save dialog cancels the quit too (the safe default — you stay in the game).

All dialogs are themed with `ColonyTheme`, given `ProcessMode.Always` (so they work over the paused tree), added to the UI layer and `PopupCentered`. Because a `ConfirmationDialog` is a `Window` (not a `Control`), it is tracked in the separate `_quitConfirm` field, and `_UnhandledInput` parks Esc while either `_overlay` **or** `_quitConfirm` is set. `onQuit` is the action (`QuitToMenu` / `GetTree().Quit()`). The title-screen `MainMenu.Quit` deliberately does **not** confirm — no game is in progress there (see [main-menu.md](main-menu.md)).

**The dirty flag (`GameController`):** a private `_dirty` bool with public `HasUnsavedChanges`/`MarkDirty` and a private `MarkClean`. `MarkDirty()` is called by `OnEndTurnPressed` and the state-mutating command handlers (the central `ApplyUnitOrder`; map commands in `HandleTileClick` — move/board/disembark; `FoundColonyWithClaim`; `DisbandSelectedUnit`; `SetDestination`; the three attack helpers; and the colony commands rename/abandon/pay-boycott/load/unload/set-export). `MarkClean()` runs after every save (`SaveTo`/`QuickSave` — so manual saves and the autosave both clear it) and in `StartGame` (a fresh or just-loaded game starts clean). It is presentation-only UI bookkeeping (ADR-006), never persisted. **Known approximation:** colony build-queue / work-assignment and Europe-screen purchases (which mutate inside their panel classes) don't set the flag directly today — in practice such a session ends a turn (which does mark dirty) before quitting; broadening the hook to those panel commands is a small follow-up.

**Look:** shares `ColonyTheme` + `ColonyArt.ParchmentSkin()` + the carved-wood border with the other menus.

**Integration points:** the pause-menu node is additive to `main.tscn` under `UI` (the visual goldens hide the `UI` layer and the panel starts hidden, so nothing regressed). The **Help** button (`UI/PauseMenu/Panel/VBox/HelpButton`, between Settings and About) grew the pause panel/border to −258/+258 (from −230/+230) for the eighth row. Save/Load call `GameController.SaveTo`/`LoadFrom`; **Quit to Main Menu** uses `MainMenu.MenuScenePath`. **Persistence:** none of its own (save files live under the save/load feature).

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | n/a (no game logic) | — | — |
| L2 Scenario | n/a | — | — |
| L3 Interaction | Yes (has UI) | `PauseMenuTests` — starts hidden/unpaused; Open pauses+shows, Resume unpauses; Resume button closes; Settings opens the overlay and Back closes it; **Help opens the overlay and Back closes it**; **About opens the overlay and Back closes it**; **Quit to Desktop / Quit to Main Menu each raise a confirmation and Cancel keeps the paused game**; Quit-to-Menu wired to a valid menu scene; **unsaved-aware quit (`86d3fq1v8`): a clean game shows the plain "Quit without saving?" confirm, a dirty game shows the "Unsaved changes" prompt with a Save button + Quit anyway, and pressing Save opens the save dialog without quitting** | ✅ |
| L4 Visual | Yes (has a screen) | `pause-menu` golden (`MenuGoldenTests`) — ⏳ needs CI-Linux regen (the Help button shifts the layout; see [help.md](help.md)) | ⏳ CI |
| L5 Soak | Covered by global suite | — | — |

## 5. Open issues / TODO

- [x] **L4 golden** for the pause menu (`MenuGoldenTests` → `pause-menu`) — added with the bundled font (Slice D).
- [ ] Optionally let Esc close the settings overlay too (currently only its Back button does).
- [x] **Save/Load** entries (Slice F — see [save-load-ui.md](save-load-ui.md)).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-27 | **Unsaved-changes-aware quit prompt** (`86d3fq1v8`, FreeCol's "save before quitting?" dialog): `GameController` gains a presentation-only **dirty flag** (`HasUnsavedChanges`/`MarkDirty`/`MarkClean`) — set on End Turn + the state-mutating command handlers, cleared on save/quicksave/autosave and in `StartGame` (new + load), never persisted. `PauseMenu` upgrades both quit buttons to `ConfirmQuitUnsaved`: a **dirty** game raises a three-way **"Unsaved changes"** prompt (**Save** opens the save dialog and quits on a successful save via `SaveThenQuit`, **Quit anyway**, **Cancel**), a **clean** game keeps the plain "Quit without saving?" confirm. The Save button (`AcceptDialog.AddButton`, named `QuitSaveButton`) is parked behind Esc like the rest of the prompt. **No save bump.** +3 L3 (`PauseMenuTests`: clean→plain confirm, dirty→unsaved prompt with Save+Quit-anyway+Cancel, Save opens the save dialog without quitting). Render-verified the prompt (committed no PNG). Pause-menu golden **unaffected** (the prompt is a transient dialog, not in the golden's curated state). | Wave 7 (`86d3fq1v8`) |
| 2026-06-17 | Slice C — in-game pause menu (Esc → Resume / Settings / Quit to Main Menu / Quit to Desktop); pauses the tree; reuses `SettingsScreen` as an overlay | 895f958 |
| 2026-06-17 | Slice D — added the `pause-menu` L4 golden (UI font bundled) | 0106d9c |
| 2026-06-17 | Slice F — added Save Game / Load Game (save-slot dialog + info popup); overlays unified into one `_overlay` in the UI layer; `pause-menu` golden regenerated | 4b71ede |
| 2026-06-24 | Added an **About** button (opens the `AboutPanel` overlay, see [about.md](about.md)) and a **"Quit without saving?"** confirmation before both Quit to Main Menu and Quit to Desktop (tracked in `_quitConfirm`; Cancel is a no-op). +3 L3; `pause-menu` golden regenerated | menus (`86d3f0vf5`/`86d3f0w2x`) |
| 2026-06-24 | Added a **Help** button (opens the `HelpPanel` overlay via the same `TrackOverlay` path, see [help.md](help.md)) between Settings and About; pause panel/border grew to −258/+258 for the eighth row. +1 L3 (`HelpButton_OpensTheHelpOverlay_BackClosesIt`); `pause-menu` golden needs CI-Linux regen (the Help button shifts the layout) | help (`86d3e98db`) |
