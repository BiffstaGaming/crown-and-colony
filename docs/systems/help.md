# System: In-game help / tutorial screen

| | |
|---|---|
| **Status** | In development (added 2026-06-24) |
| **Last verified** | 2026-06-24 @ help (`86d3e98db`) |
| **Code** | `game/presentation/HelpPanel.cs`, `game/scenes/HelpPanel.tscn`; hosts `game/presentation/MainMenu.cs` (`OnHelp`) + `game/presentation/PauseMenu.cs` (`OnHelp`); content derived from `docs/MANUAL.md` and the key table in `game/presentation/GameController.cs` (`BuildKeyBindings`) |
| **Tests** | `game/presentation/tests/HelpPanelTests.cs` (L3), `MainMenuTests.HelpButton_*` / `PauseMenuTests.HelpButton_*` (L3), `MenuGoldenTests.HelpPanel_MatchesGolden` (L4) |
| **FreeCol reference** | FreeCol's tutorial + contextual help (a guided first game + per-panel help text). Ours is a single static help/reference screen for the classic single-player game — no step-by-step tutorial yet |
| **Related systems** | [main-menu.md](main-menu.md) (opens it), [pause-menu.md](pause-menu.md) (opens it), [about.md](about.md) (the overlay skin it copies), [hud-input.md](hud-input.md) (the authoritative key table its controls section restates) |

## 1. How it works (plain English)

*Audience: anyone — no jargon.*

The **Help** screen is the in-game guide for new players. You can open it from the title screen or from the in-game pause menu; it's purely informational — there's nothing to change, just a **Back** button to close it (the game stays paused behind it when you open it mid-game).

It's a single scrollable page that covers, in plain words:

- **The goal of the game** — lead a European power settling the New World, grow colonies, and ultimately win independence.
- **The core gameplay loops** — *explore* the map, *found and run colonies*, build an *economy and trade with Europe*, win *liberty and declare independence*, deal with the *native nations*, and fight when it comes to *combat*.
- **A controls / keybindings reference** — how the mouse drives the game, how to pan and zoom, and every keyboard shortcut (Enter to end the turn, Space to skip a unit, B to build a colony, E for Europe, and so on), ending with a pointer to the full written manual.

**Worked example:**
> From the title screen you click **Help**. A parchment panel appears with headings for the goal, exploring, colonies, the economy, liberty, natives and combat, then a "Controls and keybindings" list. You scroll to the bottom, read which keys do what, and click **Back** to return to the title screen. The same screen is reachable mid-game from **Esc → Help**, and the game stays paused while you read.

**What the player sees and does:** one read-only, scrollable panel — the goal, the core loops, and a key reference — and a Back button.

## 2. Detailed rules

*Audience: designers/testers.*

| Input / condition | Result |
|---|---|
| Main menu **Help** clicked | `HelpPanel` added as an overlay over the title screen |
| Pause menu **Help** clicked | `HelpPanel` added as an overlay over the paused game (game stays paused) |
| Panel shown | Title = "Help"; body = goal + the core loops (explore / colonies / economy / liberty & independence / natives / combat) + a controls reference + a `docs/MANUAL.md` pointer |
| **Back** | Emits `Closed`; the host frees the overlay |
| **Esc** while the help overlay is up (pause-menu host) | Parked by the pause menu's overlay tracking — Esc can't dismiss the pause menu out from under the help overlay; use its **Back** button |

**Content accuracy (binding):** the **controls** section restates the *same key→action facts* as the authoritative key table in `GameController.BuildKeyBindings` (and the F1 in-game legend). The two are not auto-generated from one source — the help text is hand-written prose — so they must be kept in step when a key changes. The L3 test `HelpPanelTests.HelpPanel_ControlsReference_MatchesTheKeyTable` guards the listed keys/actions against silent drift.

**Deviations from original 1994 / FreeCol behavior:** FreeCol ships a *guided tutorial* (a scripted first game with contextual prompts) plus per-panel contextual help. Ours is a single static help/reference screen — the goal, the core loops, and a controls reference — appropriate to the classic single-player game. A step-by-step interactive tutorial is a possible later addition (see Open issues). No gameplay effect.

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Panel (`HelpPanel` / `HelpPanel.tscn`):** a `Control` overlay that copies `AboutPanel`'s composition — `Background` (FreeCol map), `Vignette`, a centre-anchored `Panel` (parchment skin + `Border` NinePatch) holding a `VBox` of `Title` (`ColonyTitle` variation), a separator, a scrollable `Body` `RichTextLabel` (bbcode), and the `BackButton`. The panel is larger than About's (640×560 vs 520×480) to fit the longer guide; the body has `scroll_active = true` so the content scrolls. `_Ready` applies `ColonyTheme` + the parchment/border art, then **overwrites** the authored placeholder body with the composed help text (`BuildBody`), and wires Back → `Closed`. Presentation-only (ADR-006) — owns no game state, reads nothing but its own static text.

**Content source:** `BuildBody()` is hand-written original prose, condensed from `docs/MANUAL.md` (sections 1–10) for the goal + loops, and its controls list restates `GameController.BuildKeyBindings`. Kept in code (not a data file) so it ships with the panel and is trivially L3-assertable. License-clean: original wording, no text copied from the 1994 manual or FreeCol.

**Hosting:** both menus instantiate `HelpScenePath` (`res://scenes/HelpPanel.tscn`) and free it on `Closed`. `MainMenu.OnHelp` adds it as a child of the menu. `PauseMenu.OnHelp` routes through `TrackOverlay` (UI layer, `ProcessMode.Always`, tracked as the single `_overlay`) so it works while the tree is paused and parks Esc until it closes — identical to the About overlay path. No hotkey is bound (F1 remains the in-game keys legend; the pause/main menus are the entry points), so `GameController._UnhandledInput` is untouched.

**Integration points:** `MainMenu.tscn` `Panel/VBox/HelpButton` (between Settings and About; the panel/border grew to −275/+275 to fit the 6th button); `main.tscn` `UI/PauseMenu/Panel/VBox/HelpButton` (between Settings and About; the pause panel/border grew to −258/+258 to fit the 8th button). No persistence (read-only). **No save change.**

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | n/a (no game logic) | — | — |
| L2 Scenario | n/a (no game logic) | — | — |
| L3 Interaction | Yes (has UI) | `HelpPanelTests` — shows goal/loops/controls + manual pointer; controls reference matches the key table; Back emits `Closed`. `MainMenuTests.HelpButton_OpensTheHelpScreenAsAnOverlay`; `PauseMenuTests.HelpButton_OpensTheHelpOverlay_BackClosesIt` | ✅ local (28/28 menu+help L3 green) |
| L4 Visual | Yes (has a screen) | `help-panel` golden (`MenuGoldenTests.HelpPanel_MatchesGolden`); `main-menu`/`pause-menu`/`info-popup` goldens shift with the new Help button | ⏳ CI regen (see below) |
| L5 Soak | Covered by global suite | — | — |

- **L1/L2 green** locally (full `GameLogic.Tests` 2173/2173 — no regression; help is presentation-only).
- **L3 green** locally via the Godot runtime: HelpPanel ×3 + the menu open/close tests pass (28/28 across HelpPanel/MainMenu/PauseMenu suites).
- **L4 — CI (Linux) regeneration required.** The committed menu goldens are Linux-rendered (CI); on this local Windows machine a fresh render of even untouched screens (e.g. `settings-screen`) differs ~20–26% from the committed golden (font/GPU rasterisation), so locally-rendered goldens are **not** committed (they would break the Linux CI gate). The `help-panel` golden test is in place; the new `help-panel` golden and the regenerated `main-menu`/`pause-menu`/`info-popup` goldens (which legitimately change — the Help button shifts those scenes) must be regenerated on CI with `GOLDEN_UPDATE=1` and committed from that environment. This mirrors how the About-screen goldens were produced.

## 5. Open issues / TODO

- [ ] **L4 goldens** for `help-panel` (new) + `main-menu` / `pause-menu` / `info-popup` (shifted by the Help button) need regenerating on the Linux CI environment and committing — not reproducible on local Windows.
- [ ] The controls list is hand-mirrored from `BuildKeyBindings` (guarded by an L3 string test, not auto-generated). If the key table grows often, consider generating the help controls section from the same list (as the F1 legend is) to remove the manual step.
- [ ] A true step-by-step **interactive tutorial** (FreeCol-style guided first game) is not implemented — this is a static help/reference screen only.
- [ ] Optionally let **Esc** close the Help overlay directly (currently only its Back button does — same as About/Settings).
- [ ] Optionally make the `docs/MANUAL.md` pointer open the manual rather than being plain text.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-24 | New in-game **Help** screen (`HelpPanel` + `HelpPanel.tscn`), reachable from a **Help** button on **both** the main menu and the pause menu: covers the goal of the game, the core loops (explore → colonies → economy/Europe → liberty/independence → natives → combat), and a controls/keybindings reference (mirrors `GameController.BuildKeyBindings` / the F1 legend). Original wording derived from `docs/MANUAL.md`; presentation-only (ADR-006), no save change. L3 `HelpPanelTests` ×3 + menu open/close ×2; L4 `help-panel` golden test added (CI regen pending). | help (`86d3e98db`) |
