# System: Unit transport (ships carry colonists)

| | |
|---|---|
| **Status** | Implemented (board/sail/disembark + shared goods/passenger capacity; no Europe screen UI yet) |
| **Last verified** | 2026-06-13 @ Phase 4 slice 5 |
| **Code** | `game/src/GameLogic/GameSession/Game.cs` (board/disembark/capacity), `Units/Unit.cs` (`CarrierId`), `Specification/UnitType.cs` (`Space`/`SpaceTaken`) |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/TransportTests.cs`, `Scenarios/JourneyTests.cs` (Journey 7) |
| **FreeCol reference** | `Unit.java` (`getCargoCapacity`, `getSpaceLeft`, `getCargoSpaceTaken`, `canAdd`), `UnitType.java` (`space`/`spaceTaken`, `getSpaceTaken`), `GoodsContainer.CARGO_SIZE` |
| **Related systems** | [europe.md](europe.md) (sailing), [immigration.md](immigration.md) (recruits to carry), [colonies.md](colonies.md), [save-load.md](save-load.md) |

## 1. How it works (plain English)

A colonist can't swim the ocean — to move one across the sea you put it **on a ship**. This is what makes immigration worth anything: a recruit waiting in Europe **boards a ship**, the ship **sails home**, and the colonist **steps ashore** in the New World, where it can found or join a colony.

**The rules, in plain words:**
- A ship has a **hold** measured in slots. A **caravel holds 2**, a galleon 6, and so on. One colonist takes **1 slot**; 100 of a trade good also takes 1 slot. Goods and passengers **share** the same hold.
- You **board** a colonist when it's with the ship — both in Europe, or the colonist standing next to the ship on the coast.
- A boarded colonist **travels with the ship** wherever it goes; it can't move, sail, or found a colony on its own while aboard.
- You **disembark** it onto a land tile next to the ship once the ship is back on the map (or take it back off onto the Europe dock).

**Worked example:**
> A caravel waits in Europe. You recruited a free colonist last turn — board it (1 of the caravel's 2 slots used). Sail to the New World; three turns later the caravel re-appears off the coast with the colonist still aboard. Disembark it onto the neighbouring grassland and found your second colony there.

**What the player sees and does:** *(no Europe/transport UI yet)* the capacity, passengers, and board/disembark actions are on the game state for the upcoming Europe screen; for now the flow is exercised through the game API and tests.

## 2. Detailed rules

| Input / condition | Result |
|---|---|
| Ship capacity | the ship type's `space` in slots (caravel 2, merchantman 4, galleon 6, …) |
| Goods slot cost | each goods type packs in 100s, rounded up: 1–100 = 1 slot, 101–200 = 2, … |
| Passenger slot cost | the carried type's `max(spaceTaken, space+1)` — a colonist is **1** |
| Board (in Europe) | passenger and ship both `InEurope`, room for the passenger |
| Board (on the map) | passenger on land next to (or on) the ship's tile, room for the passenger |
| While aboard | the passenger's location/tile mirror the ship's; it cannot move, sail, found, or board another ship |
| Sail with the ship | passengers travel with the carrier across the high seas, arriving where it arrives |
| Disembark (to map) | ship on the map; target is a **land** tile next to the ship; the unit lands and ends its turn |
| Disembark (to dock) | ship in Europe; the unit returns to the Europe dock |
| Boarding ends the turn | a boarded or disembarked unit has 0 movement left that turn |

**Deviations from original 1994 / FreeCol behavior:**
- **No "join an existing colony" on disembark.** A disembarked colonist lands on a tile as a free unit; adding it straight into a colony's population (and the reverse — pulling a colonist out of a colony onto a ship) is a later "colonist in/out of colony" mechanic. It can already **found** a colony.
- **Only ships carry; only land units are carried.** Wagon trains and ships-on-ships are out of scope (faithful for the early game).
- **Capacity now enforced for goods too** (the gap [europe.md](europe.md) flagged): loading goods or boarding a unit is rejected when the hold is full.

## 3. Technical design

**Domain model:** `Unit.CarrierId` (`int?`) marks a passenger and names its ship; `Unit.IsAboard` is the predicate. A carried unit's `Location`/`Position` are kept equal to its carrier's (`Game.SyncPassengers`), and `Unit.IsOnMap` is now `Location == OnMap && !IsAboard` so a passenger is never treated as an independent map unit.

**Data sources:** `UnitType` parses `space` (capacity) and `spaceTaken` (carry cost), both inheriting up the `extends` chain (defaults 0 and 1). `UnitType.CarrySlots = max(SpaceTaken, Space+1)` mirrors FreeCol `UnitType.getSpaceTaken`.

**Algorithms** (`GameSession/Game.cs`):
- `CargoCapacity`/`CargoSlotsUsed`/`CargoSlotsFree` — goods slots (`ceil(amount/100)` per goods type, `CargoSlotSize=100`) plus carried units' `CarrySlots`.
- `CheckBoard`/`Board`, `CheckDisembark`/`Disembark`, `DisembarkToDock` — the player actions, each guarded by a `MoveCheck`.
- `SyncPassengers(carrier)` is called wherever a carrier's location/tile changes (`Board`, `MoveUnit`, `SailToEurope`, `SailToNewWorld`, arrival in `AdvanceSailing`).
- Goods loading (`LoadFromColony`, `BuyEuropeGoods`) now checks `CargoSlotsFree`.

**Integration points:** a passenger aboard a ship in Europe **no longer counts** toward the immigration penalty (only persons standing on the dock do) — see [immigration.md](immigration.md). `CheckMove`/`CheckSailToEurope`/`CheckFoundColony` reject aboard units via `IsOnMap`; `SailToNewWorld` rejects them explicitly.

**Persistence:** save **v13** stores each unit's `CarrierId`; pre-v13 units load not-aboard.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `TransportTests`: spec capacity/carry-cost, board in Europe + on the map (adjacency), shared goods/passenger capacity, over-capacity rejection, aboard-unit guards, disembark to land + rejections (water/non-adjacent/in-Europe), disembark to dock, boarding frees the immigration penalty, save round-trip with a passenger, pre-v13 load | ✅ |
| L2 Scenario | Always | `JourneyTests.Journey7` (board → sail home → mid-voyage acid round-trip → disembark → found); `SoakTests` still green | ✅ |
| L3 Interaction | No UI yet | — (Europe screen is the next slice) | — |
| L4 Visual | No screen yet | — | — |

- **FreeCol cross-check:** `space`/`spaceTaken` and `getSpaceTaken = max(spaceTaken, space+1)` from `UnitType.java`; capacity = `getCargoCapacity`, room test = `getSpaceLeft`/`canAdd` (`Unit.java`); goods pack at `GoodsContainer.CARGO_SIZE = 100`. Caravel `space=2`, colonist carry cost 1 — pinned in tests.

## 5. Open issues / TODO

- [ ] **Europe screen UI** — board/disembark buttons, show passengers + free slots, off-map units (next slice).
- [ ] **Colonist in/out of a colony** — disembark straight into a colony (population +1) and embark a colonist from a colony.
- [ ] Capacity-aware **auto-loading**; multi-ship cargo transfer; ship loss drowning its passengers.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Ships carry colonists: `CarrierId`, board/disembark, shared goods/passenger capacity (now enforced); save v13 | Phase 4 slice 5 |
