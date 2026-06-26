# System: Audio (sound effects + music)

| | |
|---|---|
| **Status** | In development (Wave: event-SFX wiring, UI click + illegal-move buzz, looping background playlist + national anthem, music context seam, master mute + Music/SFX volume) |
| **Last verified** | 2026-06-27 @ audio wiring (`86d3fq12n`/`86d3fq16y`/`86d3fq0y8`/`86d3fq1b1`/`86d3fq1wy`/`86d3fq0z4`) |
| **Code** | `game/presentation/SoundService.cs` (`/root/Sound` autoload), `game/presentation/MusicService.cs` (`/root/Music` autoload), `game/presentation/SettingsService.cs` (audio buses + volumes + mute), `game/presentation/SettingsScreen.cs` + `game/scenes/SettingsScreen.tscn` (sliders + mute control), `game/presentation/GameController.cs` (event-SFX + music-context hook points); engine-free catalogs: `game/src/GameLogic/Audio/SoundEvent.cs`, `SoundEventCatalog.cs`, `MusicContext.cs`, `MusicTrackCatalog.cs` |
| **Tests** | `game/tests/GameLogic.Tests/Audio/SoundEventCatalogTests.cs` + `MusicTrackCatalogTests.cs` (L1 — the event/context→clip mappings); `game/presentation/tests/AudioWiringTests.cs` (L3 — the event cue path, mute persist+apply, playlist advance); `game/presentation/tests/SettingsScreenTests.cs` (L3 — volume sliders + buses) |
| **FreeCol reference** | FreeCol `SoundController` (the default looping playlist + `playMusic` anthems), `sound.event.*` SFX set, the client-options audio category (per-bus volume + mute) |
| **Related systems** | [settings.md](settings.md) (the volume sliders + master mute live there), [turns.md](turns.md) (the turn-message panel + the alert cue), [combat.md](combat.md) (the combat / ship-sunk cues) |

## 1. How it works (plain English)

*Audience: anyone — no jargon.*

The game plays **music** in the background and **sound effects** for the things that happen as you play. You control how loud each is — or silence everything — from the Settings screen.

**The rules, in plain words:**
- **Background music** plays a looping playlist of period tunes, the same on the main menu and once you are in a game. When one track ends the next begins; the order is shuffled each time the playlist starts.
- **Your nation's anthem** plays once when a game starts, over the top of the music, then the playlist carries on.
- **Sound effects** fire at the moment something happens:
  - founding a colony, a building finishing construction;
  - resolving an attack (a heavier "ship sunk" cue when you sink an enemy ship);
  - loading or unloading cargo, and cashing in a treasure train;
  - an **alert** when something happened during the other players' turn (the "what just happened" popup);
  - a short **buzz** whenever you try something the game won't let you do (an illegal move, attack, build, or cargo transfer).
- **Clicking any button** plays a small click sound — automatically, on every button in the game.
- In **Settings → Audio** you get three volume sliders — **Master**, **Music**, **Sound Effects** — and a **Mute all** switch that silences everything at once without forgetting your slider positions. Every choice takes effect immediately and is remembered next time.

**Worked example:**
> You start a new game as the Dutch: the background playlist is already humming along from the menu, and the Dutch anthem plays once over it. You sail a colonist ashore and press **B** to found a colony — a colony chime. A few turns later a warehouse finishes building — a build cue. You try to move a unit into the sea and it can't — a short buzz, and the status bar tells you why. You end the turn; a privateer sank one of your ships during the enemy phase, so the turn popup appears with an alert sound. You open Settings, drag **Music** to 40% and tick **Mute all** to take a phone call — silence. You un-tick it and the music returns at exactly 40%.

**What the player sees and does:** nothing new to learn — sounds simply accompany the actions and screens that already exist; the only controls are the Audio section of the Settings screen (three sliders + a Mute switch).

## 2. Detailed rules

*Audience: designers/testers.*

**Sound-effect cues** (logical `SoundEvent` → FreeCol clip, played on the **SFX** bus):

| Game moment | `SoundEvent` | Clip | Where it fires |
|---|---|---|---|
| Colony founded | `ColonyFounded` | `colony.ogg` | `GameController.FoundColonyWithClaim` |
| Building finished | `BuildingComplete` | `building.ogg` | `GameController.OnEndTurnPressed` (building-count snapshot diff around `EndTurn`) |
| Attack resolved | `Combat` | `attack.ogg` | `ResolveAttackOn`, `AmphibiousAssaultFrom` |
| Enemy ship sunk | `ShipSunk` | `sunk.ogg` | `ResolveAttackOn` (decisive naval win) |
| Cargo loaded / unloaded | `CargoMoved` | `load.ogg` | `LoadColonyCargo` / `UnloadColonyCargo` (success) |
| Treasure cashed in | `CargoSold` | `sell.ogg` | `CashInSelectedTreasureTrain` (confirmed) |
| Turn events occurred | `Alert` | `alert.ogg` | `OnEndTurnPressed` when the turn-message panel has ≥1 entry |
| Illegal/blocked action | `IllegalMove` | `illegal.ogg` | `NoticeBlocked` (move/attack/found/disband/goto), failed cargo transfer; **also** the generic UI button-click cue |

- **One cue per moment, most-specific wins:** when a building completes, `BuildingComplete` fires; the generic `Alert` only fires when the turn popup actually has entries (a completed build alone produces no panel entry, so the two do not double up).
- **UI click cue:** a single global hook in `SoundService` connects every `BaseButton.Pressed` (present at boot and added later) to `PlayUiClick`, which plays the `IllegalMove` clip (FreeCol reuses that clip as the click/deny cue). No per-button wiring.
- **Defensive playback:** a missing clip or absent autoload is a logged no-op, never a crash — silence always beats a hard failure (ADR-006). Headless/CI has no audio device; the services still construct and `Play` still records intent (`SoundService.LastPlayed`) without producing sound.

**Music** (played on the **Music** bus):

| Behaviour | Rule |
|---|---|
| Background playlist | 6-track shuffled loop (`MusicContext.Background`); when a track finishes the next plays, wrapping forever. Shared by the menu and gameplay (FreeCol's single default playlist). |
| Context switch | `MusicService.SetContext(MusicContext)` restarts the playlist **only when the context actually changes** (and re-shuffles); a same-context call while playing is a no-op, so menu→game is seamless. Called from `GameController.StartGame`. |
| National anthem | `PlayAnthem(nationId)` plays the nation's anthem once, then resumes the background playlist; a no-op for natives/REF/unknown ids. Cued at game start for the human player. |
| Shuffle RNG | Godot's `RandomNumberGenerator` (cosmetic ordering only — **outside** the seeded game RNG; ADR-009 simulation determinism unaffected). |

**Volume + mute** (Settings → Audio):

| Control | Range / state | Applied as |
|---|---|---|
| Master volume | 0–100% | `AudioServer.SetBusVolumeDb("Master", LinearToDb(v))` |
| Music volume | 0–100% (default 80%) | same, `"Music"` bus (routed to Master) |
| Sound Effects volume | 0–100% (default 80%) | same, `"SFX"` bus (routed to Master) |
| Mute all | on / off (default off) | `AudioServer.SetBusMute("Master", on)` — silences Music + SFX in one step **without** changing the saved slider values |

**Deviations from original 1994 / FreeCol behavior:**
- **Single shared playlist for menu + game** (faithful to FreeCol). A distinct war/high-tension music context (`86d3fq1wy`) is **not** implemented: `MusicContext` ships only `Background`, and the enum lives in the engine-free `GameLogic` assembly which this presentation wave does not modify. The `SetContext` seam is in place so a future context (added with its catalog entry) needs no presentation rewiring.
- **Building-complete cue is presentation-detected** (a building-count snapshot around `EndTurn`), because the engine surfaces no build-complete notice; this is a read-only observation of already-resolved state (ADR-006), not a rule change.

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Domain model (engine-free, `GameLogic/Audio/`):** `SoundEvent` (enum) + `SoundEventCatalog` (event→`res://` clip path map); `MusicContext` (enum) + `MusicTrackCatalog` (context→playlist + nation→anthem maps). Pure data, L1-tested without the engine.

**Presentation services:**
- `SoundService` (`/root/Sound`) — preloads every catalogued clip, plays on a 6-voice round-robin `AudioStreamPlayer` pool routed to the **SFX** bus. Auto-wires button clicks via `GetTree().NodeAdded` + a one-time boot sweep. `LastPlayed` records the last requested `SoundEvent` so headless L3 can assert the cue without real audio.
- `MusicService` (`/root/Music`) — one `AudioStreamPlayer` on the **Music** bus; the `Finished` signal advances the shuffled playlist (or falls back from a one-shot anthem). `PlayBackground` / `SetContext` / `PlayAnthem` / `Stop`.
- `SettingsService` (`/root/Settings`) — creates the Music/SFX buses (routed to Master) at boot, applies the Master/Music/SFX volumes and the **master mute** on `Apply()`, and round-trips both through `user://settings.cfg`.

**Integration points:** `GameController` holds the event-SFX + music-context hook points — thin `PlaySound(SoundEvent)` / `NoticeBlocked(reason)` / `PlayAnthem(nationId)` / `SetMusicContext(context)` calls resolved lazily by node path (no-op when an autoload is absent, e.g. a bare headless test scene). No rule logic lives here.

**Persistence:** the three volumes persist via `SettingsModel` (existing keys `master_volume` / `music_volume` / `sfx_volume`). The **master mute** is presentation-only state held on `SettingsService` (NOT on the engine-free `SettingsModel`) and persisted as an extra `master_mute` key in the same `[settings]` section of `settings.cfg`. No game-save change — **no save-version bump** (audio + the mute flag are client settings, not game state).

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `SoundEventCatalogTests`, `MusicTrackCatalogTests` (every event/context resolves to a unique well-formed `res://…/*.ogg`; every European nation resolves to an anthem; natives/REF/null → no anthem) | ✅ |
| L2 Scenario | n/a | audio is presentation-reactive — no simulation behaviour to script | — |
| L3 Interaction | Yes (has UI) | `AudioWiringTests` (autoloads boot headless without crashing; `PlayUiClick` safe + buttons auto-wired; blocked action → `IllegalMove`; blocked cargo unload → `IllegalMove`; master mute persists + applies `SetBusMute(Master)`; playlist advances track-to-track; same-context `SetContext` does not restart) + `SettingsScreenTests` (volume sliders apply to their buses) | ✅ |
| L4 Visual | If a screen changed | Settings screen gained a Mute row — covered by the existing settings golden when regenerated; no new golden added this wave | ⬜ |
| L5 Soak | Covered by global suite | — | — |

- **FreeCol cross-check:** the event→clip and context→track mappings mirror FreeCol's `sound.event.*` / default playlist / per-nation anthem naming (see the L1 catalog tests + `assets/freecol/*/PROVENANCE.md`). Real audio output is not asserted (no audio device in CI).

## 5. Open issues / TODO

- [ ] War/high-tension music context (`86d3fq1wy`) — needs a new `MusicContext` value + catalog entry in the engine-free `GameLogic` assembly, then a `SetMusicContext` call at the war/high-alarm state change. Seam (`SetContext`) is ready.
- [ ] A dedicated UI-click clip (currently the `illegal.ogg` deny clip doubles as the click) — map a distinct clip to a future `SoundEvent.UiClick` when one is sourced.
- [ ] Regenerate the Settings-screen golden to include the Mute row (deliberate golden update).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-27 | Initial documentation — audio wiring wave: event SFX, UI click + illegal-move buzz, looping background playlist + context seam + anthem, master mute + Music/SFX volume | _(this commit)_ |
