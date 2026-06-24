# System: About / Version / License screen

| | |
|---|---|
| **Status** | In development (added 2026-06-24) |
| **Last verified** | 2026-06-24 @ menus (`86d3f0w2x`/`86d3f0vy8`) |
| **Code** | `game/presentation/AboutPanel.cs`, `game/scenes/AboutPanel.tscn`; version source `game/src/GameLogic/App/AppInfo.cs` |
| **Tests** | `game/presentation/tests/AboutPanelTests.cs` (L3), `MenuGoldenTests.AboutPanel_MatchesGolden` (L4), `game/tests/GameLogic.Tests/App/AppInfoTests.cs` (L1) |
| **FreeCol reference** | `AboutPanel.java` (about/version/license screen); `FreeCol.java` `FREECOL_VERSION` / `getVersion()` (the version constant) |
| **Related systems** | [main-menu.md](main-menu.md) (opens it), [pause-menu.md](pause-menu.md) (opens it), [settings.md](settings.md) (the overlay skin it copies) |

## 1. How it works (plain English)

*Audience: anyone — no jargon.*

The **About** screen tells you which version of the game you're running and who it belongs to. You can open it from the title screen or from the in-game pause menu; it's purely informational — there's nothing to change, just a **Back** button to close it.

**The rules, in plain words:**
- It shows the game's name (**Crown & Colony**) and its **version number** (e.g. *Version 0.1.0*).
- It shows a short notice that the game is free software under the **GPL v2** licence, pointing you at the bundled `LICENSE` file (the full terms) and `CREDITS.md` (who made the art, music and data).
- It shows a copyright line, a link to the public **source code** repository, and a pointer to the **player manual** (`docs/MANUAL.md`).
- **Back** closes it and returns you to whichever menu opened it.

**Worked example:**
> From the title screen you click **About**. A parchment panel appears reading "Crown & Colony / Version 0.1.0", a paragraph saying the game is GPL-v2 free software (see LICENSE and CREDITS.md), a copyright line, the GitHub link, and "Player manual: see docs/MANUAL.md". You click **Back** and you're at the title screen again. The same screen is reachable mid-game from **Esc → About**.

**What the player sees and does:** one read-only panel — name, version, licence/credits notice, copyright, repo link, manual pointer — and a Back button.

## 2. Detailed rules

*Audience: designers/testers.*

| Input / condition | Result |
|---|---|
| Main menu **About** clicked | `AboutPanel` added as an overlay over the title screen |
| Pause menu **About** clicked | `AboutPanel` added as an overlay over the paused game (game stays paused) |
| Panel shown | Title = `AppInfo.Name`; "Version " + `AppInfo.Version`; body = GPL v2 disclaimer (→ `LICENSE` + `CREDITS.md`) + copyright + `AppInfo.RepositoryUrl` + `docs/MANUAL.md` pointer |
| **Back** | Emits `Closed`; the host frees the overlay |

**Deviations from original 1994 / FreeCol behavior:** FreeCol's `AboutPanel` shows a logo, the version/revision, the site URL and a legal disclaimer; ours is the same idea adapted to this project's licence (GPL v2) and links. No gameplay effect.

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Version source (`GameLogic.App.AppInfo`):** engine-free (ADR-006), so the version is L1-testable. `AppInfo.Version` reads this assembly's `AssemblyInformationalVersionAttribute` (set from `<Version>`/`<InformationalVersion>` in `GameLogic.csproj`), stripping any `+build` metadata suffix, and falls back to the file/assembly version then `0.0.0` — so it is **never empty** and is always semver-shaped. `AppInfo.Name` and `AppInfo.RepositoryUrl` are constants. This is the **application/release** version (FreeCol `FreeCol.getVersion()`) and is deliberately **separate** from the **save-format** version in `SaveGame.cs`, which tracks on-disk compatibility and changes on its own cadence. The csproj version is duplicated in `CrownAndColony.csproj` so the Godot assembly carries the same metadata; `AppInfo` reads the GameLogic assembly's value.

**Panel (`AboutPanel` / `AboutPanel.tscn`):** a `Control` overlay that copies `SettingsScreen`'s composition — `Background` (FreeCol map), `Vignette`, a centre-anchored `Panel` (parchment skin + `Border` NinePatch) holding a `VBox` of `Title` (`ColonyTitle` variation), `Version` label, a separator, a `Body` `RichTextLabel` (bbcode, scrollable), a spacer, and the `BackButton`. `_Ready` applies `ColonyTheme` + the parchment/border art, then **overwrites** the authored placeholder text with `AppInfo.Name`, `"Version {AppInfo.Version}"`, and the composed body, and wires Back → `Closed`. Presentation-only — reads `AppInfo` only, owns no game state.

**Hosting:** both menus instantiate `AboutScenePath` (`res://scenes/AboutPanel.tscn`) and free it on `Closed`. `MainMenu.OnAbout` adds it as a child of the menu. `PauseMenu.OnAbout` routes through `TrackOverlay` (UI layer, `ProcessMode.Always`, tracked as the single `_overlay`) so it works while the tree is paused and parks Esc until it closes.

**Integration points:** `MainMenu.tscn` `Panel/VBox/AboutButton`; `main.tscn` `UI/PauseMenu/Panel/VBox/AboutButton`. No persistence (read-only). No save change.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Yes (version accessor) | `AppInfoTests` — `Version` is a non-empty semver-shaped string; `Name`/`RepositoryUrl` present | ✅ |
| L2 Scenario | n/a (no game logic) | — | — |
| L3 Interaction | Yes (has UI) | `AboutPanelTests` — shows name/version + GPL v2 / LICENSE / CREDITS.md / repo / manual text; Back emits `Closed`. `MainMenuTests`/`PauseMenuTests` — the About button opens the overlay; pause-menu Back closes it | ✅ |
| L4 Visual | Yes (has a screen) | `about-panel` golden (`MenuGoldenTests`) | ✅ |
| L5 Soak | Covered by global suite | — | — |

## 5. Open issues / TODO

- [ ] `docs/MANUAL.md` is referenced but authored by a sibling stream — confirm the link target once it lands.
- [ ] Optionally make the repo link and the LICENSE/CREDITS pointers clickable (open the file/URL) rather than plain text.
- [ ] Optionally let Esc close the About overlay (currently only its Back button does — same as Settings).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-24 | New About / version / license screen (`AboutPanel` + `AboutPanel.tscn`), reachable from both menus; backed by the new `AppInfo` version accessor (`0.1.0`). L1 `AppInfoTests`, L3 `AboutPanelTests` + menu open/close, L4 `about-panel` golden | menus (`86d3f0w2x`/`86d3f0vy8`) |
