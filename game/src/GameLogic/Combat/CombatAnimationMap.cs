using System.Collections.Generic;

namespace CrownAndColony.GameLogic.Combat;

/// <summary>
/// The single source of truth mapping a graded <see cref="CombatResult"/> to the <see cref="CombatAnimationKind"/> the
/// presentation layer plays. Kept in the engine-free <c>GameLogic</c> assembly so the mapping is plain data: it can be
/// asserted in a unit test (every result resolves to a kind) without booting the engine. The presentation-layer
/// <c>CombatAnimation</c> node turns the kind into an on-screen lunge/flash tween over the existing unit sprites
/// (ADR-006: logic decides <em>what</em> to show; presentation decides <em>how</em> it looks).
/// </summary>
/// <remarks>
/// The combat <em>resolution</em> already happened in <c>Game.Attack*</c> by the time this maps — this is purely the
/// visual cue. The grading collapses to three kinds because the procedural animation does not distinguish a great win
/// from a normal one (FreeCol's attack animation likewise plays the same lunge regardless of margin; the demotion /
/// capture / sink is conveyed by the unit state change, not the animation).
/// </remarks>
public static class CombatAnimationMap
{
    private static readonly IReadOnlyDictionary<CombatResult, CombatAnimationKind> Kinds =
        new Dictionary<CombatResult, CombatAnimationKind>
        {
            // The attacker prevailed (decisively or not) → a committed lunge into the defender's tile.
            [CombatResult.GreatWin] = CombatAnimationKind.AttackerHit,
            [CombatResult.Win] = CombatAnimationKind.AttackerHit,
            // The attacker was beaten back (decisively or not) → the lunge stalls and the attacker recoils.
            [CombatResult.Loss] = CombatAnimationKind.AttackerRepelled,
            [CombatResult.GreatLoss] = CombatAnimationKind.AttackerRepelled,
            // Naval evade → a brief feint; the defender slipped away unharmed.
            [CombatResult.Evade] = CombatAnimationKind.Evaded,
        };

    /// <summary>The animation kind to play for <paramref name="result"/>.</summary>
    /// <exception cref="KeyNotFoundException">No kind is registered for <paramref name="result"/> — a missing-mapping bug.</exception>
    public static CombatAnimationKind KindFor(CombatResult result) => Kinds[result];

    /// <summary>Tries to resolve the animation kind for <paramref name="result"/>; returns <c>false</c> if none is registered.</summary>
    public static bool TryGetKind(CombatResult result, out CombatAnimationKind kind) => Kinds.TryGetValue(result, out kind);

    /// <summary>The full result→kind mapping (read-only) — used by tests and by the presentation-layer trigger.</summary>
    public static IReadOnlyDictionary<CombatResult, CombatAnimationKind> All => Kinds;
}
