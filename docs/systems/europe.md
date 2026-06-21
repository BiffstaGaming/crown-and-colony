# System: Europe & high-seas sailing

| | |
|---|---|
| **Status** | Implemented (sailing + cargo + sell/buy + unit purchase + the Europe screen UI) |
| **Last verified** | 2026-06-20 @ Europe buying now moves the market price (`86d3dkffz`) |
| **Code** | `game/src/GameLogic/Units/Unit.cs` (cargo, location), `GameSession/Game.cs` (sailing + Europe trade); UI: `game/presentation/EuropePanel.cs`, `GameController.cs` |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/SailingTests.cs`, `Scenarios/JourneyTests.cs` (Journey 3b); UI: `game/presentation/tests/EuropePanelTests.cs` |
| **FreeCol reference** | `Europe.java`, `Unit.java` (`getSailTurns`, `TURNS_TO_SAIL` line 2629) |
| **Related systems** | [market](market.md) (sales), [immigration](immigration.md) (recruitment dock), [transport](transport.md) (ships carry colonists), [units-movement](units-movement.md), [save-load](save-load.md) |

## 1. How it works (plain English)

Europe is across the ocean. Move a ship to the **high seas** (the deep water along the map's east/west edge), then press its **Sail to Europe** order — and after **three turns** it arrives at the docks. There you **sell** the goods in its hold for gold (minus tax) and **buy** goods to bring back. Sail it home and three turns later it re-enters the map where it left. Ships carry cargo loaded from a coastal colony.

**Sailing to Europe from the map:** select a ship and you'll see a **Sail to Europe** button in the unit panel. It only lights up once the ship is standing on a high-seas tile (the outer-edge deep water) — so the trick is to sail west or east to the map edge first, then press it. (A land unit never shows the button.)

**Worked example:** a caravel loads 100 sugar at your port, sails to the edge, crosses to Europe over three turns, sells the sugar for 200 gold, and sails home — re-appearing on the high-seas tile it departed from.

**The Europe screen:** click **Europe** (or press **E**) to open it. It shows your treasury, the immigration clock, the **recruitment dock** (three colonists, each with its current price), the **ships in port** (each with its hold, a *Sail to New World* button, its passengers, its **cargo with Sell buttons**, and a **Buy goods** dropdown), and the **colonists on the dock** (each with a *Board* button per ship that has room), plus a **Buy / train** dropdown to purchase ships, artillery or trained specialists for gold. It's the one place you recruit colonists, buy units, trade goods, put colonists on a ship, and send them home. Units in Europe are *not* drawn on the map — they live on this screen.

## 2. Detailed rules

| Action | Rule |
|---|---|
| Load cargo | ship must be on the map next to (or on) the colony; goods move warehouse → hold |
| Sail to Europe | ship must be a naval unit on a **high-seas** tile and **not under repair**; arrives in `SailTurns` (3) turns |
| In Europe | not on the map; can sell/buy; can't move on the map |
| Sell in Europe | hold → European market (price moves, tax applied) → treasury |
| Buy in Europe | gold → hold at the **ask** price; **buying raises the ask** as it drains the market (chunked, FreeCol `buyInEurope`) — see [market.md](market.md) |
| Train vs Purchase | Europe units split: **train** a priced specialist (skill > 0 — expert farmer, master carpenter…) or **purchase** a priced ship/artillery (no skill) |
| Artillery price | each artillery you buy raises **your** artillery price by **+100** (500 → 600 → 700…); ships and specialists stay flat |
| Sail home | from Europe; arrives in 3 turns at the departure high-seas tile |
| Off-map units | a sailing/Europe unit cannot be moved on the map |

**Deviations / simplifications:** sailing departs only from the actual high-seas edge tile (FreeCol allows any "high-seas-connected" tile); sail time is the fixed default 3 (FreeCol adds ship modifiers). Recruitment & immigration ([immigration.md](immigration.md)), carrying colonists on ships ([transport.md](transport.md)), and buying units (the spec `price`) all exist. A bought ship enters at the map's high-seas tile; a bought land unit waits on the dock to board a ship. Bought artillery already fights (Bombard / artillery-in-the-open offence, demotes to damaged artillery on a loss — see [combat](combat.md)); bought specialists don't yet produce **expert yields** in colonies (the remaining "actually special" gap). The **artillery price increase (+100)** now comes from the difficulty level (`Ruleset.Difficulty.ArtilleryPriceIncrease`, medium 100; see [difficulty](difficulty.md)). The Train/Purchase split is exposed as two GameLogic lists; the **UI** still uses one combined dropdown (the on-screen split is a presentation follow-up).

## 3. Technical design

- `Unit` gains `Location` (`UnitLocation`: OnMap / SailingToEurope / InEurope / SailingToNewWorld), `SailTurnsRemaining`, and a goods `Cargo` hold (`AddCargo`/`CargoOf`). `IsOnMap` gates map interactions.
- `Game`: `SailTurns` (3, base); `SailTurnsFor(owner)` shortens the crossing by the owner's Congress `model.modifier.sailHighSeas` (**Ferdinand Magellan** −1, floored at 1) — applied when a ship departs (`SailToEurope`/`SailToNewWorld`). `CheckSailToEurope`, `LoadFromColony`, `SellShipCargo`, `CheckBuyEuropeGoods`/`BuyEuropeGoods`, `UnitsInEurope`. `AdvanceSailing` runs in `EndTurn` (decrement, then dock in Europe or re-enter the map). `CheckMove` rejects off-map units.
- **Ships repair in Europe (1c-3b).** A ship damaged in combat limps to Europe under forced repair for several turns (see [combat](combat.md)). While `Unit.IsUnderRepair`, it sits in port unable to **sail home** (`SailToNewWorld` throws), **take on cargo** (`CheckBuyEuropeGoods`), or be **boarded** (`CheckBoard`) — FreeCol `isReadyToTrade`; `AdvanceRepairs` (in `EndTurn`) heals it one turn at a time. The Europe panel shows its repair countdown and hides its sail/buy controls until it is whole.
- **Persistence:** save v11 stores each unit's location, sail turns, and cargo; pre-v11 units load on-map with empty holds.
- **Unit purchase:** `UnitType.Price` (spec `price`; `IsPurchasable = Price > 0`). `Game.CheckBuyUnit`/`BuyUnit` debit gold and dock the unit in Europe; a naval unit's `Position` is set to `EuropeEntryTile()` (the first high-seas tile) so it can sail home. man-o-war (mercenary-only) and the free colonist (recruited) are not purchasable.
- **Train vs Purchase + artillery price escalation** (`86d3c9qgy`): `UnitType.Skill` (spec `skill`; 0 for colonist/ship/artillery, ≥1 for an expert) splits the priced units — `UnitType.IsTrainedInEurope` (skill > 0) vs `IsPurchasedInEurope` (skill 0), surfaced as `Game.UnitTypesTrainedInEurope()` / `UnitTypesPurchasedInEurope()`. Each player carries an escalated-price map (`Player.UnitPriceOverrides`, mutable `UnitPriceMap`); `EuropeUnitPrice(player, type) = override ?? Price` is used by `CheckBuyUnit`/`BuyUnit`. After buying the escalating unit (`ArtilleryUnitTypeId`, the only one in classic), `BuyUnit` sets the override to `pricePaid + Ruleset.Difficulty.ArtilleryPriceIncrease` (medium 100) — so the next costs +100, per player; ships/specialists never escalate. Persisted in `SavedPlayer.UnitPrices` (v29, omitted when empty → byte-identical to v28 until a price escalates).
- **Europe screen UI:** `EuropePanel` (a `PanelContainer`, like `ColonyPanel`) renders `UnitsInEurope` + `RecruitDock`/`RecruitPrice`/`Immigration` and forwards clicks to `Recruit`, `Board`, `DisembarkToDock`, `SailToNewWorld`, per-ship cargo `SellShipCargo`, a Buy-goods dropdown (`BuyEuropeGoods`, 100/pick), and a Buy/train-unit dropdown (`BuyUnit`) — all Game oracles, no rules in the scene (ADR-006). Opened from `GameController` (the **Europe** button / **E** key). The map view renders only on-map units; off-map units appear on this screen.
- **Map-side "Sail to Europe" order:** the selected-unit HUD (`UI/SelectedUnitPanel/VBox/Orders/SailToEuropeButton`, wired in `GameController`) forwards to the existing `CheckSailToEurope`/`SailToEurope` command via `ApplyUnitOrder` — the discoverable surface for *departing* to Europe (the Europe screen only handles ships already in port). It is shown only for a **naval** unit and enabled only when `CheckSailToEurope` passes (the ship is on a high-seas tile), with a tooltip pointing the player to the map edge. Presentation-only (ADR-006); the engine command and its 3-turn crossing are unchanged.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `SailingTests`: 3-turn crossing each way, sail-eligibility (naval + high-seas + **not under repair**, `CheckSailToEurope_RefusesAShipUnderRepair`), load/sell/buy validation, buy doesn't move price, off-map can't move. `EuropePurchaseTests`: `Skill` parses (expert 1 / artillery 0 / colonist 0); the Train (skill > 0) vs Purchase (skill 0) lists partition correctly + are disjoint + exclude the price-0 free colonist; buying artillery ratchets its price +100/purchase while a ship stays flat; escalation is **per-player**; the escalated price round-trips through save v29 and a no-purchase game omits the `UnitPrices` token | ✅ |
| L2 Scenario | Always | `SailingTests.FullTradeVoyage` + `JourneyTests.Journey3b` (load→sail→sell→return, incl. mid-voyage save acid test) | ✅ |
| L3 Interaction | Yes (Europe screen + map order) | `EuropePanelTests`: recruit-from-dock (gold debited); board-then-sail sends a colonist home; **sell cargo** credits the treasury; **buy-goods** loads the hold; **buy-unit** docks a purchase — all through the real scene controls. **Map-side sail** (`InputTests.SelectedUnitPanel_SailToEuropeButton_ShownForShipsOnly_EnabledOnHighSeas_AndSails`): the order is hidden for a land unit, shown-but-disabled for a ship off the high seas, and enabled on a high-seas tile where pressing it sends the ship `SailingToEurope` | ✅ |
| L4 Visual | Yes (screen) | **Europe screen golden** (`europe-panel`, `UiPanelGoldenTests.EuropePanel_DockAndShip_MatchesGolden` — the treasury/immigration header, the three-slot recruitment dock, a caravel in port + a colonist on the dock, and the Buy/train controls, driven from a fixed injected save; see [ui-panel-goldens.md](../visual-tests/ui-panel-goldens.md)) | ✅ |

- **FreeCol cross-check:** `SailTurns = 3` from `TURNS_TO_SAIL` (`Unit.java:2629`); buying moves the market (the ask rises) per FreeCol `buyInEurope` → `addGoodsToMarket(type, −amount)` — see [market.md](market.md).

## 5. Open issues / TODO

- [x] **Magellan's sail-time modifier** (−1 high-seas turn) via `SailTurnsFor` — see [founding-fathers](founding-fathers.md).
- [x] Ship combat/sinking — shipped (naval combat 1c-3a: `ResolveLoserOutcome`/`SinkShip`/`DamageShip`); see [combat](combat.md).
- [x] **Train vs Purchase split + artillery price escalation** (`86d3c9qgy`): `UnitType.Skill` partitions priced units into trained specialists / purchased ships+artillery (`UnitTypesTrainedInEurope`/`PurchasedInEurope`); buying artillery escalates that player's price +100/purchase (`Player.UnitPriceOverrides`, save v29). Follow-up: the on-screen **two-list dropdown** (the GameLogic split exists; the panel still shows one combined list). *(The +100 was moved into ruleset difficulty options — `86d3c9y08` slice 4.)*
- [ ] Making bought **specialists** actually special (expert production yields in colonies — needs the per-colonist colony-model refactor `86d3b6nrz`).
- [x] **Map-side "Sail to Europe" order** (bug fix): a HUD order button surfaces the existing `SailToEurope` command for a ship on the high seas (previously the command had no UI, so a ship on the map couldn't be sent to Europe at all). Follow-up: FreeCol's auto-prompt when a ship *moves onto* a high-seas tile (a confirm dialog), so the player needn't find the button.
- [ ] Europe screen niceties: a richer recruit/immigration display; map-side board/disembark UI.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-21 | **Map-side "Sail to Europe" order button** (bug fix): the engine command `CheckSailToEurope`/`SailToEurope` existed and was L1-tested, but **no UI ever surfaced it**, so a ship on the map could not be sent to Europe. Added a `SailToEuropeButton` to the selected-unit HUD (`main.tscn`), wired in `GameController` via `ApplyUnitOrder(CheckSailToEurope, SailToEurope)`; shown only for naval units, enabled only on a high-seas tile, tooltip points to the map edge. Presentation-only (ADR-006); no engine/save/RNG change; no golden churn (the HUD panel isn't in any golden frame). +1 L3 (`InputTests`). | P5 (bug fix) |
| 2026-06-21 | **`CheckSailToEurope` now also refuses a ship under repair** (adversarial-review follow-up): the sail oracle gains an `IsUnderRepair` guard, consistent with its peers `SailToNewWorld`/`CheckBoard`/`CheckBuyEuropeGoods` (FreeCol `isReadyToTrade`). Defence-in-depth — a damaged ship is relocated off the high seas, so it was already unreachable, but the oracle no longer contradicts the others. No save/RNG change. +1 L1 (`SailingTests.CheckSailToEurope_RefusesAShipUnderRepair`). | P5 (review follow-up) |
| 2026-06-20 | **Europe buying moves the market price** (`86d3dkffz`): `BuyEuropeGoods` no longer buys at a static ask — buying now raises the ask as it drains the market (the logic lives in `Market.Buy`/`BuyCost`; see [market.md](market.md)). The Buy-in-Europe rule + the FreeCol cross-check corrected (the old "buying does not move the price" was wrong vs `buyInEurope`). No save change; AI never buys goods in Europe so the soak is byte-stable. | Phase 5 (`86d3dkffz`) |
| 2026-06-18 | **Artillery price increase routed through the difficulty system** (`86d3c9y08` slice 4): the +100/purchase escalation now reads `Ruleset.Difficulty.ArtilleryPriceIncrease` (medium 100; the dotted `model.option.priceIncrease.artillery` id), with the recruit-price/lower-cap increments alongside it (see [immigration](immigration.md)). Behaviour-preserving at medium; no save change; soak byte-stable. See [difficulty](difficulty.md). | Phase (`86d3c9y08` slice 4) |
| 2026-06-17 | **Train/Purchase split + artillery price escalation** (`86d3c9qgy`): `UnitType.Skill` (parsed) splits priced units into trained specialists (skill > 0) vs purchased ships/artillery (skill 0) — `UnitTypesTrainedInEurope`/`PurchasedInEurope`; each player's `UnitPriceOverrides` map makes buying artillery ratchet its price +100/purchase (`EuropeUnitPrice` in `CheckBuyUnit`/`BuyUnit`), ships/specialists flat, per-player. Save **v29** (`SavedPlayer.UnitPrices`, omitted when empty → byte-identical to v28 until a price escalates). +100 hardcoded classic-medium. +7 L1 (`EuropePurchaseTests`); 778 + 4 soak green. UI two-list split deferred. | Phase 5 (`86d3c9qgy`) |
| 2026-06-13 | High-seas sailing (3 turns each way), ship cargo, sell/buy in Europe; save v11 | Phase 4 slice 3 |
| 2026-06-13 | Immigration & recruitment dock split into [immigration.md](immigration.md); save v12 | Phase 4 slice 4 |
| 2026-06-13 | Cargo capacity now enforced; carrying colonists on ships split into [transport.md](transport.md); save v13 | Phase 4 slice 5 |
| 2026-06-13 | Europe screen UI (`EuropePanel`): dock/recruit, ships in port, board/sail; off-map units rendered here (L3 tested) | Phase 4 slice 6 |
| 2026-06-13 | Europe screen: per-ship goods trading — Sell cargo + Buy-goods dropdown (L3 tested) | Phase 4 slice 10 |
| 2026-06-15 | `SailTurnsFor(owner)`: Ferdinand Magellan's `sailHighSeas` (−1) shortens the high-seas crossing (floored at 1); set on departure. Rides on the persisted Congress (no save change). See [founding-fathers](founding-fathers.md) | Phase 5 (#3 fathers) |
| 2026-06-15 | Ships **repair in Europe** (1c-3b): a damaged ship sits in port under forced repair (cannot sail/buy/board — `SailToNewWorld`/`CheckBuyEuropeGoods`/`CheckBoard` guards), `AdvanceRepairs` heals it each `EndTurn`; the panel shows the countdown and hides its controls. See [combat](combat.md) | Phase 5 slice 1c-3b |
| 2026-06-13 | Buy units in Europe (`UnitType.Price`, `BuyUnit`; high-seas entry for ships) + Buy/train dropdown on the screen (L3 tested) | Phase 4 slice 11 |
