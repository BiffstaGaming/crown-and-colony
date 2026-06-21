using System.Collections.Generic;

namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// The difficulty-scoped tuning numbers for the foreign-power (rival) AI — the colony cap it expands to, the gold it
/// keeps in reserve before splurging in Europe, and the escalating travel/seek range ladder its offensive units use.
/// A small immutable value record (no <see cref="Ruleset"/> dependency) carried on
/// <see cref="DifficultyOptions.Ai"/> and read wherever a hardcoded AI constant used to live, so a difficulty level —
/// or an Australia-style variant — can scale the rival AI's ambition without a code change.
/// </summary>
/// <remarks>
/// <para>
/// <b>Faithful-subset (no FreeCol per-difficulty option).</b> FreeCol's classic spec does <i>not</i> expose any of
/// these three as a <c>model.difficulty.*</c> option — they are hardcoded in <c>EuropeanAIPlayer</c>: the colony count
/// is the literal guard <c>nColonies &lt; 3</c>, the seek-and-destroy range is the literal <c>8 / 12 / 16</c> ladder in
/// <c>getSeekAndDestroyMission</c>, and the Europe spend reserve is our own AI budget floor (not a FreeCol concept at
/// all). The genuinely difficulty-scaled FreeCol option in this neighbourhood is <c>model.option.settlementNumber</c>,
/// which controls <i>native</i> settlement spacing, not the rival colonial AI. So these stay <b>constant across the
/// shipped levels</b> — every level reads <see cref="ClassicMedium"/> — but are now <b>data-overridable</b> (a variant
/// or future level can restate them) instead of being buried as private <c>const</c>s in <c>Game.cs</c>. The default
/// game is byte-identical: same values, no spec read, no save change.
/// </para>
/// <para>
/// Because there is no spec option to parse, <see cref="Ruleset.ParseDifficulty"/> simply assigns
/// <see cref="ClassicMedium"/> (the same pattern as <see cref="MonarchOptions.ArrearsFactor"/>, a data field kept at
/// its classic value rather than read from the spec). Pure/immutable (ADR-009): no state, no RNG.
/// </para>
/// </remarks>
/// <param name="MaxColonies">
/// Colonies a foreign power's AI founds before its remaining colonists explore instead (FreeCol's hardcoded
/// <c>nColonies &lt; 3</c>; classic value 3 on every level).
/// </param>
/// <param name="EuropeSpendFloor">
/// Gold a foreign power keeps in reserve before it invests the surplus in Europe units, so a poor power keeps
/// recruiting/building rather than draining its treasury (our AI budget floor, not a FreeCol constant; 1500 on every
/// level).
/// </param>
/// <param name="SeekRangeLadder">
/// The escalating Chebyshev range gates an offensive AI unit searches for a target through — the first gate yielding
/// any eligible target wins (FreeCol's hardcoded <c>getSeekAndDestroyMission</c> <c>8 / 12 / 16</c> ladder; the same
/// ladder on every level).
/// </param>
public sealed record AiTuning(
    int MaxColonies,
    int EuropeSpendFloor,
    IReadOnlyList<int> SeekRangeLadder)
{
    /// <summary>
    /// The classic <c>medium</c> AI tuning — the fallback and default, and (since FreeCol scales none of these by
    /// difficulty) the value every shipped level uses. Routing these off their old <c>Game.cs</c> <c>const</c>s is
    /// value-preserving, so the default game is byte-identical.
    /// </summary>
    public static readonly AiTuning ClassicMedium = new(
        MaxColonies: 3,
        EuropeSpendFloor: 1500,
        SeekRangeLadder: [8, 12, 16]);
}
