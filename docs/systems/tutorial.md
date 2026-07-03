# System: Guided-intro tutorial

| | |
|---|---|
| **Status** | Implemented (bounded / curated — 5 steps, see scope note) |
| **Last verified** | 2026-07-03 @ `TutorialService` (`86d3fq1h9`) |
| **Code** | `game/presentation/TutorialService.cs` (step sequence + advance logic); `game/presentation/TutorialPanel.cs` (the card); wired from `game/presentation/GameController.cs`; preference on `game/presentation/SettingsService.cs` |
| **Tests** | `game/presentation/tests/TutorialTests.cs` (L3) |
| **FreeCol reference** | FreeCol ships a **tutorial** as a data mod (`data/mods/tutorial/`) plus a client `tutorial` option: scripted `ModelMessage`s fire as the player reaches early milestones (first colony, first cargo, etc.). Col1 personified this as a tutorial advisor. We model the *useful part* — a short scripted sequence of contextual tips keyed to observable game state — without the mod machinery. |
| **Related systems** | [advisor.md](advisor.md), [settings.md](settings.md), [colonies.md](colonies.md), [hud-input.md](hud-input.md) |

## 1. How it works (plain English)

A brand-new player starting their first game sees a small **tutorial card** in the top-centre of the screen. It shows one friendly tip at a time, and each tip is tied to the next thing you should do. As you actually do that thing, the card moves on to the next tip. It teaches the core opening loop — explore, get a colonist ashore, found a colony, put colonists to work, end your turn — and then it's done and never bothers you again.

It is deliberately **non-nagging**: each tip appears once, the card advances the moment you reach the milestone (it never re-shows an earlier tip), and you can turn the whole thing off at any time.

**The rules, in plain words:**
- The tutorial is a fixed, ordered list of **five** tips:
  1. **Welcome** — "Click a unit, move it, explore; sail toward land to unload your colonists."
  2. **Send a colonist ashore** — get a land colonist onto the map (off the ship).
  3. **Found your first colony** — when a colonist stands on suitable land, found a colony (press B).
  4. **Put your colonists to work** — open your colony and assign colonists to produce goods.
  5. **End your turn** — press End Turn (or Enter) to advance; that's the core loop.
- Each tip **advances when its goal is met**: e.g. the "found a colony" tip disappears the instant you have a colony; the "put colonists to work" tip advances once you open a colony; the final "end your turn" tip completes the tutorial once you end a turn.
- If you're already past a step when the game starts (the classic start has colonists on the map from turn one), the tutorial **skips straight to the first tip that still applies** — you never see a tip for something you've already done.
- Two buttons on the card: **"Got it"** moves to the next tip manually; **"Skip tutorial"** turns the whole tutorial off (and remembers that choice for future games).
- The card **hides itself** whenever a full-screen screen (a colony, Europe, the pause menu, etc.) is open, so a tip never floats over another window.
- You can turn the tutorial on or off in **Settings → Tutorial hints** (default on).

**Worked example:**
> You start a new game. Your ship and colonists are already on the map, so the tutorial skips the welcome/ashore tips and shows **"Found your first colony"**. You move a colonist onto grassland and press B — a colony appears, and the card immediately switches to **"Put your colonists to work"**. You click the colony to open it, assign a farmer, and close it; the card is now **"End your turn to continue"**. You press End Turn — the card thanks you and disappears for the rest of the game.

**What the player sees and does:** a compact parchment card top-centre (a heading, a one- or two-sentence instruction, and "Got it" / "Skip tutorial" buttons). It appears on a fresh game and updates itself after every action.

> **Scope note (read this):** this is a **deliberately small, curated** guided intro — five hand-written tips keyed to cheaply-observable state, not FreeCol's full data-driven tutorial mod (which scripts many messages across the whole early game). A richer tutorial (more steps, per-screen hints, a re-playable lesson mode) is a **follow-up**. We added **no game rules** for the tutorial — every step predicate reads an existing public oracle.

## 2. Detailed rules

*Audience: designers/testers — exact, but still readable.*

`TutorialService` walks an ordered `IReadOnlyList<TutorialStep>`. Each `TutorialStep` has a stable `Kind`, the `Title`/`Body` to render, and a `GoalMet(Game, TutorialProgress) → bool` predicate. `TutorialService.Evaluate(Game)` advances the internal index past every step whose goal is already met (or that the player dismissed with "Got it"), then returns the current step to display, or `null` when the tutorial is complete / between steps. It is a **pure read** over game state (ADR-006) — no mutation, no RNG.

**Steps, in fixed order:**

| # | Kind | Advances when (`GoalMet`) | Card text (summary) |
|---|---|---|---|
| 1 | `Welcome` | the human has a land unit on the map (`PlayerUnits.Any(IsOnMap && !Type.IsNaval)`) | "Welcome to the New World" — select, move, explore, unload |
| 2 | `MoveAshore` | same land-unit-on-map condition (only shown if step 1's wasn't already met) | "Send a colonist ashore" |
| 3 | `FoundColony` | the human owns a colony (`Colonies.Any(OwnerId == HumanPlayer.PlayerId)`) | "Found your first colony" (press B) |
| 4 | `OpenColony` | the player has opened a colony panel (`TutorialProgress.ColonyOpened`, set by `NotifyColonyOpened`) | "Put your colonists to work" |
| 5 | `EndTurn` | the player has ended a turn while this step was showing (`TutorialProgress.TurnEnded`, set by `NotifyTurnEnded`) | "End your turn to continue" |

**`TutorialProgress`** carries the two UI actions that are not visible in game state alone — `ColonyOpened` and `TurnEnded`. The controller flips them via `NotifyColonyOpened()` / `NotifyTurnEnded()`. `TurnEnded` is **reset each time a step advances**, so step 5's "end a turn" counts a turn ended *while step 5 is showing*, not one the player happened to end earlier (a defeat/robustness detail).

**Notes / edge cases:**
- **Never rewinds.** The index only increases; a step shown and passed never returns. Each tip shows once (non-nagging).
- **Skip-ahead.** If several early goals are already met (classic start), `Evaluate` advances past all of them in one call and shows the first applicable tip — so a normal new game opens on "Found your first colony".
- **Steps 3 and 4 do not collide.** Founding a colony satisfies step 3; step 4 needs a *distinct* action (opening a colony), so the "work your colony" tip is not skipped the instant a colony appears.
- **"Got it"** (`DismissCurrent`) advances one step even if its game-state goal is unmet; **"Skip tutorial"** (`Skip`) marks the whole sequence complete **and** flips the client preference off (persisted) so it stays off for future games.
- **Disabled / hidden.** When the `SettingsService.TutorialHints` preference is off, or a full-screen panel / the pause menu is open, or the human is defeated, the card is hidden and no step advances from that refresh.
- **Per game.** The controller builds a **fresh `TutorialService`** on every `StartGame` (new *or* loaded), so a reload restarts the sequence from wherever the loaded state sits (a loaded mid-game with a colony opens on the "end your turn" tip, etc.). The tutorial's progress is **not** saved (it's a UI convenience), only the on/off preference is.

**Deviations from original 1994 / FreeCol behavior:**
- **Not the FreeCol tutorial mod.** FreeCol scripts its tutorial as a data mod of `ModelMessage`s across the early game; we ship a curated five-tip in-code sequence instead. *(Scope decision — the mod pipeline is out of scope for the base game; recorded so the gap is visible.)*
- **No advisor characters / portraits.** Col1 personified the tutorial as an advisor character; we show a plain parchment card (matching our [advisor.md](advisor.md) treatment). *(Cosmetic/UX choice.)*
- **Bounded, one-shot.** No re-playable lesson mode, no per-screen contextual help beyond these five milestones. *(Follow-up.)*

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Domain model (all presentation — ADR-006):**
- `TutorialStepKind` (enum, `Welcome`/`MoveAshore`/`FoundColony`/`OpenColony`/`EndTurn`) — the stable, wording-independent tag the UI/tests key off; the enum order is display order.
- `TutorialProgress` (`readonly record struct`, `bool ColonyOpened`, `bool TurnEnded`) — the observed UI-action flags a step's goal reads alongside the live `Game`, so every `GoalMet` predicate stays a pure function (no hidden capture) and is unit-testable in isolation.
- `TutorialStep` (`sealed record`, `Kind`, `Title`, `Body`, `Func<Game, TutorialProgress, bool> GoalMet`) — one step.
- `TutorialService` — engine-light (no Godot base type) so it is trivially testable and owned as a plain field by `GameController`. Holds the ordered steps + the current index + a per-step `_dismissedCurrent` flag + the two observed-action flags. `Evaluate` / `DismissCurrent` / `Skip` / `NotifyColonyOpened` / `NotifyTurnEnded` are its whole surface; `DefaultSteps()` is the shipped list (also the test seam).
- `TutorialPanel : PanelContainer` — the **code-built** card (built in `_Ready`, no `.tscn`), parchment skin (`ColonyArt.ParchmentSkin()`) + in-game theme, anchored top-centre. `ShowStep(step)` fills the title/body and reveals it; `HideCard()` hides it; the "Got it" / "Skip tutorial" buttons raise the `GotIt` / `SkipRequested` signals.

**Data sources:** none of its own. Indirectly the live `Game` state (`PlayerUnits`, `Colonies`, `HumanPlayer`, `Turn`) and unit-type flags (`Type.IsNaval`, `Type.CanFoundColony`).

**Algorithms & formulas:** no formulas — `Evaluate` is a `while` loop that advances the index while the current step's `GoalMet` is true (or it was dismissed), resetting the per-step turn-ended flag on each advance. Ordering is fixed by the append order in `DefaultSteps`.

**Integration points (in `GameController`):**
- `_tutorial` (a `TutorialService` field) is rebuilt in `StartGame` (fresh per new/loaded game). `_tutorialPanel` is created in `_Ready`, added under the `UI` CanvasLayer, and positioned top-centre in `LayoutHud` (clear of the top status strip, the top-left advisor card, and the bottom HUD).
- `RefreshTutorial()` is called from `RefreshView` (the universal post-action hook, so a milestone reached this action advances the card at once) **and** from each full-screen panel's + the pause menu's `VisibilityChanged` (so the card hides while a screen is open and re-appears/advances when it closes). It reads `SettingsService.TutorialHints`; if disabled / an overlay is open / the human is defeated it hides the card, else it shows `_tutorial.Evaluate(_game)` (or hides on `null`).
- `OpenColonyPanel` calls `_tutorial.NotifyColonyOpened()`; `OnEndTurnPressed` calls `_tutorial.NotifyTurnEnded()`.
- The card's `GotIt` → `_tutorial.DismissCurrent()` + refresh; `SkipRequested` → `_tutorial.Skip()` + `SettingsService.SetTutorialHints(false)` + `Save()` + refresh.

**Persistence:** the tutorial's **progress is never saved** (it's transient UI, no save-version bump). The **on/off preference** is a client setting: `SettingsService.TutorialHints` (default on), persisted in `user://settings.cfg`'s `[settings]` section under `tutorial_hints` — held on `SettingsService` (not the engine-free `SettingsModel`), exactly like the master-mute flag, so no model change and no save bump.

## 4. Verification

*How we know this works — the testing contract for this system (see `docs/TESTING.md` for layer definitions).*

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | The step-advance logic lives in the presentation project (references Godot types indirectly via `Game`), so it is exercised at L3 rather than the engine-free L1 xUnit suite; the service's advance/skip/never-rewind behaviour is asserted directly in `TutorialTests` (service-level cases) | ✅ (as L3) |
| L2 Scenario | Always | Covered by the L3 service-level cases driving a real seeded `Game` through found-colony → open-colony → end-turn | ✅ |
| L3 Interaction | If the system has UI | `TutorialTests` (10): panel renders a step's title/body + reveals; "Got it"/"Skip" raise their signals; the service advances through the sequence as goals are met and never rewinds; dismiss advances past an unmet step; the card shows the first applicable step on start; founding a colony advances the card; opening a colony advances the card; a disabled preference shows nothing; "Skip" hides the card + persists the preference off (reloaded from disk); the Settings **Tutorial hints** toggle flips the preference | ✅ |
| L4 Visual | If the system has a screen | No dedicated tutorial golden — the card is hidden in the camera-centred map goldens (they hide the whole `UI` layer) and behind the pause-menu golden (the card hides while the pause menu is open). The **settings-screen** golden was **deliberately regenerated** for the new "Tutorial hints" toggle row (see the changelog / regeneration note). | ✅ (no churn beyond the intended settings row) |
| L5 Soak | Covered by global suite | — (read-only, no game evolution) | — |

- **FreeCol cross-check:** not a numeric cross-check — the tutorial adds no rule. Each step's goal reuses the same state/oracle the corresponding action uses (`Colonies`/`CheckFoundColony`, colony-open, turn-end), so a tip cannot claim something the game would refuse.
- **Golden regeneration note:** the `settings-screen` golden was regenerated on the Windows dev host to capture the new toggle row (17.6% legitimate structural diff — the added control, not raster noise). Per project convention (see `docs/modules/presentation.md`), the authoritative UI goldens are regenerated on the CI Linux renderer; **re-run `GOLDEN_UPDATE=1` for `settings-screen` on CI if the Windows-baked PNG shows raster drift there.** All other goldens are unchanged.

## 5. Open issues / TODO

- [ ] Richer tutorial (follow-up): more steps (first cargo sold, first recruit, first attack), per-screen contextual hints, and a re-playable lesson mode — closer to FreeCol's full tutorial mod. Would likely warrant a data-driven step list rather than the in-code sequence.
- [ ] A New-Game dialog "start with tutorial" checkbox (the Settings toggle is the shipped control; the task's "and/or" made the dialog toggle optional). Add as a `NewGameDialog` option mirroring the other `Pending*` dials if desired.
- [ ] Consider surfacing the tutorial's step text through the localisation pass (`86d3fq1w6`) when that lands.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-07-03 | Initial documentation + implementation: the code-built `TutorialService` (5-step ordered sequence — welcome / ashore / found-colony / open-colony / end-turn, each advancing on an observable game-state or UI-action goal, never rewinding) + the dismissible `TutorialPanel` card, wired from `GameController` (top-centre, hidden behind any full-screen panel / the pause menu). On/off via the new `SettingsService.TutorialHints` client preference (Settings toggle + the card's "Skip tutorial" button; default on, persisted, **no save bump**). `86d3fq1h9`. +10 L3 (`TutorialTests`). Settings-screen golden regenerated for the new toggle; no other golden churned. | _(this commit)_ |
