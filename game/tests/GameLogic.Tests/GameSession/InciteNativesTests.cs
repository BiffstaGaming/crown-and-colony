using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Inciting a native settlement to war against a European rival (86d3fq00w / 86d3fq0bt, FreeCol
/// <c>InGameController.incite</c>): a missionary at a settlement pays the chief a gold bribe — its size set by how the
/// tribe's alarm toward the inciter compares with its alarm toward the rival (FreeCol's 10000/5000 base + 20·gap,
/// floored at 650) — and on payment the whole tribe's alarm toward that rival spikes to war level (+1000), the rival's
/// colonial tension toward the inciter rises (+250 war-inciter), the gold leaves the inciter's purse, and the
/// missionary's turn ends. Deterministic (no RNG, ADR-009). Only a missionary-role unit can do it; a solo game offers
/// no rival to incite against.
/// </summary>
public class InciteNativesTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string MissionaryRole = "model.role.missionary";
    private const string FreeColonist = "model.unit.freeColonist";

    private static int RivalId(Game game) =>
        game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;

    private static bool FreeLand(Game g, Position p) =>
        g.Map.InBounds(p) && !g.Map.TerrainAt(p).IsWater
        && g.ColonyAt(p) is null && g.NativeSettlementAt(p) is null
        && !g.Units.Any(u => u.IsOnMap && u.Position == p);

    /// <summary>A game with the human's missionary beside a native settlement; the human is given <paramref name="gold"/> to spend.</summary>
    private static (Game game, NativeSettlement settlement, Unit missionary, int rival) Stage(int gold = 100000, ulong seed = 7)
    {
        Game game = Game.New(Classic, seed);
        NativeSettlement settlement = game.NativeSettlements.First(s => s.Position.Neighbours().Any(n => FreeLand(game, n)));
        Position adj = settlement.Position.Neighbours().First(n => FreeLand(game, n));
        Unit missionary = game.SpawnUnit(Classic.Unit(FreeColonist), adj); // human-owned (ownerId 0)
        missionary.RoleId = MissionaryRole;                                // grants model.ability.inciteNatives
        game.HumanPlayer.Gold = gold;
        return (game, settlement, missionary, RivalId(game));
    }

    // ---- cost formula (InciteNativesCost) -------------------------------------------------------------------

    [Fact]
    public void Cost_WhenTribeEquallyDisposed_IsFiveThousandFloorBase()
    {
        // Both channels at 0 alarm: payingValue == targetValue (not >), so the friendlier 5000 base; gap term 0.
        (Game game, NativeSettlement settlement, _, int rival) = Stage();
        Assert.Equal(Game.InciteBaseCostFriendly, game.InciteNativesCost(settlement, inciterId: 0, rivalId: rival));
    }

    [Fact]
    public void Cost_WhenTribeHatesTheRivalMore_IsCheaper_DownToTheFloor()
    {
        // The tribe is far angrier at the rival than at us: base 5000 + 20·(0 − 800) = −11000 → floored at 650.
        (Game game, NativeSettlement settlement, _, int rival) = Stage();
        game.ChangeNativeAlarm(settlement, rival, 800);
        Assert.Equal(Game.InciteFloorCost, game.InciteNativesCost(settlement, inciterId: 0, rivalId: rival));
    }

    [Fact]
    public void Cost_WhenTribeAngrierAtUsThanTheRival_UsesTheHostileBase_PlusGap()
    {
        // The tribe is angrier at us (300) than at the rival (100): hostile base 10000 + 20·(300 − 100) = 14000.
        (Game game, NativeSettlement settlement, _, int rival) = Stage();
        game.ChangeNativeAlarm(settlement, 0, 300);     // our channel
        game.ChangeNativeAlarm(settlement, rival, 100); // the rival's channel
        int expected = Game.InciteBaseCostHostile + Game.InciteCostPerAlarmPoint * (300 - 100);
        Assert.Equal(expected, game.InciteNativesCost(settlement, inciterId: 0, rivalId: rival));
    }

    // ---- precondition gates (CheckInciteNatives) ------------------------------------------------------------

    [Fact]
    public void Check_AllowsAMissionaryBesideASettlement_WithThePriceAsCost()
    {
        (Game game, NativeSettlement settlement, Unit missionary, int rival) = Stage();
        MoveCheck check = game.CheckInciteNatives(missionary, settlement, rival);
        Assert.True(check.Allowed);
        Assert.Equal(game.InciteNativesCost(settlement, 0, rival), check.Cost);
    }

    [Fact]
    public void Check_RejectsANonMissionary()
    {
        (Game game, NativeSettlement settlement, Unit missionary, int rival) = Stage();
        missionary.RoleId = "model.role.default"; // strip the missionary role → no inciteNatives ability
        Assert.False(game.CheckInciteNatives(missionary, settlement, rival).Allowed);
    }

    [Fact]
    public void Check_RejectsWhenTheRivalIsTheInciter()
    {
        (Game game, NativeSettlement settlement, Unit missionary, _) = Stage();
        Assert.False(game.CheckInciteNatives(missionary, settlement, rivalId: 0).Allowed); // self
    }

    [Fact]
    public void Check_RejectsWhenTheRivalIsNotAColonialPower()
    {
        (Game game, NativeSettlement settlement, Unit missionary, _) = Stage();
        int nativeId = game.Players.First(p => p.PlayerType == PlayerType.Native).PlayerId;
        Assert.False(game.CheckInciteNatives(missionary, settlement, nativeId).Allowed);
    }

    [Fact]
    public void Check_RejectsWhenTheInciterCannotAfford()
    {
        (Game game, NativeSettlement settlement, Unit missionary, int rival) = Stage(gold: 100); // far below the 5000 base
        Assert.False(game.CheckInciteNatives(missionary, settlement, rival).Allowed);
    }

    [Fact]
    public void Check_RejectsWhenOutOfMovesOrFarAway()
    {
        (Game game, NativeSettlement settlement, Unit missionary, int rival) = Stage();
        missionary.MovementLeft = 0;
        Assert.False(game.CheckInciteNatives(missionary, settlement, rival).Allowed);
    }

    // ---- the command (InciteNatives) ------------------------------------------------------------------------

    [Fact]
    public void Incite_ChargesGold_RaisesTribeAlarmAtRivalToWar_RaisesRivalTension_EndsTheTurn()
    {
        (Game game, NativeSettlement settlement, Unit missionary, int rival) = Stage();
        int cost = game.InciteNativesCost(settlement, 0, rival);
        int goldBefore = game.HumanPlayer.Gold;
        int rivalAlarmBefore = settlement.AlarmFor(rival);
        int rivalTensionBefore = game.TensionBetween(rival, 0);

        Game.InciteNativesResult result = game.InciteNatives(missionary, settlement, rival);

        Assert.True(result.Incited);
        Assert.Equal(cost, result.Cost);
        Assert.Equal(rival, result.RivalId);
        Assert.Equal(goldBefore - cost, game.HumanPlayer.Gold);                                  // bribe paid
        Assert.Equal(rivalAlarmBefore + Game.InciteWarAlarm, settlement.AlarmFor(rival));        // tribe turns on the rival
        Assert.Equal(rivalTensionBefore + Game.TensionWarInciter, game.TensionBetween(rival, 0)); // rival resents the instigator
        Assert.Equal(0, missionary.MovementLeft);                                                // the audience ends the turn
    }

    [Fact]
    public void Incite_TurnsTheWholeTribeAgainstTheRival_NationWide()
    {
        (Game game, NativeSettlement settlement, Unit missionary, int rival) = Stage();
        // Another settlement of the SAME nation, away from the missionary, must also turn on the rival.
        NativeSettlement? sibling = game.NativeSettlements
            .FirstOrDefault(s => s.NationTypeId == settlement.NationTypeId && s.Id != settlement.Id);

        game.InciteNatives(missionary, settlement, rival);

        Assert.Equal(Game.InciteWarAlarm, settlement.AlarmFor(rival));
        if (sibling is not null)
        {
            Assert.Equal(Game.InciteWarAlarm, sibling.AlarmFor(rival)); // nation-wide, not just the incited camp
        }
    }

    [Fact]
    public void Incite_LeavesTheIncitersOwnAlarmChannelUntouched()
    {
        // Inciting against the rival must not change how the tribe feels about US (ADR-009 per-player channels).
        (Game game, NativeSettlement settlement, Unit missionary, int rival) = Stage();
        int ourAlarmBefore = settlement.AlarmFor(0);
        game.InciteNatives(missionary, settlement, rival);
        Assert.Equal(ourAlarmBefore, settlement.AlarmFor(0));
    }

    [Fact]
    public void Incite_IsDeterministic_NeverTouchesTheSavedRngStream()
    {
        // Incite draws no RNG, so the game's saved stream (the human's stream 0) is byte-identical across it (ADR-009).
        (Game game, NativeSettlement settlement, Unit missionary, int rival) = Stage();
        RandomState before = game.RandomState;
        game.InciteNatives(missionary, settlement, rival);
        Assert.Equal(before, game.RandomState);
    }

    [Fact]
    public void Incite_RejectsAnIllegalAttempt()
    {
        (Game game, NativeSettlement settlement, Unit missionary, int rival) = Stage(gold: 0); // cannot afford
        Assert.Throws<InvalidMoveException>(() => game.InciteNatives(missionary, settlement, rival));
    }

    [Fact]
    public void IncitableRivals_ListsTheLiveColonialRivals_NotTheInciterOrNatives()
    {
        (Game game, _, Unit missionary, int rival) = Stage();
        var rivals = game.IncitableRivals(missionary);
        Assert.Contains(rivals, p => p.PlayerId == rival);
        Assert.DoesNotContain(rivals, p => p.PlayerId == 0);                          // not the inciter
        Assert.All(rivals, p => Assert.Equal(PlayerType.Colonial, p.PlayerType));     // colonial powers only
    }
}
