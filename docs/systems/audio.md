# System: Audio (sound effects + music)

| | |
|---|---|
| **Status** | In development (Wave: event-SFX wiring, UI click + illegal-move buzz, looping background playlist + national anthem, state-driven music contexts incl. war, master mute + Music/SFX volume) |
| **Last verified** | 2026-07-03 @ state-driven music contexts (`86d3fq1wy`) |
| **Code** | `game/presentation/SoundService.cs` (`/root/Sound` autoload), `game/presentation/MusicService.cs` (`/root/Music` autoload), `game/presentation/SettingsService.cs` (audio buses + volumes + mute), `game/presentation/SettingsScreen.cs` + `game/scenes/SettingsScreen.tscn` (sliders + mute control), `game/presentation/GameController.cs` (event-SFX hooks + `RefreshMusicContext`), `game/presentation/MainMenu.cs` (menu context); engine-free: `game/src/GameLogic/Audio/SoundEvent.cs`, `SoundEventCatalog.cs`, `MusicContext.cs`, `MusicTrackCatalog.cs`, `MusicContextSelector.cs` (pure game-state→context rule) |
| **Tests** | `game/tests/GameLogic.Tests/Audio/SoundEventCatalogTests.cs` + `MusicTrackCatalogTests.cs` + `MusicContextSelectorTests.cs` (L1 — the event/context→clip mappings + the state→context rule); `game/presentation/tests/AudioWiringTests.cs` (L3 — the event cue path, mute persist+apply, playlist advance, context wiring incl. the war flip); `game/presentation/tests/SettingsScreenTests.cs` (L3 — volume sliders + buses) |
| **FreeCol reference** | FreeCol `SoundController` (the default looping playlist + `playMusic` anthems), `sound.event.*` SFX set, the client-options audio category (per-bus volume + mute) |
| **Related systems** | [settings.md](settings.md) (the volume sliders + master mute live there), [turns.md](turns.md) (the turn-message panel + the alert cue), [combat.md](combat.md) (the combat / ship-sunk cues) |

## 1. How it works (plain English)

*Audience: anyone — no jargon.*

The game plays **music** in the background and **sound effects** for the things that happen as you play. You control how loud each is — or silence everything — from the Settings screen.

**The rules, in plain words:**
- **Background music** plays a looping playlist of period tunes, the same on the main menu and once you are in a game — moving from the menu into a game never interrupts the tune that's playing. When one track ends the next begins; the order is shuffled each time the playlist starts.
- **War changes the music.** The moment you are at war with another European power — a rival nation, or the King's army after you declare independence — the playlist switches to a smaller, more martial set of tracks. Make peace (or win your independence) and the normal playlist returns. Trouble with the native tribes does **not** change the music — only wars with European-side powers do.
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
> You start a new game as the Dutch: the background playlist is already humming along from the menu, and the Dutch anthem plays once over it. You sail a colonist ashore and press **B** to found a colony — a colony chime. A few turns later a warehouse finishes building — a build cue. You try to move a unit into the sea and it can't — a short buzz, and the status bar tells you why. You end the turn; a privateer sank one of your ships during the enemy phase, so the turn popup appears with an alert sound. Later the Spanish declare war on you — the music shifts to the sterner war set until your diplomats sign a peace, when the familiar playlist returns. You open Settings, drag **Music** to 40% and tick **Mute all** to take a phone call — silence. You un-tick it and the music returns at exactly 40%.

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
| Contexts | `MusicContext.Menu` (main menu), `InGamePeace` (in a game, no European-side war), `InGameWar` (the human holds `Stance.War` toward any **non-native** player — a colonial rival, or the REF between declaring independence and winning it). Derived from game state by the pure `MusicContextSelector.For(Game?)` — `null` (no game) → `Menu`; `Uncontacted`/`Peace`/`CeaseFire`/`Alliance` all count as peace. |
| Peace / menu playlist | 6-track shuffled loop; when a track finishes the next plays, wrapping forever. `Menu` and `InGamePeace` map to the **identical** list (FreeCol's single default playlist). |
| War playlist | **Interim** 2-track subset of the same shipped tracks (`el-dorado.ogg`, `fearless-sailors.ogg`) — FreeCol ships no dedicated war/tension track and no new assets were added (recorded asset gap). Must always differ from the peace list (L1 audible-switch guard). |
| Context switch | `MusicService.SetContext(MusicContext)` restarts the playlist (re-shuffled) **only when the audible playlist actually changes**; a same-context call is a no-op, and a different context with an **identical catalog playlist** (Menu ↔ InGamePeace) is relabelled without interrupting the track — menu→game is seamless, entering/leaving war audibly switches. Fed by `GameController.RefreshMusicContext` (selector → service) in `StartGame` and on every `RefreshView` (the universal post-state-change hook — so wars started/ended by attacks, diplomacy, independence or AI turns all flip the music), and by `MainMenu._Ready` (re-asserts `Menu` on quit-to-menu). |
| Native wars | Deliberately do **not** flip the war music: a tribe's WAR stance lives in the transient native-stance channels (`Game.NativeStanceToward`), never in `Player.Stances`, and the selector additionally filters out `PlayerType.Native` (FreeCol scores native raids with SFX, not a soundtrack change). |
| National anthem | `PlayAnthem(nationId)` plays the nation's anthem once, then resumes the background playlist; a no-op for natives/REF/unknown ids. Cued at game start for the human player (after the context is set). Accepted edge: a war breaking out mid-anthem cuts the anthem in favour of the war bed. |
| Shuffle RNG | Godot's `RandomNumberGenerator` (cosmetic ordering only — **outside** the seeded game RNG). The context **selector** is pure and RNG-free (ADR-009 simulation determinism unaffected). |

**Volume + mute** (Settings → Audio):

| Control | Range / state | Applied as |
|---|---|---|
| Master volume | 0–100% | `AudioServer.SetBusVolumeDb("Master", LinearToDb(v))` |
| Music volume | 0–100% (default 80%) | same, `"Music"` bus (routed to Master) |
| Sound Effects volume | 0–100% (default 80%) | same, `"SFX"` bus (routed to Master) |
| Mute all | on / off (default off) | `AudioServer.SetBusMute("Master", on)` — silences Music + SFX in one step **without** changing the saved slider values |

**Deviations from original 1994 / FreeCol behavior:**
- **A war music context is an addition over FreeCol**, which plays its one default playlist regardless of game state (`86d3fq1wy`). The mechanism (selector + catalog + seamless switch) is real; the war **tracks** are an interim subset of the existing playlist because no dedicated war/menu asset is shipped — parity for "distinct war music" is therefore *Partial* until a war track is sourced (asset gap).
- **Menu + peace share one playlist** (faithful to FreeCol's single default playlist) — the `Menu`/`InGamePeace` split exists so the state model is explicit, not to sound different today.
- **Building-complete cue is presentation-detected** (a building-count snapshot around `EndTurn`), because the engine surfaces no build-complete notice; this is a read-only observation of already-resolved state (ADR-006), not a rule change.

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Domain model (engine-free, `GameLogic/Audio/`):** `SoundEvent` (enum) + `SoundEventCatalog` (event→`res://` clip path map); `MusicContext` (enum: `Menu`/`InGamePeace`/`InGameWar` — never persisted, re-derived every refresh) + `MusicTrackCatalog` (context→playlist + nation→anthem maps) + `MusicContextSelector` (the pure game-state→context rule: `null` game → `Menu`; human holds `Stance.War` toward any resolvable non-`Native` player → `InGameWar`; else `InGamePeace`). Pure data + a pure function, L1-tested without the engine.

**Presentation services:**
- `SoundService` (`/root/Sound`) — preloads every catalogued clip, plays on a 6-voice round-robin `AudioStreamPlayer` pool routed to the **SFX** bus. Auto-wires button clicks via `GetTree().NodeAdded` + a one-time boot sweep. `LastPlayed` records the last requested `SoundEvent` so headless L3 can assert the cue without real audio.
- `MusicService` (`/root/Music`) — one `AudioStreamPlayer` on the **Music** bus; the `Finished` signal advances the shuffled playlist (or falls back from a one-shot anthem). `PlayBackground` / `SetContext` / `PlayAnthem` / `Stop` / `CurrentContext` (read-only, for game code + L3). `SetContext` compares the target context's **catalog playlist** to the current one (`SequenceEqual`) and merely relabels `_context` when identical — the seamless-switch rule lives here, not in callers.
- `SettingsService` (`/root/Settings`) — creates the Music/SFX buses (routed to Master) at boot, applies the Master/Music/SFX volumes and the **master mute** on `Apply()`, and round-trips both through `user://settings.cfg`.

**Integration points:** `GameController` holds the event-SFX + music-context hook points — thin `PlaySound(SoundEvent)` / `NoticeBlocked(reason)` / `PlayAnthem(nationId)` / `SetMusicContext(context)` calls resolved lazily by node path (no-op when an autoload is absent, e.g. a bare headless test scene). `RefreshMusicContext()` (= `SetMusicContext(MusicContextSelector.For(_game))`) runs in `StartGame` (before the anthem — order matters) and at the top of `RefreshView`, so every state change re-derives the context with no per-event wiring; `MainMenu._Ready` re-asserts `Menu`. No rule logic lives in the presentation layer — the war predicate is the engine-free selector.

**Persistence:** the three volumes persist via `SettingsModel` (existing keys `master_volume` / `music_volume` / `sfx_volume`). The **master mute** is presentation-only state held on `SettingsService` (NOT on the engine-free `SettingsModel`) and persisted as an extra `master_mute` key in the same `[settings]` section of `settings.cfg`. No game-save change — **no save-version bump** (audio + the mute flag are client settings, not game state).

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `SoundEventCatalogTests`, `MusicTrackCatalogTests` (every event/context resolves to a unique well-formed `res://…/*.ogg`; Menu ≡ InGamePeace playlist; war differs from peace and reuses only shipped tracks; every European nation resolves to an anthem; natives/REF/null → no anthem), `MusicContextSelectorTests` (null → Menu; fresh game → peace; colonial war → war; native-only war stays peace; unknown-id war ignored; CeaseFire/Peace/Alliance/Uncontacted → peace; real REF lifecycle: declaration → war, `GiveIndependence` → peace while the REF player remains) | ✅ |
| L2 Scenario | n/a | audio is presentation-reactive — no simulation behaviour to script | — |
| L3 Interaction | Yes (has UI) | `AudioWiringTests` (autoloads boot headless without crashing; `PlayUiClick` safe + buttons auto-wired; blocked action → `IllegalMove`; blocked cargo unload → `IllegalMove`; master mute persists + applies `SetBusMute(Master)`; playlist advances track-to-track; same-context `SetContext` does not restart; game start lands on `InGamePeace`; Menu→peace relabels without restarting; a forced war stance flips to the war playlist through `RefreshView`) + `SettingsScreenTests` (volume sliders apply to their buses) | ✅ |
| L4 Visual | If a screen changed | Settings screen gained a Mute row — covered by the existing settings golden when regenerated; no new golden added this wave | ⬜ |
| L5 Soak | Covered by global suite | — | — |

- **FreeCol cross-check:** the event→clip and context→track mappings mirror FreeCol's `sound.event.*` / default playlist / per-nation anthem naming (see the L1 catalog tests + `assets/freecol/*/PROVENANCE.md`). Real audio output is not asserted (no audio device in CI).

## 5. Open issues / TODO

- [ ] **War-music asset gap** (`86d3fq1wy` follow-up): the war context plays an interim 2-track subset of the existing CC-BY playlist. Source a dedicated, GPL-v2-compatible war/tension track (and optionally a distinct menu track), add it to `MusicTrackCatalog.WarPlaylist` + `PROVENANCE.md` + the Asset Register — no code change beyond the catalog list.
- [ ] A dedicated UI-click clip (currently the `illegal.ogg` deny clip doubles as the click) — map a distinct clip to a future `SoundEvent.UiClick` when one is sourced.
- [ ] Regenerate the Settings-screen golden to include the Mute row (deliberate golden update).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-07-03 | State-driven music contexts (`86d3fq1wy`): `MusicContext` → `Menu`/`InGamePeace`/`InGameWar`; new pure `MusicContextSelector` (war = human at `Stance.War` with any non-native power; native wars excluded; REF covered via stance, not existence); `SetContext` relabels without restarting when the catalog playlist is identical (seamless menu↔peace); interim 2-track war playlist from existing assets (war-track asset gap recorded); wired via `GameController.RefreshMusicContext` (StartGame + every RefreshView) and `MainMenu._Ready` | _(this commit)_ |
| 2026-06-27 | Initial documentation — audio wiring wave: event SFX, UI click + illegal-move buzz, looping background playlist + context seam + anthem, master mute + Music/SFX volume | _(86d3fq12n wave)_ |
