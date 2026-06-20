using System.Linq;
using CrownAndColony.GameLogic.Units;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// A pending emigration choice (FreeCol's <c>selectRecruit</c>): when a player who has earned William Brewster
/// (<c>model.ability.selectRecruit</c>) is due an emigrant, the engine pauses and offers the three dock recruits so
/// the player can pick which one steps ashore in Europe, rather than a random one auto-emigrating.
/// </summary>
/// <param name="PlayerId">The player the choice belongs to (the human; an AI never pauses).</param>
/// <param name="RecruitTypeIds">The unit-type ids of the recruits currently on the dock, by slot (the choices).</param>
public sealed record PendingEmigrationChoice(int PlayerId, IReadOnlyList<string> RecruitTypeIds);

public sealed partial class Game
{
    // Set by the emigration step when a selectRecruit human is due an emigrant; cleared by ChooseEmigrant. In-memory
    // only (not persisted): a pending choice that survived a save would be a rare edge case, deferred to the save
    // stream — a reloaded game simply re-pauses on the next turn the player is still due an emigrant.
    private PendingEmigrationChoice? _pendingEmigration;

    /// <summary>
    /// The emigration choice awaiting the human player, or null when none is pending (FreeCol's <c>selectRecruit</c>
    /// prompt). Set only for a human who has earned William Brewster; the UI shows the three recruits and resolves it
    /// via <see cref="ChooseEmigrant"/>. Read-only for presentation (ADR-006).
    /// </summary>
    public PendingEmigrationChoice? PendingEmigration => _pendingEmigration;

    /// <summary>
    /// Resolves the <see cref="PendingEmigration"/> choice: the recruit in <paramref name="slot"/> emigrates to Europe
    /// (its dock slot refills with a fresh weighted draw), immigration is consumed and the next target rises — exactly
    /// the per-emigrant bookkeeping the auto-path does (FreeCol <c>ServerPlayer.csEmigrate</c>, the SELECT case). If the
    /// player is still due another emigrant after this one, the choice re-arms with the refilled dock; otherwise it
    /// clears. No-op (returns null) when no choice is pending or the slot is out of range.
    /// </summary>
    /// <param name="slot">The dock slot (0..<see cref="RecruitSlots"/>−1) of the recruit to take.</param>
    /// <returns>The emigrated unit, docked in Europe, or null when there was nothing to resolve.</returns>
    public Unit? ChooseEmigrant(int slot)
    {
        if (_pendingEmigration is not { } pending || PlayerById(pending.PlayerId) is not { } player)
        {
            return null;
        }
        if (slot < 0 || slot >= player.RecruitDock.Count)
        {
            return null;
        }

        Unit recruit = Emigrate(player, slot);   // extract precedes the immigration cut, as in the auto-path
        ReduceImmigration(player);
        player.ImmigrationRequired += Ruleset.Difficulty.CrossesIncrement;

        // A bumper crop of crosses can owe more than one emigrant in a turn — re-arm with the refilled dock so the
        // player chooses each in turn; otherwise the backlog is cleared and play resumes.
        _pendingEmigration = player.RecruitDock.Count > 0 && player.Immigration >= EffectiveImmigrationRequired(player)
            ? new PendingEmigrationChoice(player.PlayerId, player.RecruitDock.ToList())
            : null;
        return recruit;
    }
}
