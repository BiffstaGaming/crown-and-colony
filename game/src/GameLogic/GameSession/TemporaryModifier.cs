using CrownAndColony.GameLogic.Specification;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// A duration-bounded <see cref="FatherModifier"/> that is active only within a turn window
/// and is stripped once it expires — the runtime, time-limited cousin of the always-on
/// founding-father / nation modifiers (FreeCol <c>Modifier</c> carrying <c>firstTurn</c>/
/// <c>lastTurn</c>, built by <c>Modifier.makeTimedModifier</c> and removed by the
/// per-turn temporary-modifier strip).
/// </summary>
/// <remarks>
/// <para>
/// FreeCol attaches such a modifier to a player or colony and re-checks it every new turn:
/// while <see cref="AppliesTo"/> it contributes its bonus (e.g. a disaster's timed goods-party
/// penalty, an event bonus), and once <see cref="IsOutOfDate"/> the turn-loop strips it. We mirror
/// only that bounded-lifetime behaviour — the <see cref="Payload"/> reuses the existing
/// <see cref="FatherModifier"/> arithmetic so a temporary modifier folds into a value exactly like a
/// permanent one.
/// </para>
/// <para>
/// <b>Classic ships none of these</b> (no classic content registers a timed modifier), so the
/// registry on <see cref="Game"/> stays empty in the default game; nothing is registered, nothing is
/// queried, nothing expires, and the slice is never serialized — the default game is byte-identical
/// (ADR-009) and the save version is unchanged. This is the faithful subset: a variant/event that
/// wants a duration-bounded bonus registers one and it expires correctly on its last turn.
/// </para>
/// </remarks>
/// <param name="Payload">The modifier value/type/target/scope to fold while active (FreeCol the underlying <c>Modifier</c>).</param>
/// <param name="FirstTurn">The first turn the modifier is active, inclusive (FreeCol <c>Modifier.firstTurn</c>).</param>
/// <param name="LastTurn">The last turn the modifier is active, inclusive (FreeCol <c>Modifier.lastTurn</c>); it expires the turn <em>after</em> this.</param>
/// <param name="ColonyId">
/// The colony this modifier is scoped to, or <c>null</c> for a game-wide (player-scoped) modifier. FreeCol attaches a
/// disaster's timed production penalty to the <em>struck colony</em> (<c>cs.addModifier(this, colony, …)</c>), so it
/// damps only that colony's output; a colony-scoped modifier folds only when the production folding a value passes the
/// matching colony id. A <c>null</c> id is unscoped and folds everywhere (a variant/event bonus), preserving the
/// original behaviour.
/// </param>
public sealed record TemporaryModifier(FatherModifier Payload, int FirstTurn, int LastTurn, int? ColonyId = null)
{
    /// <summary>What this modifier targets (a goods id, a combat target, …) — the <see cref="Payload"/>'s target.</summary>
    public string TargetId => Payload.TargetId;

    /// <summary>
    /// Whether this modifier applies to production being folded for colony <paramref name="colonyId"/> (or, when
    /// <paramref name="colonyId"/> is <c>null</c>, to a non-colony fold such as movement/sail time). An unscoped
    /// modifier (<see cref="ColonyId"/> == <c>null</c>) matches every fold; a colony-scoped one matches only when the
    /// fold's colony id equals its own — so a disaster penalty on one colony never leaks to another (FreeCol's
    /// per-colony <c>addModifier</c> scoping).
    /// </summary>
    /// <param name="colonyId">The colony whose production is being folded, or <c>null</c> for a non-colony fold.</param>
    public bool AppliesToColony(int? colonyId) => ColonyId is null || ColonyId == colonyId;

    /// <summary>
    /// Whether this modifier is active on <paramref name="turn"/> (FreeCol <c>Feature.appliesTo(Turn)</c>):
    /// true when <c><see cref="FirstTurn"/> &lt;= turn &lt;= <see cref="LastTurn"/></c>. A modifier registered
    /// for a window contributes its bonus only inside that window.
    /// </summary>
    public bool AppliesTo(int turn) => turn >= FirstTurn && turn <= LastTurn;

    /// <summary>
    /// Whether this modifier has expired by <paramref name="turn"/> (FreeCol <c>Feature.isOutOfDate(Turn)</c>):
    /// true once <c>turn &gt; <see cref="LastTurn"/></c>. The per-turn strip removes a modifier the first turn
    /// this becomes true — so a modifier with <see cref="LastTurn"/> = T is still active on turn T and removed
    /// on turn T+1.
    /// </summary>
    public bool IsOutOfDate(int turn) => turn > LastTurn;

    /// <summary>
    /// Builds a timed modifier active from <paramref name="start"/> for <paramref name="duration"/> turns
    /// (FreeCol <c>Modifier.makeTimedModifier</c>): the last active turn is <c>start + duration - 1</c>, so a
    /// duration of 1 is active only on <paramref name="start"/> and expires the next turn. <paramref name="duration"/>
    /// must be at least 1.
    /// </summary>
    /// <param name="template">The modifier value/type/target/scope to apply while active.</param>
    /// <param name="duration">The number of turns the modifier stays active (≥ 1).</param>
    /// <param name="start">The turn the modifier becomes active (its <see cref="FirstTurn"/>).</param>
    /// <param name="colonyId">The colony to scope the modifier to (FreeCol's per-colony disaster modifier), or <c>null</c> for a game-wide modifier.</param>
    /// <returns>A <see cref="TemporaryModifier"/> bounded to <c>[start, start + duration - 1]</c>.</returns>
    public static TemporaryModifier MakeTimed(FatherModifier template, int duration, int start, int? colonyId = null)
    {
        if (duration < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "A temporary modifier must last at least one turn.");
        }

        return new TemporaryModifier(template, start, start + duration - 1, colonyId);
    }
}
