namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// A record of one tradeable good whose European <b>price changed</b> over the course of a turn of
/// <see cref="Game.EndTurn"/> — the market sibling of the custom-house / warehouse notices (FreeCol's
/// <c>PRICE_CHANGE</c> property + <c>model.market.priceIncrease</c>/<c>priceDecrease</c> "told about price"
/// <c>ModelMessage</c>, raised by <c>ServerPlayer.csFlushMarket</c>). The market price only moves when something
/// trades — the human's own Europe sells/buys, a colony's custom-house auto-sale, a trade-route delivery — and that
/// movement has no return value the human UI can read after End Turn, so the game snapshots each good's buy (ask)
/// price at the start of every <see cref="Game.EndTurn"/>, compares it after the turn resolves, and emits one of
/// these per good that moved. The presentation layer surfaces them ("⚖ The price of X rose to N").
/// </summary>
/// <remarks>
/// This is transient per-turn UI scratch: it is rebuilt at the end of every <see cref="Game.EndTurn"/> (the
/// snapshot is taken at the turn's start) and is <b>never saved or restored</b> (no save-format impact) — exactly
/// like the other per-turn notices. Only the human's market is watched (foreign powers' markets move silently).
/// The trigger and the carried prices are the <b>buy (ask)</b> price (FreeCol <c>MarketData.getCostToBuy</c>, the
/// price the change message keys off); the sell (bid) price is carried alongside for the message. Fields are raw
/// ids/amounts — formatting the English message (rose/fell, the goods name) is the presentation layer's job
/// (ADR-006). RNG-free and deterministic: the notices are emitted in the market's stable goods order.
/// </remarks>
/// <param name="GoodsId">The goods type whose price changed (e.g. <c>model.goods.sugar</c>).</param>
/// <param name="OldPrice">The good's buy (ask) price at the start of the turn.</param>
/// <param name="NewPrice">The good's buy (ask) price after the turn resolved (≠ <paramref name="OldPrice"/>).</param>
/// <param name="SellPrice">The good's current sell (bid) price, for the player-facing message.</param>
public readonly record struct PriceChangeNotice(
    string GoodsId,
    int OldPrice,
    int NewPrice,
    int SellPrice);
