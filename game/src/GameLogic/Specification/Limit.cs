namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// The comparison a <see cref="Limit"/> applies (FreeCol <c>Limit.Operator</c>): the relation
/// <c>leftHandSide OP rightHandSide</c> must hold for the limit to be satisfied.
/// </summary>
public enum LimitOperator
{
    /// <summary>Left equals right (<c>eq</c>).</summary>
    Eq,

    /// <summary>Left is strictly less than right (<c>lt</c>).</summary>
    Lt,

    /// <summary>Left is strictly greater than right (<c>gt</c>).</summary>
    Gt,

    /// <summary>Left is less than or equal to right (<c>le</c>).</summary>
    Le,

    /// <summary>Left is greater than or equal to right (<c>ge</c>).</summary>
    Ge,
}

/// <summary>
/// The kind of game-state count an <see cref="Operand"/> resolves to (FreeCol <c>Operand.OperandType</c>) — the
/// faithful subset the classic ruleset actually uses. With a literal <see cref="Operand.Value"/> set the operand is
/// that constant and the type is ignored; otherwise the engine reads the named quantity at the operand's
/// <see cref="LimitScopeLevel"/>.
/// </summary>
public enum OperandType
{
    /// <summary>No aggregate kind — the operand is a literal value, an option lookup, or a method-name read.</summary>
    None,

    /// <summary>A count of units (classic: the wagon-train build cap's left-hand side, player-scoped).</summary>
    Units,

    /// <summary>A count of settlements/colonies (classic: the wagon-train cap's right-hand side; the independence coastal-colony gate).</summary>
    Settlements,

    /// <summary>A count of founding fathers (classic: unused by any <c>&lt;event&gt;</c>, kept for fidelity).</summary>
    FoundingFathers,

    /// <summary>The current game year (classic: the independence and Spanish-succession year gates, game-scoped).</summary>
    Year,

    /// <summary>An integer game option named by <see cref="Operand.Type"/> (classic: <c>model.option.lastColonialYear</c>, game-scoped).</summary>
    Option,
}

/// <summary>
/// How broadly an <see cref="Operand"/> is read (FreeCol <c>Operand.ScopeLevel</c>): the game as a whole, a single
/// player, a single settlement, or none (a literal). The level decides which state the evaluation reads — e.g. a
/// player-scoped <c>getSoL</c> reads that player's national Sons-of-Liberty.
/// </summary>
public enum LimitScopeLevel
{
    /// <summary>Not scoped — a literal value with no game-state read.</summary>
    None,

    /// <summary>Resolved against a single settlement (classic events use none; kept for fidelity).</summary>
    Settlement,

    /// <summary>Resolved against a single player (classic: <c>getSoL</c>, settlement counts).</summary>
    Player,

    /// <summary>Resolved against the whole game (classic: year, option).</summary>
    Game,
}

/// <summary>
/// One side of a <see cref="Limit"/> comparison (FreeCol <c>Operand</c> — itself a <c>Scope</c>): it resolves to an
/// integer read from game state. Resolution order mirrors FreeCol <c>Operand.getValue</c>:
/// <list type="number">
/// <item>a literal <see cref="Value"/> wins outright (the constant);</item>
/// <item>otherwise, at <see cref="LimitScopeLevel.Game"/> scope, a <see cref="OperandType.Year"/> /
/// <see cref="OperandType.Option"/> read, or a player-aggregate count summed over live players;</item>
/// <item>at <see cref="LimitScopeLevel.Player"/> scope, a per-player count (<see cref="OperandType.Units"/> /
/// <see cref="OperandType.Settlements"/> / <see cref="OperandType.FoundingFathers"/>) or a named
/// <see cref="MethodName"/> read (classic: <c>getSoL</c>).</item>
/// </list>
/// Only the operand kinds the classic ruleset uses are evaluated; anything outside that subset yields <c>null</c>,
/// which (faithfully to FreeCol) makes the whole limit evaluate <c>true</c> (a null operand never constrains).
/// </summary>
/// <param name="OperandType">The aggregate kind to count, or <see cref="Specification.OperandType.None"/> for a literal / option / method read.</param>
/// <param name="ScopeLevel">The level at which the operand is read.</param>
/// <param name="Value">A literal integer value, or <c>null</c> to read from game state.</param>
/// <param name="MethodName">A named integer read on the scoped object (classic: <c>getSoL</c> on a player), or <c>null</c>.</param>
/// <param name="MethodValue">A method-result predicate value for filtered settlement counts (classic: the independence coastal gate's <c>isConnectedPort=true</c>), or <c>null</c>.</param>
/// <param name="Type">The id the operand refers to — the option id for an <see cref="Specification.OperandType.Option"/> read, or <c>null</c>.</param>
public sealed record Operand(
    OperandType OperandType,
    LimitScopeLevel ScopeLevel,
    int? Value,
    string? MethodName = null,
    string? MethodValue = null,
    string? Type = null);

/// <summary>
/// A spec <c>&lt;limit&gt;</c> (FreeCol <c>Limit</c>): a comparison <c>LeftHandSide Operator RightHandSide</c> over
/// game state that gates the availability of a thing — a unit-build cap (the wagon train) or a game event (declaring
/// independence, the Spanish succession). The engine evaluates it against a <see cref="GameSession.Game"/> /
/// <see cref="GameSession.Player"/> (see <c>Game.EvaluateLimit</c>).
/// </summary>
/// <param name="Id">The limit's id (e.g. <c>model.limit.spanishSuccession.year</c>), used to fetch a named limit off an <see cref="SpecEvent"/>.</param>
/// <param name="LeftHandSide">The left operand.</param>
/// <param name="Operator">The comparison to apply.</param>
/// <param name="RightHandSide">The right operand.</param>
public sealed record Limit(string Id, Operand LeftHandSide, LimitOperator Operator, Operand RightHandSide);

/// <summary>
/// A spec <c>&lt;event&gt;</c> (FreeCol <c>Event</c>): a special game occurrence — declaring independence, the
/// Spanish succession — that fires once all its <see cref="Limits"/> are satisfied (FreeCol gates each event on its
/// own limits in the server tick, e.g. <c>ServerGame.spanishSuccessionReady</c>). Carries an optional
/// <see cref="ScoreValue"/> (the declare-independence event scores 100) and its named limit map.
/// </summary>
/// <param name="Id">The event id (e.g. <c>model.event.spanishSuccession</c>).</param>
/// <param name="ScoreValue">The score this event is worth when it fires (classic: declare-independence = 100, succession = 0).</param>
/// <param name="Limits">The event's limits, keyed by limit id — all must hold for the event to fire.</param>
public sealed record SpecEvent(string Id, int ScoreValue, IReadOnlyDictionary<string, Limit> Limits)
{
    /// <summary>The event's limit with the given id (FreeCol <c>Event.getLimit</c>), or <c>null</c> if it has no such limit.</summary>
    /// <param name="id">The limit id, e.g. <c>model.limit.spanishSuccession.year</c>.</param>
    public Limit? Limit(string id) => Limits.TryGetValue(id, out Limit? limit) ? limit : null;
}
