# Testing & QA Standards — Crown & Colony

Binding, same status as `DOCUMENTATION.md`. Defines the five-layer test pyramid, required coverage, and CI gates. "Tests pass" in this project means **behavior verified at every required layer**, never "it compiles."

> **Latest results + screenshots:** see [QA-REPORT.md](QA-REPORT.md) for a point-in-time snapshot (test counts per layer + the visual goldens embedded), and the [CI runs](https://github.com/BiffstaGaming/crown-and-colony/actions) for always-current pass/fail.
> **End-to-end journeys:** [TEST-PLAN.md](TEST-PLAN.md) specifies the connected player-journey tests (a `[Trait("Category","E2E")]` category within L2, plus one representative L3 scene journey) — milestone-asserted, not just end-state invariants.

## Foundational rule: determinism (ADR-009)

All randomness flows through a **seeded, injectable RNG** owned by the game state — no direct `Random`/`GD.Randf()` anywhere in game logic or presentation. Same seed + same inputs = identical game, always. This is what makes scenario tests (L2) and visual goldens (L4) reliable instead of flaky. Violating this rule breaks the entire QA strategy; treat it like a compile error.

## The five layers

| Layer | What | Tooling | Runs |
|---|---|---|---|
| **L1 Unit** | Rules, formulas, state transitions in `GameLogic` (engine-free C#) | xUnit | Every push |
| **L2 Scenario** | Scripted games run headlessly: real turns, asserted outcomes; FreeCol cross-checks | xUnit (still engine-free) | Every push |
| **L3 Interaction** | Real scenes driven by simulated input (clicks/keys/signals), asserting logic-layer effects | GdUnit4Net scene runner | Every PR |
| **L4 Visual** | Golden-screenshot diffs of key screens in curated deterministic states | Custom harness (headless render + image diff w/ tolerance) | Every PR |
| **L5 Smoke/soak** | Boot the real game; long AI-vs-AI autoplay; zero errors; turn-time perf budget | Headless Godot + script | Nightly |

### L1 — Unit (always required)
Pure xUnit against `GameLogic`. No Godot references permitted in the test project or the code under test. Target: every formula, rule, and edge case in the system doc's "Detailed rules" section has a test that pins it.

### L2 — Scenario (always required)
The heart of "actual testing". Fixture builders create game states ("colony with expert farmer on grassland"); tests run real turns through the real engine loop and assert outcomes ("10 turns → +60 food"). Includes:
- **FreeCol cross-checks**: same setup, expected outcome taken from FreeCol's behavior (documented in the system doc's Verification section).
- **Full-game sims**: AI vs AI to completion with invariant assertions (no negative goods, population conserved, no exceptions).

### L3 — Interaction (required for any feature with UI)
GdUnit4Net scene runner: load the actual scene, simulate input, await signals, assert that the logic layer received the right commands and the UI reflects state changes. State assertions live here — **not** in visual tests.

### L4 — Visual regression (required for each game screen)
- Goldens are PNGs committed under `game/tests/visual/goldens/`, rendered at a **fixed resolution, fixed theme, fixed seed, software renderer**.
- Each golden has a definition file from `templates/TEMPLATE-visual-test.md` (scene, fixture state, tolerance, what a human checks on failure).
- Diff with per-pixel tolerance + max-differing-pixel threshold to absorb minor AA/font noise; CI uploads baseline/actual/diff artifacts on failure.
- **Keep this suite small and targeted**: a handful of curated states per key screen (map, colony, Europe, …). If it can be asserted as state, it belongs in L1–L3, not here.
- Intentional UI change → regenerate goldens via the update script; new goldens are reviewed in the PR like any code change.

### L5 — Smoke & soak (global, nightly)
Boot to main menu and into a new game headlessly; autoplay N full AI games; assert zero errors/warnings-as-errors; assert per-turn time stays within the performance budget (budget defined when Phase 1 lands).

## CI gates (GitHub Actions)

- **Push to any branch:** L1 + L2. Red = broken, fix before anything else.
- **PR to main:** + L3 + L4. A red visual diff blocks merge until fixed or goldens are deliberately regenerated (regeneration must be visible in the PR).
- **Nightly:** L5 + full FreeCol cross-check suite.
- CI uses a software GPU (SwiftShader/OSMesa) for L3/L4 headless rendering.
- Planned guard: warn when `GameLogic` changes without a `docs/systems/` change in the same PR.

## Per-system coverage contract

Every system doc's **Verification** section carries this table (template updated accordingly):

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | | |
| L2 Scenario | Always | | |
| L3 Interaction | If the system has UI | | |
| L4 Visual | If the system has a screen | | |
| L5 Soak | Covered by global suite | — | — |

A feature is **not done** until its required layers are green in CI (see definition of done in `DOCUMENTATION.md` / `CLAUDE.md`).
