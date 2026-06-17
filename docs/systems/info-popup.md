# System: Info Popup (reusable modal)

| | |
|---|---|
| **Status** | Implemented |
| **Last verified** | 2026-06-17 @ (pending) |
| **Code** | `game/presentation/InfoPopup.cs`, `game/scenes/InfoPopup.tscn` |
| **Tests** | `game/presentation/tests/InfoPopupTests.cs` (L3); `MenuGoldenTests` → `info-popup` (L4) |
| **FreeCol reference** | conceptual (information dialog) |
| **Related systems** | [main-menu.md](main-menu.md), [save-load.md](save-load.md) (first consumer) |

## 1. How it works (plain English)

*Audience: anyone — no jargon.*

A small **information popup** the game uses to tell you something one-off — "Game saved", "No saved games", and the like. It appears centred over whatever screen you're on, dims the rest, and waits for you to press **OK**.

**The rules, in plain words:**
- It shows a **title** and a **message**, with a single **OK** button.
- Pressing OK closes it and you're back where you were.

**Worked example:**
> You save your game from the pause menu. A parchment popup appears reading "Saved — Your game has been saved." over the dimmed menu. You press **OK** and it disappears.

**What the player sees and does:** a centred modal (title + message + OK) over a dimmed screen; OK dismisses it.

## 2. Detailed rules

*Audience: designers/testers.*

| Input / condition | Result |
|---|---|
| A screen calls `InfoPopup.Show(host, title, message)` | The popup appears over `host`, dimming it; the message wraps within the panel |
| Click **OK** | The popup emits `Closed` and (via `Show`) frees itself |

- It is **modal**: the dim backdrop blocks input to whatever is beneath until dismissed.
- Single-button only — this is an *information* popup, not a yes/no confirmation (a confirm dialog can come later).

**Deviations from original 1994 / FreeCol behavior:** none of note — a standard UI affordance.

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Domain model:** none — presentation-only (ADR-006).

**Component:** `InfoPopup` (`Control`) = a full-rect dim `ColorRect` + a fixed-size centred parchment `PanelContainer` (title / wrapped message / OK) + the carved-wood `NinePatchRect` border, sharing `ColonyTheme` + `ColonyArt.ParchmentSkin()`. `Configure(title, message)` sets the labels; OK emits the `Closed` signal.

**Usage:** the static `InfoPopup.Show(Node host, string title, string message)` instantiates `InfoPopup.tscn`, adds it as a child of `host`, configures it, and frees it on `Closed` — a one-liner for any screen. Callers may also observe `Closed` on the returned instance.

**Integration points:** none yet wired into game flow; its first consumer is the Save/Load dialog (see [save-load.md](save-load.md)). **Persistence:** none.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | n/a (no game logic) | — | — |
| L2 Scenario | n/a | — | — |
| L3 Interaction | Yes (has UI) | `InfoPopupTests` — `Show` displays the configured title/message; OK emits `Closed` and frees the popup | ✅ |
| L4 Visual | Yes (has a screen) | `info-popup` golden (`MenuGoldenTests`) | ✅ |
| L5 Soak | Covered by global suite | — | — |

## 5. Open issues / TODO

- [ ] A sibling **confirm dialog** (OK / Cancel) for destructive actions (e.g. overwrite/delete a save, quit to desktop) — when needed.
- [ ] Optionally dismiss on Esc/Enter (kept to the OK button for now to avoid clashing with the pause menu's Esc handling).
- [ ] Fixed panel size fits short notices; very long messages would clip — revisit if a long-text use appears.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-17 | Slice E — reusable info popup (title + message + OK) with a static `Show` helper; L3 test + `info-popup` golden | (pending) |
