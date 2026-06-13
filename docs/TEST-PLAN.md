# Test Plan — End-to-End Journeys

Binding companion to [TESTING.md](TESTING.md) (the five-layer pyramid) and [QA-REPORT.md](QA-REPORT.md) (latest results). This plan defines the project's **end-to-end (E2E) tests**: connected player journeys verified as one flow, with milestone assertions at every step.

## 1. Purpose & scope

The five-layer pyramid proves each *piece* works. It did **not**, until this plan, prove that the pieces *connect* — that founding a colony actually leaves it with the buildings the economy tick needs, that produced goods actually reach the treasury, that bells actually become an elected father. An E2E test drives one **complete player journey** through the real `Game`/`Colony` API (or the real Godot scene) and asserts that **each step's output is the next step's input**.

E2E differs from the existing layers:
- **L2 scenario** tests run real turns but assert *broad end-state invariants* ("no negative stores", "population conserved"). They run the journey but don't check the links.
- **L3 interaction** tests verify the UI wiring of *one action in isolation* (a click moves a unit).
- **E2E** asserts the *chain*: new game → reveal → move → explore → found → auto-assign → produce → sell → elect, with an assertion proving each handoff.

## 2. Where E2E sits in the pyramid

E2E is **not a sixth layer** — it is a **category within existing layers**, tagged `[Trait("Category","E2E")]` (xUnit) / a dedicated GdUnit suite:

- **Logic-level E2E (primary)** lives in L2: `game/tests/GameLogic.Tests/Scenarios/JourneyTests.cs`. Engine-free, deterministic by seed, runs on **every push** with the other L1/L2 tests. The bulk of journey coverage lives here because it's fast, stable, and exercises the *same* `Game` API the UI calls.
- **Scene-level E2E (one representative)** lives in L3: `game/presentation/tests/JourneyE2ETests.cs`. Drives the real scene end-to-end (new → select → move → found → open panel → staff → end turn) to prove the **UI→logic seam holds across a multi-step flow**, not just per button. Exactly one per playable screen — the seam, not the rules, is what L3 uniquely proves.

CI gates (unchanged): push = L1+L2 (incl. logic E2E); PR = +L3+L4 (incl. scene E2E); nightly = L5.

## 3. Principles

1. **Determinism (ADR-009):** every journey is seed-pinned; pinned seeds are recorded here. A map-gen change that shifts a pinned seed makes the *milestone* assertions localize exactly which link broke.
2. **Milestone assertions:** assert after every step that its output is correct *and* feeds the next step — not just a final invariant.
3. **Guard, don't assume:** map-dependent steps (a start tile boxed in by water; a pop-1 colony with no grain neighbour) are *branched on*, not hard-asserted, so the journey stays green across seeds while still asserting the chain whenever the path exists.
4. **One acid-test round-trip per journey:** end each journey with `SaveGame.From(g).ToJson()` round-tripping byte-identical (the project's save acid test); Journey 4 takes it *after* an election.
5. **No Godot in logic-level E2E:** keeps the bulk fast and flake-free.

## 4. Journey catalogue

| # | Journey | Priority | Layer | Status |
|---|---|---|---|---|
| 1 | Explore & found a colony | P0 | L2 (+L3 slice) | ✅ buildable now |
| 2 | Colony economy cycle (raw → refine → construct → grow) | P0 | L2 | ✅ buildable now |
| 3 | Trade to treasury (produce → sell → price → tax → gold) | P1 | L2 | ✅ buildable now |
| 3b | Full trade voyage (load → sail → sell in Europe → return) | P1 | L2 | ✅ buildable now (sailing shipped) |
| 4 | Liberty & sequential Founding-Father elections | P1 | L2 | ✅ buildable now (effects out of scope) |
| 5 | Scene E2E: new → select → move → found → panel → staff → tick | P0 | L3 | ✅ buildable now |
| 6 | Immigration & recruitment (accrue → emigrate → penalty → recruit) | P2 | L2 | ✅ buildable now (immigration shipped) |
| 7 | A recruit reaches the New World (board → sail → disembark → found) | P2 | L2 | ✅ buildable now (transport shipped) |
| 8 | An elected father grants its bonus (choose → elect → boosted production) | P2 | L2 | ✅ buildable now (modifier system shipped) |

## 5. Journey 1 — Explore & found a colony *(golden path, built first)*

Seed `424242` (proven to found cleanly). Milestones, each asserted:
1. **New game** — turn 1, exactly 1 unit on explored non-water settleable ground, `Explored.Count ≥ 4`, all explored tiles in bounds.
2. **Explore** *(guarded)* — if a legal neighbour move exists: `MoveUnit` succeeds, `Explored.Count` strictly increases, every occupied tile stays revealed (no fog violation). If boxed in: found from the start tile (legal).
3. **Found is legal** — `CheckFoundColony(unit).Allowed`.
4. **Found** — founder consumed (`Units.Count == 0`), exactly one colony at the founding tile, population 1, named, no current build.
5. **Free base buildings present** — every ruleset building with no cost and no upgrade-from (town hall, pasture, …) is in the colony, derived from the ruleset not hard-coded.
6. **Auto-assignment** *(branched, pop 1)* — `TileWorkers.Count ≤ 1`; if assigned, it's a grain tile adjacent to the colony with positive yield and 0 idle.
7. **Economy tick + liberty** — `EndTurn`: turn 2, no negative stores, **1 liberty** (town hall rang a bell → liberty), bells drained from the warehouse.
8. **Save/load acid test** — round-trips byte-identical; colony and liberty preserved.

Plus a **determinism guard**: two identical decision sequences on `Game.New(Classic, 424242)` produce equal save JSON.

## 6. Journey 2 — Colony economy cycle

Deterministic fixture (a hand-built all-plains map via `SaveGame.Restore`, no map RNG), pop 2 colony. Milestones:
1. **Assign + produce + refine in one tick** — colonist A → a grain tile (yield 5); colonist B → carpenter's house; seed lumber. `EndTurn`: food = centre 3 + farm 5 − eat 4 = 4; **3 lumber consumed → 3 hammers produced**; cotton from the centre square untouched. *Proves raw tile yield and building refinement both feed the warehouse in one connected tick.*
2. **Construct** — `SetBuild` a buildable; seed its hammer cost; `EndTurn` completes it (`HasBuilding` true, materials consumed).
3. **Grow** — seed 200 food; `EndTurn`: population +1, newborn auto-assigned.
4. **Round-trip** — byte-identical.

## 7. Journey 3 — Trade to treasury

Sugar seeded into the colony store stands in for accumulated production (Journey 2 already proves production). Milestones:
1. **Sell** — `SellColonyGoods(colony, sugar, n)`: store → 0, `Gold` increases by exactly the returned credited amount.
2. **Price moves** — `Market.AmountInMarket(sugar)` increased, `BidPrice(sugar) ≤` its initial.
3. **Tax** — with non-zero `startingTax`, credited gold equals integer-truncated post-tax revenue (cross-checks the tax math inside a full journey).
4. **Round-trip** — gold + moved market preserved.

> **Shipping leg (blocked):** the produce→sell→treasury chain is buildable now via the slice-1 `SellColonyGoods` abstraction. The *load-cargo → sail → voyage-time* leg is blocked on ships sailing to Europe (P4 slice 3) and will be appended to this journey when that ships.

## 8. Journey 4 — Liberty & sequential elections

Fully deterministic (town hall = 1 bell/turn). Milestones:
1. **First election** — found colony; `ChooseFather(offered[0])`; end turns until elected: `Congress == [father1]`, liberty reset, new offers exclude father1.
2. **Cost escalation asserted directly** — `TotalFoundingFatherCost()` returns **97** (not 24) after the first election.
3. **Second election** — choose another; end turns until the cost-97 threshold: `Congress.Count == 2`, both retained, neither re-offered.
4. **Round-trip *after* an election** — byte-identical (no current test does this).

## 9. Journey 5 — Scene-level E2E (L3)

The single representative scene journey, reusing the `ClickTile`/`FindButton`/`GameOf` helpers from `InputTests`/`ColonyPanelTests`:
new game → click unit (selected) → click adjacent tile (moved) → press B (founded) → open colony panel → staff a building via its `+` button → end turn → assert the staffed building produced. Proves the UI→logic seam across a flow, which L2 cannot see.

## 9b. Journey 6 — Immigration & recruitment (L2)

Seed `42`, `startingGold 1000` (founds cleanly). Milestones, each asserted as a connected chain:
1. **New game + found** — the dock holds three recruitable types; pool 0, target 15, price 200.
2. **Accrual feeds Europe** — 3 immigration/turn (1 chapel cross + 2 player bonus); no emigrant at 12, exactly one docked in Europe the turn the pool hits 15; the target escalates to 17 and the pool resets.
3. **Europe penalty stalls the pool** — with one person idling in Europe, a single chapel cross is cancelled by the −4 (clamped); no second emigrant appears.
4. **Paid recruit** — gold buys the chosen slot's unit into Europe (now two there), the dock refills, gold is debited by exactly the price, and the base price escalates by 30 (200 → 230).
5. **Acid round-trip** — the whole immigration + dock + Europe-units state round-trips byte-identical.

## 9c. Journey 7 — A recruit reaches the New World (L2)

Hand-built (a caravel + a recruit, both in Europe; a coast to land on). Milestones, each asserted as a connected chain:
1. **Board** — the recruit boards the ship on the Europe dock (1 of the caravel's 2 slots used).
2. **Sail home** — after the crossing the ship is back on the map with the recruit **still aboard** at the ship's tile (the passenger tracked the carrier).
3. **Acid round-trip (mid-voyage)** — a passenger-aboard game round-trips byte-identical, carrier id preserved.
4. **Disembark** — onto the adjacent coast; the recruit is a free on-map unit, the hold empties.
5. **Found** — the disembarked colonist founds a colony: the immigration → New World loop is closed.

## 9d. Journey 8 — An elected father grants its bonus (L2)

Constructed (a pop-1 colony with the colonist in the town hall = 4 bells/turn; Thomas Jefferson chosen, liberty one short of his cost). Milestones, each asserted as a connected chain:
1. **Election turn** — the 4 unmodified bells tip liberty over the 24 cost; Jefferson joins Congress; liberty resets to the surplus (3).
2. **Bonus takes effect** — the next turn the same 4 bells become **6** liberty (+50%): the elected modifier is live.
3. **Persists across reload** — save → load → save is byte-identical, and another turn on the reload still yields the boosted +6 (the effect rides on the persisted Congress).

## 10. Fixtures & helpers

- **Seed policy:** `424242` is the pinned founding seed (used by `InputTests`, `TileWorkerTests`). Record any new pinned seed here.
- **Deterministic maps:** hand-built terrain via `SaveGame.Restore` (the `ColonyOnCross` pattern in `TileWorkerTests`) for journeys that must not depend on map gen.
- **Convention:** logic E2E tests carry `[Trait("Category","E2E")]` and live in `Scenarios/JourneyTests.cs`; they run on the every-push gate (they are **not** in the soak category).

## 11. Traceability matrix

| Feature | Slice tests | E2E journey | Status |
|---|---|---|---|
| Map gen + fog + movement | `GameTests`, `WorldTests` | Journey 1 | ✅ covered-e2e |
| Founding + free base buildings + auto-assign | `GameTests`, `TileWorkerTests`, `BuildingTests` | Journey 1 | ✅ covered-e2e |
| Tile work + building refinement + construction + growth | `TileWorkerTests`, `BuildingTests`, `ColonyEconomyTests`, `ProductionChainTests` | Journey 2 | ✅ covered-e2e |
| Market + treasury + tax | `MarketTests` | Journey 3 | ✅ covered-e2e |
| High-seas sailing + Europe trade | `SailingTests` | Journey 3b | ✅ covered-e2e |
| Liberty + father election + cost escalation | `FoundingFatherTests` | Journey 4 | ✅ covered-e2e |
| UI→logic seam (select/move/found/staff) | `InputTests`, `ColonyPanelTests`, `MainSceneTests` | Journey 5 | ✅ covered-e2e |
| Immigration + recruitment + recruit-price escalation | `ImmigrationTests` | Journey 6 | ✅ covered-e2e |
| Unit transport (board/sail/disembark + capacity) | `TransportTests` | Journey 7 | ✅ covered-e2e |
| Founding-Father effects (modifiers + abilities) | `FoundingFatherEffectsTests` | Journey 8 | ✅ covered-e2e (applied effects; rest deferred) |

## 12. Blocked journeys & roadmap

Blocked journeys are kept as named stubs (above), each with its missing feature. When **ships sail** (P4 slice 3) Journey 3 gained its load→sail→sell-in-Europe→sail-back leg (now Journey 3b). When **immigration** landed (P4 slice 4) the recruitment journey was added (now **Journey 6**); **transport** added **Journey 7** (slice 5). The **modifier system** (slice 7) added **Journey 8** as a dedicated, deterministic father-effect journey — chosen over extending Journey 4, whose elected fathers are RNG-determined and so cannot deterministically assert a *specific* father's bonus. Extend journeys *in place* where the flow is deterministic; otherwise add a focused one.

## 13. Definition of done (E2E journey)

A journey is **done** only when: its milestone assertions are green in CI · this plan's catalogue + traceability matrix are updated · and the relevant system doc's Verification table references the journey. (The same no-drift rule as everything else.)

## Changelog

| Date | Change |
|---|---|
| 2026-06-13 | Plan created; Journeys 1–5 specified (designed via a 5-agent coverage-gap audit workflow) |
| 2026-06-13 | Journey 6 (immigration & recruitment) built and added (P4 slice 4) |
| 2026-06-13 | Journey 7 (a recruit reaches the New World) built and added (P4 slice 5) |
| 2026-06-13 | Journey 8 (an elected father grants its bonus) built and added (P4 slice 7) |
