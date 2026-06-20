namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// A record of one good a colony's <b>custom house</b> auto-sold to Europe during a colony turn of
/// <see cref="Game.EndTurn"/> — the export sibling of the combat/raid notices. Each turn a custom-house colony
/// ships every eligible good's surplus over its retain level to the owner's European market (FreeCol
/// <c>ServerColony.csNewTurn</c>'s customs sale); that sale has no return value the human UI can read, so the
/// game collects these notices and the presentation layer surfaces them after End Turn ("your custom house in
/// X sold N sugar for G gold").
/// </summary>
/// <remarks>
/// This is transient per-turn UI scratch: it is cleared at the start of every <see cref="Game.EndTurn"/> and is
/// never saved or restored (no save-format impact). Only human-owned colonies' sales are recorded — foreign
/// powers' custom houses still sell, they just produce no player-facing notice. Fields are raw ids/names/amounts —
/// formatting the English message is the presentation layer's job (ADR-006).
/// </remarks>
/// <param name="ColonyId">The selling colony's id.</param>
/// <param name="ColonyName">The selling colony's name.</param>
/// <param name="GoodsId">The goods type sold (e.g. <c>model.goods.sugar</c>).</param>
/// <param name="Amount">How much of that good was shipped to Europe.</param>
/// <param name="Gold">The gold credited to the treasury for the sale, after tax.</param>
public readonly record struct CustomHouseSaleNotice(
    int ColonyId,
    string ColonyName,
    string GoodsId,
    int Amount,
    int Gold);
