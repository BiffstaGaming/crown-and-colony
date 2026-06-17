# System: Save / Load UI (slot dialog)

| | |
|---|---|
| **Status** | Implemented (Slice F) |
| **Last verified** | 2026-06-17 @ 4b71ede |
| **Code** | `game/presentation/SaveLoadDialog.cs`, `game/scenes/SaveLoadDialog.tscn`; `game/presentation/GameController.cs` (`SaveTo`/`LoadFrom`/`PendingLoadPath`) |
| **Tests** | `game/presentation/tests/SaveLoadTests.cs` (L3) |
| **FreeCol reference** | conceptual (load/save game dialog) |
| **Related systems** | [save-load.md](save-load.md) (the save **format** it reads/writes), [main-menu.md](main-menu.md), [pause-menu.md](pause-menu.md), [info-popup.md](info-popup.md) (confirmations) |

## 1. How it works (plain English)

*Audience: anyone — no jargon.*

A **save-slot dialog** lets you keep up to five saved games and pick between them. You reach it from the main menu's **Load Game** button and from the in-game pause menu's **Save Game** / **Load Game**.

**The rules, in plain words:**
- There are **five slots**. Each shows either "empty" or which turn its save is on.
- **Save** writes the current game into the slot you pick (overwriting whatever was there).
- **Load** starts the game in the slot you pick. From the main menu it boots straight into that game; from the pause menu it replaces the game you're in.
- Empty slots can't be loaded (they're greyed out in Load mode).

**Worked example:**
> Mid-game you press Esc → **Save Game**, pick **Slot 2**; a popup confirms "Your game has been saved." Days later you launch the game, click **Load Game** on the title screen, pick **Slot 2 — Turn 47**, and you're back exactly where you left off.

**What the player sees and does:** a dialog titled Save Game or Load Game, five slot buttons + Back; picking a slot saves/loads and an info popup confirms a save.

## 2. Detailed rules

*Audience: designers/testers.*

| Input / condition | Result |
|---|---|
| Open in **Load** mode | Lists 5 slots; each filled slot shows `Turn N`; empty slots are disabled |
| Open in **Save** mode | Lists 5 slots; all are choosable (an existing save is overwritten) |
| Choose a slot | Calls the host's action for that slot's path, then closes the dialog |
| **Back** | Closes without choosing |
| Main-menu **Load** → choose | Sets `GameController.PendingLoadPath` and switches to the game scene, which boots from that save |
| Pause **Save** → choose | `GameController.SaveTo(path)`, then an info popup "Your game has been saved." (game stays paused) |
| Pause **Load** → choose | `GameController.LoadFrom(path)`, unpauses, info popup "Your saved game has been loaded." |

- Slots are `user://saves/slot1.json` … `slot5.json` (directory created on first save).
- A slot whose file can't be parsed shows "(unreadable)" but is still selectable in Save mode (it will be overwritten).

**Deviations from original 1994 / FreeCol behavior:** five fixed slots rather than free-form named saves (a naming/▸more-slots pass can come later). No overwrite confirmation yet.

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Domain model:** none — presentation-only (ADR-006). It reads/writes the existing `SaveGame` format ([save-load.md](save-load.md)); it never changes that format.

**Dialog:** `SaveLoadDialog` (`Control`) builds five slot `Button`s in code from disk (`FileAccess.FileExists` + a cheap `SaveGame.FromJson(...).Turn` for the label). `Open(Mode, Action<string> onChoose)` sets the title/slots and shows it; choosing a slot invokes `onChoose(path)` then emits `Closed`. The host performs the actual save/load (only it has the game / the navigation), keeping the dialog generic.

**Controller hooks (`GameController`):**
- `SaveTo(path)` — `SaveGame.From(game, variant).ToJson()` → file (creates `SavesDir`).
- `LoadFrom(path)` — `SaveGame.FromJson` → `Resolve(variant)` → `Restore` → `StartGame` (mirrors quick-load).
- `static PendingLoadPath` — set by the main menu before the scene change; `_Ready` loads it (and clears it) instead of starting a new game. Static so it survives the scene switch.

**Hosts:** the **main menu** opens the dialog as an overlay (Load mode) → sets `PendingLoadPath` + `ChangeSceneToFile(main)`. The **pause menu** opens it as an `Always`-process overlay in the UI layer (so it works while the tree is paused), saving/loading via the host `GameController` and confirming with an [InfoPopup](info-popup.md).

**Persistence:** five JSON saves under `user://saves/` (plus the existing F5 `user://quicksave.json`).

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | n/a (no game logic) | — | — |
| L2 Scenario | n/a | — | — |
| L3 Interaction | Yes (has UI) | `SaveLoadTests` — lists 5 slots; choosing a filled slot invokes the callback + closes; Back closes; `SaveTo`→`LoadFrom` round-trips the turn; `PendingLoadPath` boots a saved game | ✅ |
| L4 Visual | Deferred | golden depends on on-disk slot state (non-deterministic) — revisit with a fixed fixture | ⬜ |
| L5 Soak | Covered by global suite | — | — |

## 5. Open issues / TODO

- [ ] **Overwrite confirmation** (a yes/no confirm dialog) before replacing a filled slot in Save mode.
- [ ] Free-form **named saves** and/or more slots; show a timestamp alongside the turn.
- [ ] **L4 golden** with a deterministic slot fixture (clear/seed the saves dir).
- [ ] Surface the existing F5 quicksave as a slot, and add Save/Load to other entry points as they appear.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-17 | Slice F — five-slot save/load dialog; menu Load Game + pause Save/Load wired; `GameController.SaveTo`/`LoadFrom`/`PendingLoadPath` | 4b71ede |
