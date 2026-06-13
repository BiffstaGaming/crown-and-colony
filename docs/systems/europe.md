# System: Europe & high-seas sailing

| | |
|---|---|
| **Status** | Implemented (sailing + cargo + sell/buy + the Europe screen UI) |
| **Last verified** | 2026-06-13 @ Phase 4 slice 6 |
| **Code** | `game/src/GameLogic/Units/Unit.cs` (cargo, location), `GameSession/Game.cs` (sailing + Europe trade); UI: `game/presentation/EuropePanel.cs`, `GameController.cs` |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/SailingTests.cs`, `Scenarios/JourneyTests.cs` (Journey 3b); UI: `game/presentation/tests/EuropePanelTests.cs` |
| **FreeCol reference** | `Europe.java`, `Unit.java` (`getSailTurns`, `TURNS_TO_SAIL` line 2629) |
| **Related systems** | [market](market.md) (sales), [immigration](immigration.md) (recruitment dock), [transport](transport.md) (ships carry colonists), [units-movement](units-movement.md), [save-load](save-load.md) |

## 1. How it works (plain English)

Europe is across the ocean. Sail a ship to the **high seas** (the map's outer edge), order it to Europe, and after **three turns** it arrives at the docks. There you **sell** the goods in its hold for gold (minus tax) and **buy** goods to bring back. Sail it home and three turns later it re-enters the map where it left. Ships carry cargo loaded from a coastal colony.

**Worked example:** a caravel loads 100 sugar at your port, sails to the edge, crosses to Europe over three turns, sells the sugar for 200 gold, and sails home — re-appearing on the high-seas tile it departed from.

**The Europe screen:** click **Europe** (or press **E**) to open it. It shows your treasury, the immigration clock, the **recruitment dock** (three colonists, each with its current price), the **ships in port** (each with its hold, a *Sail to New World* button, its passengers, its **cargo with Sell buttons**, and a **Buy goods** dropdown), and the **colonists on the dock** (each with a *Board* button per ship that has room). It's the one place you recruit colonists, trade goods, put colonists on a ship, and send them home. Units in Europe are *not* drawn on the map — they live on this screen.

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

**Deviations / simplifications:** sailing departs only from the actual high-seas edge tile (FreeCol allows any "high-seas-connected" tile); sail time is the fixed default 3 (FreeCol adds ship modifiers). Recruitment & immigration ([immigration.md](immigration.md)) and carrying colonists on ships ([transport.md](transport.md), with cargo capacity now enforced) both exist; buying ships/artillery in Europe is still a later slice.

## 3. Technical design

- `Unit` gains `Location` (`UnitLocation`: OnMap / SailingToEurope / InEurope / SailingToNewWorld), `SailTurnsRemaining`, and a goods `Cargo` hold (`AddCargo`/`CargoOf`). `IsOnMap` gates map interactions.
- `Game`: `SailTurns` (3); `CheckSailToEurope`/`SailToEurope`, `SailToNewWorld`, `LoadFromColony`, `SellShipCargo`, `CheckBuyEuropeGoods`/`BuyEuropeGoods`, `UnitsInEurope`. `AdvanceSailing` runs in `EndTurn` (decrement, then dock in Europe or re-enter the map). `CheckMove` rejects off-map units.
- **Persistence:** save v11 stores each unit's location, sail turns, and cargo; pre-v11 units load on-map with empty holds.
- **Europe screen UI:** `EuropePanel` (a `PanelContainer`, like `ColonyPanel`) renders `UnitsInEurope` + `RecruitDock`/`RecruitPrice`/`Immigration` and forwards clicks to `Recruit`, `Board`, `DisembarkToDock`, `SailToNewWorld`, plus per-ship cargo `SellShipCargo` and a Buy-goods dropdown (`BuyEuropeGoods`, 100/pick) — all Game oracles, no rules in the scene (ADR-006). Opened from `GameController` (the **Europe** button / **E** key). The map view renders only on-map units; off-map units appear on this screen.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `SailingTests`: 3-turn crossing each way, sail-eligibility (naval + high-seas), load/sell/buy validation, buy doesn't move price, off-map can't move | ✅ |
| L2 Scenario | Always | `SailingTests.FullTradeVoyage` + `JourneyTests.Journey3b` (load→sail→sell→return, incl. mid-voyage save acid test) | ✅ |
| L3 Interaction | Yes (Europe screen) | `EuropePanelTests`: recruit-from-dock (gold debited); board-then-sail sends a colonist home; **sell cargo** credits the treasury; **buy-goods dropdown** loads the hold — all through the real scene controls | ✅ |
| L4 Visual | UI hidden in goldens | — (L4 captures hide the UI layer; see [QA-REPORT](../QA-REPORT.md)) | — |

- **FreeCol cross-check:** `SailTurns = 3` from `TURNS_TO_SAIL` (`Unit.java:2629`); buying-doesn't-move-price per `Market`/`Europe` behaviour.

## 5. Open issues / TODO

- [ ] Ship combat/sinking; sail-time modifiers; buying ships/units in Europe.
- [ ] Europe screen niceties: selling/buying goods from the screen, a richer recruit/immigration display; carrying a colonist straight into a colony on disembark.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | High-seas sailing (3 turns each way), ship cargo, sell/buy in Europe; save v11 | Phase 4 slice 3 |
| 2026-06-13 | Immigration & recruitment dock split into [immigration.md](immigration.md); save v12 | Phase 4 slice 4 |
| 2026-06-13 | Cargo capacity now enforced; carrying colonists on ships split into [transport.md](transport.md); save v13 | Phase 4 slice 5 |
| 2026-06-13 | Europe screen UI (`EuropePanel`): dock/recruit, ships in port, board/sail; off-map units rendered here (L3 tested) | Phase 4 slice 6 |
| 2026-06-13 | Europe screen: per-ship goods trading — Sell cargo + Buy-goods dropdown (L3 tested) | Phase 4 slice 10 |
