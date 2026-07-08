# System: Opening Cinematic (new-game intro)

| | |
|---|---|
| **Status** | Implemented |
| **Last verified** | 2026-07-08 (variant-aware beats) |
| **Code** | `game/presentation/OpeningCinematic.cs`, `game/presentation/MainMenu.cs` (wiring), `game/presentation/SettingsScreen.cs` (toggle), `game/src/GameLogic/App/SettingsModel.cs` (`PlayIntro`), `game/src/GameLogic/Specification/GameVariant.cs` (`OpeningBeats`, per-variant beats) |
| **Tests** | `game/presentation/tests/OpeningCinematicTests.cs` (L3), `game/tests/GameLogic.Tests/App/SettingsModelTests.cs` (L1), `game/tests/GameLogic.Tests/Specification/AustraliaReskinTests.cs` (L1 — variant beats) |
| **FreeCol reference** | FreeCol has only a static splash (`Canvas`/`MainPanel` background), no cinematic — this is the faithful *Colonization 1994* intro idea, kept asset-safe (text on the existing parchment/map art). |
| **Related systems** | [settings.md](settings.md), [presentation module](../modules/presentation.md) |

## 1. How it works (plain English)

*Audience: anyone — no jargon, no class names.*

When you start a **brand-new game** from the main menu, a short atmospheric intro plays before the map appears: a few narrative "story" panels that set the scene, each fading gently in and out over the old-map/parchment background the menu already uses. Then the game board appears and play begins.

**The story matches the world you chose.** In the classic Colonial-America game the panels tell the 1492 expedition scene — the King's charter, the ocean crossing, the landfall, the send-off. In the **Australian Federation** game they instead tell that world's story: the First Fleet's 1788 arrival at Sydney Cove, the hard early penal years, the spread into six colonies, and the road to Federation in 1901. (The Australian text is written soberly and honestly — it acknowledges that the British arrived on a continent already home to many peoples, and does not glorify that; see doc 19's tone rules.)

The intro is **always skippable**. A **click** jumps to the next panel; the **Skip ▸** button or the **Esc** key ends the whole thing at once and drops you straight into the game. You are never trapped waiting.

If you'd rather never see it, turn it off: **Settings → Game → "Play opening intro"**. With it off, confirming a new game goes straight to the map with no intro at all. It's on by default.

**The rules, in plain words:**
- The intro plays only when you start a **new** game from the menu — not when you load a save, and not in the middle of play.
- It shows a fixed, short sequence of narrative panels — **the same every time for a given world** (classic shows the 1492 panels; the Australian Federation shows its 1788→1901 panels) — then hands off to the game.
- Any click advances; Skip or Esc ends it immediately.
- The "Play opening intro" setting (on by default) decides whether it plays at all. It is a personal preference saved with your other settings — it is **not** part of a saved game.

**Worked example:**
> You click **New Game**, choose your options, and press **Start**. The map fades to a parchment panel reading *"In the year of our Lord 1492, the Crown grants you a charter…"*. You watch two panels, then press **Esc** — the intro ends and your colonists' ship appears on the ocean, ready for turn one. Next time, you untick "Play opening intro" in Settings, so **Start** takes you straight to the map.

**What the player sees and does:** the intro overlay (backdrop + a centred parchment text panel + a "Skip ▸" button + a "Click or press Esc to skip" hint). Clicking advances a beat; Skip/Esc ends it. The Settings screen's Game section gains a "Play opening intro" toggle.

## 2. Detailed rules

*Audience: designers/testers — exact, but still readable.*

| Input / condition | Result |
|---|---|
| Confirm New-Game dialog, "Play intro" **on** | The `OpeningCinematic` overlay is shown; the game boots only when it finishes/skips. |
| Confirm New-Game dialog, "Play intro" **off** | No overlay; the game scene boots immediately (byte-identical to the pre-cinematic flow). |
| No `/root/Settings` autoload present (bare test scene) | Treated as "off" — boots straight to the game, so headless flows are never delayed. |
| **Left-click** anywhere on the overlay | Advances to the next beat; on the last beat, ends the cinematic. |
| **Skip ▸** button | Ends the cinematic immediately (→ game). |
| **Esc** (`ui_cancel`) | Ends the cinematic immediately (→ game). |
| Cinematic plays through all beats | Ends after the last beat's fade-out (→ game). |
| Any end trigger firing twice | Ignored after the first — the hand-off to the game happens exactly once. |
| Load Game / in-game / continue | Never shows the cinematic (it is only on the interactive *New Game* path). |

- **Beats:** a fixed ordered list of 4 narrative strings, each fading in (0.8s), holding (2.6s), fading out (0.7s) via a `Tween`. The full run is ~16s if unskipped; a click cuts each beat short. **The beats are per-variant** (`GameVariant.OpeningBeats`): classic's are the 1492 scene (charter → crossing → landfall → send-off); the Australian Federation's are its 1788→1901 arc (First Fleet landing → hard penal years → six colonies → road to Federation). The host injects the selected variant's beats via `OpeningCinematic.SetBeats(...)` before `Play()`; unset → the default (classic) beats.
- **Determinism (ADR-009):** the beats and timings are constants — no `Random`/`GD.Randf()`. The same new game (same variant) always shows the same sequence.

**Deviations from original 1994 / FreeCol behavior:** *Colonization 1994* had a produced intro cinematic with art/animation; ours is a tasteful text-on-parchment sequence (no new assets — licensing). FreeCol has no cinematic at all (only a static splash), so this is an addition, not a port. Both deviations are deliberate: asset-safety and faithfulness to the *idea* of an opening sequence.

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Domain model:**
- `OpeningCinematic` (`Control`, presentation-only) — builds its surface in code (backdrop `TextureRect` reusing `res://assets/freecol/ui/map.jpg`, a dim `ColorRect` vignette, a centred parchment `PanelContainer` + beat `Label`, a `SkipButton`, a hint `Label`). `Play()` shows it and starts the first beat; `PlayBeat(i)` drives the fade-in/hold/fade-out `Tween` chain and recurses to `i+1`; a one-shot `Finish()` raises the `Finished` signal. Its beats are an injectable instance field `_beats` (defaulting to `GameVariants.Default.OpeningBeats` — the classic 1492 beats) set via `SetBeats(IReadOnlyList<string>?)` before `Play()`; a null/empty list is ignored (the classic default stays). It owns **no game state** (ADR-006) and adds no RNG (ADR-009).
- `GameVariant.OpeningBeats` (`IReadOnlyList<string>`, engine-free, `GameLogic/Specification`) — a defaulted per-variant display field (sibling of `CongressName` etc., ADR-018). Classic-defaults to `GameVariants.ClassicOpeningBeats` (the 1492 beats — the shared byte-identical default for any variant that supplies none); `GameVariants.Australia` supplies `AustraliaOpeningBeats` (the 1788→1901 arc). A display-only reskin: the cinematic machinery is variant-agnostic; only the text differs.
- `SettingsModel.PlayIntro` (engine-free, `GameLogic/App`) — the persisted "Play intro" client option (default `true`), round-tripped through `SettingsService`'s `settings.cfg` like every other client option.

**Data sources:** no XML/ruleset data. The backdrop is the existing menu art; the beat text is original in-code prose (GPL-clean), held per-variant on `GameVariant.OpeningBeats`. The Australian beats follow doc 03 (the 1788–1901 story arc) and doc 19's tone rules (sober, historically honest — the arrival came to a land already home to many peoples; no triumphalism).

**Algorithms & formulas:** none beyond the fixed fade timings (`FadeIn`/`Hold`/`FadeOut` constants in `OpeningCinematic`).

**Integration points:**
- `MainMenu.OnNewGame` collects the New-Game dialog picks onto the existing `GameController.Pending*` / `NewGameDialog.Pending*` statics, then calls `StartGameAfterIntro()`.
- `MainMenu.StartGameAfterIntro()` reads the gate `ShouldPlayIntro()` (`internal`, reads `/root/Settings` → `SettingsModel.PlayIntro`). Off → `ChangeSceneToFile(GameScenePath)` at once. On → show an `OpeningCinematic`, first calling `SetBeats((GameController.PendingVariant ?? GameVariants.Default).OpeningBeats)` so the intro matches the chosen world, then `Play()`; its `Finished` signal calls `ChangeSceneToFile(GameScenePath)` **exactly once**. (`PendingVariant` is set by `NewGameDialog.OnStart` before this runs; the `?? Default` fallback covers any path that hasn't picked one.)
- **Crucially, the cinematic is NOT injected into `GameController.StartNewGame`** — that method (called directly by many L3 tests and by the New-Game dialog's boot) is unchanged in signature and behaviour, so the L3 fast path and the goldens that call it stay intact.

**Persistence:** only the `PlayIntro` client option is persisted (in `settings.cfg` via `SettingsService`). **The save format is untouched** (`SaveGame.CurrentVersion` unchanged) — the cinematic threads no game state.

## 4. Verification

*How we know this works — the testing contract for this system (see `docs/TESTING.md` for layer definitions).*

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `SettingsModelTests` — `PlayIntro` default (true), round-trip, missing-key fallback; `AustraliaReskinTests` — `OpeningBeats` differs classic vs Australia (both non-empty; classic opens on 1492, Australia on 1788) and the classic default is the shared instance | ✅ |
| L2 Scenario | N/A (no rules/turn logic) | — | — |
| L3 Interaction | Yes (it has UI) | `OpeningCinematicTests` — shows the first beat; Skip/Esc/click-through raise `Finished` once; injected Australia beats make the first panel the 1788 scene (not 1492); the MainMenu New-Game path shows the cinematic when the setting is on and gates it (`ShouldPlayIntro`) when off | ✅ |
| L4 Visual | Not added | The cinematic is a transient, time-varying fade sequence (no stable frame to golden); the `settings-screen` golden was regenerated for the new "Play intro" row | ✅ (settings golden regen) |
| L5 Soak | Covered by global suite | — | — |

- **FreeCol cross-check:** not applicable — FreeCol has no cinematic (static splash only); this is a faithful nod to the 1994 intro, not a port.

## 5. Open issues / TODO

- [ ] Optional: a war/menu **music** cue under the intro is deliberately out of scope here (a separate audio task) — the cinematic plays silent.
- [ ] Optional future polish: per-beat background art or a Ken-Burns pan, if/when license-clear art is available (none added now — asset-safety).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-07-08 | Variant-aware beats: added `GameVariant.OpeningBeats` (classic-defaulted to the 1492 beats; Australia supplies its own sober 1788→1901 arc per docs 03/19). `OpeningCinematic` now plays injected beats (`SetBeats`), and `MainMenu` passes the selected variant's beats. Classic text byte-identical; determinism/skip unchanged. Fixes the playtest bug where the American 1492 story played for the Australian Federation. | _(this branch)_ |
| 2026-07-03 | Initial: skippable new-game opening cinematic (4 fade beats), wired into the interactive New-Game path; "Play intro" settings toggle (default on); save format untouched. | `86d3fq1kf` |
