using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Specification;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// The generic limit-evaluation engine (FreeCol <c>Limit</c> + <c>Operand</c> + <c>Event</c>): evaluates a spec
/// <see cref="Limit"/> — a comparison <c>left OP right</c> over game state — and fires a spec <see cref="SpecEvent"/>
/// once all its limits hold. This is the data-driven replacement for the scattered hardcoded year/SoL gates: a
/// variant ruleset can retune a gate (e.g. the Spanish-succession trigger year) by editing the XML alone, and the
/// engine reads it. The classic ruleset's limit values match the previous hardcoded constants exactly, so the default
/// game's behaviour is byte-identical (ADR-006/009).
/// </summary>
public sealed partial class Game
{
    /// <summary>
    /// Whether every limit gating the named spec <c>&lt;event&gt;</c> currently holds for <paramref name="player"/>
    /// (FreeCol's per-event gate, e.g. <c>ServerGame.spanishSuccessionReady</c> AND-ing the event's limits): the event
    /// may fire when all its limits evaluate true. An unknown event id, or an event with no limits, returns
    /// <c>false</c> (nothing to fire) — except that an event present but limitless would be vacuously ready; we treat
    /// a missing event as not-ready so the caller's hardcoded fallback stays in charge. Player-scoped limits read
    /// <paramref name="player"/>; game-scoped limits ignore it (pass any player, or the human). Pure — no RNG, no state
    /// change (ADR-009): callers act on the boolean.
    /// </summary>
    /// <param name="eventId">The spec event id, e.g. <c>model.event.spanishSuccession</c>.</param>
    /// <param name="player">The player the player-scoped limits are read against.</param>
    /// <returns>True iff the event exists and all of its limits are satisfied.</returns>
    public bool CheckSpecEvent(string eventId, Player player)
    {
        SpecEvent? ev = Ruleset.Event(eventId);
        if (ev is null || ev.Limits.Count == 0)
        {
            return false; // no such event / no gates → leave the hardcoded path in charge
        }
        return ev.Limits.Values.All(limit => EvaluateLimit(limit, player));
    }

    /// <summary>
    /// Evaluates a single spec <see cref="Limit"/> (<c>left OP right</c>) against the current game state for
    /// <paramref name="player"/> — the heart of the engine, mirroring FreeCol <c>Limit.evaluate(Player)</c>. Each side
    /// is resolved to an integer (<see cref="ResolveOperand"/>); a side that resolves to <c>null</c> (an operand kind
    /// outside the supported subset) makes the limit <b>true</b> — faithful to FreeCol, where a null operand never
    /// constrains. Otherwise the two integers are compared with the limit's <see cref="LimitOperator"/>.
    /// </summary>
    /// <param name="limit">The limit to evaluate.</param>
    /// <param name="player">The player against which player/settlement-scoped operands are read.</param>
    /// <returns>The truth of <c>left OP right</c>, or <c>true</c> if either side is unresolvable.</returns>
    public bool EvaluateLimit(Limit limit, Player player)
    {
        int? lhs = ResolveOperand(limit.LeftHandSide, player);
        int? rhs = ResolveOperand(limit.RightHandSide, player);
        if (lhs is not { } left || rhs is not { } right)
        {
            return true; // a null operand never constrains (FreeCol Limit.evaluate: lhs==null||rhs==null → true)
        }
        int cmp = left.CompareTo(right);
        return limit.Operator switch
        {
            LimitOperator.Eq => cmp == 0,
            LimitOperator.Lt => cmp < 0,
            LimitOperator.Gt => cmp > 0,
            LimitOperator.Le => cmp <= 0,
            LimitOperator.Ge => cmp >= 0,
            _ => false,
        };
    }

    /// <summary>
    /// Resolves an <see cref="Operand"/> to its integer value for the given <paramref name="player"/> (FreeCol
    /// <c>Operand.getValue</c>), covering the kinds the classic ruleset uses:
    /// <list type="bullet">
    /// <item>a literal <see cref="Operand.Value"/> wins outright;</item>
    /// <item><b>game</b> scope: <see cref="OperandType.Year"/> → the current year; <see cref="OperandType.Option"/> →
    /// the named integer game option (classic: <c>model.option.lastColonialYear</c>);</item>
    /// <item><b>player</b> scope: a <see cref="Operand.MethodName"/> read (classic: <c>getSoL</c> →
    /// <see cref="NationalSonsOfLiberty"/>); <see cref="OperandType.Units"/> → the player's unit count;
    /// <see cref="OperandType.Settlements"/> → its colony count, optionally filtered by an
    /// <c>isConnectedPort=true</c> method predicate (the independence coastal gate);
    /// <see cref="OperandType.FoundingFathers"/> → its elected-father count.</item>
    /// </list>
    /// Anything outside this subset returns <c>null</c> (no constraint), documented as the faithful-subset boundary.
    /// </summary>
    private int? ResolveOperand(Operand operand, Player player)
    {
        if (operand.Value is { } literal)
        {
            return literal; // a constant operand
        }
        return operand.ScopeLevel switch
        {
            LimitScopeLevel.Game => ResolveGameOperand(operand),
            LimitScopeLevel.Player => ResolvePlayerOperand(operand, player),
            _ => null, // settlement/none scope: no classic event uses it → no constraint
        };
    }

    /// <summary>Resolves a game-scoped operand: the current year, or a named integer game option (classic: lastColonialYear). Null for an unsupported kind.</summary>
    private int? ResolveGameOperand(Operand operand) => operand.OperandType switch
    {
        OperandType.Year => CurrentYear,
        OperandType.Option => ResolveIntOption(operand.Type),
        _ => null,
    };

    /// <summary>The integer value of a named game option, for an <see cref="OperandType.Option"/> operand. Only the
    /// option ids the classic events reference are mapped (currently <c>model.option.lastColonialYear</c>); an
    /// unmapped id returns null (no constraint).</summary>
    private int? ResolveIntOption(string? optionId) => optionId switch
    {
        "model.option.lastColonialYear" => Ruleset.LastColonialYear,
        _ => null,
    };

    /// <summary>
    /// Resolves a player-scoped operand for <paramref name="player"/>: a <c>method-name</c> read (classic:
    /// <c>getSoL</c>), or a unit / settlement / founding-father count. A settlement count may carry an
    /// <c>isConnectedPort=true</c> predicate (the independence coastal gate), counting only connected-port colonies.
    /// Null for an unsupported kind/method.
    /// </summary>
    private int? ResolvePlayerOperand(Operand operand, Player player)
    {
        // A method-name read takes precedence (FreeCol falls through to invokeMethod when the operand type is NONE).
        if (operand.OperandType == OperandType.None && operand.MethodName is { } method)
        {
            return method switch
            {
                "getSoL" => NationalSonsOfLiberty(player),
                _ => null, // an unsupported player method → no constraint
            };
        }
        return operand.OperandType switch
        {
            OperandType.Units => _units.Count(u => u.OwnerId == player.PlayerId && !u.IsNative),
            OperandType.Settlements => CountSettlements(operand, player),
            OperandType.FoundingFathers => player.Congress.Count,
            _ => null,
        };
    }

    /// <summary>
    /// A player-scoped settlement count for a <see cref="OperandType.Settlements"/> operand: all the player's colonies,
    /// or — when the operand carries the <c>isConnectedPort=true</c> method predicate (the independence coastal gate) —
    /// only its connected-port (coastal) colonies (<see cref="IsColonyCoastal"/>, FreeCol <c>Colony.isConnectedPort</c>).
    /// </summary>
    private int CountSettlements(Operand operand, Player player)
    {
        IEnumerable<Colony> colonies = ColoniesOf(player);
        if (operand.MethodName == "isConnectedPort" && operand.MethodValue == "true")
        {
            colonies = colonies.Where(IsColonyCoastal);
        }
        return colonies.Count();
    }
}
