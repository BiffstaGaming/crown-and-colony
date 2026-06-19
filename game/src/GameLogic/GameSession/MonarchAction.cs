namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// An action the player's home-nation Monarch can take on a turn (FreeCol <c>Monarch.MonarchAction</c>). The
/// weighted chooser (<see cref="Game.GetMonarchActionChoices"/>) offers the valid ones each turn past the grace
/// period; <see cref="NoAction"/> dominates early and fades as the game runs on. <see cref="ForceTax"/> and
/// <see cref="Displeasure"/> are never chosen by the chooser (they are consequences of a player response).
/// </summary>
public enum MonarchAction
{
    /// <summary>The King does nothing this turn.</summary>
    NoAction = 0,

    /// <summary>Demand a tax rise (peace-time framing).</summary>
    RaiseTaxAct,

    /// <summary>Demand a tax rise (war framing).</summary>
    RaiseTaxWar,

    /// <summary>Force a small tax rise outright (only when the player can no longer hold a tea party).</summary>
    ForceTax,

    /// <summary>Lower the tax (war goodwill).</summary>
    LowerTaxWar,

    /// <summary>Lower the tax (other goodwill).</summary>
    LowerTaxOther,

    /// <summary>Waive the tax demand (no change).</summary>
    WaiveTax,

    /// <summary>Add units to the Royal Expeditionary Force.</summary>
    AddToRef,

    /// <summary>Make peace with a rival on the player's behalf.</summary>
    DeclarePeace,

    /// <summary>Declare war on a rival on the player's behalf.</summary>
    DeclareWar,

    /// <summary>Offer free land military support.</summary>
    SupportLand,

    /// <summary>Offer free naval support (after privateer raids).</summary>
    SupportSea,

    /// <summary>Offer mercenaries for gold.</summary>
    MonarchMercenaries,

    /// <summary>Offer Hessian mercenaries for gold (a larger, costlier force).</summary>
    HessianMercenaries,

    /// <summary>The King is displeased (a consequence of declining an affordable offer).</summary>
    Displeasure,
}

/// <summary>
/// A monarch demand awaiting the human's accept/reject (FreeCol <c>MonarchSession</c>; transient — not saved). For a
/// tax demand it carries the proposed <paramref name="TaxRaise"/> and the goods the King eyes (so a reject can check
/// whether they are still in the colony — a tea party — or already gone — a forced rise). Later items extend it for
/// mercenary offers (force + price).
/// </summary>
/// <param name="Action">The monarch action being demanded.</param>
/// <param name="TaxRaise">The proposed new tax rate (for a RAISE_TAX demand).</param>
/// <param name="GoodsId">The taxed goods' id (for a RAISE_TAX demand).</param>
/// <param name="ColonyId">The colony holding those goods.</param>
/// <param name="GoodsAmount">How much of the goods the demand concerns (capped at one cargo).</param>
/// <param name="Offer">The units offered (a mercenary offer); null for a tax demand.</param>
/// <param name="Price">The gold price of the offer (a mercenary offer); 0 for a tax demand.</param>
public sealed record PendingMonarchDemand(
    MonarchAction Action, int TaxRaise = 0, string? GoodsId = null, int ColonyId = 0, int GoodsAmount = 0,
    IReadOnlyList<ForceEntry>? Offer = null, int Price = 0);

/// <summary>One block of like units in a monarch force or offer (the REF, mercenaries, military support).</summary>
/// <param name="UnitTypeId">The unit type id.</param>
/// <param name="RoleId">The military role id the units carry (null = the default role).</param>
/// <param name="Count">How many.</param>
public sealed record ForceEntry(string UnitTypeId, string? RoleId, int Count);

/// <summary>The most valuable tradeable goods stockpile in a player's colonies (FreeCol <c>getMostValuableGoods</c>).</summary>
/// <param name="GoodsId">The goods type id.</param>
/// <param name="ColonyId">The colony holding it.</param>
/// <param name="Amount">The amount concerned (capped at one cargo).</param>
public sealed record ValuableGoods(string GoodsId, int ColonyId, int Amount);
