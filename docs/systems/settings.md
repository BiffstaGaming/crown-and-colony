# System: Settings (application options)

| | |
|---|---|
| **Status** | In development (Slice B shipped — video + audio; SFX + Music buses both live; accessibility — UI scale + colourblind palette; a Game section — autosave period) |
| **Last verified** | 2026-06-24 @ autosave period (`86d3f0vb8`) |
| **Code** | `game/src/GameLogic/App/SettingsModel.cs` (pure), `game/presentation/SettingsService.cs` (autoload), `game/presentation/SettingsScreen.cs` + `game/scenes/SettingsScreen.tscn`, `game/presentation/AccessibilityPalette.cs` (colourblind colour source) |
| **Tests** | `game/tests/GameLogic.Tests/App/SettingsModelTests.cs` (L1); `game/presentation/tests/SettingsScreenTests.cs` (L3) |
| **FreeCol reference** | FreeCol's client options dialog (conceptual — we ship a minimal subset); UI scale ≈ ClientOptions `DISPLAY_SCALING` / `MAIN_FONT_SIZE`; autosave period ≈ ClientOptions `AUTOSAVE_PERIOD` |
| **Related systems** | [main-menu.md](main-menu.md) (entry point), [pause-menu.md](pause-menu.md) (also hosts it), [save-load-ui.md](save-load-ui.md) (consumes the autosave period at turn end) |

## 1. How it works (plain English)

*Audience: anyone — no jargon.*

A **Settings** screen, opened from the main menu or the in-game pause menu, lets you change how the game looks and sounds. Your choices take effect immediately and are remembered the next time you play.

**The rules, in plain words:**
- **Video** — Window Mode (Windowed or Fullscreen) and VSync (on/off).
- **Audio** — Master, Music, and Sound Effects volume sliders (0–100%).
- **Accessibility** —
  - **UI Scale** — a slider (75%–200%) that makes the whole interface (all text and buttons) smaller or bigger. Useful on a very high-resolution display where the menus look tiny, or if you just want larger text.
  - **Colourblind palette** — a toggle that swaps the colours used to tell players apart on the map (the rings under foreign units, the dots on the mini-map) for a high-contrast set chosen to stay distinguishable for colourblind players.
- **Game** —
  - **Autosave every N turns (0 = off)** — how often the game saves itself automatically. `1` (the default) means every turn; `5` means every fifth turn; `0` turns autosave off. The autosave is kept separate from your five manual save slots and is never overwritten by them (see [save-load-ui.md](save-load-ui.md)).
- Changes apply the instant you make them. **Back** saves them and returns to the menu.

**Worked example:**
> You open Settings and drag **Master** down to 50% — the game's output volume halves right away. You switch **Window Mode** to Fullscreen; the window goes full-screen immediately. You drag **UI Scale** to 150% and every menu, label and button grows by half on the spot. You turn on **Colourblind palette** and the player colours on the map switch to the high-contrast set. Under **Game** you set **Autosave** to 5, so from now on the game autosaves every fifth turn. You press **Back**: your choices are written to disk, and next launch the game starts at 50% master, full-screen, 150% UI scale, with the colourblind palette on and autosaving every 5 turns.

**What the player sees and does:** one screen with a Video, an Audio, an Accessibility and a Game section, each control applying live; a Back button that saves and returns.

> Note: **sound effects now play** through the SFX bus (`SoundService` — e.g. founding a colony, resolving an attack), so the **Sound Effects** slider is live. **Music now plays too** through the Music bus (`MusicService` — a looping background playlist on the menu and in-game, plus national anthems), so the **Music** slider is live as well.

## 2. Detailed rules

*Audience: designers/testers.*

| Setting | Values | Default | Applied as |
|---|---|---|---|
| Window Mode | Windowed, Fullscreen | Windowed | `DisplayServer.WindowSetMode` (Fullscreen = borderless) |
| VSync | on / off | on | `DisplayServer.WindowSetVsyncMode` |
| Master volume | 0–100% (linear 0–1) | 100% | `AudioServer` "Master" bus, dB = `LinearToDb(linear)` |
| Music volume | 0–100% | 80% | "Music" bus (created at startup, routed to Master) |
| Sound Effects volume | 0–100% | 80% | "SFX" bus (created at startup, routed to Master) |
| UI Scale | 75%–200% (factor 0.75–2.0, step 5%) | 100% | root viewport `ContentScaleFactor` (scales every Control under the window) |
| Colourblind palette | on / off | off | `AccessibilityPalette.ColorblindMode` — swaps the player-colour set the map overlays draw |
| Autosave period | 0–100 turns (0 = off) | 1 | none directly — read at turn end by `GameController.MaybeAutosave`, which writes `user://saves/autosave.json` when `Turn % N == 0` (see [save-load-ui.md](save-load-ui.md)) |

- **Persistence:** stored in `user://settings.cfg` (a Godot `ConfigFile`, one `[settings]` section). Written on **Back**. Keys: `ui_scale` (a factor like `1.5`), `colorblind_mode` (`true`/`false`), `autosave_period` (an integer like `5`). **Note:** this is the application-settings file, *not* the game-save format — adding these options needed **no save-version bump**.
- **Robustness:** a missing file → all defaults; a corrupt/partial file → each bad or absent value falls back to its default, and all values are clamped into range (volumes to `[0,1]`, UI scale to `[0.75, 2.0]` (NaN → 1.0), autosave period to `[0, 100]`, unknown window mode → Windowed, anything but `true` for the colourblind flag → off). Invalid state can never be produced or persisted.

**UI Scale — how it scales everything at once:** rather than rebuilding `ColonyTheme`'s `DefaultFontSize`, the option drives the root viewport's `ContentScaleFactor`. Godot multiplies the whole window's content (every label, button, dropdown — and their fonts) by that factor in one step, so a single setting resizes the entire UI live and on boot without touching the theme. It is FreeCol's `DISPLAY_SCALING` / `MAIN_FONT_SIZE` rolled into one factor.

**Colourblind palette — what changes:** the toggle only affects the **player-identification** colours on the map (it does not recolour terrain or art). When on, `AccessibilityPalette` returns colours from the **Okabe–Ito colour-universal palette** instead of: each foreign nation's ruleset hue (a stable Okabe–Ito hue per nation), the native-power earthy red-brown, the plain rival-red fallback, and the mini-map's own/rival/native dots. When off, every accessor is a pass-through returning the original colour — so default behaviour and its goldens are unchanged.

**Deviations from original 1994 / FreeCol behavior:** this is a modern client-options screen, not a 1994 feature. We ship video + audio essentials, two accessibility options (UI scale, colourblind palette) and one Game option (autosave period); no resolution picker, language, or gameplay-rule options yet. The colourblind palette has **no FreeCol equivalent**; the autosave period mirrors FreeCol's `ClientOptions.AUTOSAVE_PERIOD`. These are **application/client** settings, deliberately separate from per-game **rule** options (difficulty, custom-house export mode), which belong to a New Game / game-options surface — autosave-period is a client preference (how often *this client* saves), so it lives here, not in a save.

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Domain model:** `SettingsModel` (`GameLogic/App`, pure C#, no Godot) — the data plus `Clamp()` (validate/normalise) and `ToDictionary()`/`FromDictionary()` (string round-trip for persistence). It lives in the engine-free library purely so it gets L1 coverage; it is app config, not game rules. The accessibility fields are `UiScale` (`float`, clamped to `[MinUiScale, MaxUiScale]` = `[0.75, 2.0]`, NaN → 1.0) and `ColorblindMode` (`bool`). The Game field is `AutosavePeriod` (`int`, clamped to `[0, MaxAutosavePeriod]` = `[0, 100]`, default `1`; `0` = autosave off) — persisted under key `autosave_period`. `MinUiScale`/`MaxUiScale`/`MaxAutosavePeriod` are public consts so the UI controls and tests share the one source of range.

**Autoload:** `SettingsService` (`/root/Settings`, registered in `project.godot`) owns the live `SettingsModel`. On `_Ready` it ensures the Music/SFX audio buses exist, `Load`s `user://settings.cfg` into the model, and `Apply`s it to the engine. API: `Settings` (the live model), `UpdateAndApply(mutate)` (mutate → clamp → apply, no save), `Save()`, `Apply()`. `Apply()` additionally sets `GetTree().Root.ContentScaleFactor = UiScale` (null-guarded for headless/CI) and pushes `ColorblindMode` to `AccessibilityPalette.ColorblindMode` — so both accessibility options take effect on boot and on every change, exactly like the existing video/audio settings.

**Colourblind palette:** `AccessibilityPalette` (`presentation/`, static, ADR-006) is the single colour source for the map's player-identification markers. It holds the `ColorblindMode` flag (set by `SettingsService.Apply()`) and exposes pass-through-or-swap accessors — `RivalNation(shortName, default)`, `Native(default)`, `RivalFallback(default)`, `Own(default)`, `Rival(default)`. When the flag is off they return the supplied default unchanged; when on they return colours from the **Okabe–Ito colour-universal palette** (Masataka Okabe & Kei Ito, 2008, <https://jfly.uni-koeln.de/color/> — published for free reuse, no licensing constraint). A foreign nation is keyed onto the Okabe–Ito cycle by a deterministic FNV-1a hash of its short name, so a given nation always maps to the same hue. Consumers: `GameController.OwnerColorOf` (unit owner rings) and `MiniMap` (colony/unit/settlement dots, now computed-property accessors so a redraw re-reads the flag).

**UI:** `SettingsScreen` (`Control`) is a reusable **overlay**: a host (the main menu or the pause menu) instantiates it as a child and removes it when it emits `Closed`. It resolves the autoload (falling back to a transient `SettingsService` child if absent), populates its controls, and on each control change calls `UpdateAndApply` (live). A `_populating` guard stops the change handlers firing while controls are set programmatically. **Back** calls `Save()` then emits `Closed` — it does not change scenes itself, so the host (and a paused game beneath the pause menu) is preserved. The **Game** section's autosave control is a `SpinBox` (`AutosaveSpin`, range `[0, MaxAutosavePeriod]`, step 1) whose `ValueChanged` writes `AutosavePeriod`; it has no live engine effect (it is read at turn end by `GameController.MaybeAutosave`). Look is shared with the menu via `ColonyTheme` + `ColonyArt.ParchmentSkin()` + the carved-wood border.

**Audio buses:** the default project has only **Master**. `SettingsService.EnsureAudioBuses` creates **Music** and **SFX** (routed to Master) at startup so all three volume sliders drive real buses. Both now have consumers — `SoundService` (autoload `/root/Sound`) plays game sound effects on **SFX** (`86d3c9xrp`); `MusicService` (autoload `/root/Music`) plays a looping background playlist + national anthems on **Music** (`86d3c9xu1`). See [presentation.md](../modules/presentation.md).

**Data sources:** none (no ruleset/XML). **Integration points:** opened from the main menu's Settings button; applies via `DisplayServer`/`AudioServer`. **Persistence:** `user://settings.cfg`.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Yes (pure model) | `SettingsModelTests` — defaults (incl. `UiScale`=1.0, `ColorblindMode`=off, `AutosavePeriod`=1), `Clamp` (volumes + UI-scale range/NaN + autosave-period range), dictionary round-trip (all fields incl. accessibility + autosave-period), missing/garbage-safe parse (incl. `ui_scale`/`colorblind_mode`/`autosave_period`) | ✅ |
| L2 Scenario | n/a | — | — |
| L3 Interaction | Yes (has UI) | `SettingsScreenTests` — controls render/populate; Master slider applies to the Master bus + updates its label; Music/SFX buses exist; save→load disk round-trip. Autosave turn-end behaviour covered by `SaveLoadTests.EndingATurn_WritesTheAutosave` (see [save-load-ui.md](save-load-ui.md)) | ✅ existing; new UI-scale/colourblind/autosave controls covered by L1 round-trip + the L4 golden |
| L4 Visual | Yes (has a screen) | `settings-screen` golden (`MenuGoldenTests`) — now includes the Accessibility + Game (autosave) sections; **regenerated** with this change (`GOLDEN_UPDATE=1`); CI regenerates/checks it on the PR | ✅ |
| L5 Soak | Covered by global suite | — | — |

## 5. Open issues / TODO

- [x] **L4 golden** for the settings screen (`MenuGoldenTests` → `settings-screen`) — added with the bundled font (Slice D).
- [x] Route **SFX** through the SFX bus — `SoundService` plays FreeCol SFX on the SFX bus (`86d3c9xrp`).
- [x] Route **Music** through the Music bus — `MusicService` plays a looping FreeCol background playlist + national anthems on the Music bus (`86d3c9xu1`).
- [x] **Accessibility — UI scale** (`86d3f0vw7`): a 75–200% content-scale slider; resizes the whole UI live + on boot.
- [x] **Accessibility — colourblind palette** (`86d3f0wgv`): a toggle swapping the map's player colours to the Okabe–Ito colour-universal set via `AccessibilityPalette`.
- [x] **Game — autosave period** (`86d3f0vb8`): a 0–100 turn integer option (0 = off, default 1); read at turn end to write the autosave (see [save-load-ui.md](save-load-ui.md)).
- [ ] Possible later additions: resolution picker, gameplay options tab, key rebinding, language; per-deficiency palette variants (currently one colour-universal set).
- [x] Reused by the in-game pause menu as an overlay (Slice C — see [pause-menu.md](pause-menu.md)).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-17 | Slice B — settings screen + persistence: `SettingsModel` (L1) + `SettingsService` autoload (`user://settings.cfg`) + Video/Audio UI; wired the menu's Settings button | 11da6fa |
| 2026-06-17 | Slice C — made `SettingsScreen` a reusable overlay (emits `Closed`, no self-navigation); menu + pause menu host it | 895f958 |
| 2026-06-17 | Slice D — bundled UI font (Cardo) cascades here; added the `settings-screen` L4 golden | 0106d9c |
| 2026-06-22 | The **SFX** bus gets its first consumer: `SoundService` (`/root/Sound`) plays FreeCol GPL-v2 sound effects through it (colony-founded, combat) — so the Sound Effects slider is now live. Music still pending. See [presentation.md](../modules/presentation.md). | `86d3c9xrp` |
| 2026-06-22 | The **Music** bus gets its consumer: `MusicService` (`/root/Music`) loops a shuffled FreeCol background playlist (CC BY 4.0) on the menu + in-game and plays per-nation anthems (GPL v2) — so the Music slider is now live. See [presentation.md](../modules/presentation.md). | `86d3c9xu1` |
| 2026-06-24 | **Accessibility — UI scale**: added `SettingsModel.UiScale` (0.75–2.0, default 1.0), applied via the root viewport `ContentScaleFactor` (whole-UI live resize, on boot too); new Accessibility section + slider in `SettingsScreen`. settings.cfg only — no save bump. (`86d3f0vw7`) | _local_ |
| 2026-06-24 | **Accessibility — colourblind palette**: added `SettingsModel.ColorblindMode` (default off) + `AccessibilityPalette` (presentation) swapping the map's player colours to the Okabe–Ito colour-universal set; consumed by `UnitMarker`/`GameController` owner rings + `MiniMap` dots; toggle in `SettingsScreen`. settings.cfg only — no save bump. (`86d3f0wgv`) | _local_ |
| 2026-06-24 | **Game — autosave period**: added `SettingsModel.AutosavePeriod` (int, 0–100, default 1; key `autosave_period`) + a new "Game" section with a SpinBox in `SettingsScreen`; consumed at turn end by `GameController.MaybeAutosave` to write `user://saves/autosave.json` (see [save-load-ui.md](save-load-ui.md)). settings.cfg only — no save bump. Regenerated the `settings-screen` golden. (`86d3f0vb8`) | _local_ |
