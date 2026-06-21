namespace CrownAndColony.GameLogic.Combat;

/// <summary>
/// The visual flavour of the short attack animation the presentation layer plays when a combat resolves. This lives in
/// the engine-free <c>GameLogic</c> assembly (not the Godot presentation layer) so the <see cref="CombatResult"/>→kind
/// mapping (<see cref="CombatAnimationMap"/>) is plain data that can be unit-tested without booting the engine
/// (ADR-006: logic decides <em>what</em> to show; presentation decides <em>how</em> it looks).
/// </summary>
/// <remarks>
/// Crown &amp; Colony plays a <em>procedural</em> attack animation — a short lunge of the attacker toward the defender
/// plus a flash, tweened over the existing unit sprites (no new art). This mirrors the shape of FreeCol's
/// <c>animation.Animations.unitAttack</c>, which moves the attacker's sprite toward the defender (FreeCol's per-type
/// <c>.sza</c> sprite-sequence frames cover only the soldier/dragoon roles, so the move/lunge tween is the faithful
/// common denominator). The kind only varies the emphasis, not the motion.
/// </remarks>
public enum CombatAnimationKind
{
    /// <summary>The attacker won — a confident lunge toward the defender's tile, no defender recoil.</summary>
    AttackerHit,

    /// <summary>The attacker lost — the lunge stalls and the attacker recoils/flashes (it was beaten back).</summary>
    AttackerRepelled,

    /// <summary>A naval evade — a brief, half-strength feint; no one is hurt (the defender slipped away).</summary>
    Evaded,
}
