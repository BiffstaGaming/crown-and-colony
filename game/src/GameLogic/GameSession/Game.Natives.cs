using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// Player-vs-native scout/missionary actions that go beyond the peaceful speak-with-chief / learn-skill /
/// establish-mission set: <b>inciting a native settlement to war against a European rival</b> (a missionary action,
/// FreeCol <c>InGameController.incite</c>). The scout-initiated <b>demand-tribute-from-natives</b> action already lives
/// on the main <see cref="Game"/> partial (<see cref="Game.CheckDemandTribute"/>/<see cref="Game.DemandTribute(Unit, Position)"/>);
/// the on-map <c>NativeSettlementPanel</c> simply surfaces it. This partial holds the native-incite rules only — kept off
/// the 12k-line core file but the same <c>Game</c> class (ADR-006: every outcome is a Game oracle the panel forwards to).
/// </summary>
public partial class Game
{
    /// <summary>The role ability a unit must carry to incite a tribe (FreeCol <c>model.ability.inciteNatives</c>, granted by <c>model.role.missionary</c>).</summary>
    private const string InciteNativesAbility = "model.ability.inciteNatives";

    /// <summary>
    /// Alarm the incited tribe gains toward the rival on a successful incite (FreeCol <c>Tension.WAR_MODIFIER</c> =
    /// <c>Level.HATEFUL.limit</c> = 1000): a full war-level spike that drives the tribe's braves against that power.
    /// Applied to the settlement's per-rival alarm channel, nation-wide (every settlement of that nation), so the whole
    /// tribe turns on the rival — mirroring FreeCol applying the tension to the native <em>player</em>.
    /// </summary>
    public const int InciteWarAlarm = 1000;

    /// <summary>The minimum an incite ever costs (FreeCol <c>InGameController.incite</c> floors <c>goldToPay</c> at 650).</summary>
    internal const int InciteFloorCost = 650;

    /// <summary>The base bribe when the tribe is angrier at the inciter than at the rival — a hard sell (FreeCol's 10000).</summary>
    internal const int InciteBaseCostHostile = 10000;

    /// <summary>The base bribe when the tribe is at least as friendly to the inciter as to the rival — an easy sell (FreeCol's 5000).</summary>
    internal const int InciteBaseCostFriendly = 5000;

    /// <summary>Gold added per point the inciter's alarm exceeds the rival's (FreeCol <c>goldToPay += 20 * (payingValue - targetValue)</c>).</summary>
    internal const int InciteCostPerAlarmPoint = 20;

    /// <summary>
    /// The gold an incite of <paramref name="settlement"/> against the rival <paramref name="rivalId"/> costs the inciter
    /// (FreeCol <c>InGameController.incite</c>): based on the gap between the tribe's alarm toward the <b>inciter</b>
    /// (<c>payingValue</c>) and toward the <b>rival</b> (<c>targetValue</c>). When the tribe is angrier at the inciter
    /// than at the rival (a harder sell) the base is <see cref="InciteBaseCostHostile"/> (10000), else
    /// <see cref="InciteBaseCostFriendly"/> (5000); then <c>+ 20 × (inciterAlarm − rivalAlarm)</c>, floored at
    /// <see cref="InciteFloorCost"/> (650). So a tribe that already hates the rival incites cheaply, and one that likes
    /// the rival (but not you) is dear. Pure; draws no RNG (ADR-009). The inciter's perspective is the human channel
    /// when called for the human; the rival's is its own channel.
    /// </summary>
    /// <param name="settlement">The settlement being incited.</param>
    /// <param name="inciterId">The colonial player paying the bribe (the human is <see cref="HumanAlarmChannel"/>).</param>
    /// <param name="rivalId">The colonial player the tribe is being incited against.</param>
    internal int InciteNativesCost(NativeSettlement settlement, int inciterId, int rivalId)
    {
        int payingValue = settlement.AlarmFor(inciterId);
        int targetValue = settlement.AlarmFor(rivalId);
        int gold = payingValue > targetValue ? InciteBaseCostHostile : InciteBaseCostFriendly;
        gold += InciteCostPerAlarmPoint * (payingValue - targetValue);
        return System.Math.Max(gold, InciteFloorCost);
    }

    /// <summary>
    /// The live European rivals <paramref name="unit"/>'s owner could incite this settlement's tribe against — every
    /// colonial power other than the inciter (FreeCol offers <c>getLiveEuropeanPlayers(player)</c>), in stable player-id
    /// order. Empty in a solo game (no rival exists). A read helper for the panel's rival picker (ADR-006).
    /// </summary>
    public IReadOnlyList<Player> IncitableRivals(Unit unit) =>
        _players
            .Where(p => p.PlayerId != unit.OwnerId && p.PlayerType == PlayerType.Colonial)
            .OrderBy(p => p.PlayerId)
            .ToList();

    /// <summary>
    /// Whether <paramref name="unit"/> may incite <paramref name="settlement"/>'s tribe against the rival
    /// <paramref name="rivalId"/> now (FreeCol's <c>incite</c> preconditions): an on-map <b>missionary-role</b> unit
    /// (carrying <see cref="InciteNativesAbility"/>) with movement left, on or adjacent to the settlement, where
    /// <paramref name="rivalId"/> is a distinct live colonial power and the inciter can afford the bribe. A read oracle
    /// (ADR-006): no RNG, no mutation. The <see cref="MoveCheck.Cost"/> carries the incite price (FreeCol
    /// <see cref="InciteNativesCost"/>) so the panel can show it on the button and re-check affordability.
    /// </summary>
    public MoveCheck CheckInciteNatives(Unit unit, NativeSettlement settlement, int rivalId) =>
        CheckInciteNatives(PlayerById(unit.OwnerId) ?? _human, unit, settlement, rivalId);

    /// <summary>Whether <paramref name="player"/>'s <paramref name="unit"/> may incite the tribe against <paramref name="rivalId"/> (the owner-scoped form the rules engine and AI use).</summary>
    internal MoveCheck CheckInciteNatives(Player player, Unit unit, NativeSettlement settlement, int rivalId)
    {
        if (!unit.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (!Ruleset.Role(unit.RoleId).GrantedAbilities.GetValueOrDefault(InciteNativesAbility))
        {
            return MoveCheck.No($"A {unit.Type.ShortName} cannot incite the natives — only a missionary can.");
        }
        if (unit.MovementLeft <= 0)
        {
            return MoveCheck.No("No movement left this turn.");
        }
        if (unit.Position != settlement.Position && !unit.Position.IsAdjacentTo(settlement.Position))
        {
            return MoveCheck.No("Move next to the settlement to incite its tribe.");
        }
        if (rivalId == player.PlayerId)
        {
            return MoveCheck.No("You cannot incite a tribe against yourself.");
        }
        if (PlayerById(rivalId) is not { PlayerType: PlayerType.Colonial })
        {
            return MoveCheck.No("There is no such rival power to incite against.");
        }
        int cost = InciteNativesCost(settlement, player.PlayerId, rivalId);
        if (player.Gold < cost)
        {
            return MoveCheck.No($"The chief demands {cost} gold to make war on them — you cannot afford it.");
        }
        return MoveCheck.Yes(cost);
    }

    /// <summary>
    /// The outcome of an <see cref="InciteNatives(Unit, NativeSettlement, int)"/> attempt: <see cref="Cost"/> is the gold
    /// paid (0 if the incite did not go through) and <see cref="RivalId"/> is the power the tribe was turned against. A
    /// single-shot result the presentation reports as "the tribe will war on the X (N gold)".
    /// </summary>
    /// <param name="Incited">True when the tribe was turned against the rival (the bribe was paid).</param>
    /// <param name="Cost">The gold paid to the chief (0 on a no-op).</param>
    /// <param name="RivalId">The rival the tribe was incited against.</param>
    public readonly record struct InciteNativesResult(bool Incited, int Cost, int RivalId);

    /// <summary>
    /// Incites <paramref name="settlement"/>'s tribe to war against the rival <paramref name="rivalId"/> with the human's
    /// missionary <paramref name="unit"/> (FreeCol <c>InGameController.incite</c>): pays the chief the
    /// <see cref="InciteNativesCost"/> bribe, then raises that nation's alarm toward the rival by
    /// <see cref="InciteWarAlarm"/> (1000 — war level, every settlement of the tribe) and the rival's colonial tension
    /// toward the inciter by <see cref="TensionWarInciter"/> (250). The tribe's resulting hostility drives its braves at
    /// the rival under the existing native-AI rules; the actual stance change is left to fall out naturally. The gold
    /// simply leaves the inciter's purse (no native treasury is modelled, as with scout beads / plunder). Ends the unit's
    /// turn. Deterministic — draws no RNG (ADR-009).
    /// </summary>
    /// <returns>Whether the tribe was incited and how much it cost.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckInciteNatives(Unit, NativeSettlement, int)"/>.</exception>
    public InciteNativesResult InciteNatives(Unit unit, NativeSettlement settlement, int rivalId) =>
        InciteNatives(_human, unit, settlement, rivalId);

    /// <summary>Incites on behalf of <paramref name="player"/> (the unit's owner) — the owner-scoped form for the AI / foreign powers.</summary>
    internal InciteNativesResult InciteNatives(Player player, Unit unit, NativeSettlement settlement, int rivalId)
    {
        MoveCheck check = CheckInciteNatives(player, unit, settlement, rivalId);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        int cost = check.Cost;
        player.Gold -= cost; // the bribe leaves the inciter's purse (no native treasury modelled)

        // Turn the whole tribe against the rival: a war-level alarm spike on the rival's per-settlement channel, applied
        // nation-wide (FreeCol applies WAR_MODIFIER to the native player; we track alarm per settlement, so every
        // settlement of this nation gets it). The rival then resents the instigator (colonial tension +250).
        foreach (NativeSettlement s in _nativeSettlements.Where(s => s.NationTypeId == settlement.NationTypeId))
        {
            ChangeNativeAlarm(s, rivalId, InciteWarAlarm);
        }
        ChangeTension(rivalId, player.PlayerId, TensionWarInciter, symmetric: false); // FreeCol TENSION_ADD_WAR_INCITER

        unit.MovementLeft = 0; // the audience ends the unit's turn
        return new InciteNativesResult(Incited: true, Cost: cost, RivalId: rivalId);
    }
}
