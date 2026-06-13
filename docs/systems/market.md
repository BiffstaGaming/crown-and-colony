# System: European market & treasury

| | |
|---|---|
| **Status** | Implemented (data layer — direct colony→Europe sales; ship transport is a later slice) |
| **Last verified** | 2026-06-13 @ Phase 4 slice 1 |
| **Code** | `game/src/GameLogic/Trade/Market.cs`, `GameSession/Game.cs` (`Gold`, `TaxRate`, `SellColonyGoods`) |
| **Tests** | `game/tests/GameLogic.Tests/Trade/MarketTests.cs` |
| **FreeCol reference** | `MarketData.java` (`price()` lines 319–383), `Market.java`, `server/model/ServerPlayer.java` (`sellInEurope`) |
| **Related systems** | [colonies](colonies.md), [ruleset-data](ruleset-data.md), [save-load](save-load.md) |

## 1. How it works (plain English)

You sell your colonies' goods to Europe for **gold**, and Europe charges a **sales tax** on every sale. Prices follow supply and demand: the more of a good Europe already holds, the less it pays for more of it — so dumping 600 sugar drives the sugar price down as you sell. Each good has a **sell price** (what you receive) and a higher **buy price** (what you'd pay to buy it back); the gap between them is fixed per good.

**Worked example:** sugar starts selling at 2 gold each. Sell 600 sugar with no tax and you get 1,200 gold — but the price has dropped to 1 gold by the end, because Europe is now glutted with sugar. Add a 50% tax and you'd keep only 600 of that.

## 2. Detailed rules

- Every tradeable good seeds from the spec's `<market>`: `initial-amount` (Europe's starting stock), `initial-price` (sell/bid), `price-difference` (the spread → buy/ask = bid + difference).
- **Initial prices (verified against the classic spec):** food 1/9, sugar 2/4, tobacco 2/4, ore 4/7, silver 16/18, rum 10/12, muskets 1/3.
- **Price formula** (FreeCol `MarketData.price()`): `bid = round(initialPrice × initialAmount ÷ amountInMarket)`, `ask = bid + spread`. Selling adds the goods to `amountInMarket`, lowering the bid.
- **Chunking:** sales are processed in batches of 100; each batch is priced, then moves the market, so one huge sale can't be dumped at the opening price.
- **Hard bounds:** bid never below 1, ask never above 19.
- **New World cap:** sugar/tobacco/cotton/furs and the goods made from them can't have their sell price pushed above `initialPrice + 2`.
- **Jump clamp:** a single recompute can't move the price by more than the spread (anti-exploit); the inventory is nudged back to stay consistent.
- **Tax:** `goldCredited = (100 − taxRate) × revenue ÷ 100`, integer-truncated (the player can lose a gold to rounding). Tax starts at 0%.
- Non-tradeable goods (grain, fish, bells, crosses — anything without a `<market>`) cannot be sold.

**Deviations from FreeCol:** for this slice, `SellColonyGoods` sells straight from a colony warehouse to Europe (an abstraction); requiring an actual ship to carry the cargo is Phase 4 slice 3. Buying goods (which doesn't move the price in FreeCol) is not yet exposed.

## 3. Technical design

- `GoodsType` gains `IsNewWorldGoods` and a `GoodsMarket?` (InitialAmount/InitialPrice/PriceDifference; null = untradeable), parsed from `<market>`.
- `Market` (engine-free, `Trade/Market.cs`): per-good `{ amountInMarket, bid, ask }`; `BidPrice`/`AskPrice`/`AmountInMarket`; `Sell(goodsId, amount, taxPercent) → SaleResult(beforeTax, afterTax)` chunks by 100 and recomputes per chunk. `Recompute` is a faithful port of `MarketData.price()` (float supply formula, new-world cap, jump clamp, [1,19] bounds). `SaveDeltas`/`LoadDeltas` persist only inventories that moved from their seed.
- `Game`: `Gold`, `TaxRate`, `Market`; `SellColonyGoods(colony, goodsId, amount)` deducts stores and credits the treasury. `Game.New` takes optional `startingGold`/`startingTax` (default 0 = classic).
- **Persistence:** save v9 stores `Gold`, `Tax`, and the sparse moved-market map; pre-v9 saves load with 0 gold/tax and a ruleset-seeded market.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `MarketTests`: initial prices vs spec (7 goods), inventory rise + bid fall (sugar 600 → bid 1, gold 1200), tax truncation (33% on 112 → 75), [1,19] bounds across all goods, New World cap, SellColonyGoods deduct/credit/validate | ✅ |
| L2 Scenario | Always | save round-trip preserves gold/tax/moved-market; pre-v9 reseeds | ✅ |
| L3 Interaction | No UI yet | — (Europe screen is slice 3) | — |
| L4 Visual | No screen yet | — | — |

- **FreeCol cross-check:** `Recompute` ported line-for-line from `MarketData.price()`; initial prices verified against the spec; the sugar sell-down (600 → bid 1, inventory 2100) and silver tax (75) hand-computed from the reference formula.

## 5. Open issues / TODO

- [ ] Buying goods from Europe (ask price, no price rise) — with the Europe screen (slice 3).
- [ ] Sales require a ship to carry cargo (slice 3); tea-party/monarch tax rises (later).
- [ ] Cross-check the chunked sell-down revenue against a FreeCol play-through for a known seed.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Market price model + treasury + sales tax + SellColonyGoods; save v9 | Phase 4 slice 1 |
