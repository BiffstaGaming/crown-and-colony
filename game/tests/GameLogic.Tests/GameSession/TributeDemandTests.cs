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
/// Tribute/ransom demands by offensive colonial units (86d3drn3b, FreeCol <c>InGameController.demandTribute</c> — the
/// <c>TradeContext.Tribute</c> negotiation): an armed unit adjacent to a native settlement may shake it down for gold
/// instead of attacking. The settlement decides by its alarm band — Happy/Content pays <c>roll/10</c>, Displeased
/// <c>roll/20</c> (both capped at 100), Angry/Hateful refuses — and a 5-turn cooldown stops repeat demands. Either way
/// the demand is an insult (+200 alarm) and ends the unit's turn. The roll draws from the demander's own stream
/// (a foreign power never perturbs the human's stream 0, ADR-009). Transient — the cooldown is not serialized.
/// </summary>
public class TributeDemandTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Artillery = "model.unit.artillery"; // innate offence 7 — "armed" without a role
    private const string Colonist = "model.unit.freeColonist";

    /// <summary>A fixed RNG: <see cref="Next(int)"/> returns min(value, max-1); doubles unused here.</summary>
    private sealed class TestRandom(int next) : IGameRandom
    {
        public int Next(int maxExclusive) => System.Math.Min(next, maxExclusive - 1);
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => 0.0;
        public RandomState SaveState() => new(0, 0);
    }

    private static bool FreeLand(Game g, Position p) =>
        g.Map.InBounds(p) && !g.Map.TerrainAt(p).IsWater
        && g.ColonyAt(p) is null && g.NativeSettlementAt(p) is null
        && !g.Units.Any(u => u.IsOnMap && u.Position == p);

    /// <summary>A game with the human's artillery beside a native settlement, the settlement's alarm set to <paramref name="alarm"/>.</summary>
    private static (Game game, NativeSettlement settlement, Unit gun) Stage(int alarm, ulong seed = 7)
    {
        Game game = Game.New(Classic, seed);
        NativeSettlement settlement = game.NativeSettlements.First();
        game.ChangeNativeAlarm(settlement, alarm - settlement.Alarm); // set to exactly `alarm`
        Position adj = settlement.Position.Neighbours().First(n => FreeLand(game, n));
        Unit gun = game.SpawnUnit(Classic.Unit(Artillery), adj); // human-owned (ownerId 0) offensive unit
        return (game, settlement, gun);
    }

    /// <summary>The tribute a Happy/Content (divisor 10) or Displeased (divisor 20) settlement yields when the gift
    /// roll is forced to its minimum (Next(range·factor) = 0): <c>min((min·factor)/divisor, 100)</c>.</summary>
    private static int ExpectedMinRollGold(NativeSettlement settlement, int divisor)
    {
        SettlementGifts g = Classic.Settlement(settlement.SettlementTypeId).Gifts!;
        int roll = g.Minimum * g.Factor; // Next((max-min+1)·factor) forced to 0 → roll = min·factor
        return System.Math.Min(roll / divisor, 100);
    }

    // ---- precondition checks (CheckDemandTribute) ----------------------------------------------------------

    [Fact]
    public void CheckDemandTribute_AllowsAnArmedUnitAdjacentToASettlement()
    {
        (Game game, NativeSettlement settlement, Unit gun) = Stage(alarm: 0);
        Assert.True(game.CheckDemandTribute(gun, settlement.Position).Allowed);
    }

    [Fact]
    public void CheckDemandTribute_RejectsAUnitWithNoOffensiveStrength()
    {
        (Game game, NativeSettlement settlement, _) = Stage(alarm: 0);
        Position adj2 = settlement.Position.Neighbours().Last(n => FreeLand(game, n));
        Unit colonist = game.SpawnUnit(Classic.Unit(Colonist), adj2); // unarmed — no offence
        Assert.False(game.CheckDemandTribute(colonist, settlement.Position).Allowed);
    }

    [Fact]
    public void CheckDemandTribute_RejectsANonAdjacentUnit()
    {
        (Game game, NativeSettlement settlement, Unit gun) = Stage(alarm: 0);
        gun.MovementLeft = 9; // plenty of moves — the failure must be distance, not the move gate
        // Find a free land tile that is neither on nor adjacent to the settlement.
        Position? far = null;
        for (int y = 0; y < game.Map.Height && far is null; y++)
        {
            for (int x = 0; x < game.Map.Width; x++)
            {
                var p = new Position(x, y);
                if (FreeLand(game, p) && p != settlement.Position && !p.IsAdjacentTo(settlement.Position))
                {
                    far = p;
                    break;
                }
            }
        }
        Unit remote = game.SpawnUnit(Classic.Unit(Artillery), far!.Value);
        Assert.False(game.CheckDemandTribute(remote, settlement.Position).Allowed);
    }

    [Fact]
    public void CheckDemandTribute_RejectsWhenThereIsNoSettlement()
    {
        (Game game, _, Unit gun) = Stage(alarm: 0);
        Position empty = gun.Position.Neighbours().First(n => FreeLand(game, n));
        Assert.False(game.CheckDemandTribute(gun, empty).Allowed);
    }

    // ---- accept/refuse band rule (EvaluateTributeDemand) ----------------------------------------------------

    [Theory]
    [InlineData(0, 10)]    // Happy → roll/10
    [InlineData(300, 10)]  // Content → roll/10
    [InlineData(650, 20)]  // Displeased → roll/20
    public void EvaluateTributeDemand_PaysByBand(int alarm, int divisor)
    {
        (Game game, NativeSettlement settlement, _) = Stage(alarm);
        int gold = game.EvaluateTributeDemand(settlement, playerId: 0, new TestRandom(next: 0)); // roll-range draw forced to 0 (human channel)
        Assert.Equal(ExpectedMinRollGold(settlement, divisor), gold);
        Assert.True(gold > 0); // a calm settlement always yields at least its floor
    }

    [Fact]
    public void EvaluateTributeDemand_CapsAtOneHundred()
    {
        (Game game, NativeSettlement settlement, _) = Stage(alarm: 0); // Happy, /10
        int gold = game.EvaluateTributeDemand(settlement, playerId: 0, new TestRandom(next: 9999)); // force the roll high (human channel)
        Assert.InRange(gold, 1, 100); // never above the 100-gold ceiling
    }

    [Theory]
    [InlineData(750)] // Angry
    [InlineData(900)] // Hateful
    public void EvaluateTributeDemand_RefusesWhenTooAlarmed(int alarm)
    {
        (Game game, NativeSettlement settlement, _) = Stage(alarm);
        Assert.Equal(0, game.EvaluateTributeDemand(settlement, playerId: 0, new TestRandom(next: 0)));
    }

    [Fact]
    public void EvaluateTributeDemand_RefusesOnCooldown()
    {
        (Game game, NativeSettlement settlement, _) = Stage(alarm: 0);
        settlement.LastTribute = game.Turn; // demanded of this very turn → lastTribute + 5 >= turn
        Assert.Equal(0, game.EvaluateTributeDemand(settlement, playerId: 0, new TestRandom(next: 0)));
    }

    // ---- DemandTribute command (transfer + alarm + cooldown + turn) -----------------------------------------

    [Fact]
    public void DemandTribute_WhenCalm_PaysGold_RaisesAlarm_StampsCooldown_EndsTheTurn()
    {
        (Game game, NativeSettlement settlement, Unit gun) = Stage(alarm: 0);
        int goldBefore = game.HumanPlayer.Gold;
        int alarmBefore = settlement.Alarm;
        int expected = ExpectedMinRollGold(settlement, divisor: 10);

        Game.TributeResult result = game.DemandTribute(gun, settlement.Position, new TestRandom(next: 0));

        Assert.True(result.Paid);
        Assert.Equal(expected, result.Gold);
        Assert.Equal(goldBefore + expected, game.HumanPlayer.Gold);     // minted to the demander
        Assert.Equal(alarmBefore + NativeSettlement.TensionAddNormal, settlement.Alarm); // +200 insult
        Assert.Equal(game.Turn, settlement.LastTribute);               // cooldown stamped
        Assert.Equal(0, gun.MovementLeft);                            // the demand ended the turn
    }

    [Fact]
    public void DemandTribute_WhenTooAlarmed_RefusesButStillInsults()
    {
        (Game game, NativeSettlement settlement, Unit gun) = Stage(alarm: 900); // Hateful → refuses
        int goldBefore = game.HumanPlayer.Gold;
        int alarmBefore = settlement.Alarm;

        Game.TributeResult result = game.DemandTribute(gun, settlement.Position, new TestRandom(next: 0));

        Assert.False(result.Paid);
        Assert.Equal(0, result.Gold);
        Assert.Equal(goldBefore, game.HumanPlayer.Gold);              // nothing extracted
        Assert.Equal(System.Math.Min(alarmBefore + NativeSettlement.TensionAddNormal, NativeSettlement.MaxAlarm), settlement.Alarm);
        Assert.Equal(game.Turn, settlement.LastTribute);             // cooldown stamped even on refusal
        Assert.Equal(0, gun.MovementLeft);
    }

    [Fact]
    public void DemandTribute_Twice_SecondIsRefusedByCooldown()
    {
        (Game game, NativeSettlement settlement, Unit gun) = Stage(alarm: 0);
        game.DemandTribute(gun, settlement.Position, new TestRandom(next: 0)); // first pays
        gun.MovementLeft = 1; // refresh so the second attempt passes the move gate
        int goldAfterFirst = game.HumanPlayer.Gold;

        Game.TributeResult second = game.DemandTribute(gun, settlement.Position, new TestRandom(next: 0));

        Assert.False(second.Paid);                       // same turn → on cooldown
        Assert.Equal(goldAfterFirst, game.HumanPlayer.Gold);
    }

    [Fact]
    public void DemandTribute_RejectsAnIllegalDemand()
    {
        (Game game, NativeSettlement settlement, Unit gun) = Stage(alarm: 0);
        gun.MovementLeft = 0; // spent — no demand allowed
        Assert.Throws<InvalidMoveException>(() => game.DemandTribute(gun, settlement.Position, new TestRandom(next: 0)));
    }

    [Fact]
    public void DemandTribute_ExplicitRng_NeverTouchesTheGamesSavedStream()
    {
        // The explicit-RNG overload draws only from the passed RNG, so the game's saved stream (the human's stream 0)
        // is byte-identical across the demand (ADR-009) — the property a foreign-power demand relies on.
        (Game game, NativeSettlement settlement, Unit gun) = Stage(alarm: 0);
        RandomState before = game.RandomState;
        game.DemandTribute(gun, settlement.Position, new TestRandom(next: 0));
        Assert.Equal(before, game.RandomState);
    }
}
