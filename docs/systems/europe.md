# System: Europe & high-seas sailing

| | |
|---|---|
| **Status** | Implemented (sailing + cargo + sell/buy + unit purchase + the Europe screen UI) |
| **Last verified** | 2026-06-13 @ Phase 4 slice 11 |
| **Code** | `game/src/GameLogic/Units/Unit.cs` (cargo, location), `GameSession/Game.cs` (sailing + Europe trade); UI: `game/presentation/EuropePanel.cs`, `GameController.cs` |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/SailingTests.cs`, `Scenarios/JourneyTests.cs` (Journey 3b); UI: `game/presentation/tests/EuropePanelTests.cs` |
| **FreeCol reference** | `Europe.java`, `Unit.java` (`getSailTurns`, `TURNS_TO_SAIL` line 2629) |
| **Related systems** | [market](market.md) (sales), [immigration](immigration.md) (recruitment dock), [transport](transport.md) (ships carry colonists), [units-movement](units-movement.md), [save-load](save-load.md) |

## 1. How it works (plain English)

Europe is across the ocean. Sail a ship to the **high seas** (the map's outer edge), order it to Europe, and after **three turns** it arrives at the docks. There you **sell** the goods in its hold for gold (minus tax) and **buy** goods to bring back. Sail it home and three turns later it re-enters the map where it left. Ships carry cargo loaded from a coastal colony.

**Worked example:** a caravel loads 100 sugar at your port, sails to the edge, crosses to Europe over three turns, sells the sugar for 200 gold, and sails home — re-appearing on the high-seas tile it departed from.

**The Europe screen:** click **Europe** (or press **E**) to open it. It shows your treasury, the immigration clock, the **recruitment dock** (three colonists, each with its current price), the **ships in port** (each with its hold, a *Sail to New World* button, its passengers, its **cargo with Sell buttons**, and a **Buy goods** dropdown), and the **colonists on the dock** (each with a *Board* button per ship that has room), plus a **Buy / train** dropdown to purchase ships, artillery or trained specialists for gold. It's the one place you recruit colonists, buy units, trade goods, put colonists on a ship, and send them home. Units in Europe are *not* drawn on the map — they live on this screen.

## 2. Detailed rules

| Action | Rule |
|---|---|
| Load cargo | ship must be on the map next to (or on) the colony; goods move warehouse → hold |
| Sail to Europe | ship must be a naval unit on a **high-seas** tile; arrives in `SailTurns` (3) turns |
| In Europe | not on the map; can sell/buy; can't move on the map |
| Sell in Europe | hold → European market (price moves, tax applied) → treasury |
| Buy in Europe | gold → hold at the **ask** price; **buying does not move the price** (FreeCol) |
| Sail home | from Europe; arrives in 3 turns at the departure high-seas tile |
| Off-map units | a sailing/Europe unit cannot be moved on the map |

**Deviations / simplifications:** sailing departs only from the actual high-seas edge tile (FreeCol allows any "high-seas-connected" tile); sail time is the fixed default 3 (FreeCol adds ship modifiers). Recruitment & immigration ([immigration.md](immigration.md)), carrying colonists on ships ([transport.md](transport.md)), and buying units (the spec `price`) all exist. A bought ship enters at the map's high-seas tile; a bought land unit waits on the dock to board a ship. Bought specialists/artillery have no special effect yet (expert yields + combat are future).

## 3. Technical design

- `Unit` gains `Location` (`UnitLocation`: OnMap / SailingToEurope / InEurope / SailingToNewWorld), `SailTurnsRemaining`, and a goods `Cargo` hold (`AddCargo`/`CargoOf`). `IsOnMap` gates map interactions.
- `Game`: `SailTurns` (3, base); `SailTurnsFor(owner)` shortens the crossing by the owner's Congress `model.modifier.sailHighSeas` (**Ferdinand Magellan** −1, floored at 1) — applied when a ship departs (`SailToEurope`/`SailToNewWorld`). `CheckSailToEurope`, `LoadFromColony`, `SellShipCargo`, `CheckBuyEuropeGoods`/`BuyEuropeGoods`, `UnitsInEurope`. `AdvanceSailing` runs in `EndTurn` (decrement, then dock in Europe or re-enter the map). `CheckMove` rejects off-map units.
- **Ships repair in Europe (1c-3b).** A ship damaged in combat limps to Europe under forced repair for several turns (see [combat](combat.md)). While `Unit.IsUnderRepair`, it sits in port unable to **sail home** (`SailToNewWorld` throws), **take on cargo** (`CheckBuyEuropeGoods`), or be **boarded** (`CheckBoard`) — FreeCol `isReadyToTrade`; `AdvanceRepairs` (in `EndTurn`) heals it one turn at a time. The Europe panel shows its repair countdown and hides its sail/buy controls until it is whole.
- **Persistence:** save v11 stores each unit's location, sail turns, and cargo; pre-v11 units load on-map with empty holds.
- **Unit purchase:** `UnitType.Price` (spec `price`; `IsPurchasable = Price > 0`). `Game.CheckBuyUnit`/`BuyUnit` debit gold and dock the unit in Europe; a naval unit's `Position` is set to `EuropeEntryTile()` (the first high-seas tile) so it can sail home. man-o-war (mercenary-only) and the free colonist (recruited) are not purchasable.
- **Europe screen UI:** `EuropePanel` (a `PanelContainer`, like `ColonyPanel`) renders `UnitsInEurope` + `RecruitDock`/`RecruitPrice`/`Immigration` and forwards clicks to `Recruit`, `Board`, `DisembarkToDock`, `SailToNewWorld`, per-ship cargo `SellShipCargo`, a Buy-goods dropdown (`BuyEuropeGoods`, 100/pick), and a Buy/train-unit dropdown (`BuyUnit`) — all Game oracles, no rules in the scene (ADR-006). Opened from `GameController` (the **Europe** button / **E** key). The map view renders only on-map units; off-map units appear on this screen.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `SailingTests`: 3-turn crossing each way, sail-eligibility (naval + high-seas), load/sell/buy validation, buy doesn't move price, off-map can't move | ✅ |
| L2 Scenario | Always | `SailingTests.FullTradeVoyage` + `JourneyTests.Journey3b` (load→sail→sell→return, incl. mid-voyage save acid test) | ✅ |
| L3 Interaction | Yes (Europe screen) | `EuropePanelTests`: recruit-from-dock (gold debited); board-then-sail sends a colonist home; **sell cargo** credits the treasury; **buy-goods** loads the hold; **buy-unit** docks a purchase — all through the real scene controls | ✅ |
| L4 Visual | UI hidden in goldens | — (L4 captures hide the UI layer; see [QA-REPORT](../QA-REPORT.md)) | — |

- **FreeCol cross-check:** `SailTurns = 3` from `TURNS_TO_SAIL` (`Unit.java:2629`); buying-doesn't-move-price per `Market`/`Europe` behaviour.

## 5. Open issues / TODO

- [x] **Magellan's sail-time modifier** (−1 high-seas turn) via `SailTurnsFor` — see [founding-fathers](founding-fathers.md).
- [ ] Ship combat/sinking; making bought specialists/artillery actually special (expert yields + combat).
- [ ] Europe screen niceties: a richer recruit/immigration display; map-side board/disembark UI.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | High-seas sailing (3 turns each way), ship cargo, sell/buy in Europe; save v11 | Phase 4 slice 3 |
| 2026-06-13 | Immigration & recruitment dock split into [immigration.md](immigration.md); save v12 | Phase 4 slice 4 |
| 2026-06-13 | Cargo capacity now enforced; carrying colonists on ships split into [transport.md](transport.md); save v13 | Phase 4 slice 5 |
| 2026-06-13 | Europe screen UI (`EuropePanel`): dock/recruit, ships in port, board/sail; off-map units rendered here (L3 tested) | Phase 4 slice 6 |
| 2026-06-13 | Europe screen: per-ship goods trading — Sell cargo + Buy-goods dropdown (L3 tested) | Phase 4 slice 10 |
| 2026-06-15 | `SailTurnsFor(owner)`: Ferdinand Magellan's `sailHighSeas` (−1) shortens the high-seas crossing (floored at 1); set on departure. Rides on the persisted Congress (no save change). See [founding-fathers](founding-fathers.md) | Phase 5 (#3 fathers) |
| 2026-06-15 | Ships **repair in Europe** (1c-3b): a damaged ship sits in port under forced repair (cannot sail/buy/board — `SailToNewWorld`/`CheckBuyEuropeGoods`/`CheckBoard` guards), `AdvanceRepairs` heals it each `EndTurn`; the panel shows the countdown and hides its controls. See [combat](combat.md) | Phase 5 slice 1c-3b |
| 2026-06-13 | Buy units in Europe (`UnitType.Price`, `BuyUnit`; high-seas entry for ships) + Buy/train dropdown on the screen (L3 tested) | Phase 4 slice 11 |
