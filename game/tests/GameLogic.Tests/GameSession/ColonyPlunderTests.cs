using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Combat;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Colony capture plunder (FreeCol <c>csCaptureColony</c> / <c>Colony.getPlunderRange</c>): a winning colony
/// assault sacks the former owner's treasury — <c>rnd[0, upper] + 1</c> gold with
/// <c>upper = ownerGold × (colonyPop+1)/(ownerColoniesPop+1)</c>, capped at the victim's purse, transferred to
/// the captor. A broke owner yields nothing. The draw is on the captor's stream (human stream 0, or a foreign
/// power's own stream — so a foreign capture never perturbs the human's stream 0, ADR-009).
/// </summary>
public class ColonyPlunderTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0x901DUL;
    private const string Artillery = "model.unit.artillery"; // offence 7, movement 3

    /// <summary>Fixed combat band (<paramref name="nextDouble"/>) + fixed integer draw (<paramref name="next"/>, clamped in range) — forces a win and a chosen plunder amount.</summary>
    private sealed class TestRandom(double nextDouble, int next) : IGameRandom
    {
        public int Next(int maxExclusive) => System.Math.Min(next, maxExclusive - 1);
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => nextDouble;
        public RandomState SaveState() => new(0, 0);
    }

    private static bool FreeLand(Game g, Position p) =>
        g.Map.InBounds(p) && !g.Map.TerrainAt(p).IsWater
        && g.ColonyAt(p) is null && g.NativeSettlementAt(p) is null
        && !g.Units.Any(u => u.IsOnMap && u.Position == p);

    /// <summary>A human-founded colony handed to a rival (its sole colony), the rival given <paramref name="rivalGold"/>, and a human artillery beside it.</summary>
    private static (Game game, Colony colony, Unit attacker, Player rival, Player human) StageRival(int rivalGold)
    {
        Game game = Game.New(Classic, Seed);
        Player human = game.HumanPlayer;
        Player rival = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        colony.OwnerId = rival.PlayerId; // a rival colony (its only one → upper == its whole purse)
        rival.Gold = rivalGold;
        Position adj = colony.Position.Neighbours().First(n => FreeLand(game, n));
        Unit attacker = game.SpawnUnit(Classic.Unit(Artillery), adj); // human-owned (OwnerId 0)
        return (game, colony, attacker, rival, human);
    }

    [Fact]
    public void CapturingAColony_PlundersTheTreasury_ToTheCaptor()
    {
        var (game, colony, attacker, rival, human) = StageRival(rivalGold: 1000);
        // Single colony of population 1 → upper = 1000 × 2/2 = 1000; draw 400 → plunder 401.
        game.AttackColony(attacker, colony.Position, new TestRandom(nextDouble: 0.0, next: 400));

        Assert.Equal(401, human.Gold);  // the captor gains the plunder…
        Assert.Equal(599, rival.Gold);  // …the former owner loses it
    }

    [Fact]
    public void ABrokeOwner_YieldsNoPlunder()
    {
        var (game, colony, attacker, rival, human) = StageRival(rivalGold: 0);
        game.AttackColony(attacker, colony.Position, new TestRandom(nextDouble: 0.0, next: 400));

        Assert.Equal(0, human.Gold);
        Assert.Equal(0, rival.Gold); // nothing to take (FreeCol canBePlundered = checkGold(1))
    }

    [Fact]
    public void Plunder_IsCappedAtTheVictimsPurse()
    {
        var (game, colony, attacker, rival, human) = StageRival(rivalGold: 10);
        // upper = 10; a draw of 10 → 11 raw, capped at the 10 the rival actually holds (no negative gold).
        game.AttackColony(attacker, colony.Position, new TestRandom(nextDouble: 0.0, next: 10));

        Assert.Equal(10, human.Gold);
        Assert.Equal(0, rival.Gold);
    }

    [Fact]
    public void RepelledAssault_PlundersNothing()
    {
        var (game, colony, attacker, rival, human) = StageRival(rivalGold: 1000);
        game.AttackColony(attacker, colony.Position, new TestRandom(nextDouble: 0.99, next: 400)); // forced loss

        Assert.Equal(0, human.Gold);
        Assert.Equal(1000, rival.Gold); // a repelled assault sacks nothing
    }

    [Fact]
    public void ForeignCapturePlunder_DebitsTheHuman_WithoutTouchingItsStream0()
    {
        // A foreign power captures the human's colony at war; the human (with gold) is plundered on the POWER's
        // stream. Peace vs war, one EndTurn: the war game's human loses gold yet its stream 0 is byte-identical.
        Game peace = StageForeignCapture(atWar: false);
        Game war = StageForeignCapture(atWar: true);

        peace.EndTurn();
        war.EndTurn();

        Assert.Equal(power(war).PlayerId, war.Colonies.First().OwnerId); // the war game really captured the colony…
        Assert.True(war.HumanPlayer.Gold < peace.HumanPlayer.Gold);      // …and plundered the human's treasury…
        Assert.Equal(peace.RandomState, war.RandomState);                // …yet stream 0 is untouched (power's stream)
    }

    private static Player power(Game g) => g.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);

    /// <summary>The human founds a colony and is given gold; a foreign power's artillery stands beside it, optionally at war.</summary>
    private static Game StageForeignCapture(bool atWar)
    {
        Game game = Game.New(Classic, seed: 7); // seed 7 captures on turn 1 (see ForeignColonyCaptureTests)
        Player rival = power(game);
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        game.HumanPlayer.Gold = 1000; // a treasury to plunder
        Position adj = colony.Position.Neighbours().First(n => FreeLand(game, n));
        Unit attacker = game.SpawnUnit(Classic.Unit(Artillery), adj);
        attacker.OwnerId = rival.PlayerId;
        if (atWar)
        {
            game.SetStance(rival.PlayerId, game.HumanPlayer.PlayerId, Stance.War);
        }
        return game;
    }
}
