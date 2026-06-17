# System: Settings (application options)

| | |
|---|---|
| **Status** | In development (Slice B shipped — video + audio) |
| **Last verified** | 2026-06-17 @ (pending) |
| **Code** | `game/src/GameLogic/App/SettingsModel.cs` (pure), `game/presentation/SettingsService.cs` (autoload), `game/presentation/SettingsScreen.cs` + `game/scenes/SettingsScreen.tscn` |
| **Tests** | `game/tests/GameLogic.Tests/App/SettingsModelTests.cs` (L1); `game/presentation/tests/SettingsScreenTests.cs` (L3) |
| **FreeCol reference** | FreeCol's client options dialog (conceptual — we ship a minimal subset) |
| **Related systems** | [main-menu.md](main-menu.md) (entry point) |

## 1. How it works (plain English)

*Audience: anyone — no jargon.*

A **Settings** screen, opened from the main menu, lets you change how the game looks and sounds. Your choices take effect immediately and are remembered the next time you play.

**The rules, in plain words:**
- **Video** — Window Mode (Windowed or Fullscreen) and VSync (on/off).
- **Audio** — Master, Music, and Sound Effects volume sliders (0–100%).
- Changes apply the instant you make them. **Back** saves them and returns to the menu.

**Worked example:**
> You open Settings and drag **Master** down to 50% — the game's output volume halves right away. You switch **Window Mode** to Fullscreen; the window goes full-screen immediately. You press **Back**: your choices are written to disk, and next launch the game starts at 50% master, full-screen.

**What the player sees and does:** one screen with a Video section and an Audio section, each control applying live; a Back button that saves and returns.

> Note: there is no music or sound yet (audio assets are a later task), so the Music/SFX sliders set the volume of buses that nothing plays through yet — they are wired and persisted, ready for when audio lands.

## 2. Detailed rules

*Audience: designers/testers.*

| Setting | Values | Default | Applied as |
|---|---|---|---|
| Window Mode | Windowed, Fullscreen | Windowed | `DisplayServer.WindowSetMode` (Fullscreen = borderless) |
| VSync | on / off | on | `DisplayServer.WindowSetVsyncMode` |
| Master volume | 0–100% (linear 0–1) | 100% | `AudioServer` "Master" bus, dB = `LinearToDb(linear)` |
| Music volume | 0–100% | 80% | "Music" bus (created at startup, routed to Master) |
| Sound Effects volume | 0–100% | 80% | "SFX" bus (created at startup, routed to Master) |

- **Persistence:** stored in `user://settings.cfg` (a Godot `ConfigFile`, one `[settings]` section). Written on **Back**.
- **Robustness:** a missing file → all defaults; a corrupt/partial file → each bad or absent value falls back to its default, and all values are clamped into range (volumes to `[0,1]`, unknown window mode → Windowed). Invalid state can never be produced or persisted.

**Deviations from original 1994 / FreeCol behavior:** this is a modern client-options screen, not a 1994 feature. We ship only video + audio essentials for now (no resolution picker, language, or gameplay options yet). These are **application** settings, deliberately separate from per-game **rule** options (difficulty, custom-house export mode), which belong to a New Game / game-options surface.

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Domain model:** `SettingsModel` (`GameLogic/App`, pure C#, no Godot) — the data plus `Clamp()` (validate/normalise) and `ToDictionary()`/`FromDictionary()` (string round-trip for persistence). It lives in the engine-free library purely so it gets L1 coverage; it is app config, not game rules.

**Autoload:** `SettingsService` (`/root/Settings`, registered in `project.godot`) owns the live `SettingsModel`. On `_Ready` it ensures the Music/SFX audio buses exist, `Load`s `user://settings.cfg` into the model, and `Apply`s it to the engine. API: `Settings` (the live model), `UpdateAndApply(mutate)` (mutate → clamp → apply, no save), `Save()`, `Apply()`.

**UI:** `SettingsScreen` (`Control`) resolves the autoload (falling back to a transient `SettingsService` child if absent), populates its controls, and on each control change calls `UpdateAndApply` (live). A `_populating` guard stops the change handlers firing while controls are set programmatically. **Back** calls `Save()` then `ChangeSceneToFile(MainMenu.MenuScenePath)`. Look is shared with the menu via `ColonyTheme` + `ColonyArt.ParchmentSkin()` + the carved-wood border.

**Audio buses:** the default project has only **Master**. `SettingsService.EnsureAudioBuses` creates **Music** and **SFX** (routed to Master) at startup so all three volume sliders drive real buses, ready for the audio [ART] task to route players through them.

**Data sources:** none (no ruleset/XML). **Integration points:** opened from the main menu's Settings button; applies via `DisplayServer`/`AudioServer`. **Persistence:** `user://settings.cfg`.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Yes (pure model) | `SettingsModelTests` — defaults, `Clamp`, dictionary round-trip, missing/garbage-safe parse | ✅ |
| L2 Scenario | n/a | — | — |
| L3 Interaction | Yes (has UI) | `SettingsScreenTests` — controls render/populate; Master slider applies to the Master bus + updates its label; Music/SFX buses exist; save→load disk round-trip | ✅ |
| L4 Visual | Deferred | golden blocked on the UI-font task (ClickUp `86d3c9y32`) | ⬜ |
| L5 Soak | Covered by global suite | — | — |

## 5. Open issues / TODO

- [ ] **L4 golden** for the settings screen — blocked on a licence-clear UI font (ClickUp `86d3c9y32`).
- [ ] Route music/SFX players through the Music/SFX buses when audio assets land (ClickUp `86d3c9xu1` / `86d3c9xrp`).
- [ ] Possible later additions: resolution picker, gameplay options tab, key rebinding, language.
- [ ] Reuse this screen from the in-game pause menu (separate slice) — may move from a full scene to an overlay then.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-17 | Slice B — settings screen + persistence: `SettingsModel` (L1) + `SettingsService` autoload (`user://settings.cfg`) + Video/Audio UI; wired the menu's Settings button | (pending) |
