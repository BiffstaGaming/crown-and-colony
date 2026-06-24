# System: HUD input (keyboard & mouse)

| | |
|---|---|
| **Status** | Implemented (left-click select/move; keyboard hotkeys via one authoritative key table; F1 keys legend; Space skip-unit; D disband + confirm; arrow-key & right/middle-drag camera pan; Ctrl+C centre; right-click tile context menu) |
| **Last verified** | 2026-06-24 @ HUD-input set (disband + hotkey completeness + skip-unit + keyboard pan/centre + right-click menu); `86d3f0vgd`/`86d3f0vjg`/`86d3f0vuy`/`86d3f0vqf`/`86d3f0vrz` |
| **Code** | `game/presentation/GameController.cs` (`_UnhandledInput`, `KeyBindings`/`BuildKeyBindings`, `SkipSelectedUnit`, `DisbandSelectedUnit`, `CenterOnSelectedUnit`, `OpenSaveDialog`/`OpenLoadDialog`, `ToggleKeysLegend`/`BuildKeysLegend`, `HandleRightClick`, `IsTextInputFocused`), `game/presentation/CameraController.cs` (arrow-key pan in `_Process`, right-drag-vs-click), `game/presentation/PauseMenu.cs` (`OpenSave`/`OpenLoad`), `game/src/GameLogic/GameSession/Game.Goto.cs` (`NextUnitToMove` skip param) |
| **Tests** | `game/presentation/tests/InputTests.cs` (L3), `game/tests/GameLogic.Tests/GameSession/GotoTests.cs` (`NextUnitToMove_SkipSet_*`, L1) |
| **FreeCol reference** | `freecol/src/net/sf/freecol/client/gui/action/` — `DisbandUnitAction` (D), `EndTurnAction` (ENTER), `SaveAction` (Ctrl+S) / `OpenAction` (Ctrl+O), `SkipUnitAction` (SPACE), `CenterAction` (Ctrl+C); `freecol/src/net/sf/freecol/client/gui/panel/TilePopup.java` (right-click tile menu) |
| **Related systems** | [units-movement](units-movement.md), [save-load-ui](save-load-ui.md), [pause-menu](pause-menu.md) |

## 1. How it works (plain English)

The map is driven with the mouse and a handful of keyboard shortcuts. **Left-click** a tile to select your unit (or move/attack with one selected); **left-click your colony** to open it. **Right-click** a tile opens a small menu: pick any one of your units standing there (handy when several are stacked on one square — a left-click would only grab the first), centre the view there, or send the selected unit there ("go to").

The view itself pans by **dragging with the right or middle mouse button**, by spinning the **mouse wheel** to zoom, or by holding the **arrow keys** to slide the camera around (it keeps moving while you hold them, and feels the same speed however far you've zoomed in or out). Press **Ctrl+C** to snap the camera back onto the unit you've got selected.

For orders and game control there's a shortcut for everything, and you never have to remember them: press **F1** to pop up a legend listing every key and what it does. The important ones: **Enter** ends the turn, **Space** skips the current unit (you'll deal with it later this turn — see [units-movement](units-movement.md)), **W** jumps to the next unit that still needs orders, **D** disbands the selected unit (it asks you to confirm first, because it's gone for good), **Ctrl+S** / **Ctrl+O** save and load, and **B / E / L / F / C** open build-colony / Europe / find-settlement / founding-fathers / the Colopedia. While you're typing into a text box (a save-slot name, a search field) the shortcuts stand down so your letters don't trigger actions.

**What the player sees and does:** the map and HUD; the selected-unit panel grows a **Skip** and a **Disband** button (Disband only lights up when the unit can actually be disbanded); a confirmation box appears before a disband; an F1 legend overlay toggles on the right of the screen.

## 2. Detailed rules

**Authoritative key table** — both the keypress dispatch and the F1 legend are generated from one list (`GameController.BuildKeyBindings`), so a key and its on-screen label can never drift apart.

| Key(s) | Action | Notes |
|---|---|---|
| Enter / Keypad-Enter | End turn | Same path as the End-Turn button (`OnEndTurnPressed`); disabled-state honoured via that path |
| Space | Skip unit this turn | Flags the selected unit skipped (session-only set), then cycles to the next unit needing orders |
| W | Next unit needing orders | `SelectNextUnitToMove`; skips units flagged by Space until turn rollover |
| G | Go to (arm destination) | Next click sets the selected unit's standing destination |
| B | Build colony | |
| D | Disband unit | Opens a confirmation dialog; gated on `CheckDisband` |
| E / L / F / C | Europe / Find settlement / Founding fathers / Colopedia | |
| Ctrl+C | Centre camera on selected unit | Reuses `CenterCameraOnTile` |
| N | New map | |
| Ctrl+S / Ctrl+O | Save / Load (named-slot dialog) | Pauses the game behind the dialog (the PauseMenu path); unpauses when it closes |
| F5 / F9 | Quick save / Quick load | Unchanged; the single-file `user://quicksave.json` |
| F1 | Toggle keys legend | The on-screen list, generated from this table |

- A hotkey fires only when **no text field owns focus** (`GetViewport().GuiGetFocusOwner()` is not a `LineEdit`/`TextEdit`) — so typing into a slot/search field never triggers an action.
- A hotkey requires its **exact** modifier state: a plain-key binding needs Ctrl/Alt/Shift/Meta all clear; a `Ctrl+X` binding needs Ctrl held and the others clear (`KeyChord.Matches`).
- **WASD is deliberately not a pan** — W=next-unit and C=colopedia are letter hotkeys; the camera pans on arrows + right/middle drag instead.

**Right-click tile menu** (`HandleRightClick`, on right-button **release without a drag**):

| Entry | Effect |
|---|---|
| Activate `<unit>` (#id) | One per own on-map unit on the tile (id order) → selects that unit (fixes stack-select) |
| Centre here | Recentres the camera on the tile |
| Go to here | Sets the selected unit's standing destination (no-op with no selection) |

- The right **drag** still pans: `CameraController` tracks whether the right-button motion exceeded a small threshold (4 px). On release it consumes the event only if it actually dragged, so a genuine pan never opens the menu; a drag-free right-click falls through to `GameController` and opens the menu. Middle-mouse is the drag-free pan fallback.

**Deviations from original 1994 / FreeCol behavior:** none of substance. The skip set is a presentation-side, session-only convenience (FreeCol skips per-unit in its own client state); the right-click menu is a trimmed `TilePopup` (activate / centre / go-to only) for the single-player classic game.

## 3. Technical design

**Domain model:** all input lives in presentation (ADR-006); no game rules here. `GameController._UnhandledInput` switches on event type, then dispatches keys through `KeyBindings` (an `IReadOnlyList<KeyBinding>`; each `KeyBinding` = `KeyChord[]` + `Action` + label). `KeyChord` is a `readonly record struct(Key Code, bool Ctrl)` with `Matches(InputEventKey)` and a `ToString()` for the legend. `CameraController` owns the view transform only.

**Skip-unit (session-only):** the controller holds `_skippedThisTurn` (`HashSet<int>` of unit ids), never serialized (ADR-009). `Game.NextUnitToMove(player, skip)` gained an optional `IReadOnlySet<int>? skip` parameter so the "needs orders" predicate stays authoritative in `GameLogic` while the UI excludes skipped ids. The set is cleared at turn rollover (`OnEndTurnPressed`) and on `StartGame` (new/loaded game).

**Save/Load hotkeys:** `Ctrl+S`/`Ctrl+O` call `PauseMenu.OpenSave()`/`OpenLoad()`, which pause the tree and open the existing `SaveLoadDialog` overlay (the same path the pause menu's Save/Load buttons use), then unpause when the dialog closes if the pause menu itself is hidden (i.e. the hotkey entry, not the Esc-menu flow).

**Integration points:** `_UnhandledInput` ordering — `CameraController` (a child node) receives `_unhandled_input` before the `GameController` root, so its right-release `SetInputAsHandled()` (after a real drag) suppresses the menu. Arrow-key pan is a continuous poll in `CameraController._Process` (reads `Input.IsKeyPressed`), not event-driven, so it doesn't compete with the key-dispatch table.

**Persistence:** none. Skip set, legend visibility, camera position — all session-only.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `GotoTests.NextUnitToMove_SkipSet_PassesOverSkippedUnits_ButStillOffersTheRest` (skip-param oracle) | ✅ |
| L2 Scenario | n/a | input is presentation; the underlying commands (Disband/EndTurn/goto) are covered in their own systems | — |
| L3 Interaction | Yes (UI) | `InputTests`: `SelectedUnitPanel_DisbandButton_*`, `PressingD_*`, `PressingEnter_EndsTheTurn`, `PressingSpace_SkipsTheUnit_*`, `CtrlC_CentresTheCameraOnTheSelectedUnit`, `F1_TogglesTheKeysLegend`, `RightClickTileMenu_ActivatesAUnitInAStack` | ⏳ CI |
| L4 Visual | No (no new screen) | — | — |
| L5 Soak | Covered by global suite | — | — |

- **L1 green** locally (`NextUnitToMove` skip tests, 2/2). **L3** compiles against the Godot project (`dotnet build` clean) and the scene imports clean; the headless GdUnit runtime can't connect in the dev sandbox, so the L3 suite runs on the PR CI gate.
- **FreeCol cross-check:** key choices match FreeCol's accelerator actions (D / ENTER / Ctrl+S / Ctrl+O / SPACE / Ctrl+C) and the `TilePopup` concept.

## 5. Open issues / TODO

- [ ] L3 suite verified green only on CI in this environment (headless GdUnit runtime limitation).
- [ ] Right-click menu is single-player-classic scope; a multiplayer/observer build may want a richer `TilePopup`.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-24 | Initial doc: keyboard/mouse HUD input — authoritative key table, F1 legend, Space skip-unit, D disband+confirm, arrow/right-drag pan, Ctrl+C centre, right-click tile menu | _local_ |
