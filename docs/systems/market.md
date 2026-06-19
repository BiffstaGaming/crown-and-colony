# System: European market & treasury

| | |
|---|---|
| **Status** | Implemented (supply-price model + treasury + sales tax; sells direct or via a ship in Europe; buying in Europe; **per-player markets** exercised by the foreign-power AI, FP-5) |
| **Last verified** | 2026-06-19 @ Dutch trade advantage (`tradeBonus` −50%) (`86d3dfyrm`) |
| **Code** | `game/src/GameLogic/Trade/Market.cs`, `GameSession/Player.cs` (`Market`), `GameSession/Game.cs` (`Gold`, `TaxRate`, `SellColonyGoods`, `SellShipCargo`, `BuyEuropeGoods` — each with a `Player`-taking internal overload) |
| **Tests** | `game/tests/GameLogic.Tests/Trade/MarketTests.cs`, `GameSession/ForeignPowerEconomyTests.cs` (per-player independence + save round-trip) |
| **FreeCol reference** | `MarketData.java` (`price()` lines 319–383), `Market.java`, `server/model/ServerPlayer.java` (`sellInEurope`) |
| **Related systems** | [colonies](colonies.md), [ruleset-data](ruleset-data.md), [save-load](save-load.md) |

## 1. How it works (plain English)

You sell your colonies' goods to Europe for **gold**, and Europe charges a **sales tax** on every sale. Prices follow supply and demand: the more of a good Europe already holds, the less it pays for more of it — so dumping 600 sugar drives the sugar price down as you sell. Each good has a **sell price** (what you receive) and a higher **buy price** (what you'd pay to buy it back); the gap between them is fixed per good.

**Worked example:** sugar starts selling at 2 gold each. Sell 600 sugar with no tax and you get 1,200 gold — but the price has dropped to 1 gold by the end, because Europe is now glutted with sugar. Add a 50% tax and you'd keep only 600 of that.

**National trade advantage (the Dutch):** the Dutch are the traders of the New World — Europe's market reacts to their selling **half as strongly**. When the Dutch dump 600 sugar, the market behaves as if only 300 arrived, so the price slides half as far and they keep selling at a good rate for longer. (It's a per-nation trait: the human only gets it by playing the Dutch; every other nation moves the market normally.)

## 2. Detailed rules

- Every tradeable good seeds from the spec's `<market>`: `initial-amount` (Europe's starting stock), `initial-price` (sell/bid), `price-difference` (the spread → buy/ask = bid + difference).
- **Initial prices (verified against the classic spec):** food 1/9, sugar 2/4, tobacco 2/4, ore 4/7, silver 16/18, rum 10/12, muskets 1/3.
- **Price formula** (FreeCol `MarketData.price()`): `bid = round(initialPrice × initialAmount ÷ amountInMarket)`, `ask = bid + spread`. Selling adds the goods to `amountInMarket`, lowering the bid.
- **Chunking:** sales are processed in batches of 100; each batch is priced, then moves the market, so one huge sale can't be dumped at the opening price.
- **Hard bounds:** bid never below 1, ask never above 19.
- **New World cap:** sugar/tobacco/cotton/furs and the goods made from them can't have their sell price pushed above `initialPrice + 2`.
- **Jump clamp:** a single recompute can't move the price by more than the spread (anti-exploit); the inventory is nudged back to stay consistent.
- **Tax:** `goldCredited = (100 − taxRate) × revenue ÷ 100`, integer-truncated (the player can lose a gold to rounding). Tax starts at 0%.
- Non-tradeable goods (grain, fish, bells, crosses — anything without a `<market>`) cannot be sold. **Food *is* tradeable** in the classic spec (1/9) — only grain/fish (its unstorable raw forms) are not — so a seller that wants to keep its colonists fed must exclude food deliberately; the foreign-power AI does (see [players.md](players.md)).
- **Per-player markets (ADR-019):** every `Player` owns its **own** `Market` instance. A sale moves only the seller's market — the human's and other powers' prices are unaffected. From FP-5 the foreign powers trade on their own markets each turn (selling their colonies' surplus), so the per-player price model is exercised in real games; each market persists independently in the save.
- **Dutch trade advantage (`tradeBonus`, FreeCol `Modifier.TRADE_BONUS`):** the trade nation type (`model.nationType.trade` — the Dutch) carries `model.modifier.tradeBonus` **−50%**, so each chunk of one of their sells adds only **half** as much to `amountInMarket` — the price falls half as fast as they sell (they still pocket the full chunk revenue; only the price-moving inventory is scaled). The 4th nation-type advantage to fold through the shared `NationTypeModifiers` seam (after the English religious-unrest, French native-alarm, Spanish offence). A player with no such nation type (the human's default) is unaffected. **Faithful subset:** FreeCol applies `tradeBonus` to *both* buying and selling; our buy path doesn't move the market (see the Deviations note), so the advantage surfaces on **sells only**.

**Deviations from FreeCol:** `SellColonyGoods` still sells straight from a colony warehouse to Europe (a convenience abstraction kept from slice 1); since slice 3 a ship can also carry cargo and sell it in Europe (`SellShipCargo`) and buy goods at the ask price without moving the price (`BuyEuropeGoods`) — and since slice 10 both are on the **Europe screen** (per-ship Sell buttons + a Buy-goods dropdown), see [europe.md](europe.md).

## 3. Technical design

- `GoodsType` gains `IsNewWorldGoods` and a `GoodsMarket?` (InitialAmount/InitialPrice/PriceDifference; null = untradeable), parsed from `<market>`.
- `Market` (engine-free, `Trade/Market.cs`): per-good `{ amountInMarket, bid, ask }`; `BidPrice`/`AskPrice`/`AmountInMarket`; `Sell(goodsId, amount, taxPercent, volumeFactor = 1.0) → SaleResult(beforeTax, afterTax)` chunks by 100 and recomputes per chunk. **`volumeFactor`** scales each chunk's `amountInMarket` delta (`+= round(chunk × volumeFactor)`) — 1.0 normally, 0.5 for the Dutch trade advantage; the default keeps every existing caller byte-identical. `Recompute` is a faithful port of `MarketData.price()` (float supply formula, new-world cap, jump clamp, [1,19] bounds). `SaveDeltas`/`LoadDeltas` persist only inventories that moved from their seed.
- `Game`: `Gold`, `TaxRate`, `Market`; `SellColonyGoods(colony, goodsId, amount)` / `SellShipCargo(...)` deduct stores and credit the treasury, passing **`MarketVolumeFactor(player)`** into `Market.Sell`. `MarketVolumeFactor(player)` folds the player's nation-type `model.modifier.tradeBonus` onto 1.0 via `NationTypeModifiers` (Dutch → 0.5; no nation → 1.0); `tradeBonus` is nation-type-only (no founding father carries it). No RNG, no save change — the factor is read live from the player's nation type. `Game.New` takes optional `startingGold`/`startingTax` (default 0 = classic).
- **Persistence:** save v9 stores `Gold`, `Tax`, and the sparse moved-market map; pre-v9 saves load with 0 gold/tax and a ruleset-seeded market.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `MarketTests`: initial prices vs spec (7 goods), inventory rise + bid fall (sugar 600 → bid 1, gold 1200), tax truncation (33% on 112 → 75), [1,19] bounds across all goods, New World cap, SellColonyGoods deduct/credit/validate. **Dutch trade advantage:** exactly one nation type carries `tradeBonus`; `Sell` with `volumeFactor 0.5` absorbs strictly less volume so the bid holds up; `volumeFactor 1.0` byte-identical to the implicit default; a **Dutch human** depresses the price less than a no-nation human selling the same colony goods. `ForeignPowerEconomyTests`: a power's sale moves only its own market (human/other power untouched) | ✅ |
| L2 Scenario | Always | save round-trip preserves gold/tax/moved-market (per player); a moved foreign-power market round-trips; pre-v9 reseeds | ✅ |
| L3 Interaction | Yes (Europe screen) | `EuropePanelTests`: Sell-cargo button credits the treasury; Buy-goods dropdown loads the hold (real scene controls) | ✅ |
| L4 Visual | UI hidden in goldens | — | — |

- **FreeCol cross-check:** `Recompute` ported line-for-line from `MarketData.price()`; initial prices verified against the spec; the sugar sell-down (600 → bid 1, inventory 2100) and silver tax (75) hand-computed from the reference formula.

## 5. Open issues / TODO

- [x] Ship-carried sales (`SellShipCargo`) and buying in Europe (`BuyEuropeGoods`, ask price, no price rise) — done in slice 3 ([europe.md](europe.md)).
- [x] A goods buy/sell **UI** on the Europe screen — done (slice 10: per-ship Sell buttons + Buy-goods dropdown).
- [x] Per-player markets exercised for real — the foreign-power AI sells on its own market each turn; independence + per-player save round-trip tested (FP-5 ✅).
- [x] **Dutch trade advantage** (`86d3dfyrm`) — `tradeBonus −50%` halves the market's absorption of the Dutch player's sells (`MarketVolumeFactor` → `Market.Sell(volumeFactor)`). See §2/§3.
- [ ] **Two-sided `tradeBonus`** — apply the advantage to buying too, once the buy path moves the market (today buying is price-static, so the sell-only subset is faithful to what we model).
- [ ] Tea-party / monarch tax rises (later, with foreign-power events).
- [ ] Cross-check the chunked sell-down revenue against a FreeCol play-through for a known seed.
- [ ] AI buying goods in Europe (needs a docked AI ship — FP-6).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-19 | **Dutch trade nation-type advantage** (`86d3dfyrm`, FreeCol `model.modifier.tradeBonus` −50%): `Market.Sell` gains a `volumeFactor` (default 1.0) that scales each chunk's `amountInMarket` delta; `Game.MarketVolumeFactor(player)` folds the player's nation-type `tradeBonus` (Dutch → 0.5) via the shared `NationTypeModifiers` seam and is passed into the two sell sites (`SellColonyGoods`/`SellShipCargo`). The 4th nation-type advantage. Human defaults to no nation (1.0 → default game byte-identical); no RNG, no save change. Faithful subset: sells only (our buy path is price-static). +4 L1 (`MarketTests`); 1149 + soak green | Phase 5 (`86d3dfyrm`) |
| 2026-06-13 | Market price model + treasury + sales tax + SellColonyGoods; save v9 | Phase 4 slice 1 |
| 2026-06-13 | Goods buy/sell on the Europe screen (Sell cargo + Buy-goods dropdown); L3-tested | Phase 4 slice 10 |
| 2026-06-14 | FP-5: per-player markets exercised by the foreign-power AI (sells colony surplus on its own market each turn); `SellColonyGoods`/`SellShipCargo`/`BuyEuropeGoods` player overloads; per-player independence + save round-trip tested | FP-5 |
