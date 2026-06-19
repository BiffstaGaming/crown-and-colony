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
