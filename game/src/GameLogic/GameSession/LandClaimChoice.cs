namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// How a player resolves a <b>forced</b> native-land claim — the choice surfaced when founding a colony on, or
/// working, a tile a native nation owns (FreeCol <c>ServerPlayer.csClaimLand</c> price semantics: a positive price is
/// paid, a negative price <c>STEAL_LAND</c> is stolen). Mirrors the original game's "the natives own this land —
/// buy it, take it, or abandon the attempt?" prompt. <see cref="Abandon"/> is the no-op outcome (the action is simply
/// not performed); only <see cref="Buy"/> and <see cref="Steal"/> are passed to the founding / work-assignment APIs.
/// </summary>
public enum LandClaimChoice
{
    /// <summary>Cancel the founding / work assignment — no land changes hands, no tension. Surfaced for the human's
    /// dialog; never passed to the action itself (the caller simply does not proceed).</summary>
    Abandon = 0,

    /// <summary>Pay the tribe the land price in gold for the tile (free + peaceful under Peter Minuit). FreeCol
    /// <c>csClaimLand</c> with a positive (or zero) price.</summary>
    Buy = 1,

    /// <summary>Take the tile by force — no gold, but the owning nation's settlements gain land-taken alarm toward
    /// the acting player. FreeCol <c>csClaimLand</c> with <c>STEAL_LAND</c> (a negative price).</summary>
    Steal = 2,
}
