# System: Save / Load UI (slot dialog)

| | |
|---|---|
| **Status** | Implemented (Slice F; + autosave + slot delete/overwrite/timestamp, `86d3f0vb8`/`86d3f0vkg`) |
| **Last verified** | 2026-06-24 @ autosave + slot UX (`86d3f0vb8`, `86d3f0vkg`) |
| **Code** | `game/presentation/SaveLoadDialog.cs`, `game/scenes/SaveLoadDialog.tscn`; `game/presentation/GameController.cs` (`SaveTo`/`LoadFrom`/`PendingLoadPath`, `AutosavePath`, `MaybeAutosave`) |
| **Tests** | `game/presentation/tests/SaveLoadTests.cs` (L3); autosave-period field in `game/tests/GameLogic.Tests/App/SettingsModelTests.cs` (L1) |
| **FreeCol reference** | conceptual (load/save game dialog); autosave ≈ `ClientOptions.AUTOSAVE_PERIOD`; overwrite confirm ≈ `CONFIRM_SAVE_OVERWRITE` |
| **Related systems** | [save-load.md](save-load.md) (the save **format** it reads/writes), [main-menu.md](main-menu.md), [pause-menu.md](pause-menu.md), [info-popup.md](info-popup.md) (confirmations) |

## 1. How it works (plain English)

*Audience: anyone — no jargon.*

A **save-slot dialog** lets you keep up to five saved games and pick between them. You reach it from the main menu's **Load Game** button and from the in-game pause menu's **Save Game** / **Load Game**.

**The rules, in plain words:**
- There are **five slots**. Each shows either "empty" or which turn its save is on **and when you saved it** (date + time).
- **Save** writes the current game into the slot you pick. If that slot already holds a save, the game **asks you to confirm** before overwriting it — Cancel leaves it untouched.
- **Load** starts the game in the slot you pick. From the main menu it boots straight into that game; from the pause menu it replaces the game you're in.
- Empty slots can't be loaded (they're greyed out in Load mode).
- A filled slot has a **Delete** button next to it; pressing it (and confirming) removes that save and frees the slot.
- The game also keeps a separate **Autosave** entry (see below). It appears in the list once it exists, you can **load** it like any slot, but a manual **Save** can never overwrite it and it has no Delete button here (the game manages it).

**Autosave:** by default the game quietly saves itself at the end of **every** turn into its own Autosave slot, so you always have a recent save to fall back on even if you forget to save manually. You can change how often this happens — or turn it off — in **Settings → Game → "Autosave every N turns (0 = off)"**. With it set to, say, 5, the game autosaves at the end of turns 5, 10, 15, and so on.

**Worked example:**
> Mid-game you press Esc → **Save Game**, pick **Slot 2 — Turn 47 (2026-06-24 21:03)**; because that slot already had a save, the game asks "Overwrite it?" — you confirm and a popup confirms "Your game has been saved." Later you decide Slot 3 is junk, press its **Delete**, confirm, and the slot reads "empty" again. The next day you launch the game, click **Load Game**, and pick the **Autosave** entry to resume from the last turn you played.

**What the player sees and does:** a dialog titled Save Game or Load Game; five slot rows (each with a Delete button when filled) plus an Autosave row when one exists, and Back; picking a slot saves/loads (with an overwrite confirm when needed) and an info popup confirms a save.

## 2. Detailed rules

*Audience: designers/testers.*

| Input / condition | Result |
|---|---|
| Open in **Load** mode | Lists 5 slots (+ the Autosave entry if it exists); each filled slot shows `Turn N  (yyyy-MM-dd HH:mm)`; empty slots are disabled |
| Open in **Save** mode | Lists 5 slots (+ the Autosave entry); the 5 manual slots are all choosable; the **Autosave entry is disabled** (a manual save can't target it) |
| Choose an **empty** slot (Save) | Calls the host's action for that slot's path, then closes the dialog |
| Choose a **filled** slot (Save) | Pops a yes/no **"Overwrite save?"** confirm; **OK** → save + close; **Cancel** → no-op, dialog stays open |
| Choose a slot (Load) | Calls the host's action for that slot's path, then closes the dialog |
| **Delete** (on a filled, deletable slot) | Pops a yes/no **"Delete save?"** confirm; **OK** → removes the file + rebuilds the list (the row becomes "empty"); **Cancel** → no-op |
| **Back** | Closes without choosing |
| Main-menu **Load** → choose | Sets `GameController.PendingLoadPath` and switches to the game scene, which boots from that save |
| Pause **Save** → choose | `GameController.SaveTo(path)`, then an info popup "Your game has been saved." (game stays paused) |
| Pause **Load** → choose | `GameController.LoadFrom(path)`, unpauses, info popup "Your saved game has been loaded." |
| End of a player turn | If `SettingsModel.AutosavePeriod` (the "Autosave every N turns" option) is `> 0` and `Turn % N == 0`, `GameController.MaybeAutosave` writes `user://saves/autosave.json` via `SaveTo` |

- Manual slots are `user://saves/slot1.json` … `slot5.json`; the autosave is `user://saves/autosave.json` (directory created on first save).
- The saved date/time is read from the file's modification time (`FileAccess.GetModifiedTime`) — **no save-header/format change**. If the engine reports no timestamp (0, e.g. some CI virtual filesystems) the suffix is omitted.
- A slot whose file can't be parsed shows "(unreadable)" but is still selectable in Save mode (it will be overwritten, after the confirm).
- **Autosave is a distinct entry**, never a manual-save target and not deletable from this dialog, so manual saves and the autosave never clobber each other.

**Deviations from original 1994 / FreeCol behavior:** five fixed manual slots + one autosave, rather than free-form named saves (a naming/▸more-slots pass can come later). Overwrite confirmation, per-slot delete, autosave, and a saved timestamp are now implemented (FreeCol `CONFIRM_SAVE_OVERWRITE` / `AUTOSAVE_PERIOD`).

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Domain model:** none — presentation-only (ADR-006). It reads/writes the existing `SaveGame` format ([save-load.md](save-load.md)); it never changes that format.

**Dialog:** `SaveLoadDialog` (`Control`) builds slot **rows** in code from disk. Each row is an `HBoxContainer` (named `SlotRow`) whose first child is the load/save `Button` (named `SlotButton`, expand-fill); a filled, deletable slot also gets a `Delete` `Button` (named `DeleteButton`). The label is `<name> — Turn N  (yyyy-MM-dd HH:mm)` — the turn from a cheap `SaveGame.FromJson(...).Turn`, the timestamp from `FileAccess.GetModifiedTime` (no save-format change). `BuildSlots()` lays out the five manual slots, then appends the **Autosave** row iff `autosave.json` exists. `Open(Mode, Action<string> onChoose)` sets the title/slots and shows it.
- **Choosing:** an empty slot (Save) or any filled slot (Load) calls `onChoose(path)` then emits `Closed`. Saving over a **filled** manual slot first pops a `ConfirmationDialog` ("Overwrite save?"); only **OK** proceeds to `onChoose`.
- **Delete:** pops a `ConfirmationDialog` ("Delete save?"); **OK** → `DirAccess.RemoveAbsolute(path)` then `BuildSlots()` rebuilds the list in place.
- **Autosave row:** read-only here — its button is disabled in Save mode (`choosableWhenFilled: _mode == Mode.Load`), and it carries no Delete button (`deletable: false`).
- Both confirms reuse a shared `Confirm(...)` helper that adds an `Always`-process `ConfirmationDialog` as a child so it works while the tree is paused behind the dialog; Cancel is a pure no-op.

**Controller hooks (`GameController`):**
- `SaveTo(path)` — `SaveGame.From(game, variant).ToJson()` → file (creates `SavesDir`). Unchanged; reused by autosave.
- `LoadFrom(path)` — `SaveGame.FromJson` → `Resolve(variant)` → `Restore` → `StartGame` (mirrors quick-load).
- `static PendingLoadPath` — set by the main menu before the scene change; `_Ready` loads it (and clears it) instead of starting a new game. Static so it survives the scene switch.
- `static AutosavePath` (`user://saves/autosave.json`) + `MaybeAutosave()` — called at the end of `OnEndTurnPressed`. It reads the live `SettingsModel.AutosavePeriod` from the `/root/Settings` autoload (absent → period 0 → skip) and, when `period > 0 && Turn % period == 0`, calls `SaveTo(AutosavePath)`. No new save format — it writes the existing format to a dedicated file.

**Hosts:** the **main menu** opens the dialog as an overlay (Load mode) → sets `PendingLoadPath` + `ChangeSceneToFile(main)`. The **pause menu** opens it as an `Always`-process overlay in the UI layer (so it works while the tree is paused), saving/loading via the host `GameController` and confirming with an [InfoPopup](info-popup.md).

**Persistence:** five manual JSON saves + one autosave under `user://saves/` (plus the existing F5 `user://quicksave.json`). The "autosave every N turns" preference lives in `user://settings.cfg` via `SettingsModel.AutosavePeriod` (see [settings.md](settings.md)) — not in any save file.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Yes (autosave-period field is game logic) | `SettingsModelTests` — `AutosavePeriod` default (1), clamp `[0,100]`, dictionary round-trip, garbage-safe parse | ✅ |
| L2 Scenario | n/a | — | — |
| L3 Interaction | Yes (has UI) | `SaveLoadTests` — lists 5 manual slots; autosave appears as a 6th entry; autosave not a Save target; choosing a filled slot invokes the callback + closes; **delete** removes the file + rebuilds the row; **overwrite** confirm-cancel is a no-op / confirm chooses; ending a turn writes the autosave; Back closes; `SaveTo`→`LoadFrom` round-trips the turn; `PendingLoadPath` boots a saved game | ✅ |
| L4 Visual | Deferred (dialog) | dialog golden depends on on-disk slot state (non-deterministic) — revisit with a fixed fixture. The Settings screen's new "Game / Autosave" row is covered by the regenerated `settings-screen` golden (see [settings.md](settings.md)) | ⬜ |
| L5 Soak | Covered by global suite | — | — |

## 5. Open issues / TODO

- [x] **Overwrite confirmation** (a yes/no confirm dialog) before replacing a filled slot in Save mode (`86d3f0vkg`).
- [x] **Per-slot delete** + **saved timestamp** alongside the turn (`86d3f0vkg`).
- [x] **Autosave** at end of turn, gated by a Settings option (`86d3f0vb8`).
- [ ] Free-form **named saves** and/or more slots.
- [ ] **L4 golden** for the dialog itself with a deterministic slot fixture (clear/seed the saves dir).
- [ ] Surface the existing F5 quicksave as a slot, and add Save/Load to other entry points as they appear.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-17 | Slice F — five-slot save/load dialog; menu Load Game + pause Save/Load wired; `GameController.SaveTo`/`LoadFrom`/`PendingLoadPath` | 4b71ede |
| 2026-06-24 | **Autosave** (`86d3f0vb8`): `GameController.AutosavePath` + `MaybeAutosave` write `user://saves/autosave.json` at turn end, gated by the new `SettingsModel.AutosavePeriod` option (default 1, 0 = off); the dialog lists it as a distinct, load-only entry manual saves never overwrite. No save-format bump. | _local_ |
| 2026-06-24 | **Slot UX** (`86d3f0vkg`): per-slot **Delete** (with confirm), **overwrite confirmation** when saving over a filled slot (cancellable), and a **saved date/time** in the label read from `FileAccess.GetModifiedTime` — no save-header change. Slots are now `HBoxContainer` rows. | _local_ |
