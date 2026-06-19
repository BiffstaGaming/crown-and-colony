# System: Trade routes (automatic goods haulage)

| | |
|---|---|
| **Status** | Implemented (GameLogic): define a route, assign a carrier, it auto-hauls each turn. The route-management **UI** is P7 (`86d3c9rrd`). |
| **Last verified** | 2026-06-19 @ trade-route GameLogic (`86d3c9rq1`, save v43) |
| **Code** | `game/src/GameLogic/GameSession/TradeRoute.cs` (model); `Game.cs` (`CreateTradeRoute`/`CheckAssignTradeRoute`/`AssignTradeRoute`/`ClearTradeRoute`/`ProcessTradeRoutes`/`ServeTradeRouteStop`); `Player.TradeRoutes`/`NextTradeRouteId`; `Units/Unit.cs` (`TradeRouteId`/`TradeRouteStopIndex`) |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/TradeRouteTests.cs` |
| **FreeCol reference** | `TradeRoute`, `TradeRouteStop`, `Unit.tradeRoute`/`currentStop`, the AI trade-route mission |
| **Related systems** | [transport](transport.md) (the load/unload seam), [colonies](colonies.md), [save-load](save-load.md) |

## 1. How it works (plain English)

Hauling goods between your colonies by hand — load a wagon here, drive it there, unload, repeat, every turn — is tedious. A **trade route** automates it. You set up a route once: a named list of **stops**, where each stop is one of your colonies and the goods to **pick up** there. Then you **assign a carrier** (a ship or a wagon train) to the route, and from then on it does the rounds by itself.

Each turn the carrier heads for its next stop. When it gets there it **drops off** everything it's carrying that the stop *doesn't* want loaded (delivering it into that colony's warehouse), **picks up** the goods the stop *does* list, and sets off for the stop after that — looping back to the first stop after the last.

**Worked example:**
> Your inland colony **Alpha** makes more sugar than it can use; your port **Beta** ships sugar to Europe. Build a wagon train, make a route: *Stop 1 = Alpha, load sugar; Stop 2 = Beta, load nothing.* Assign the wagon. Now every few turns it carries Alpha's sugar to Beta and comes back empty for more — no clicking required.

**What the player sees and does:** nothing yet — the route-editor screen (build a route, assign units) is the **P7** task `86d3c9rrd`. The GameLogic is exposed for that UI to call.

## 2. Detailed rules

- **A route** is a player-owned, named, ordered ring of **stops**; each stop = a colony + the list of goods to **load** there. A route needs **≥ 2 stops** to move anything (a 0/1-stop route is inert).
- **Creating** (`CreateTradeRoute(player, name, stops)`): every stop must name a colony the player **owns** (else rejected); the route gets the next per-player id and joins `Player.TradeRoutes`.
- **Assigning** (`CheckAssignTradeRoute`/`AssignTradeRoute`): only a **carrier** — a unit with cargo space (a ship or wagon train) — owned by a player that has the route may be assigned; it starts at the first stop. `ClearTradeRoute` removes it.
- **Each turn** (`ProcessTradeRoutes`, run in the colonial turn for the human **and** the foreign powers), for every assigned carrier on the map:
  - if it's **on or next to** its current stop's colony → **deliver** every good it holds that the stop does *not* list to load (`UnloadToColony`), then **load** the stop's listed goods up to its free hold (`LoadFromColony`), then **advance** to the next stop (wrapping);
  - otherwise → **step toward** the stop's colony (`StepToward`, one tile, greedy).
- **Self-healing:** a carrier whose route was deleted is quietly **un-assigned**; a stop whose colony no longer exists (abandoned/captured) is **skipped** to the next stop.
- **Capacity:** loading respects the carrier's hold (goods pack in 100s per slot); it loads only what fits and what the colony has.

**Deviations from original / FreeCol:**
- **Greedy movement, not pathfinding.** The carrier steps toward its stop with the same greedy `StepToward` the AI besiege/garrison uses (no naval A*), so on a convoluted coastline it can stall — the same limitation the foreign-power AI has. A naval pathfinder is a follow-up.
- **Delivery rule is "load-list complement".** A stop delivers everything not in its `LoadGoodsIds`; FreeCol also supports explicit per-stop unload lists and a "load to a maximum" cargo plan. Our simpler complement covers the classic "pick up here, drop off there" loop.
- **No Europe stop.** FreeCol routes can include Europe (sell/buy there); our stops are colonies only (a Europe stop needs the docked-ship sell/buy flow). Deferred.
- **The route-editor UI** (`86d3c9rrd`) is P7; today routes are created/assigned through the GameLogic API.

## 3. Technical design

- **Model** (`TradeRoute.cs`): `TradeRoute(int Id, string Name, IReadOnlyList<TradeRouteStop> Stops)` + `TradeRouteStop(int ColonyId, IReadOnlyList<string> LoadGoodsIds)` — immutable records. Held on `Player.TradeRoutes` (mutable `TradeRoutesList` for the rules); `Player.NextTradeRouteId` allocates stable ids. A carrier references its route by `Unit.TradeRouteId` (int?) + `Unit.TradeRouteStopIndex`.
- **Ops** (`Game.cs`, ADR-006 Check+do): `CreateTradeRoute` (owned-colony validation), `CheckAssignTradeRoute`/`AssignTradeRoute` (carrier + route-exists gate), `ClearTradeRoute`.
- **Auto-haul** (`ProcessTradeRoutes`): called from `RunPlayerTurn`'s colonial path after immigration, for every colonial player. Reuses the carrier-haulage seam from [transport](transport.md): `UnloadToColony` then `LoadFromColony`. The load amount fits the hold via the same slot math as cargo looting (`SlotsFor`/`CargoSlotSize`/`CargoSlotsFree`).
- **Determinism (ADR-009):** the only RNG is `StepToward`'s tiebreak, drawn from the **mover's own stream** (the human's stream 0 for a human hauler, a foreign power's own stream otherwise). A **route-less** player iterates zero carriers, so `ProcessTradeRoutes` draws **nothing** — a game with no routes is byte-identical and no golden churns. Load/unload draw no RNG.
- **Save v43** (additive, omit-when-default): `SavedPlayer.TradeRoutes` (a list of `SavedTradeRoute`/`SavedTradeRouteStop`, omitted when the player has none) + `SavedUnit.TradeRouteId`/`TradeRouteStop` (omitted for a route-less unit). A game with no trade routes serializes **byte-identically to v42**. `NextTradeRouteId` is re-derived on load as `max(route id) + 1`.

## 4. Verification

| Layer | Required? | Tests | Status |
|---|---|---|---|
| L1 Unit | Always | `TradeRouteTests`: `CreateTradeRoute` rejects a non-owned-colony stop; `AssignTradeRoute` is carrier-only (a free colonist is refused, a wagon accepted) + `ClearTradeRoute`; the v43 tokens are omitted for a route-less game (byte-identical) and the version is current | ✅ |
| L2 Scenario | Always | `TradeRouteTests`: an assigned wagon hauls sugar Alpha→Beta over several turns (source emptied, destination stocked); routes + the carrier's assignment + mid-route stop index round-trip through save v43 | ✅ |

- **FreeCol cross-check:** ✅ the route = an ordered ring of stops, each loading listed goods and delivering the rest, advancing per turn — `TradeRoute`/`Unit.currentStop`.

## 5. Open issues / TODO

- [x] **Trade-route GameLogic** (`86d3c9rq1`): model + create/assign/clear + per-turn auto-haul + save v43.
- [ ] **Route-editor UI** (`86d3c9rrd`, P7): build/edit a route, assign units, see each carrier's progress.
- [ ] **Naval pathfinding** for haul movement (today greedy `StepToward`); **Europe stops** (sell/buy on the route); explicit per-stop unload lists / load-to-max cargo plans.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-19 | **Trade-route GameLogic** (`86d3c9rq1`, save **v43**): `TradeRoute`/`TradeRouteStop` model; `Player.TradeRoutes`/`NextTradeRouteId` + `Unit.TradeRouteId`/`TradeRouteStopIndex`; `CreateTradeRoute`/`AssignTradeRoute`/`ClearTradeRoute` ops; `ProcessTradeRoutes` per-turn auto-haul (deliver-then-load-then-advance, reusing `Load`/`UnloadToColony`, greedy `StepToward`). Save v43 additive omit-when-default (route-less game byte-identical to v42). RNG-free re stream 0 (no routes → no draw). +5 L1/L2 `TradeRouteTests`; 1136 + 4 soak green. UI is P7 (`86d3c9rrd`) | P5 (`86d3c9rq1`) |
