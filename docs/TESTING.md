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

#### Differential-fidelity harness (`[Trait("Category","Fidelity")]`)

A reusable L2 scaffold that runs a **scripted, seeded** scenario on our engine and asserts the observable outcome against a value **extracted from FreeCol's own source/data** — so a silent divergence from the reference behaviour fails CI rather than passing unnoticed. It lives in `game/tests/GameLogic.Tests/Scenarios/DifferentialFidelityTests.cs` and is the structural counterpart to the per-system FreeCol cross-checks: one place to drop a "this must still match FreeCol" guard rail.

- **Shape:** each case is a `FidelityInvariant<T>` — a name, the **FreeCol citation** it pins (file + line + the documented value), the scripted `Observe()` action that drives our engine, and the `Expected` outcome read straight off the FreeCol reference. A shared `Verify(...)` runner asserts equality and surfaces the citation in the failure message ("we drifted from FreeCol's documented X"). Adding a new cross-checked invariant is a few lines.
- **Determinism:** seeded RNG only (ADR-009), no wall-clock, no ambient state. Run the suite in isolation with `dotnet test … --filter "FullyQualifiedName~Fidelity"`.
- **Test-infrastructure only:** the harness changes no production code. If a case ever reveals a *real* fidelity bug (engine ≠ FreeCol), it is documented (here + the relevant system doc) and fixed in a separate production change — not patched inside the harness.
- **Current invariants (5), each FreeCol-sourced:**
  1. **Combat odds** = `attack / (attack + defence)` — `freecol/src/.../SimpleCombatModel.java:110`; modifiers `ATTACK_BONUS +50%`, `FORTIFIED +50%`, `model.tile.hills` defence `+100%` (classic spec). Fortified soldier (def 1+1) on hills vs armed brave (off 1+2): `4.5 / (4.5 + 6) = 0.4286`.
  2. **Combat resolution bands** — `SimpleCombatModel.generateAttackResult`: first 10% of the win range = great win, last 10% of the loss range = great loss; a seeded sample never leaves the four valid bands.
  3. **Market supply price** — `freecol/src/.../MarketData.java:322-324` `price()` supply formula; classic spec `model.goods.sugar` `initial-amount=1500 initial-price=2`. Selling 600 raises inventory 1500→2100, drops the bid 2→1, credits 1200 at 0% tax (driven end-to-end through `Game.SellColonyGoods`).
  4. **Native tension delta** — `freecol/src/.../Tension.java:45` `TENSION_ADD_LAND_TAKEN = 200`; taking land lifts a calm settlement's alarm by exactly 200, stepping the band HAPPY → CONTENT (`Tension.java:72-76` level limits).
  5. **Sons of Liberty + production bonus** — `freecol/src/.../Colony.java:1294` `calculateSoLPercentage` with `LIBERTY_PER_REBEL = 200` (`Colony.java:66`): SoL% = `floor(liberty·100 / (200·pop))`, clamped 0–100; the −2..+2 production-bonus tiers (`calculateProductionBonus`).
  - (Plus a **calendar** invariant: `Turn.getTurnYear`/`getTurnSeason` with `startingYear=1492, seasonYear=1600, seasons=2` — turn 1 = 1492, turn 109 = Spring 1600, turn 110 = Autumn 1600, turn 111 = Spring 1601.)
- **CI gate:** these run on the every-push L2 gate (they are **not** in the soak category). No engine divergence was found when the harness was first written (2026-06-21) — the only initial failures were mistakes in the test's own expected values, corrected before commit.

### L3 — Interaction (required for any feature with UI)
GdUnit4Net scene runner: load the actual scene, simulate input, await signals, assert that the logic layer received the right commands and the UI reflects state changes. State assertions live here — **not** in visual tests.

### L4 — Visual regression (required for each game screen)
- Goldens are PNGs committed under `game/tests/visual/goldens/`, rendered at a **fixed resolution, fixed theme, fixed seed, software renderer**.
- Each golden has a definition file from `templates/TEMPLATE-visual-test.md` (scene, fixture state, tolerance, what a human checks on failure).
- Diff with per-pixel tolerance + max-differing-pixel threshold to absorb minor AA/font noise; CI uploads baseline/actual/diff artifacts on failure.
- **Keep this suite small and targeted**: a handful of curated states per key screen (map, colony, Europe, …). If it can be asserted as state, it belongs in L1–L3, not here.
- Intentional UI change → regenerate goldens via the update script; new goldens are reviewed in the PR like any code change.

### L5 — Smoke & soak (global, nightly)
Boot to main menu and into a new game headlessly; autoplay N full AI games; assert zero errors/warnings-as-errors; assert per-turn time stays within the performance budget (average `EndTurn` < 2 ms, enforced by `SoakTests.TurnProcessing_StaysWithinPerformanceBudget`).

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
