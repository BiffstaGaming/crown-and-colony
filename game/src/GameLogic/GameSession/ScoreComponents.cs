namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// The itemised parts of a player's final score (FreeCol <c>ServerPlayer.updateScore</c>), as returned by
/// <see cref="Game.ScoreBreakdown"/>. A pure value object: each summand is a whole-point contribution, and
/// <see cref="Total"/> reassembles them exactly as <see cref="Game.PlayerScore"/> does — the subtotal of the five
/// summands, then the independence percentage bonus applied to that subtotal. Holds no game references and is never
/// persisted; the victory / end-of-game screen reads it to show the score line-by-line.
/// </summary>
/// <param name="UnitValues">Σ of the player's units' ruleset score values (FreeCol <c>UnitType.getScoreValue</c>).</param>
/// <param name="ColonyLiberty">Σ of the liberty (bells) banked across the player's colonies.</param>
/// <param name="FoundingFatherPoints">5 points per elected Founding Father (FreeCol <c>SCORE_FOUNDING_FATHER</c>).</param>
/// <param name="GoldPoints">⌊0.001·gold⌋ — one point per 1000 gold (FreeCol <c>SCORE_GOLD</c>).</param>
/// <param name="HistoryPoints">Σ of the human's scored history events (region discovery today); 0 for a non-human.</param>
/// <param name="IndependenceBonusPercent">The percentage bonus applied to the subtotal for a nation that has won
/// independence — 100/50/25 for the first/second/third such power, else 0 (FreeCol's INDEPENDENCE history switch).</param>
public readonly record struct ScoreComponents(
    int UnitValues,
    int ColonyLiberty,
    int FoundingFatherPoints,
    int GoldPoints,
    int HistoryPoints,
    int IndependenceBonusPercent)
{
    /// <summary>The sum of the five summands before the independence bonus is applied.</summary>
    public int Subtotal => UnitValues + ColonyLiberty + FoundingFatherPoints + GoldPoints + HistoryPoints;

    /// <summary>The points the independence bonus adds (<c>Subtotal·percent/100</c>, integer-truncated, matching FreeCol).</summary>
    public int IndependenceBonus => Subtotal * IndependenceBonusPercent / 100;

    /// <summary>The final score: <see cref="Subtotal"/> plus the <see cref="IndependenceBonus"/>; equals <see cref="Game.PlayerScore"/>.</summary>
    public int Total => Subtotal + IndependenceBonus;
}
