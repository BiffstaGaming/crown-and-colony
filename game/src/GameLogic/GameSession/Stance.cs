namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// The diplomatic relationship one colonial player holds toward another (ADR-019, FP-6a). A faithful
/// subset of FreeCol's <c>Stance</c>: the foundation only ever produces these three — the default
/// <see cref="Uncontacted"/>, <see cref="Peace"/> on first contact, and <see cref="War"/> on attack.
/// </summary>
/// <remarks>
/// Ordered so the zero value is <see cref="Uncontacted"/> — an absent map entry (a never-met pair, or a
/// save written before stances existed) reads as "not yet met" for free. FreeCol additionally has
/// <c>Alliance</c> and <c>CeaseFire</c>; those require diplomacy actions and the tension→stance AI machine
/// (<c>getStanceFromTension</c>), both deferred to FP-6b — they will slot in as new values when added.
/// </remarks>
public enum Stance
{
    /// <summary>The two players have not yet met (the default for any unrecorded pair).</summary>
    Uncontacted = 0,

    /// <summary>At peace — set both ways on first contact.</summary>
    Peace = 1,

    /// <summary>At war — set both ways when one attacks the other's unit (or, later, colony).</summary>
    War = 2,
}
