# Visual tests: menu / UI goldens

| | |
|---|---|
| **Golden files** | `main-menu.png`, `settings-screen.png`, `pause-menu.png`, `info-popup.png` (in `game/tests/visual/goldens/`) |
| **Test** | `game/presentation/tests/MenuGoldenTests.cs` (GdUnit4, runs with the L3 suite) |
| **Compare helper** | `game/presentation/tests/GoldenAssert.cs` (shared with the [map goldens](map-goldens.md)) |
| **Scenes** | `res://scenes/MainMenu.tscn`, `SettingsScreen.tscn`, `main.tscn` (the pause overlay opened over a seeded game); the info popup shown over the menu |
| **Resolution** | 1024×600 window capture |
| **Fixtures** | `main-menu` / `info-popup`: the menu as booted; `settings-screen`: the settings autoload reset to defaults first (deterministic control positions); `pause-menu`: `StartNewGame(424242)` then `PauseMenu.Open()` over the seeded map |
| **Tolerance** | per-channel Δ ≤ 8; ≤ **2%** of pixels may exceed it (looser than the map goldens — text frames vary more across platforms' font rasterisation) |
| **Last regenerated** | 2026-06-18 — baseline (font slice bundled Cardo; `main-menu` + `pause-menu` regenerated in the save/load slice as their buttons changed) |

## Why these exist now (and didn't before)

UI screens previously had **no** golden because text rendering varied by platform — which is exactly why the map goldens hide the UI layer. Bundling a **licence-clear UI font** (Cardo, SIL OFL, set as `ColonyTheme`'s default) makes glyphs consistent, and that is what unblocks these. A modestly looser **2%** tolerance still absorbs cross-platform font antialiasing while catching real UI regressions (a moved or renamed button diffs far more than 2%).

## What these goldens verify

The four front-end screens render as designed — the parchment/wood framing, the Cardo serif, and the button layout/labels:
- `main-menu` — title + New Game / Load Game / Settings / Quit over the antique-map backdrop.
- `settings-screen` — Video (Window Mode, VSync) + Audio (Master/Music/SFX) at their **default** values.
- `pause-menu` — Resume / Save Game / Load Game / Settings / Quit to Main Menu / Quit to Desktop over a dimmed, seeded game.
- `info-popup` — a modal (title + message + OK) over the menu.

## When they fail, a human should check

- [ ] **Intentional change** (a button added/renamed, a layout or theme tweak)? → regenerate (`GOLDEN_UPDATE=1`), eyeball the new PNGs, commit them with the change.
- [ ] **Cross-platform font antialiasing only?** A tiny (<~2%) diff on a different OS should be absorbed by the tolerance; if it isn't, regenerate on that platform.
- [ ] **Unintended:** a theme / font / parchment-skin regression leaking into every screen.

## Known acceptable variation

GPU / FreeType rasterisation differences across platforms, absorbed by the 2% tolerance. These goldens were generated on Windows; a one-time regen on CI's platform may be needed if text AA drifts past tolerance (the L4 suite only runs on PRs, so this never blocks a push to `main`).
