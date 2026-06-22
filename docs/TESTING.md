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
- **Current invariants (11), each FreeCol-sourced:**
  1. **Combat odds** = `attack / (attack + defence)` — `freecol/src/.../SimpleCombatModel.java:110`; modifiers `ATTACK_BONUS +50%`, `FORTIFIED +50%`, `model.tile.hills` defence `+100%` (classic spec). Fortified soldier (def 1+1) on hills vs armed brave (off 1+2): `4.5 / (4.5 + 6) = 0.4286`.
  2. **Combat resolution bands** — `SimpleCombatModel.generateAttackResult`: first 10% of the win range = great win, last 10% of the loss range = great loss; a seeded sample never leaves the four valid bands.
  3. **Market supply price** — `freecol/src/.../MarketData.java:322-324` `price()` supply formula; classic spec `model.goods.sugar` `initial-amount=1500 initial-price=2`. Selling 600 raises inventory 1500→2100, drops the bid 2→1, credits 1200 at 0% tax (driven end-to-end through `Game.SellColonyGoods`).
  4. **Native tension delta** — `freecol/src/.../Tension.java:45` `TENSION_ADD_LAND_TAKEN = 200`; taking land lifts a calm settlement's alarm by exactly 200, stepping the band HAPPY → CONTENT (`Tension.java:72-76` level limits).
  5. **Sons of Liberty + production bonus** — `freecol/src/.../Colony.java:1294` `calculateSoLPercentage` with `LIBERTY_PER_REBEL = 200` (`Colony.java:66`): SoL% = `floor(liberty·100 / (200·pop))`, clamped 0–100; the −2..+2 production-bonus tiers (`calculateProductionBonus`).
  6. **Calendar** — `Turn.getTurnYear`/`getTurnSeason` with `startingYear=1492, seasonYear=1600, seasons=2` — turn 1 = 1492, turn 109 = Spring 1600, turn 110 = Autumn 1600, turn 111 = Spring 1601.
  7. **Colony food growth threshold** — `freecol/src/.../Settlement.java:54` `FOOD_PER_COLONIST = 200`: the engine's `Colony.FoodForGrowth` equals 200, and a real turn on a colony banking ≥ 200 food grows by exactly one colonist (a 400-food stock yields one growth, never two).
  8. **Emigration crosses-required increment** — `freecol/src/.../Player.java:1267-1279` `updateImmigrationRequired` + classic-medium `crossesIncrement = 2`: a cross-producing colony reaches the target, emigrates one colonist, and the immigration target rises 15 → 17 (driven end-to-end through `EndTurn`).
  9. **Founding-father total cost** — `freecol/src/.../Player.java:1544` `getTotalFoundingFatherCost` + classic-medium `foundingFatherFactor = 40`: `(count==0) ? 40 : 2·(count+1)·40 + 1` → 40 / 161 / 241 / 321 for 0–3 elected (read off `Game.TotalFoundingFatherCost`).
  10. **Native land price** — `freecol/src/.../Player.java:3245` `getLandPrice` + classic-medium `landPriceFactor = 60`, `+100` base: `60 · Σ(tile potential of every good except the primary-food aggregate) + 100`, asserted against a real native-owned land tile.
  11. **Treasure transport fee + monarch tax ceiling** — `freecol/src/.../Unit.java:3797` `getTransportFee` (`treasureTransportFee = 60`%): a 1000-gold train cashed in at 0 tax banks `1000 − 600 = 400`. And `freecol/src/.../Monarch.java:480` `raiseTax` clamped to `taxMaximum()` = `MAXIMUM_TAX` (`Monarch.java:297`, classic-medium 65): a raise from the cap stays at 65, and a 200-seed sample never exceeds it.
- **CI gate:** these run on the every-push L2 gate (they are **not** in the soak category). No engine divergence has been found — neither when the harness was first written (5 invariants, 2026-06-21; the only initial failures were mistakes in the test's own expected values) nor when it was extended to 11 invariants (2026-06-22, Wave 7 follow-up): every cited FreeCol value matched our engine.

### L3 — Interaction (required for any feature with UI)
GdUnit4Net scene runner: load the actual scene, simulate input, await signals, assert that the logic layer received the right commands and the UI reflects state changes. State assertions live here — **not** in visual tests.

**Reset global input state between L3 cases (order-independence, kanban 86d3dyywj).** Godot's `Input` is a **process singleton**, not per-scene. Each `[TestCase]` loads a fresh scene, but the scene runner's `SetMousePos`/`SimulateMouseMove`/`SimulateKey*`/`SimulateMouseButton*` calls mutate that shared `Input`. State therefore bleeds between cases: the cursor stays parked at the previous case's last tile, and parsed button/key events can linger in the input buffer. A stale cursor makes the next case's first relative mouse-move start from the wrong origin, so a click can land on a corner-HUD overlay instead of the intended tile — an **order-dependent flake whose failing set shifts run-to-run**. Any L3 suite that drives input (clicks/keys) must reset `Input` in `[BeforeTest]` **and** `[AfterTest]`: parse a left-mouse-button *release*, parse a *release* for each key it presses, `Input.WarpMouse(Vector2.Zero)` to a neutral origin, then `Input.FlushBufferedEvents()`. `InputTests.ResetGlobalInputState` is the reference implementation; clicks additionally `FlushBufferedEvents()` after `SetMousePos` so the warp lands before the press. Use the one-click `SimulateMouseButtonPressed` (press+release) — never the holding `SimulateMouseButtonPress` — so no button is left flagged held.

### L4 — Visual regression (required for each game screen)
- Goldens are PNGs committed under `game/tests/visual/goldens/`, rendered at a **fixed resolution, fixed theme, fixed seed, software renderer**.
- Each golden has a definition file from `templates/TEMPLATE-visual-test.md` (scene, fixture state, tolerance, what a human checks on failure). Current definition sets: [map-goldens.md](visual-tests/map-goldens.md) (the map view), [menu-goldens.md](visual-tests/menu-goldens.md) (the front-end menus), and [ui-panel-goldens.md](visual-tests/ui-panel-goldens.md) (the in-game colony + Europe management screens).
- Diff with per-pixel tolerance + max-differing-pixel threshold to absorb minor AA/font noise; CI uploads baseline/actual/diff artifacts on failure.
- **Keep this suite small and targeted**: a handful of curated states per key screen (map, colony, Europe, …). If it can be asserted as state, it belongs in L1–L3, not here.
- Intentional UI change → regenerate goldens via the update script; new goldens are reviewed in the PR like any code change.

### L5 — Smoke & soak (global, nightly)
Boot to main menu and into a new game headlessly; autoplay N full AI games; assert zero errors/warnings-as-errors; assert per-turn time stays within the performance budget. Two perf gates, both in the `Category=Soak` suite the nightly runs (and excluded from the every-push gate):
- **Idle-tick budget** — `SoakTests.TurnProcessing_StaysWithinPerformanceBudget`: a single managed game then 1000 bare `EndTurn` ticks, average < 2 ms.
- **AI-autoplay turn-time gate** — `SoakTests.AiAutoplay_TurnTime_StaysWithinPerformanceBudget` (kanban 86d3dzdzr): a seeded full-game all-AI autoplay (`Game.New(seed)` + 250 `EndTurn`s over 5 seeds; the human idles, so every foreign-power economy/turn and native nation runs each round) asserting **both** the per-turn average **and** the total wall-time stay under budget, so a perf regression in the AI turn loop fails the nightly. Budget: **6 ms/turn average and 8 s total** — ~4× the dev-box measurement (~1.5 ms/turn, ~1.9 s for 1250 turns; 2026-06-22), generous headroom so a noisy CI runner can't flake it while an order-of-magnitude regression still trips it. Deterministic seeds; never drives the human, so it leaves stream 0 and all soak-asserted state untouched.

## CI gates (GitHub Actions)

- **Push to any branch:** L1 + L2. Red = broken, fix before anything else.
- **PR to main:** + L3 + L4. A red visual diff blocks merge until fixed or goldens are deliberately regenerated (regeneration must be visible in the PR).
- **Nightly:** L5 + full FreeCol cross-check suite.
- CI uses a software GPU (SwiftShader/OSMesa) for L3/L4 headless rendering.
- Planned guard: warn when `GameLogic` changes without a `docs/systems/` change in the same PR.

### Known CI host behaviour: GdUnit leak-at-exit (kanban 86d3c7yk3)

When the L3/L4 GdUnit job finishes, the **runner reports the test outcome correctly (all tests pass, exit 0 from the runner's point of view)**, but the Godot *host process* can still exit **non-zero** because Godot's object/RID/texture leak detector fires during process teardown and prints `Texture … leaked` / `N RIDs of type "CanvasItem" were leaked` / `ObjectDB instances leaked at exit`.

**Root cause (two compounding factors, neither a test failure):**
1. **Scenes are never freed.** GdUnit4Net's `ISceneRunner.Load(...)` defaults to `autoFree:false`, and `ISceneRunner.Dispose()` only calls `Free()` on the scene when `autoFree:true`. Our scene tests load a scene per `[TestCase]` and don't dispose the runner, so every loaded scene tree (its `CanvasItem`s and the textures it holds) stays alive until the process exits — at which point the renderer's `_free_rids` leak check reports them. Even a single trivial scene test leaks (≈14 `CanvasItem` RIDs + the scene's textures).
2. **C#/Godot finalizer timing.** Godot's C# bindings finalize `RefCounted` resources (`Texture2D`, `ImageTexture`, `StyleBoxTexture`, …) on the **.NET GC, which runs after the engine main loop has already exited** (Godot issues [#107579](https://github.com/godotengine/godot/issues/107579), [#84483](https://github.com/godotengine/godot/issues/84483)). Static texture caches (`ColonyArt._terrain`, `ColonyMarker.Settlement`, `NativeSettlementMarker.*`, `ColonyPanel._panelBackground`) hold process-lifetime refs that the detector also flags. This makes a fully-clean process exit essentially unachievable for a headless C# Godot host, independent of how carefully scenes are freed.

**Mitigation (in `ci.yml`, documented and bounded):** the scene-test step treats the **TRX as the source of truth, not the process exit code.** An attempt is a real pass iff the TRX shows tests ran with `failed=0` **and** `error=0`; in that case a non-zero host exit is logged as a warning and treated as success. A genuine test failure (`failed>0`/`error>0`) or a host crash/connect-timeout (no TRX produced) still fails the step and triggers the retry/`exit 1` path. **This masks only the leak-at-exit warning — never a real test failure.** If a future change ever needs the host to exit cleanly (e.g. a leak-count assertion), the leak source must be fixed first: pass `autoFree:true` to every `ISceneRunner.Load` *and* dispose the runner (`using`), plus clear the static texture caches on teardown.

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
