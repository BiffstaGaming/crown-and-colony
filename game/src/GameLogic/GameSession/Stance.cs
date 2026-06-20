namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// The diplomatic relationship one colonial player holds toward another (ADR-019, FP-6a). A faithful
/// subset of FreeCol's <c>Stance</c>: the foundation only ever produces these three — the default
/// <see cref="Uncontacted"/>, <see cref="Peace"/> on first contact, and <see cref="War"/> on attack.
/// </summary>
/// <remarks>
/// Ordered so the zero value is <see cref="Uncontacted"/> — an absent map entry (a never-met pair, or a
/// save written before stances existed) reads as "not yet met" for free. <see cref="CeaseFire"/> is produced
/// by the tension→stance machine (FP-6b) as a war cools toward peace. Values are explicit and appended (not in
/// FreeCol's declaration order) so the saved ordinals of the earlier values stay stable. <see cref="Alliance"/>
/// is only set by a diplomacy action (a <c>StanceTradeItem</c>); it is appended as 4 so every earlier ordinal is
/// untouched.
/// </remarks>
public enum Stance
{
    /// <summary>The two players have not yet met (the default for any unrecorded pair).</summary>
    Uncontacted = 0,

    /// <summary>At peace — set both ways on first contact.</summary>
    Peace = 1,

    /// <summary>At war — set both ways when one attacks the other's unit (or, later, colony).</summary>
    War = 2,

    /// <summary>A truce — a war that has cooled (tension fell below the war band); de-escalates to <see cref="Peace"/> as tension keeps falling (FP-6b).</summary>
    CeaseFire = 3,

    /// <summary>Allied — set only by a diplomacy action (a stance trade item). Appended (=4) so earlier saved ordinals are unchanged; the tension→stance machine never produces it.</summary>
    Alliance = 4,
}
