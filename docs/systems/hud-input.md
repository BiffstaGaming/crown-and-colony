# System: HUD input (keyboard & mouse)

| | |
|---|---|
| **Status** | Implemented (left-click select/move; keyboard hotkeys via one authoritative table of **named, rebindable `InputMap` actions**; in-game key-rebinding screen reachable from Settings; F1 keys legend; Space skip-unit; D disband + confirm; arrow-key & right/middle-drag camera pan; Ctrl+C centre; right-click tile context menu) |
| **Last verified** | 2026-06-24 @ keybinding remap — named `InputMap` actions + rebind screen (`86d3f0wjj`); prior HUD-input set `86d3f0vgd`/`86d3f0vjg`/`86d3f0vuy`/`86d3f0vqf`/`86d3f0vrz` |
| **Code** | `game/presentation/GameController.cs` (`_UnhandledInput`, `KeyBindings`/`BuildKeyBindings` — now named action ids, `BuildKeysLegendText`/`KeyChordsFor`, `SkipSelectedUnit`, `DisbandSelectedUnit`, `CenterOnSelectedUnit`, `OpenSaveDialog`/`OpenLoadDialog`, `ToggleKeysLegend`, `HandleRightClick`, `IsTextInputFocused`, `IsDuplicateKeyDown`), `game/project.godot` (`[input]` action defaults), `game/src/GameLogic/App/KeyBindingsModel.cs` (engine-free action list + overrides), `game/presentation/KeyBindingsService.cs` (`InputMap` ↔ `settings.cfg`), `game/presentation/KeyBindingsScreen.cs` + `game/scenes/KeyBindingsScreen.tscn` (rebind UI), `game/presentation/SettingsService.cs` (`LoadAndApply` on boot), `game/presentation/SettingsScreen.cs` (entry button), `game/presentation/CameraController.cs` (arrow-key pan, right-drag-vs-click), `game/presentation/PauseMenu.cs` (`OpenSave`/`OpenLoad`), `game/src/GameLogic/GameSession/Game.Goto.cs` (`NextUnitToMove` skip param) |
| **Tests** | `game/presentation/tests/InputTests.cs` (L3, incl. `RebindingEndTurnToAnotherKey_*`), `game/presentation/tests/SettingsScreenTests.cs` (L3 rebind screen), `game/tests/GameLogic.Tests/App/KeyBindingsModelTests.cs` (L1), `game/tests/GameLogic.Tests/GameSession/GotoTests.cs` (`NextUnitToMove_SkipSet_*`, L1) |
| **FreeCol reference** | `freecol/src/net/sf/freecol/client/gui/action/` — `DisbandUnitAction` (D), `EndTurnAction` (ENTER), `SaveAction` (Ctrl+S) / `OpenAction` (Ctrl+O), `SkipUnitAction` (SPACE), `CenterAction` (Ctrl+C); `freecol/src/net/sf/freecol/client/gui/panel/TilePopup.java` (right-click tile menu) |
| **Related systems** | [units-movement](units-movement.md), [save-load-ui](save-load-ui.md), [pause-menu](pause-menu.md) |

## 1. How it works (plain English)

The map is driven with the mouse and a handful of keyboard shortcuts. **Left-click** a tile to select your unit (or move/attack with one selected); **left-click your colony** to open it. **Right-click** a tile opens a small menu: pick any one of your units standing there (handy when several are stacked on one square — a left-click would only grab the first), centre the view there, or send the selected unit there ("go to").

The view itself pans by **dragging with the right or middle mouse button**, by spinning the **mouse wheel** to zoom, or by holding the **arrow keys** to slide the camera around (it keeps moving while you hold them, and feels the same speed however far you've zoomed in or out). Press **Ctrl+C** to snap the camera back onto the unit you've got selected.

For orders and game control there's a shortcut for everything, and you never have to remember them: press **F1** to pop up a legend listing every key and what it does. The important ones: **Enter** ends the turn, **Space** skips the current unit (you'll deal with it later this turn — see [units-movement](units-movement.md)), **W** jumps to the next unit that still needs orders, **D** disbands the selected unit (it asks you to confirm first, because it's gone for good), **Ctrl+S** / **Ctrl+O** save and load, and **B / E / L / F / C** open build-colony / Europe / find-settlement / founding-fathers / the Colopedia. While you're typing into a text box (a save-slot name, a search field) the shortcuts stand down so your letters don't trigger actions.

**Don't like a key? Change it.** Open **Settings → Key Bindings…** for a list of every shortcut and what it's currently set to. Click **Rebind** next to one and press the key you'd rather use — it takes effect straight away. **Reset** puts a single shortcut back to its original; **Reset all to defaults** restores the whole list. Your changes are saved and remembered the next time you play, and the F1 legend always shows your current keys (not the originals). The rebinding lives with your other options, separate from your saved games — so changing a key never touches a save.

**What the player sees and does:** the map and HUD; the selected-unit panel grows a **Skip** and a **Disband** button (Disband only lights up when the unit can actually be disbanded); a confirmation box appears before a disband; an F1 legend overlay toggles on the right of the screen.

## 2. Detailed rules

**Authoritative key table** — both the keypress dispatch and the F1 legend are generated from one list (`GameController.BuildKeyBindings`), so a key and its on-screen label can never drift apart. Each row is a **named `InputMap` action** (defined in `project.godot` `[input]`); the **Default key** column is the shipped default, but every action is **rebindable** (Settings → Key Bindings), so the live key may differ. The action id (left column) is the stable `InputMap` / persistence name.

| Action id | Default key(s) | Action | Notes |
|---|---|---|---|
| `end_turn` | Enter / Keypad-Enter | End turn | Same path as the End-Turn button (`OnEndTurnPressed`); disabled-state honoured via that path |
| `skip_unit` | Space | Skip unit this turn | Flags the selected unit skipped (session-only set), then cycles to the next unit needing orders |
| `next_unit` | W | Next unit needing orders | `SelectNextUnitToMove`; skips units flagged by Space until turn rollover |
| `goto_mode` | G | Go to (arm destination) | Next click sets the selected unit's standing destination |
| `build_colony` | B | Build colony | |
| `disband_unit` | D | Disband unit | Opens a confirmation dialog; gated on `CheckDisband` |
| `open_europe` / `find_settlement` / `founding_fathers` / `colopedia` | E / L / F / C | Europe / Find settlement / Founding fathers / Colopedia | |
| `center_unit` | Ctrl+C | Centre camera on selected unit | Reuses `CenterCameraOnTile` |
| `new_map` | N | New map | |
| `save_game` / `load_game` | Ctrl+S / Ctrl+O | Save / Load (named-slot dialog) | Pauses the game behind the dialog (the PauseMenu path); unpauses when it closes |
| `quick_save` / `quick_load` | F5 / F9 | Quick save / Quick load | Unchanged; the single-file `user://quicksave.json` |
| `toggle_legend` | F1 | Toggle keys legend | The on-screen list, generated from this table + the live `InputMap` |

- A hotkey fires only when **no text field owns focus** (`GetViewport().GuiGetFocusOwner()` is not a `LineEdit`/`TextEdit`) — so typing into a slot/search field never triggers an action.
- A hotkey requires its **exact** modifier state: dispatch uses `InputEvent.IsActionPressed(actionId, exactMatch: true)`, so a plain-key action will not fire while Ctrl is held and a `Ctrl+X` action will not fire without it (e.g. C=Colopedia vs Ctrl+C=centre stay distinct) — the same discipline the old `KeyChord.Matches` enforced.
- **WASD is deliberately not a pan** — W=next-unit and C=colopedia are letter hotkeys; the camera pans on arrows + right/middle drag instead.

**Key rebinding** (`KeyBindingsScreen`, opened from Settings → *Key Bindings…*):

- Each rebindable action shows its current key. **Rebind** captures the next key press (Esc cancels; a lone modifier key is ignored so you can hold Ctrl for a Ctrl-chord). **Reset** returns one action to its default; **Reset all to defaults** clears every override.
- A captured key that collides with another action is **allowed** (the player's choice) but flagged in the hint line — there is no hard block.
- **Persistence:** overrides are written to `user://settings.cfg` under a `[keybindings]` section (key = action id, value = `keycode[,ctrl]`) on **Back**. Only *overridden* actions are stored; an action at its default writes nothing. This is the application-settings file, **not** the game save — rebinding needs **no save-version bump** (ADR-009). On boot, `SettingsService` applies the saved overrides to the `InputMap` before the game scene runs, so a rebound key works immediately and survives restart.

**Right-click tile menu** (`HandleRightClick`, on right-button **release without a drag**):

| Entry | Effect |
|---|---|
| Activate `<unit>` (#id) | One per own on-map unit on the tile (id order) → selects that unit (fixes stack-select) |
| Centre here | Recentres the camera on the tile |
| Go to here | Sets the selected unit's standing destination (no-op with no selection) |

- The right **drag** still pans: `CameraController` tracks whether the right-button motion exceeded a small threshold (4 px). On release it consumes the event only if it actually dragged, so a genuine pan never opens the menu; a drag-free right-click falls through to `GameController` and opens the menu. Middle-mouse is the drag-free pan fallback.

**Deviations from original 1994 / FreeCol behavior:** none of substance. The skip set is a presentation-side, session-only convenience (FreeCol skips per-unit in its own client state); the right-click menu is a trimmed `TilePopup` (activate / centre / go-to only) for the single-player classic game.

## 3. Technical design

**Domain model:** all input lives in presentation (ADR-006); no game rules here. `GameController._UnhandledInput` switches on event type, then dispatches keys through `KeyBindings` (an `IReadOnlyList<KeyBinding>`; each `KeyBinding` = a named `InputMap` action id + the `Action` method + a label). Dispatch is `@event.IsActionPressed(binding.ActionId, exactMatch: true)`, so the raw key→action mapping lives in Godot's `InputMap` (defaults in `project.godot` `[input]`), not in hardcoded keycode comparisons — which is what makes the keys rebindable. `CameraController` owns the view transform only.

**Named actions, defaults, and rebinding:**
- `KeyBindingsModel` (`GameLogic/App`, pure C#, no Godot) is the **engine-free** source of the action list (`Actions`: id + label + default `KeyChord(long Keycode, bool Ctrl)`) and the player's *overrides*. It holds the override map, `ChordFor`/`HasOverride`/`Set`/`Reset`/`ResetAll`, and the `ToDictionary`/`FromDictionary` round-trip (overrides only; an override equal to the default is dropped). It lives in the engine-free library purely for L1 coverage — it is app config, not game rules.
- `KeyBindingsService` (`presentation`, static) bridges the model to Godot: `EventFor(chord)` → `InputEventKey` (logical `Keycode` + `CtrlPressed`, matching the legacy logical-keycode dispatch); `Apply(model)` writes *overridden* actions into the `InputMap` (`ActionEraseEvents` + `ActionAddEvent`), leaving default actions untouched so a multi-event default like End-Turn's Enter + Keypad-Enter survives until deliberately rebound; `Rebind`/`Load`/`Save` for the screen; `Describe(chord)` for the shared key label ("Ctrl+S"/"Enter"/"Space"/…).
- `KeyBindingsScreen` (`Control` overlay, emits `Closed`) is opened from `SettingsScreen`'s *Key Bindings…* button. It builds one row per action, captures the next key in `_Input` while "listening" (swallowing it with `SetInputAsHandled`), applies each rebind live via `KeyBindingsService.Rebind`, and persists on **Back**.
- **Boot apply:** `SettingsService._Ready` calls `KeyBindingsService.LoadAndApply()` (after loading the video/audio settings) so saved overrides reach the `InputMap` before any scene runs.
- **F1 legend regeneration:** `BuildKeysLegendText` reads each action's *current* key(s) from the live `InputMap` (`KeyChordsFor` → `InputMap.ActionGetEvents`, keeping only `InputEventKey`s) and formats them with `KeyBindingsService.Describe`. The label is rebuilt every time the legend is shown, so it can never drift from what dispatch fires — both read the same `InputMap`.

**Skip-unit (session-only):** the controller holds `_skippedThisTurn` (`HashSet<int>` of unit ids), never serialized (ADR-009). `Game.NextUnitToMove(player, skip)` gained an optional `IReadOnlySet<int>? skip` parameter so the "needs orders" predicate stays authoritative in `GameLogic` while the UI excludes skipped ids. The set is cleared at turn rollover (`OnEndTurnPressed`) and on `StartGame` (new/loaded game).

**Save/Load hotkeys:** `Ctrl+S`/`Ctrl+O` call `PauseMenu.OpenSave()`/`OpenLoad()`, which pause the tree and open the existing `SaveLoadDialog` overlay (the same path the pause menu's Save/Load buttons use), then unpause when the dialog closes if the pause menu itself is hidden (i.e. the hotkey entry, not the Esc-menu flow).

**Same-frame key de-duplication:** the key branch ignores a key-down that exactly repeats (same raw keycode + Ctrl, a `(Key, bool)` tuple) one already dispatched on the current `Engine.GetProcessFrames()` (`GameController.IsDuplicateKeyDown`). It keys on the raw keycode, **not** the action id, so it is unaffected by any rebind. A real, single physical press produces exactly one `_UnhandledInput` call, so this is a no-op in a live game; it exists because the L3 GdUnit `SceneRunner` delivers each simulated press *twice* in the same frame (it both pumps the global `Input` pipeline and calls `_unhandled_input` directly). Without the guard a turn-advancing key (Enter) would advance twice and a toggle (F1) would flip straight back to its start. Genuine OS key repeats arrive as `Echo` events (already filtered out by the `Echo: false` pattern), and a deliberate second press lands on a later frame, so neither is affected.

**Integration points:** `_UnhandledInput` ordering — `CameraController` (a child node) receives `_unhandled_input` before the `GameController` root, so its right-release `SetInputAsHandled()` (after a real drag) suppresses the menu. Arrow-key pan is a continuous poll in `CameraController._Process` (reads `Input.IsKeyPressed`), not event-driven, so it doesn't compete with the key-dispatch table.

**Persistence:** skip set, legend visibility, camera position — all session-only. **Key-binding overrides** persist to `user://settings.cfg` (`[keybindings]` section) via `KeyBindingsService.Save` — the application-settings file, *not* the game save (no save-version bump, ADR-009). `KeyBindingsService.Save` rebuilds the section from scratch (erase + repopulate) so a Reset-to-default removes the key from disk, and leaves the `[settings]` video/audio block untouched.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `KeyBindingsModelTests` (defaults, override Set/Reset/clear-on-default, dictionary round-trip, garbage/unknown-id safe — 10/10); `GotoTests.NextUnitToMove_SkipSet_*` (skip-param oracle) | ✅ |
| L2 Scenario | n/a | input is presentation; the underlying commands (Disband/EndTurn/goto) are covered in their own systems; soak byte-identical (settings.cfg only, no save change) | — |
| L3 Interaction | Yes (UI) | `InputTests` (33 — incl. `PressingEnter_EndsTheTurn`, `PressingSpace_*`, `CtrlC_*`, `F1_TogglesTheKeysLegend`, `RightClickTileMenu_*`, `SelectedUnitPanel_DisbandButton_*`, `PressingD_*`, **`RebindingEndTurnToAnotherKey_MakesTheNewKeyEndTheTurn_AndTheLegendUpdates`**); `SettingsScreenTests` (`SettingsScreen_HasAKeyBindingsButton`, `KeyBindingsScreen_ListsActions_*`, `KeyBindingsScreen_CaptureAndBack_PersistsTheOverride_AndReloads`) | ✅ local — all green (`dotnet test … --settings gdunit.runsettings`) |
| L4 Visual | Settings screen golden affected | `settings-screen` (`MenuGoldenTests`) gains a *Key Bindings…* button → **regenerate on CI** (Linux render). No new golden for the rebind screen itself yet. | ⚠ CI-regenerate |
| L5 Soak | Covered by global suite | byte-identical (5/5) — bindings live in `settings.cfg`, never the save | ✅ |

- **L1 green** locally (`KeyBindingsModelTests` 10/10, `NextUnitToMove` skip tests). **L3 verified green locally** on this machine (`InputTests` 33/33 + the new `SettingsScreenTests` rebind cases) under the headless GdUnit runner — the migration to named `InputMap` actions did not regress Enter/Space/W/G/B/D/F1/right-click/Ctrl+C/Ctrl+S/Ctrl+O; the dedup guard and text-focus guard still hold.
- **FreeCol cross-check:** key choices match FreeCol's accelerator actions (D / ENTER / Ctrl+S / Ctrl+O / SPACE / Ctrl+C) and the `TilePopup` concept; the rebinding surface mirrors FreeCol's client key-mapping options conceptually (we ship a minimal in-game remap screen).

## 5. Open issues / TODO

- [x] **Key rebinding** (`86d3f0wjj`): hotkeys are named, rebindable `InputMap` actions with an in-game rebind screen (Settings → Key Bindings); overrides persist to `settings.cfg`.
- [ ] `settings-screen` L4 golden needs CI regeneration (the new *Key Bindings…* button changed the layout). No dedicated golden for `KeyBindingsScreen` yet — add one if its look stabilises.
- [ ] Rebind screen allows (but only flags) a key collision between two actions; a future pass could offer to swap or block.
- [ ] Right-click menu is single-player-classic scope; a multiplayer/observer build may want a richer `TilePopup`.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-24 | **Keybinding remap** (`86d3f0wjj`): migrated the hardcoded hotkeys to named `InputMap` actions (defaults in `project.godot` `[input]`); added `KeyBindingsModel` (L1) + `KeyBindingsService` + a `KeyBindingsScreen` rebind overlay reachable from Settings. A key can be rebound, persists to `settings.cfg` (`[keybindings]`), is applied to the `InputMap` on boot, and the F1 legend regenerates from the live map. Dedup + text-focus guards preserved; soak byte-identical (no save change, ADR-009). | _local_ |
| 2026-06-24 | Key dispatch de-duplicates an identical same-frame key-down (`IsDuplicateKeyDown`), so the L3 runner's double event delivery can't fire a hotkey twice (fixes `PressingEnter` double-advance + `F1` double-toggle). No effect on real single presses. | _local_ |
| 2026-06-24 | Initial doc: keyboard/mouse HUD input — authoritative key table, F1 legend, Space skip-unit, D disband+confirm, arrow/right-drag pan, Ctrl+C centre, right-click tile menu | _local_ |
