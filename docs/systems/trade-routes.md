# System: Trade routes (automatic goods haulage)

| | |
|---|---|
| **Status** | Implemented: define a route, assign a carrier, it auto-hauls each turn — driven from the **route-management UI** (`86d3c9rrd`, shipped) or the GameLogic API. **Validation warnings** (`86d3drn0j`) surface FreeCol-style advisory problems (warns, never blocks). |
| **Last verified** | 2026-06-21 @ trade-route validation warnings (`86d3drn0j`); save unchanged (derived read, still v50) |
| **Code** | `game/src/GameLogic/GameSession/TradeRoute.cs` (model + `TradeRouteWarning`/`TradeRouteWarningKind`); `Game.cs` (`CreateTradeRoute`/`CheckAssignTradeRoute`/`AssignTradeRoute`/`ClearTradeRoute`/`RemoveTradeRoute`/`ProcessTradeRoutes`/`ServeTradeRouteStop`/`ValidateTradeRoute`/`ValidateTradeRoutesOf`); `Player.TradeRoutes`/`NextTradeRouteId`; `Units/Unit.cs` (`TradeRouteId`/`TradeRouteStopIndex`); **UI:** `game/presentation/TradeRoutePanel.cs` + `GameController.OpenTradeRoutePanel` |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/TradeRouteTests.cs`, `TradeRouteValidationTests.cs`; **L3:** `game/presentation/tests/TradeRoutePanelTests.cs` |
| **FreeCol reference** | `TradeRoute` (incl. `verify()`), `TradeRouteStop`, `GameOptions.ENHANCED_TRADE_ROUTES`, `Unit.tradeRoute`/`currentStop`, the AI trade-route mission |
| **Related systems** | [transport](transport.md) (the load/unload seam), [colonies](colonies.md), [save-load](save-load.md) |

## 1. How it works (plain English)

Hauling goods between your colonies by hand — load a wagon here, drive it there, unload, repeat, every turn — is tedious. A **trade route** automates it. You set up a route once: a named list of **stops**, where each stop is one of your colonies and the goods to **pick up** there. Then you **assign a carrier** (a ship or a wagon train) to the route, and from then on it does the rounds by itself.

Each turn the carrier heads for its next stop. When it gets there it **drops off** everything it's carrying that the stop *doesn't* want loaded (delivering it into that colony's warehouse), **picks up** the goods the stop *does* list, and sets off for the stop after that — looping back to the first stop after the last.

**Worked example:**
> Your inland colony **Alpha** makes more sugar than it can use; your port **Beta** ships sugar to Europe. Build a wagon train, make a route: *Stop 1 = Alpha, load sugar; Stop 2 = Beta, load nothing.* Assign the wagon. Now every few turns it carries Alpha's sugar to Beta and comes back empty for more — no clicking required.

**What the player sees and does:** click **Trade Routes** to open the route screen. It lists your existing routes (each with its stops, a dropdown to assign one of your ships/wagons, and a **Delete** button), and a **New route** section: pick a *from* colony, a *to* colony, optionally a good to load at the first stop, and click **Create route**. Deleting a route quietly frees whatever carrier was running it.

**Warnings (advice, not a wall):** the game can check a route and tell you when it probably won't do what you want — but it never *stops* you running it (just like the original). It flags four kinds of problem:

> - **Too few stops** — a route with only one stop can't move anything (a carrier needs somewhere to take the goods *to*).
> - **A stop that isn't yours** — a stop points at a colony you don't own any more (you abandoned it, or it was captured), so the carrier can't trade there.
> - **Nothing to carry** — no stop lists any goods to pick up, so the carrier would just shuttle back and forth empty.
> - **A good that never gets dropped off** — you've set the *same* good to load at *every* stop, so the carrier picks it up everywhere and never has a stop where it puts it down. It would haul that good around forever and never deliver it. (Fix: make sure at least one stop on the route does *not* load it — that's where it gets delivered.)

> **Worked example:** you make a route *Stop 1 = Alpha, load sugar; Stop 2 = Beta, load sugar*. The game warns: "sugar is loaded at every stop, so it is never delivered anywhere." Change Beta to load *nothing* and the warning clears — now Beta is where the sugar gets dropped off.

## 2. Detailed rules

- **A route** is a player-owned, named, ordered ring of **stops**; each stop = a colony + the list of goods to **load** there. A route needs **≥ 2 stops** to move anything (a 0/1-stop route is inert).
- **Creating** (`CreateTradeRoute(player, name, stops)`): every stop must name a colony the player **owns** (else rejected); the route gets the next per-player id and joins `Player.TradeRoutes`.
- **Assigning** (`CheckAssignTradeRoute`/`AssignTradeRoute`): only a **carrier** — a unit with cargo space (a ship or wagon train) — owned by a player that has the route may be assigned; it starts at the first stop. `ClearTradeRoute` removes one carrier from its route.
- **Deleting** (`RemoveTradeRoute(player, routeId)`): drops the route from the player and **un-assigns every carrier** that was running it (a no-op for an unknown id).
- **Each turn** (`ProcessTradeRoutes`, run in the colonial turn for the human **and** the foreign powers), for every assigned carrier on the map:
  - if it's **on or next to** its current stop's colony → **deliver** every good it holds that the stop does *not* list to load (`UnloadToColony`), then **load** the stop's listed goods up to its free hold (`LoadFromColony`), then **advance** to the next stop (wrapping);
  - otherwise → **step toward** the stop's colony (`StepToward`, one tile, greedy).
- **Self-healing:** a carrier whose route was deleted is quietly **un-assigned**; a stop whose colony no longer exists (abandoned/captured) is **skipped** to the next stop.
- **Capacity:** loading respects the carrier's hold (goods pack in 100s per slot); it loads only what fits and what the colony has.
- **Validation** (`ValidateTradeRoute(route)` / `ValidateTradeRoutesOf(player)`, FreeCol `TradeRoute.verify()`): a **pure read** returning advisory `TradeRouteWarning`s — it never blocks the route (FreeCol warns, it does not stop you). A valid route returns **no** warnings. The checks, faithful to FreeCol:
  - **`NotEnoughStops`** — fewer than two stops. Reported on its own (short-circuits, like FreeCol): the cargo checks presuppose a ring of ≥ 2 stops to deliver around, so they don't run for a sub-2-stop route.
  - **`InvalidStop`** (carries the offending stop index) — a stop names a colony the route's owner does not own, or that no longer exists.
  - **`AllEmpty`** — no stop lists any goods to load, so the route would haul nothing.
  - **`GoodsAlwaysPresent`** (carries the offending good) — a good is loaded at **every** stop, so it is never unloaded anywhere and can never be delivered (it just rides the carrier). FreeCol names one such good; we report **each** so the player can fix them all. Suppressed when **ENHANCED_TRADE_ROUTES** is on.

**Deviations from original / FreeCol:**
- **Greedy movement, not pathfinding.** The carrier steps toward its stop with the same greedy `StepToward` the AI besiege/garrison uses (no naval A*), so on a convoluted coastline it can stall — the same limitation the foreign-power AI has. A naval pathfinder is a follow-up.
- **Delivery rule is "load-list complement".** A stop delivers everything not in its `LoadGoodsIds`; FreeCol also supports explicit per-stop unload lists and a "load to a maximum" cargo plan. Our simpler complement covers the classic "pick up here, drop off there" loop.
- **No Europe stop.** FreeCol routes can include Europe (sell/buy there); our stops are colonies only (a Europe stop needs the docked-ship sell/buy flow). Deferred.
- **The route-editor UI** (`86d3c9rrd`) covers create / assign / delete and a from→to + load-good quick-create; it does **not** yet expose multi-stop rings, per-stop load editing, or live carrier-progress (the GameLogic supports arbitrary stop lists — that richer editor is a follow-up). The classic game's editor is likewise list-driven.

## 3. Technical design

- **Model** (`TradeRoute.cs`): `TradeRoute(int Id, string Name, IReadOnlyList<TradeRouteStop> Stops)` + `TradeRouteStop(int ColonyId, IReadOnlyList<string> LoadGoodsIds)` — immutable records. Held on `Player.TradeRoutes` (mutable `TradeRoutesList` for the rules); `Player.NextTradeRouteId` allocates stable ids. A carrier references its route by `Unit.TradeRouteId` (int?) + `Unit.TradeRouteStopIndex`. **Validation surface:** `TradeRouteWarning(RouteId, Kind, StopIndex?, GoodsId?, Message)` (a pure-data record, the C# analogue of FreeCol's `verify()` `StringTemplate`) + the `TradeRouteWarningKind` enum (`NotEnoughStops`/`InvalidStop`/`AllEmpty`/`GoodsAlwaysPresent`).
- **Validation** (`Game.cs`, FreeCol `TradeRoute.verify()`): `ValidateTradeRoute(route)` resolves the route's owner from `Players` (so callers needn't pass it) and `ValidateTradeRoutesOf(player)` walks all of a player's routes; both delegate to a private `ValidateTradeRoute(owner, route)` that yields the warnings in check order. **Faithful to `verify()`:** `< 2` stops returns `NotEnoughStops` immediately (so the cargo checks never fire on a sub-2-stop route, matching FreeCol's early return); each stop is checked against the owner's colonies (`InvalidStop` with the index); `anyCargo` gates `AllEmpty`; `alwaysPresent` is the **intersection** of every stop's `LoadGoodsIds` (seed from stop 0, `IntersectWith` each later stop — the C# of FreeCol's `always.retainAll(stop.getCargo())`), reported per-good (ordered) unless `EnhancedTradeRoutes`. Pure read: no state change, no RNG, no save field — so the default game stays byte-identical and no version bump (a derived read).
- **ENHANCED_TRADE_ROUTES** (`Game.EnhancedTradeRoutes`): FreeCol's `GameOptions.ENHANCED_TRADE_ROUTES` (`model.option.enhancedTradeRoutes`), which relaxes the always-present check. The classic ruleset ships it `defaultValue="false"` and we do **not** yet parse boolean game options into the ruleset, so the property is a hard-coded `false` today — the default, faithful behaviour. When that option plumbing lands (`Ruleset.cs`), this property becomes a one-line read; the relaxation logic is already in place and tested-by-construction (the `else if (!EnhancedTradeRoutes …)` branch).
- **Ops** (`Game.cs`, ADR-006 Check+do): `CreateTradeRoute` (owned-colony validation), `CheckAssignTradeRoute`/`AssignTradeRoute` (carrier + route-exists gate), `ClearTradeRoute` (one carrier), `RemoveTradeRoute` (drop the route + un-assign all its carriers).
- **UI** (`TradeRoutePanel.cs`, ADR-006 presentation-only): a `PanelContainer` controller built programmatically into the fixed `VBox/Dynamic` shell (like `EuropePanel`). It only renders `HumanPlayer.TradeRoutes` and forwards clicks to the oracles above — every rule (validation, the per-turn haul, save) stays in GameLogic. Wired in `GameController` via `OpenTradeRoutePanel`/the **Trade Routes** button.
- **Auto-haul** (`ProcessTradeRoutes`): called from `RunPlayerTurn`'s colonial path after immigration, for every colonial player. Reuses the carrier-haulage seam from [transport](transport.md): `UnloadToColony` then `LoadFromColony`. The load amount fits the hold via the same slot math as cargo looting (`SlotsFor`/`CargoSlotSize`/`CargoSlotsFree`).
- **Determinism (ADR-009):** the only RNG is `StepToward`'s tiebreak, drawn from the **mover's own stream** (the human's stream 0 for a human hauler, a foreign power's own stream otherwise). A **route-less** player iterates zero carriers, so `ProcessTradeRoutes` draws **nothing** — a game with no routes is byte-identical and no golden churns. Load/unload draw no RNG.
- **Save v43** (additive, omit-when-default): `SavedPlayer.TradeRoutes` (a list of `SavedTradeRoute`/`SavedTradeRouteStop`, omitted when the player has none) + `SavedUnit.TradeRouteId`/`TradeRouteStop` (omitted for a route-less unit). A game with no trade routes serializes **byte-identically to v42**.
- **Save v45** (additive, omit-when-default): `SavedPlayer.NextTradeRouteId` — the monotonic id counter — is now **persisted** (omitted while still 1, so a route-free game stays byte-identical to v44). Before v45 it was re-derived on load as `max(route id) + 1`, which **reused an id** after you deleted the highest-numbered route and reloaded (the new route collided with a surviving one, breaking the assign/haul lookups that match by id). FreeCol persists its game-wide `nextId` for exactly this reason. A pre-v45 save (no field) still falls back to `max(route id) + 1` on load.

## 4. Verification

| Layer | Required? | Tests | Status |
|---|---|---|---|
| L1 Unit | Always | `TradeRouteTests`: `CreateTradeRoute` rejects a non-owned-colony stop; `AssignTradeRoute` is carrier-only (a free colonist is refused, a wagon accepted) + `ClearTradeRoute`; the v43 tokens are omitted for a route-less game (byte-identical) and the version is current. **`TradeRouteValidationTests`** (validation): a valid route has no warnings; `< 2` stops → `NotEnoughStops` only; an abandoned-colony stop → `InvalidStop` with the right index; no cargo → `AllEmpty`; a good at every stop → `GoodsAlwaysPresent` naming it; a swapped pair (each delivered somewhere) → no warning; multiple always-present goods each reported; validation mutates nothing | ✅ |
| L2 Scenario | Always | `TradeRouteTests`: an assigned wagon hauls sugar Alpha→Beta over several turns (source emptied, destination stocked); routes + the carrier's assignment + mid-route stop index round-trip through save v43; `RemoveTradeRoute` deletes the route and un-assigns its carrier (unknown id = no-op); **the `NextTradeRouteId` counter survives save/load so ids aren't reused after a delete-then-reload (v45), and a pre-v45 save falls back to `max id + 1`**. `TradeRouteValidationTests`: `ValidateTradeRoutesOf` aggregates across a player's routes (good route contributes nothing, bad route's warning surfaces) and is empty for a route-less player | ✅ |
| L3 Interaction | UI | `TradeRoutePanelTests` (scene runner on `main.tscn`): the **Create route** button makes a route from the two default colony ends; the per-route **Assign** dropdown assigns a carrier; the **Delete** button removes the route | ✅ |

- **FreeCol cross-check:** ✅ the route = an ordered ring of stops, each loading listed goods and delivering the rest, advancing per turn — `TradeRoute`/`Unit.currentStop`. ✅ validation mirrors `TradeRoute.verify()`'s four cases (`notEnoughStops`/`invalidStop`/`allEmpty`/`alwaysPresent`) and its `ENHANCED_TRADE_ROUTES` relaxation; we warn-don't-block (FreeCol's `verify()` is advisory) and report every problem rather than only the first.

## 5. Open issues / TODO

- [x] **Trade-route GameLogic** (`86d3c9rq1`): model + create/assign/clear + per-turn auto-haul + save v43.
- [x] **Route-management UI** (`86d3c9rrd`): list routes, create (from→to + load good), assign a carrier, delete (`TradeRoutePanel`).
- [x] **Stable route ids across save/load** (`86d3dkh5p`): `NextTradeRouteId` persisted (save v45) so a deleted route's id is never reused after a reload.
- [x] **Validation warnings** (`86d3drn0j`): `ValidateTradeRoute`/`ValidateTradeRoutesOf` — FreeCol `verify()`'s four cases, warns-not-blocks, pure read (no save change).
- [ ] **Surface the warnings in the UI:** `TradeRoutePanel` should call `ValidateTradeRoutesOf(HumanPlayer)` and show each route's warnings (the GameLogic read is ready; only the panel rendering is left).
- [ ] **Parse ENHANCED_TRADE_ROUTES** into the ruleset so `Game.EnhancedTradeRoutes` reads the real option (today hard-coded `false`, the classic default).
- [ ] **Richer route editor:** multi-stop rings, per-stop load editing, live carrier-progress (the GameLogic already supports arbitrary stop lists).
- [ ] **Naval pathfinding** for haul movement (today greedy `StepToward`); **Europe stops** (sell/buy on the route); explicit per-stop unload lists / load-to-max cargo plans.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-21 | **Trade-route validation warnings** (`86d3drn0j`): `Game.ValidateTradeRoute(route)` + `ValidateTradeRoutesOf(player)` return advisory `TradeRouteWarning`s (new `TradeRouteWarning`/`TradeRouteWarningKind` in `TradeRoute.cs`), mirroring FreeCol `TradeRoute.verify()`: `NotEnoughStops` (< 2 stops, short-circuits like FreeCol's early return), `InvalidStop` (a stop not the owner's colony, with the index), `AllEmpty` (no stop loads goods), `GoodsAlwaysPresent` (a good loaded at every stop → never delivered; the intersection of all stops' load lists). Warns-not-blocks (FreeCol `verify()` is advisory) and reports **every** problem (FreeCol returns only the first). `ENHANCED_TRADE_ROUTES` relaxation honoured via `Game.EnhancedTradeRoutes` (hard-coded `false` — the classic default `defaultValue="false"`; not yet parsed, noted). **Pure derived read: no state change, no RNG, no save field → default game byte-identical, no version bump.** +10 L1/L2 (`TradeRouteValidationTests`); 1618 logic + 4 soak green. | P7 (`86d3drn0j`) |
| 2026-06-20 | **Persist the route-id counter** (`86d3dkh5p`, save **v45**): `Player.NextTradeRouteId` is now saved (additive `SavedPlayer.NextTradeRouteId`, omit-when-1 → route-free game byte-identical to v44) instead of re-derived as `max(id)+1` on load — which **reused an id** after deleting the highest route and reloading (colliding with a surviving route, breaking the by-id assign/haul lookups; FreeCol persists `Game.nextId` for this reason). Restored exactly in `Game.BuildPlayer`; pre-v45 saves fall back to `max(id)+1`. RNG-free; soak byte-stable (no AI creates routes). +2 L1 (`TradeRouteTests`: id-reuse regression + pre-v45 fallback); 18 version-pins → 45; 1193 green. | P5 (`86d3dkh5p`) |
| 2026-06-20 | **Route-management UI** (`86d3c9rrd`): `TradeRoutePanel` (list routes + per-route assign-carrier dropdown + delete; new-route from→to + load-good quick-create), wired into `GameController` via `OpenTradeRoutePanel` + a **Trade Routes** button. Added `Game.RemoveTradeRoute` (drop route + un-assign its carriers). Presentation-only (ADR-006): forwards to the existing oracles, no new rules. +1 L1/L2 (`RemoveTradeRoute`) + 3 L3 (`TradeRoutePanelTests`); 1173 + 3 L3 green. No save change. | P5 (`86d3c9rrd`) |
| 2026-06-19 | **Trade-route GameLogic** (`86d3c9rq1`, save **v43**): `TradeRoute`/`TradeRouteStop` model; `Player.TradeRoutes`/`NextTradeRouteId` + `Unit.TradeRouteId`/`TradeRouteStopIndex`; `CreateTradeRoute`/`AssignTradeRoute`/`ClearTradeRoute` ops; `ProcessTradeRoutes` per-turn auto-haul (deliver-then-load-then-advance, reusing `Load`/`UnloadToColony`, greedy `StepToward`). Save v43 additive omit-when-default (route-less game byte-identical to v42). RNG-free re stream 0 (no routes → no draw). +5 L1/L2 `TradeRouteTests`; 1136 + 4 soak green. UI is P7 (`86d3c9rrd`) | P5 (`86d3c9rq1`) |
