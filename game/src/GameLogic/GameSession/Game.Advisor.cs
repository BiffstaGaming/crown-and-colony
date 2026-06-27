using System.Collections.Generic;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World.Improvements;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// The <b>kind</b> of advice an <see cref="AdvisorRecommendation"/> conveys — a stable tag (independent of the wording)
/// the UI can use to pick an icon/order, and tests can assert against without matching on display text.
/// </summary>
public enum AdvisorRecommendationKind
{
    /// <summary>The unit is idle (active, has moves, not busy) and has no standing order — fortify, sentry, or move it.</summary>
    Idle,

    /// <summary>The unit stands where a colony could be founded.</summary>
    FoundColony,

    /// <summary>A tooled pioneer stands on terrain it could improve (build a road / plow / clear forest).</summary>
    Improve,

    /// <summary>The unit is next to a native settlement teaching a skill this unit can learn.</summary>
    LearnSkill,
}

/// <summary>
/// One concrete, player-facing recommendation from the active-unit advisor: a stable <see cref="Kind"/> plus the
/// already-formatted English <see cref="Text"/> to render. Computed by the oracle (<see cref="Game.AdviseUnit"/>) — the
/// presentation layer only displays it (ADR-006).
/// </summary>
/// <param name="Kind">The category of advice (for icon/order/testing), independent of the wording.</param>
/// <param name="Text">The ready-to-show recommendation sentence.</param>
public readonly record struct AdvisorRecommendation(AdvisorRecommendationKind Kind, string Text);

public sealed partial class Game
{
    // The three classic pioneer improvements, in the order we surface them (road first, then plow, then clear-forest):
    // only the first applicable one is advised so a pioneer never gets three near-identical "build X here" lines.
    private static readonly string[] AdvisorImprovementOrder =
    {
        TileImprovementType.RoadId,
        TileImprovementType.PlowId,
        TileImprovementType.ClearForestId,
    };

    /// <summary>
    /// The <b>active-unit advisor</b> oracle (parity <c>86d3fq1re</c>): a short, curated, <b>bounded</b> list of concrete
    /// recommendations for what the player could usefully do with <paramref name="unit"/> right now, derived purely from
    /// the existing read-only legality oracles and the current game state. Read-only — it never mutates the unit, the
    /// game, or the save (ADR-006), and uses no randomness (ADR-009).
    /// <para>
    /// The recommendations, in display order, each gated by the same oracle the action itself uses (so the advisor never
    /// suggests an illegal move):
    /// </para>
    /// <list type="number">
    ///   <item>A land unit standing on a foundable tile → "You could found a colony here" (<see cref="CheckFoundColony"/>).</item>
    ///   <item>A tooled pioneer on improvable terrain → "Build a road/plow/clear the forest here" — the first applicable
    ///   improvement only (<see cref="CheckBuildImprovement"/>).</item>
    ///   <item>A unit next to a native settlement teaching a skill it can learn → "Learn &lt;skill&gt; at this settlement"
    ///   (<see cref="CheckLearnSkill"/>).</item>
    ///   <item>An idle unit (active, has moves, not busy, no goto) → "No orders — fortify, sentry, or move".</item>
    /// </list>
    /// <para>
    /// Deliberately a small fixed set, not an open-ended advisory engine (a fuller advisor — economy/military/colony
    /// goals — is a follow-up). Returns an empty list when there is nothing worth saying (a normal mid-move unit, a
    /// carried passenger, a unit at sea/in Europe, or one not owned by the human).
    /// </para>
    /// </summary>
    /// <param name="unit">The selected/active unit to advise.</param>
    /// <returns>0–4 recommendations, in display order; empty when there is nothing to advise.</returns>
    public IReadOnlyList<AdvisorRecommendation> AdviseUnit(Unit unit)
    {
        var advice = new List<AdvisorRecommendation>();

        // The advisor only ever speaks about the human's own units, and only while they are a unit the player can act on
        // (on the map, under its own command — not a passenger aboard a ship, not at sea, not docked in Europe).
        if (!IsHumanOwned(unit) || !unit.IsOnMap)
        {
            return advice;
        }

        // 1. Found a colony where it stands.
        if (CheckFoundColony(unit).Allowed)
        {
            advice.Add(new AdvisorRecommendation(
                AdvisorRecommendationKind.FoundColony,
                "You could found a colony here."));
        }

        // 2. Improve the terrain (the first applicable improvement only — road, else plow, else clear-forest).
        foreach (string improvementId in AdvisorImprovementOrder)
        {
            if (CheckBuildImprovement(unit, improvementId).Allowed)
            {
                advice.Add(new AdvisorRecommendation(
                    AdvisorRecommendationKind.Improve,
                    ImproveAdviceText(improvementId)));
                break;
            }
        }

        // 3. Learn a skill at an adjacent native settlement that teaches one this unit can learn.
        if (LearnableSkillAdvice(unit) is { } learn)
        {
            advice.Add(learn);
        }

        // 4. Idle with no standing order — offered last, as the catch-all "you forgot to give this unit orders".
        if (IsIdleNeedingOrders(unit))
        {
            advice.Add(new AdvisorRecommendation(
                AdvisorRecommendationKind.Idle,
                "No orders — fortify, sentry, or move this unit."));
        }

        return advice;
    }

    /// <summary>
    /// Whether <paramref name="unit"/> is idle and awaiting orders: it is free to act (no fortify/sentry order, not
    /// digging in an improvement, no standing goto) yet still has movement left this turn. Mirrors the "needs orders"
    /// condition the unit-cycling uses (a unit with a standing order or goto is skipped, not flagged idle).
    /// </summary>
    private static bool IsIdleNeedingOrders(Unit unit) =>
        unit.Orders == UnitOrders.Active
        && !unit.IsImproving
        && !unit.IsGoingTo
        && unit.MovementLeft > 0;

    // The "build X here" line for an improvement id, phrased per improvement (road/plow/clear-forest).
    private static string ImproveAdviceText(string improvementId) => improvementId switch
    {
        TileImprovementType.RoadId => "Build a road here.",
        TileImprovementType.PlowId => "Plow this tile to improve its yield.",
        TileImprovementType.ClearForestId => "Clear the forest here for lumber.",
        _ => "Improve this tile.",
    };

    // The learn-skill recommendation for the first adjacent settlement that teaches a skill this unit can learn, or null.
    private AdvisorRecommendation? LearnableSkillAdvice(Unit unit)
    {
        foreach (NativeSettlement settlement in NativeSettlements)
        {
            if ((unit.Position == settlement.Position || unit.Position.IsAdjacentTo(settlement.Position))
                && CheckLearnSkill(unit, settlement).Allowed)
            {
                string skill = SkillDisplayName(settlement.LearnableSkill!);
                return new AdvisorRecommendation(
                    AdvisorRecommendationKind.LearnSkill,
                    $"Learn {skill} at this native settlement.");
            }
        }
        return null;
    }

    // Turns a skill unit-type id (e.g. "model.unit.expertOreMiner") into a readable phrase ("expert ore miner") by
    // taking the last id segment and splitting camelCase. Display-only humanisation — no rule meaning.
    private string SkillDisplayName(string unitTypeId)
    {
        string shortName = Ruleset.Unit(unitTypeId).ShortName; // e.g. "expertOreMiner"
        var sb = new System.Text.StringBuilder(shortName.Length + 4);
        for (int i = 0; i < shortName.Length; i++)
        {
            char c = shortName[i];
            if (i > 0 && char.IsUpper(c))
            {
                sb.Append(' ');
            }
            sb.Append(i == 0 ? char.ToLowerInvariant(c) : c);
        }
        return sb.ToString();
    }
}
