using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Multi-player scaffolding (FP-3b, ADR-019): the human plus inert foreign colonial powers and the native
/// nations as players, the ring-buffer turn (the human acts, the others are inert), and the multi-element
/// save. The foreign powers/natives draw no RNG and take no turn, so seeded games/goldens stay byte-stable.
/// </summary>
public class MultiPlayerTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Fact]
    public void NewGame_HasHuman_ForeignPowers_AndNativePlayers()
    {
        var game = Game.New(Classic, seed: 7);

        Player human = Assert.Single(game.Players, p => p.IsHuman);
        Assert.Equal(0, human.PlayerId);
        Assert.Equal(PlayerType.Colonial, human.PlayerType);
        Assert.Null(human.NationId);
        Assert.Same(human, game.HumanPlayer);
        Assert.Same(human, game.CurrentPlayer); // it is the human's turn

        // Three inert foreign colonial powers, each a real European nation.
        var foreignPowers = game.Players.Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).ToList();
        Assert.Equal(3, foreignPowers.Count);
        Assert.All(foreignPowers, p => Assert.Contains(Classic.EuropeanNations, n => n.Id == p.NationId));

        // Every distinct native nation present is a Native player.
        var nativeNations = game.NativeSettlements.Select(s => s.NationTypeId).Distinct().ToList();
        var nativePlayers = game.Players.Where(p => p.PlayerType == PlayerType.Native).ToList();
        Assert.Equal(nativeNations.Count, nativePlayers.Count);
        Assert.All(nativePlayers, p => Assert.Contains(p.NationId, nativeNations));

        // Player ids are dense and unique (0..N-1).
        Assert.Equal(Enumerable.Range(0, game.Players.Count), game.Players.Select(p => p.PlayerId).OrderBy(i => i));
    }

    [Fact]
    public void ForeignPowers_StartInert_WithUnitsInEurope_HiddenFromTheHuman()
    {
        var game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);

        var theirUnits = game.Units.Where(u => u.OwnerNationId is null && u.OwnerId == power.PlayerId).ToList();
        Assert.NotEmpty(theirUnits);
        Assert.All(theirUnits, u => Assert.Equal(UnitLocation.InEurope, u.Location));
        // The human's Europe view excludes the foreign power's units (it has none of its own at the start).
        Assert.Empty(game.UnitsInEurope);
    }

    [Fact]
    public void EndTurn_RunsTheHumansEconomy_AndLeavesForeignPowersInert()
    {
        var game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        int turnBefore = game.Turn;

        game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap)); // give the human's economy something to do
        game.EndTurn();

        Assert.Equal(turnBefore + 1, game.Turn);            // the world advanced once
        Assert.Same(game.HumanPlayer, game.CurrentPlayer);  // control returned to the human
        // The foreign power stayed inert — no economy ran for it.
        Assert.Equal(0, power.Gold);
        Assert.Equal(0, power.Immigration);
        Assert.Equal(0, power.Liberty);
    }

    [Fact]
    public void MultiPlayerSave_RoundTripsAllPlayers_AndForeignUnits()
    {
        var game = Game.New(Classic, seed: 7);

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(
            game.Players.Select(p => (p.PlayerId, p.NationId, p.IsHuman, p.PlayerType)).OrderBy(t => t.PlayerId),
            loaded.Players.Select(p => (p.PlayerId, p.NationId, p.IsHuman, p.PlayerType)).OrderBy(t => t.PlayerId));

        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        Assert.Equal(
            game.Units.Count(u => u.OwnerId == power.PlayerId && u.OwnerNationId is null),
            loaded.Units.Count(u => u.OwnerId == power.PlayerId && u.OwnerNationId is null));
    }
}
