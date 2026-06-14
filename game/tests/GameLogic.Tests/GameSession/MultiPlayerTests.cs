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
    public void ForeignPowers_StartOnTheMap_HiddenFromTheHuman()
    {
        var game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);

        var theirUnits = game.Units.Where(u => u.OwnerNationId is null && u.OwnerId == power.PlayerId).ToList();
        Assert.NotEmpty(theirUnits);
        Assert.Contains(theirUnits, u => u.IsOnMap);                   // landed on the map (FP-4), not docked in Europe
        Assert.DoesNotContain(theirUnits, u => game.PlayerUnits.Contains(u)); // never the human's
        // A foreign power lands far from the human, so its units do not lift the human's fog.
        Assert.All(theirUnits.Where(u => u.IsOnMap), u => Assert.False(game.IsVisible(u.Position)));
        Assert.Empty(game.UnitsInEurope);                              // the human has none of its own in Europe
    }

    [Fact]
    public void EndTurn_RunsForeignPowerAi_AndItsEconomy()
    {
        var game = Game.New(Classic, seed: 7);
        int turnBefore = game.Turn;

        game.EndTurn();

        Assert.Equal(turnBefore + 1, game.Turn);            // the world advanced once
        Assert.Same(game.HumanPlayer, game.CurrentPlayer);  // control returned to the human
        // A foreign power acted — it founded a colony on the land it settled (FP-4).
        Assert.Contains(game.Colonies, c => game.Players.Any(p =>
            p.PlayerId == c.OwnerId && !p.IsHuman && p.PlayerType == PlayerType.Colonial));
        // …and it now runs an economy (FP-5): it accrued immigration on its own (the flat +2 player bonus,
        // independent of terrain) and it has its own Europe recruit dock.
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        Assert.True(power.Immigration > 0, "the foreign power accrued no immigration");
        Assert.NotEmpty(power.RecruitDock);
    }

    [Fact]
    public void ForeignPowerAi_IsReplayStable_ForAFixedSeed()
    {
        var a = Game.New(Classic, seed: 31337);
        var b = Game.New(Classic, seed: 31337);
        for (int turn = 0; turn < 20; turn++)
        {
            a.EndTurn();
            b.EndTurn();
        }

        // Same seed → byte-identical games after 20 turns of foreign-power AI: every AI choice draws from
        // that player's own deterministic stream (ADR-009), and the human's stream 0 is never touched.
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());
        Assert.Contains(a.Colonies, c => c.OwnerId != 0); // a foreign power was active — it founded a colony
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
