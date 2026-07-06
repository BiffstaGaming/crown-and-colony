namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// The Sons-of-Liberty / tory thresholds that set a colony's production bonus — FreeCol difficulty options
/// <c>veryGoodGovernmentLimit</c>/<c>goodGovernmentLimit</c>/<c>badGovernmentLimit</c>/<c>veryBadGovernmentLimit</c>.
/// They differ by difficulty (the tory-penalty limits tighten on harder levels: veryEasy 8/12 … medium 6/10 …
/// veryHard 4/8). A small value record (no <see cref="Ruleset"/> dependency) so the pure colony logic can carry it.
/// </summary>
/// <param name="VeryGood">SoL% at or above which government is "very good" → +2 per worker (medium 100).</param>
/// <param name="Good">SoL% at or above which government is "good" → +1 per worker (medium 50).</param>
/// <param name="Bad">Tory count above which government is "bad" → −1 per worker (medium 6).</param>
/// <param name="VeryBad">Tory count above which FreeCol's government is "very bad" → −2 per worker (medium 10). <b>Parsed but no longer applied to the production bonus</b> — Col1 has a single −1 bad-government tier (see <c>Colony.ProductionBonus</c>, 86d3kgbtj).</param>
public sealed record GovernmentLimits(int VeryGood, int Good, int Bad, int VeryBad)
{
    /// <summary>The classic <c>model.difficulty.medium</c> thresholds (100/50/6/10) — the fallback and default.</summary>
    public static readonly GovernmentLimits ClassicMedium = new(VeryGood: 100, Good: 50, Bad: 6, VeryBad: 10);
}
